using System.Buffers.Binary;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging;

namespace Pathstitch.App.Services;

public interface IGeometryWorkerRuntimeResolver
{
    GeometryWorkerRuntime? Resolve();
}

public sealed record GeometryWorkerRuntime(string PythonExecutable, string ModuleRoot);

public sealed class AppOwnedGeometryWorkerRuntimeResolver : IGeometryWorkerRuntimeResolver
{
    public GeometryWorkerRuntime? Resolve()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "GeometryWorker", rid),
            Path.Combine(AppContext.BaseDirectory, "GeometryWorker"),
        };
        foreach (var root in roots)
        {
            var python = OperatingSystem.IsWindows()
                ? Path.Combine(root, "python.exe")
                : Path.Combine(root, "bin", "python3.11");
            if (File.Exists(python)
                && File.Exists(Path.Combine(root, "pathstitch_core", "geometry_worker.py")))
            {
                return new GeometryWorkerRuntime(python, root);
            }
        }

        return null;
    }
}

public sealed class PackagedStepGeometryKernelService(
    ILogger<PackagedStepGeometryKernelService> logger,
    IGeometryWorkerRuntimeResolver runtimeResolver) : IStepGeometryKernelService, IDisposable
{
    private const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<PackagedStepGeometryKernelService> _logger = logger;
    private readonly IGeometryWorkerRuntimeResolver _runtimeResolver = runtimeResolver;
    private readonly ConcurrentDictionary<string, StepGeometryDocument> _documentsById = new(StringComparer.Ordinal);
    private Process? _process;
    private Stream? _input;
    private Stream? _output;
    private long _nextRequestId;
    private StepGeometryProtocolInfo? _protocol;

    public async Task<StepGeometryProtocolInfo> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        if (_protocol is not null)
            return _protocol;
        var response = await SendAsync("handshake", new { }, cancellationToken).ConfigureAwait(false);
        _protocol = response.GetProperty("protocol").Deserialize<StepGeometryProtocolInfo>(JsonOptions)
            ?? throw new GeometryWorkerException(new GeometryKernelFailure(
                GeometryKernelFailureCode.ProtocolMismatch,
                GeometryKernelOperation.Import,
                "Geometry worker returned an empty protocol handshake."));
        if (_protocol.ProtocolVersion != ProtocolVersion)
            throw new GeometryWorkerException(new GeometryKernelFailure(
                GeometryKernelFailureCode.ProtocolMismatch,
                GeometryKernelOperation.Import,
                $"Geometry worker protocol {_protocol.ProtocolVersion} does not match app protocol {ProtocolVersion}."));
        return _protocol;
    }

    public async Task<StepGeometryImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var protocol = await HandshakeAsync(cancellationToken).ConfigureAwait(false);
            var response = await SendAsync("import", new { sourcePath }, cancellationToken).ConfigureAwait(false);
            var topology = response.GetProperty("topology").Deserialize<StepGeometryDocument>(JsonOptions);
            if (topology is not null)
                _documentsById[topology.DocumentId] = topology;
            var viewport = response.GetProperty("viewport");
            var bodies = viewport.GetProperty("bodies").EnumerateArray().Select(ParseBody).ToArray();
            return new StepGeometryImportResult(
                true,
                $"Imported {bodies.Length} STEP B-rep body/bodies through packaged OCCT worker.",
                protocol,
                topology,
                viewport.GetRawText(),
                bodies);
        }
        catch (GeometryWorkerException ex)
        {
            return new StepGeometryImportResult(false, ex.Message, Failure: ex.Failure);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new StepGeometryImportResult(
                false,
                ex.Message,
                Failure: new GeometryKernelFailure(
                    GeometryKernelFailureCode.BackendFailure,
                    GeometryKernelOperation.Import,
                    ex.Message,
                    ex.GetType().FullName,
                    IsRetryable: true));
        }
    }

    public async Task<StepGeometryCombineResult> CombineAsync(
        string existingSourcePath,
        string incomingSourcePath,
        CancellationToken cancellationToken = default)
    {
        var output = CreateOutputPath("step-combined", ".step");
        try
        {
            var response = await SendAsync("combine", new
            {
                input = existingSourcePath,
                incoming = incomingSourcePath,
                output,
            }, cancellationToken).ConfigureAwait(false);
            var data = response.GetProperty("data");
            var outputPath = data.TryGetProperty("output", out var outputValue)
                ? outputValue.GetString()
                : output;
            if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
                throw new InvalidDataException("STEP combine worker did not create its output document.");
            var bodyCount = data.TryGetProperty("body_count", out var bodyCountValue)
                ? bodyCountValue.GetInt32()
                : 0;
            return new StepGeometryCombineResult(
                true,
                $"Combined {bodyCount} STEP B-rep body/bodies through packaged OCCT worker.",
                outputPath,
                bodyCount);
        }
        catch (GeometryWorkerException ex)
        {
            TryDeleteFile(output);
            return new StepGeometryCombineResult(false, ex.Message, Failure: ex.Failure);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(output);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDeleteFile(output);
            return new StepGeometryCombineResult(
                false,
                ex.Message,
                Failure: new GeometryKernelFailure(
                    GeometryKernelFailureCode.BackendFailure,
                    GeometryKernelOperation.Import,
                    ex.Message,
                    ex.GetType().FullName,
                    IsRetryable: true));
        }
    }

    public async Task<EditorOperationResult> ProjectAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default)
    {
        var output = CreateOutputPath("step-projection");
        var document = TryFindDocument(request.SourceModelPath);
        var faceId = request.FaceId ?? TryGetFaceId(document, request.FaceBodyIndex, request.FaceIndex);
        var visibleBodyIds = request.VisibleBodyIds ?? TryGetBodyIds(document, request.VisibleBodyIndices);
        var bodyOffsets = request.BodyOffsets.ToDictionary(
            offset => offset.BodyIndex.ToString(),
            offset => new[] { offset.X, offset.Y, offset.Z });
        try
        {
            var response = await SendAsync("project", new
            {
                input = request.SourceModelPath,
                output,
                document_id = document?.DocumentId,
                face_id = faceId,
                visible_body_ids = visibleBodyIds,
                body_index = 0,
                plane_type = request.PlaneType,
                face_index = request.FaceIndex,
                face_body_index = request.FaceBodyIndex,
                visible_bodies = request.VisibleBodyIndices,
                body_offsets = bodyOffsets,
                offset = request.Offset,
            }, cancellationToken).ConfigureAwait(false);
            var geometry = response.GetProperty("data").GetProperty("typedGeometry")
                .Deserialize<StepOperationGeometry>(JsonOptions);
            return new EditorOperationResult(true, "Projected STEP B-rep through packaged OCCT worker.", output, Geometry: geometry);
        }
        catch (GeometryWorkerException ex)
        {
            return new EditorOperationResult(false, ex.Message, Failure: ex.Failure);
        }
    }

    public async Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default)
    {
        var output = CreateOutputPath("step-unfold");
        var document = TryFindDocument(request.SourceModelPath);
        var selectedFaceIds = request.SelectedFaceIds ?? request.SelectedFaces
            .Select(face => face.FaceId ?? TryGetFaceId(document, face.BodyIndex, face.FaceIndex))
            .Where(id => id is not null)
            .Cast<string>()
            .ToArray();
        var visibleBodyIds = request.VisibleBodyIds ?? TryGetBodyIds(document, request.VisibleBodyIndices);
        try
        {
            var operation = string.Equals(request.NetLayout, "connected", StringComparison.OrdinalIgnoreCase)
                ? "unfold"
                : "unfold_faces";
            var response = await SendAsync(operation, new
            {
                input = request.SourceModelPath,
                output,
                document_id = document?.DocumentId,
                face_ids = selectedFaceIds,
                visible_body_ids = visibleBodyIds,
                whole_body = request.WholeBody,
                faces = request.SelectedFaces.Select(face => new { body_index = face.BodyIndex, face_index = face.FaceIndex }).ToArray(),
                distortion_mode = request.DistortionMode,
                mode = request.UnrollMode,
                decoration = request.SeamDecoration,
                anchor = request.AnchorFace is { } anchor
                    ? new { body_index = anchor.BodyIndex, face_index = anchor.FaceIndex }
                    : null,
                seam_control_mode = request.SeamControlMode,
                forced_seams = (request.ForcedSeams ?? []).Select(edge => new
                {
                    body_index = edge.BodyIndex,
                    edge_index = edge.EdgeIndex,
                }).ToArray(),
                forbidden_seams = (request.ForbiddenSeams ?? []).Select(edge => new
                {
                    body_index = edge.BodyIndex,
                    edge_index = edge.EdgeIndex,
                }).ToArray(),
                seam_decorations = (request.SeamDecorations ?? []).Select(item => new
                {
                    body_index = item.Edge.BodyIndex,
                    edge_index = item.Edge.EdgeIndex,
                    decoration = item.Decoration,
                }).ToArray(),
            }, cancellationToken).ConfigureAwait(false);
            var geometry = response.GetProperty("data").GetProperty("typedGeometry")
                .Deserialize<StepOperationGeometry>(JsonOptions);
            return new EditorOperationResult(true,
                request.NetLayout.Equals("connected", StringComparison.OrdinalIgnoreCase)
                    ? "Unfolded connected STEP net through packaged OCCT worker."
                    : "Flattened separate STEP pieces through packaged OCCT worker.",
                output, Geometry: geometry);
        }
        catch (GeometryWorkerException ex)
        {
            return new EditorOperationResult(false, ex.Message, Failure: ex.Failure);
        }
    }

    public async Task<EditorFaceDistortionResult> ComputeDistortionAsync(
        string sourcePath,
        SelectedFace3D face,
        string distortionMode,
        CancellationToken cancellationToken = default)
    {
        var document = TryFindDocument(sourcePath);
        var faceId = face.FaceId ?? TryGetFaceId(document, face.BodyIndex, face.FaceIndex);
        try
        {
            var response = await SendAsync("distortion", new
            {
                input = sourcePath,
                document_id = document?.DocumentId,
                face_id = faceId,
                body_index = face.BodyIndex,
                face_index = face.FaceIndex,
                distortion_mode = distortionMode,
            }, cancellationToken).ConfigureAwait(false);
            return new EditorFaceDistortionResult(true, "Computed STEP face distortion through packaged OCCT worker.", response.GetProperty("data").GetRawText());
        }
        catch (GeometryWorkerException ex)
        {
            return new EditorFaceDistortionResult(false, ex.Message, Failure: ex.Failure);
        }
    }

    private async Task<JsonElement> SendAsync(string operation, object payload, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureWorker();
            var input = _input ?? throw new InvalidOperationException("Geometry worker input stream is unavailable.");
            var output = _output ?? throw new InvalidOperationException("Geometry worker output stream is unavailable.");
            var id = Interlocked.Increment(ref _nextRequestId);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, protocolVersion = ProtocolVersion, operation, payload }, JsonOptions);
            var header = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)bytes.Length));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await input.WriteAsync(header, timeout.Token).ConfigureAwait(false);
            await input.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
            await input.FlushAsync(timeout.Token).ConfigureAwait(false);
            await ReadExactlyAsync(output, header, timeout.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length is 0 or > 128 * 1024 * 1024)
                throw new InvalidDataException($"Geometry worker returned invalid frame length {length}.");
            var responseBytes = new byte[length];
            await ReadExactlyAsync(output, responseBytes, timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(responseBytes);
            var response = document.RootElement.Clone();
            if (response.GetProperty("id").GetInt64() != id)
                throw new InvalidDataException("Geometry worker response ID did not match the request.");
            if (!response.GetProperty("ok").GetBoolean())
                throw CreateWorkerException(response.GetProperty("error"), operation);
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StopWorker();
            throw new GeometryWorkerException(new GeometryKernelFailure(
                GeometryKernelFailureCode.Timeout,
                MapOperation(operation),
                $"The packaged geometry worker timed out while performing {operation}.",
                IsRetryable: true));
        }
        catch (OperationCanceledException)
        {
            StopWorker();
            throw;
        }
        catch (GeometryWorkerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StopWorker();
            throw new GeometryWorkerException(new GeometryKernelFailure(
                GeometryKernelFailureCode.BackendFailure,
                MapOperation(operation),
                $"The packaged geometry worker transport failed while performing {operation}.",
                ex.Message,
                IsRetryable: true));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureWorker()
    {
        if (_process is { HasExited: false })
            return;
        StopWorker();
        var runtime = _runtimeResolver.Resolve()
            ?? throw new GeometryWorkerException(new GeometryKernelFailure(
                GeometryKernelFailureCode.SourceUnavailable,
                GeometryKernelOperation.Import,
                "The app-owned geometry worker runtime is missing. Reinstall Pathstitch with the GeometryWorker payload."));
        var start = new ProcessStartInfo(runtime.PythonExecutable, "-B -m pathstitch_core.geometry_worker")
        {
            WorkingDirectory = runtime.ModuleRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.Environment["PYTHONPATH"] = runtime.ModuleRoot;
        start.Environment.Remove("PYTHONHOME");
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        _process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start geometry worker.");
        _input = _process.StandardInput.BaseStream;
        _output = _process.StandardOutput.BaseStream;
        _ = Task.Run(async () =>
        {
            var stderr = await _process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogDebug("Geometry worker stderr: {WorkerStderr}", stderr);
        });
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (count == 0)
                throw new EndOfStreamException("Geometry worker exited before completing its response.");
            read += count;
        }
    }

    private static Body3D ParseBody(JsonElement body)
    {
        var bodyIndex = body.GetProperty("body_index").GetInt32();
        var faces = body.GetProperty("faces").EnumerateArray().Select(face => new Face3D(
            face.GetProperty("face_index").GetInt32(),
            face.GetProperty("type").GetString() ?? "Other",
            face.GetProperty("area").GetDouble()) { BodyIndex = bodyIndex }).ToArray();
        return new Body3D(bodyIndex, body.GetProperty("name").GetString() ?? $"Body {bodyIndex + 1}", faces);
    }

    private StepGeometryDocument? TryFindDocument(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return null;
        using var stream = File.OpenRead(sourcePath);
        var documentId = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()[..24];
        return _documentsById.TryGetValue(documentId, out var document) ? document : null;
    }

    private static string? TryGetFaceId(StepGeometryDocument? document, int? bodyIndex, int? faceIndex)
        => document is not null
            && bodyIndex is >= 0 && bodyIndex < document.Bodies.Count
            && faceIndex is >= 0 && faceIndex < document.Bodies[bodyIndex.Value].Faces.Count
                ? document.Bodies[bodyIndex.Value].Faces[faceIndex.Value].Id
                : null;

    private static IReadOnlyList<string> TryGetBodyIds(
        StepGeometryDocument? document,
        IReadOnlyList<int> bodyIndices)
        => document is null
            ? []
            : bodyIndices.Where(index => index >= 0 && index < document.Bodies.Count)
                .Select(index => document.Bodies[index].Id)
                .ToArray();

    private static GeometryWorkerException CreateWorkerException(JsonElement error, string operation)
    {
        var code = error.GetProperty("code").GetString() switch
        {
            "source-unavailable" => GeometryKernelFailureCode.SourceUnavailable,
            "invalid-input" => GeometryKernelFailureCode.InvalidInput,
            "geometry-not-found" => GeometryKernelFailureCode.GeometryNotFound,
            "protocol-mismatch" => GeometryKernelFailureCode.ProtocolMismatch,
            _ => GeometryKernelFailureCode.BackendFailure,
        };
        return new GeometryWorkerException(new GeometryKernelFailure(
            code,
            MapOperation(operation),
            error.GetProperty("message").GetString() ?? "Geometry worker failed.",
            error.TryGetProperty("diagnostic", out var diagnostic) ? diagnostic.GetString() : null,
            error.TryGetProperty("retryable", out var retryable) && retryable.GetBoolean()));
    }

    private static GeometryKernelOperation MapOperation(string operation) => operation switch
    {
        "project" => GeometryKernelOperation.Projection,
        "unfold" => GeometryKernelOperation.Unfold,
        "distortion" => GeometryKernelOperation.Distortion,
        _ => GeometryKernelOperation.Import,
    };

    private static string CreateOutputPath(string prefix, string extension = ".dxf")
    {
        var root = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
        Directory.CreateDirectory(root);
        return Path.Combine(root, $"{prefix}-{Guid.NewGuid():N}{extension}");
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void StopWorker()
    {
        _input?.Dispose();
        _output?.Dispose();
        if (_process is { HasExited: false })
            _process.Kill(entireProcessTree: true);
        _process?.Dispose();
        _process = null;
        _input = null;
        _output = null;
        _protocol = null;
    }

    public void Dispose()
    {
        StopWorker();
        _gate.Dispose();
    }

    private sealed class GeometryWorkerException(GeometryKernelFailure failure) : Exception(failure.Message)
    {
        public GeometryKernelFailure Failure { get; } = failure;
    }
}
