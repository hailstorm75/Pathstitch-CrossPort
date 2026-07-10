using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging;

namespace Pathstitch.App.Services;

public sealed class OpenGeometryEditor3DOperationService(
    ILogger<OpenGeometryEditor3DOperationService> logger,
    OpenGeometryKernelBridge openGeometryKernelBridge,
    IStepGeometryKernelService? stepGeometryKernelService = null) : IEditor3DOperationService
{
    private const int TrianglesPerViewportFace = 750;
    private const double OutputGapMillimeters = 10.0;
    private const double ProjectionKernelValidationWidth = 0.01;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly ILogger<OpenGeometryEditor3DOperationService> _logger = logger;
    private readonly OpenGeometryKernelBridge _openGeometryKernelBridge = openGeometryKernelBridge;
    private readonly IStepGeometryKernelService? _stepGeometryKernelService = stepGeometryKernelService;

    public Task<EditorModelLoadResult> LoadModelAsync(string sourceModelPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsStepPath(sourceModelPath))
            return LoadStepModelAsync(sourceModelPath, cancellationToken);

        if (string.IsNullOrWhiteSpace(sourceModelPath) || !File.Exists(sourceModelPath))
        {
            const string message = "The selected 3D source model could not be found.";
            return Task.FromResult(new EditorModelLoadResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Import, message)));
        }

        try
        {
            var workspace = BuildWorkspace([sourceModelPath], startingBodyIndex: 0);
            return Task.FromResult(BuildLoadResult(workspace, sourceModelPath, $"Loaded {workspace.Bodies.Count} mesh body/bodies from {Path.GetFileName(sourceModelPath)}."));
        }
        catch (NotSupportedException ex)
        {
            return Task.FromResult(new EditorModelLoadResult(
                false,
                ex.Message,
                Failure: CreateFailure(GeometryKernelFailureCode.UnsupportedFormat, GeometryKernelOperation.Import, ex.Message)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load 3D source model {SourceModelPath} through OpenGeometry mesh bridge.", sourceModelPath);
            var message = $"3D model load failed: {ex.Message}";
            return Task.FromResult(new EditorModelLoadResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.BackendFailure, GeometryKernelOperation.Import, message, ex)));
        }
    }

    public async Task<EditorModelLoadResult> LoadModelsAsync(
        IReadOnlyList<string> sourceModelPaths,
        string? existingSourceModelPath = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPaths = sourceModelPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPaths.Length == 0)
        {
            const string message = "No valid 3D source models were selected.";
            return new EditorModelLoadResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.InvalidInput, GeometryKernelOperation.Import, message));
        }

        if (normalizedPaths.Any(IsStepPath)
            && (normalizedPaths.Length != 1 || !string.IsNullOrWhiteSpace(existingSourceModelPath)))
        {
            const string message = "STEP B-rep import currently accepts one document at a time; save the workspace before importing another document.";
            return new EditorModelLoadResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.InvalidInput, GeometryKernelOperation.Import, message));
        }

        if (normalizedPaths.Length == 1
            && (string.IsNullOrWhiteSpace(existingSourceModelPath) || !File.Exists(existingSourceModelPath)))
        {
            return await LoadModelAsync(normalizedPaths[0], cancellationToken).ConfigureAwait(false);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bodies = new List<MeshBody>();
            if (!string.IsNullOrWhiteSpace(existingSourceModelPath) && File.Exists(existingSourceModelPath))
                bodies.AddRange(LoadWorkspace(existingSourceModelPath).Bodies);

            var incoming = BuildWorkspace(normalizedPaths, bodies.Count).Bodies;
            bodies.AddRange(incoming);

            var workspace = new MeshWorkspace(bodies);
            var cachePath = SaveWorkspace(workspace);
            var message = !string.IsNullOrWhiteSpace(existingSourceModelPath)
                ? $"Appended {normalizedPaths.Length} mesh model(s) into the current OpenGeometry workspace."
                : $"Loaded {normalizedPaths.Length} mesh model(s) into the OpenGeometry workspace.";

            return BuildLoadResult(workspace, cachePath, message);
        }
        catch (NotSupportedException ex)
        {
            return new EditorModelLoadResult(
                false,
                ex.Message,
                Failure: CreateFailure(GeometryKernelFailureCode.UnsupportedFormat, GeometryKernelOperation.Import, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to combine {ModelCount} 3D source models through OpenGeometry mesh bridge.", normalizedPaths.Length);
            var message = $"3D model import failed: {ex.Message}";
            return new EditorModelLoadResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.BackendFailure, GeometryKernelOperation.Import, message, ex));
        }
    }

    public Task<EditorFaceDistortionResult> ComputeFaceDistortionAsync(
        string? sourceModelPath,
        SelectedFace3D selectedFace,
        string distortionMode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (sourceModelPath is not null && IsStepPath(sourceModelPath))
        {
            return _stepGeometryKernelService?.ComputeDistortionAsync(sourceModelPath, selectedFace, distortionMode, cancellationToken)
                ?? Task.FromResult(MissingStepRuntimeDistortion());
        }

        if (string.IsNullOrWhiteSpace(sourceModelPath) || !File.Exists(sourceModelPath))
        {
            const string message = "No usable OpenGeometry mesh source asset is available for distortion analysis. Re-import the model or reopen a .stch with embedded 3D data.";
            return Task.FromResult(new EditorFaceDistortionResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Distortion, message)));
        }

        try
        {
            var workspace = LoadWorkspace(sourceModelPath);
            var body = workspace.Bodies.FirstOrDefault(candidate => candidate.BodyIndex == selectedFace.BodyIndex);
            if (body is null)
            {
                var message = $"Body index {selectedFace.BodyIndex} is out of range.";
                return Task.FromResult(new EditorFaceDistortionResult(
                    false,
                    message,
                    Failure: CreateFailure(GeometryKernelFailureCode.GeometryNotFound, GeometryKernelOperation.Distortion, message)));
            }

            var chunk = body.FaceChunks.FirstOrDefault(candidate => candidate.FaceIndex == selectedFace.FaceIndex);
            if (chunk is null)
            {
                var message = $"Face chunk {selectedFace.FaceIndex} was not found.";
                return Task.FromResult(new EditorFaceDistortionResult(
                    false,
                    message,
                    Failure: CreateFailure(GeometryKernelFailureCode.GeometryNotFound, GeometryKernelOperation.Distortion, message)));
            }

            var distortion = CalculateChunkDistortion(body, chunk, distortionMode);
            var payloadJson = JsonSerializer.Serialize(new
            {
                body_index = selectedFace.BodyIndex,
                face_index = selectedFace.FaceIndex,
                distortion,
            }, JsonOptions);

            return Task.FromResult(new EditorFaceDistortionResult(true, "Mesh distortion analysis updated from mesh projection metrics.", payloadJson));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute OpenGeometry mesh distortion for body {BodyIndex}, face chunk {FaceIndex}.", selectedFace.BodyIndex, selectedFace.FaceIndex);
            var message = $"3D distortion analysis failed: {ex.Message}";
            return Task.FromResult(new EditorFaceDistortionResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.BackendFailure, GeometryKernelOperation.Distortion, message, ex)));
        }
    }

    public Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SourceModelPath is not null && IsStepPath(request.SourceModelPath))
        {
            return _stepGeometryKernelService?.UnfoldAsync(request, cancellationToken)
                ?? Task.FromResult(MissingStepRuntimeOperation(GeometryKernelOperation.Unfold));
        }

        if (string.IsNullOrWhiteSpace(request.SourceModelPath) || !File.Exists(request.SourceModelPath))
        {
            const string message = "No usable OpenGeometry mesh source asset is available for flattening. Re-import the model or reopen a .stch with embedded 3D data.";
            return Task.FromResult(new EditorOperationResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Unfold, message)));
        }

        try
        {
            var workspace = LoadWorkspace(request.SourceModelPath);
            var targetBodies = ResolveVisibleBodies(workspace, request.VisibleBodyIndices);
            var selected = request.SelectedFaces.ToHashSet();
            var outputPolylines = new List<DxfPolyline>();
            var currentX = 0.0;
            var flattened = 0;

            foreach (var body in targetBodies)
            {
                var chunks = request.WholeBody
                    ? body.FaceChunks
                    : body.FaceChunks.Where(chunk => selected.Contains(new SelectedFace3D(body.BodyIndex, chunk.FaceIndex))).ToArray();

                foreach (var chunk in chunks)
                {
                    for (var triangleIndex = chunk.StartTriangleIndex; triangleIndex < chunk.StartTriangleIndex + chunk.TriangleCount; triangleIndex++)
                    {
                        var triangle = body.Triangles[triangleIndex];
                        var a = body.Vertices[triangle.A];
                        var b = body.Vertices[triangle.B];
                        var c = body.Vertices[triangle.C];
                        var flattenedTriangle = FlattenTriangle(a, b, c, currentX);
                        if (flattenedTriangle.Count < 3)
                            continue;

                        outputPolylines.Add(new DxfPolyline(flattenedTriangle, IsClosed: true));
                        currentX += TriangleWidth(flattenedTriangle) + OutputGapMillimeters;
                        flattened++;
                    }
                }
            }

            if (outputPolylines.Count == 0)
            {
                var message = request.WholeBody
                    ? "No mesh triangles could be flattened from the visible bodies."
                    : "No selected mesh face chunks could be flattened.";
                return Task.FromResult(new EditorOperationResult(
                    false,
                    message,
                    Failure: CreateFailure(GeometryKernelFailureCode.GeometryNotFound, GeometryKernelOperation.Unfold, message)));
            }

            var outputPath = CreateGeneratedOutputPath("unfold");
            EditorDxfDocument.SaveLwPolylines(outputPath, "UNFOLDED_3D", outputPolylines);

            return Task.FromResult(new EditorOperationResult(
                true,
                request.WholeBody
                    ? $"Flattened {flattened} mesh triangle(s) from the visible bodies through the OpenGeometry mesh bridge."
                    : $"Flattened {flattened} selected mesh triangle(s) through the OpenGeometry mesh bridge.",
                outputPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unfold OpenGeometry mesh source {SourceModelPath}.", request.SourceModelPath);
            var message = $"3D flattening failed: {ex.Message}";
            return Task.FromResult(new EditorOperationResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.BackendFailure, GeometryKernelOperation.Unfold, message, ex)));
        }
    }

    public async Task<EditorOperationResult> ProjectEdgesAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SourceModelPath is not null && IsStepPath(request.SourceModelPath))
        {
            return _stepGeometryKernelService is null
                ? MissingStepRuntimeOperation(GeometryKernelOperation.Projection)
                : await _stepGeometryKernelService.ProjectAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(request.SourceModelPath) || !File.Exists(request.SourceModelPath))
        {
            const string message = "No usable OpenGeometry mesh source asset is available for projection. Re-import the model or reopen a .stch with embedded 3D data.";
            return new EditorOperationResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Projection, message));
        }

        try
        {
            var workspace = LoadWorkspace(request.SourceModelPath);
            var basis = ResolveProjectionBasis(workspace, request);
            var targetBodies = ResolveVisibleBodies(workspace, request.VisibleBodyIndices);
            if (targetBodies.Count == 0)
            {
                const string message = "No visible mesh bodies found to project.";
                return new EditorOperationResult(
                    false,
                    message,
                    Failure: CreateFailure(GeometryKernelFailureCode.GeometryNotFound, GeometryKernelOperation.Projection, message));
            }

            var polylines = new List<DxfPolyline>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var body in targetBodies)
            {
                var offset = request.BodyOffsets.FirstOrDefault(candidate => candidate.BodyIndex == body.BodyIndex);
                foreach (var triangle in body.Triangles)
                {
                    var a = ApplyOffset(body.Vertices[triangle.A], offset);
                    var b = ApplyOffset(body.Vertices[triangle.B], offset);
                    var c = ApplyOffset(body.Vertices[triangle.C], offset);
                    AddProjectedEdge(polylines, seenKeys, basis, a, b);
                    AddProjectedEdge(polylines, seenKeys, basis, b, c);
                    AddProjectedEdge(polylines, seenKeys, basis, c, a);
                }
            }

            if (polylines.Count == 0)
            {
                const string message = "No projectable mesh edges found.";
                return new EditorOperationResult(
                    false,
                    message,
                    Failure: CreateFailure(GeometryKernelFailureCode.GeometryNotFound, GeometryKernelOperation.Projection, message));
            }

            var kernelOffsetResult = await _openGeometryKernelBridge
                .TryOffsetPolylinesAsync(polylines, ProjectionKernelValidationWidth, cancellationToken)
                .ConfigureAwait(false);

            if (!kernelOffsetResult.IsSuccess)
            {
                return new EditorOperationResult(
                    false,
                    $"OpenGeometry kernel projection failed: {kernelOffsetResult.Error ?? "unknown OpenGeometry worker failure"}",
                    Failure: CreateFailure(
                        GeometryKernelFailureCode.BackendFailure,
                        GeometryKernelOperation.Projection,
                        kernelOffsetResult.Error ?? "Unknown OpenGeometry worker failure."));
            }

            var kernelPolylines = ConvertOffsetRegionsToPolylines(kernelOffsetResult);
            if (kernelPolylines.Count == 0)
            {
                return new EditorOperationResult(
                    false,
                    "OpenGeometry kernel projection produced no usable analytic regions.",
                    Failure: CreateFailure(
                        GeometryKernelFailureCode.GeometryNotFound,
                        GeometryKernelOperation.Projection,
                        "OpenGeometry kernel projection produced no usable analytic regions."));
            }

            var outputPath = CreateGeneratedOutputPath("projected");
            EditorDxfDocument.SaveLwPolylines(
                outputPath,
                "OPEN_GEOMETRY_OFFSET",
                ArrangePolylinesForOutput(kernelPolylines));

            return new EditorOperationResult(
                true,
                $"Projected {polylines.Count} mesh edge(s) from the visible workspace bodies onto {DescribeProjectionBasis(request)} through the OpenGeometry kernel. OpenGeometry wrote {kernelOffsetResult.Regions.Count} analytic region(s) as {kernelPolylines.Count} closed DXF loop(s).",
                outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to project OpenGeometry mesh source {SourceModelPath}.", request.SourceModelPath);
            var message = $"3D projection failed: {ex.Message}";
            return new EditorOperationResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.BackendFailure, GeometryKernelOperation.Projection, message, ex));
        }
    }

    private async Task<EditorModelLoadResult> LoadStepModelAsync(string sourceModelPath, CancellationToken cancellationToken)
    {
        if (_stepGeometryKernelService is null)
        {
            const string message = "The app-owned STEP geometry worker runtime is not installed.";
            return new EditorModelLoadResult(
                false,
                message,
                Failure: CreateFailure(GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Import, message));
        }

        var result = await _stepGeometryKernelService.ImportAsync(sourceModelPath, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? new EditorModelLoadResult(
                true,
                result.Message,
                sourceModelPath,
                result.ViewportJson,
                result.ViewportBodies,
                StepTopology: result.Document)
            : new EditorModelLoadResult(false, result.Message, Failure: result.Failure);
    }

    private static bool IsStepPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".step", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".stp", StringComparison.OrdinalIgnoreCase);
    }

    private static EditorOperationResult MissingStepRuntimeOperation(GeometryKernelOperation operation)
    {
        const string message = "The app-owned STEP geometry worker runtime is not installed.";
        return new EditorOperationResult(
            false,
            message,
            Failure: CreateFailure(GeometryKernelFailureCode.SourceUnavailable, operation, message));
    }

    private static EditorFaceDistortionResult MissingStepRuntimeDistortion()
    {
        const string message = "The app-owned STEP geometry worker runtime is not installed.";
        return new EditorFaceDistortionResult(
            false,
            message,
            Failure: CreateFailure(GeometryKernelFailureCode.SourceUnavailable, GeometryKernelOperation.Distortion, message));
    }

    private static GeometryKernelFailure CreateFailure(
        GeometryKernelFailureCode code,
        GeometryKernelOperation operation,
        string message,
        Exception? exception = null)
        => new(code, operation, message, exception?.GetType().FullName, IsRetryable: code is GeometryKernelFailureCode.BackendFailure);

    private static EditorModelLoadResult BuildLoadResult(MeshWorkspace workspace, string sourceModelPath, string message)
    {
        var scene = BuildViewportScene(workspace);
        return new EditorModelLoadResult(
            true,
            message,
            SourceModelPath: sourceModelPath,
            ViewportJson: scene.ViewportJson,
            Bodies: scene.Bodies);
    }

    private static MeshWorkspace BuildWorkspace(IReadOnlyList<string> sourceModelPaths, int startingBodyIndex)
    {
        var bodies = new List<MeshBody>(sourceModelPaths.Count);
        for (var i = 0; i < sourceModelPaths.Count; i++)
        {
            var path = sourceModelPaths[i];
            var bodyIndex = startingBodyIndex + i;
            var mesh = LoadMesh(path);
            bodies.Add(MeshBody.FromMesh(bodyIndex, Path.GetFileNameWithoutExtension(path), path, mesh));
        }

        return new MeshWorkspace(bodies);
    }

    private static MeshWorkspace LoadWorkspace(string sourceModelPath)
    {
        if (Path.GetExtension(sourceModelPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var workspace = JsonSerializer.Deserialize<MeshWorkspace>(File.ReadAllText(sourceModelPath), JsonOptions);
            if (workspace is not null)
                return workspace;
        }

        return BuildWorkspace([sourceModelPath], startingBodyIndex: 0);
    }

    private static string SaveWorkspace(MeshWorkspace workspace)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"mesh_workspace_{Guid.NewGuid():N}.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(workspace, JsonOptions));
        return outputPath;
    }

    private static MeshData LoadMesh(string sourceModelPath)
        => Path.GetExtension(sourceModelPath).ToLowerInvariant() switch
        {
            ".obj" => LoadObjMesh(sourceModelPath),
            ".stl" => LoadStlMesh(sourceModelPath),
            ".step" or ".stp" => throw new NotSupportedException("STEP import is not available through OpenGeometry in this desktop bridge yet. Convert STEP files to OBJ or STL meshes before import until OpenGeometry exposes STEP import."),
            _ => throw new NotSupportedException($"Unsupported 3D source model extension: {Path.GetExtension(sourceModelPath)}."),
        };

    private static MeshData LoadObjMesh(string sourceModelPath)
    {
        var vertices = new List<Vector3d>();
        var triangles = new List<TriangleIndex>();

        foreach (var line in File.ReadLines(sourceModelPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            if (string.Equals(parts[0], "v", StringComparison.OrdinalIgnoreCase) && parts.Length >= 4)
            {
                if (TryParseDouble(parts[1], out var x)
                    && TryParseDouble(parts[2], out var y)
                    && TryParseDouble(parts[3], out var z))
                {
                    vertices.Add(new Vector3d(x, y, z));
                }

                continue;
            }

            if (!string.Equals(parts[0], "f", StringComparison.OrdinalIgnoreCase))
                continue;

            var indices = parts
                .Skip(1)
                .Select(token => ParseObjVertexIndex(token, vertices.Count))
                .Where(index => index >= 0 && index < vertices.Count)
                .ToArray();

            for (var i = 1; i + 1 < indices.Length; i++)
                triangles.Add(new TriangleIndex(indices[0], indices[i], indices[i + 1]));
        }

        if (vertices.Count == 0 || triangles.Count == 0)
            throw new InvalidOperationException($"No valid triangular mesh could be parsed from OBJ file {Path.GetFileName(sourceModelPath)}.");

        return new MeshData(vertices, triangles);
    }

    private static MeshData LoadStlMesh(string sourceModelPath)
    {
        var bytes = File.ReadAllBytes(sourceModelPath);
        if (LooksLikeBinaryStl(bytes))
            return LoadBinaryStlMesh(bytes, sourceModelPath);

        return LoadAsciiStlMesh(sourceModelPath);
    }

    private static bool LooksLikeBinaryStl(byte[] bytes)
    {
        if (bytes.Length < 84)
            return false;

        var triangleCount = BitConverter.ToUInt32(bytes, 80);
        return 84L + ((long)triangleCount * 50L) == bytes.Length;
    }

    private static MeshData LoadBinaryStlMesh(byte[] bytes, string sourceModelPath)
    {
        var vertices = new List<Vector3d>();
        var triangles = new List<TriangleIndex>();

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadBytes(80);
        var triangleCount = reader.ReadUInt32();
        for (var i = 0; i < triangleCount; i++)
        {
            _ = reader.ReadSingle();
            _ = reader.ReadSingle();
            _ = reader.ReadSingle();

            var start = vertices.Count;
            vertices.Add(ReadStlVector(reader));
            vertices.Add(ReadStlVector(reader));
            vertices.Add(ReadStlVector(reader));
            triangles.Add(new TriangleIndex(start, start + 1, start + 2));
            _ = reader.ReadUInt16();
        }

        if (triangles.Count == 0)
            throw new InvalidOperationException($"No triangles could be parsed from STL file {Path.GetFileName(sourceModelPath)}.");

        return new MeshData(vertices, triangles);
    }

    private static MeshData LoadAsciiStlMesh(string sourceModelPath)
    {
        var vertices = new List<Vector3d>();
        var triangles = new List<TriangleIndex>();
        var pending = new List<Vector3d>(3);

        foreach (var line in File.ReadLines(sourceModelPath))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || !string.Equals(parts[0], "vertex", StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryParseDouble(parts[1], out var x)
                && TryParseDouble(parts[2], out var y)
                && TryParseDouble(parts[3], out var z))
            {
                pending.Add(new Vector3d(x, y, z));
            }

            if (pending.Count != 3)
                continue;

            var start = vertices.Count;
            vertices.AddRange(pending);
            triangles.Add(new TriangleIndex(start, start + 1, start + 2));
            pending.Clear();
        }

        if (triangles.Count == 0)
            throw new InvalidOperationException($"No triangles could be parsed from STL file {Path.GetFileName(sourceModelPath)}.");

        return new MeshData(vertices, triangles);
    }

    private static Vector3d ReadStlVector(BinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static int ParseObjVertexIndex(string token, int vertexCount)
    {
        var indexToken = token.Split('/')[0];
        if (!int.TryParse(indexToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawIndex))
            return -1;

        return rawIndex > 0 ? rawIndex - 1 : vertexCount + rawIndex;
    }

    private static OpenGeometryViewportScene BuildViewportScene(MeshWorkspace workspace)
    {
        var viewportBodies = new List<ViewportBodyPayload>(workspace.Bodies.Count);
        var bodies = new List<Body3D>(workspace.Bodies.Count);

        var min = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var max = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };

        foreach (var body in workspace.Bodies)
        {
            var viewportFaces = new List<ViewportFacePayload>(body.FaceChunks.Count);
            var faceSummaries = new List<Face3D>(body.FaceChunks.Count);

            foreach (var chunk in body.FaceChunks)
            {
                var vertices = new List<double>(chunk.TriangleCount * 9);
                var indices = new List<int>(chunk.TriangleCount * 3);
                var vertexOffset = 0;

                for (var triangleIndex = chunk.StartTriangleIndex; triangleIndex < chunk.StartTriangleIndex + chunk.TriangleCount; triangleIndex++)
                {
                    var triangle = body.Triangles[triangleIndex];
                    foreach (var point in new[] { body.Vertices[triangle.A], body.Vertices[triangle.B], body.Vertices[triangle.C] })
                    {
                        vertices.Add(point.X);
                        vertices.Add(point.Y);
                        vertices.Add(point.Z);
                        ExpandBounds(point, min, max);
                    }

                    indices.Add(vertexOffset);
                    indices.Add(vertexOffset + 1);
                    indices.Add(vertexOffset + 2);
                    vertexOffset += 3;
                }

                var area = CalculateChunkArea(body, chunk);
                viewportFaces.Add(new ViewportFacePayload(chunk.FaceIndex, "Mesh", area, vertices, indices));
                faceSummaries.Add(new Face3D(chunk.FaceIndex, "Mesh", area) { BodyIndex = body.BodyIndex });
            }

            viewportBodies.Add(new ViewportBodyPayload(body.BodyIndex, body.Name, viewportFaces, []));
            bodies.Add(new Body3D(body.BodyIndex, body.Name, faceSummaries) { Visible = true });
        }

        if (!double.IsFinite(min[0]))
        {
            min = [0.0, 0.0, 0.0];
            max = [0.0, 0.0, 0.0];
        }

        var viewportJson = JsonSerializer.Serialize(new
        {
            bodies = viewportBodies,
            bbox = new
            {
                min,
                max,
                center = new[]
                {
                    (min[0] + max[0]) / 2.0,
                    (min[1] + max[1]) / 2.0,
                    (min[2] + max[2]) / 2.0,
                },
            },
        }, JsonOptions);

        return new OpenGeometryViewportScene(viewportJson, bodies);
    }

    private static double CalculateChunkArea(MeshBody body, MeshFaceChunk chunk)
    {
        var area = 0.0;
        for (var triangleIndex = chunk.StartTriangleIndex; triangleIndex < chunk.StartTriangleIndex + chunk.TriangleCount; triangleIndex++)
        {
            var triangle = body.Triangles[triangleIndex];
            area += TriangleArea(body.Vertices[triangle.A], body.Vertices[triangle.B], body.Vertices[triangle.C]);
        }

        return area;
    }

    private static IReadOnlyList<MeshBody> ResolveVisibleBodies(MeshWorkspace workspace, IReadOnlyList<int> visibleBodyIndices)
    {
        var visible = visibleBodyIndices.ToHashSet();
        return workspace.Bodies
            .Where(body => visible.Count == 0 || visible.Contains(body.BodyIndex))
            .ToArray();
    }

    private static ProjectionBasis ResolveProjectionBasis(MeshWorkspace workspace, EditorProjectionRequest request)
    {
        if (!string.Equals(request.PlaneType, "face", StringComparison.OrdinalIgnoreCase))
            return ProjectionBasis.FromPlaneName(request.PlaneType, request.Offset);

        if (request.FaceBodyIndex is null || request.FaceIndex is null)
            throw new InvalidOperationException("Select one mesh face before running face-based projection.");

        var body = workspace.Bodies.FirstOrDefault(candidate => candidate.BodyIndex == request.FaceBodyIndex.Value);
        if (body is null)
            throw new InvalidOperationException($"Body {request.FaceBodyIndex.Value + 1} was not found for face-based projection.");

        var chunk = body.FaceChunks.FirstOrDefault(candidate => candidate.FaceIndex == request.FaceIndex.Value);
        if (chunk is null)
            throw new InvalidOperationException($"Face chunk {request.FaceIndex.Value} was not found for face-based projection.");

        var bodyOffset = request.BodyOffsets.FirstOrDefault(candidate => candidate.BodyIndex == body.BodyIndex);
        var normal = CalculateAreaWeightedNormal(body, chunk);
        if (normal.Length <= 1e-9)
            throw new InvalidOperationException("The selected mesh face cannot define a projection plane because its normal is degenerate.");

        normal = Normalize(normal);
        var centroid = ApplyOffset(CalculateChunkCentroid(body, chunk), bodyOffset);
        var origin = centroid + (normal * request.Offset);
        var uAxis = BuildPlaneUAxis(normal);
        var vAxis = Normalize(Vector3d.Cross(normal, uAxis));
        if (vAxis.Length <= 1e-9)
            throw new InvalidOperationException("The selected mesh face cannot define a stable projection basis.");

        return new ProjectionBasis(origin, uAxis, vAxis);
    }

    private static string DescribeProjectionBasis(EditorProjectionRequest request)
        => string.Equals(request.PlaneType, "face", StringComparison.OrdinalIgnoreCase)
            ? $"mesh face B{(request.FaceBodyIndex ?? 0) + 1}:F{request.FaceIndex ?? 0}"
            : $"{(string.IsNullOrWhiteSpace(request.PlaneType) ? "XY" : request.PlaneType.ToUpperInvariant())} origin plane";

    private static IReadOnlyList<DxfPolyline> ArrangePolylinesForOutput(IReadOnlyList<DxfPolyline> polylines)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;

        foreach (var point in polylines.SelectMany(polyline => polyline.Points))
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
        }

        if (!double.IsFinite(minX) || !double.IsFinite(minY))
            return polylines;

        return polylines
            .Select(polyline => new DxfPolyline(
                polyline.Points.Select(point => new DxfPoint(point.X - minX, point.Y - minY)).ToArray(),
                polyline.IsClosed))
            .ToArray();
    }

    private static IReadOnlyList<DxfPolyline> ConvertOffsetRegionsToPolylines(OpenGeometryOffsetResult offsetResult)
    {
        if (offsetResult.Regions.Count == 0)
            return [];

        var polylines = new List<DxfPolyline>();
        foreach (var region in offsetResult.Regions)
        {
            AddOffsetLoop(polylines, region.Outer);
            foreach (var hole in region.Holes)
                AddOffsetLoop(polylines, hole);
        }

        return polylines;
    }

    private static void AddOffsetLoop(ICollection<DxfPolyline> polylines, IReadOnlyList<OpenGeometryPoint> points)
    {
        if (points.Count < 3)
            return;

        var dxfPoints = points
            .Where(static point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .Select(static point => new DxfPoint(point.X, point.Y))
            .ToArray();

        if (dxfPoints.Length < 3)
            return;

        polylines.Add(new DxfPolyline(dxfPoints, IsClosed: true));
    }

    private static void AddProjectedEdge(
        ICollection<DxfPolyline> polylines,
        ISet<string> seenKeys,
        ProjectionBasis basis,
        Vector3d start,
        Vector3d end)
    {
        var projectedStart = basis.Project(start);
        var projectedEnd = basis.Project(end);
        if (Distance(projectedStart, projectedEnd) <= 1e-6)
            return;

        var forward = $"{RoundKey(projectedStart.X)},{RoundKey(projectedStart.Y)};{RoundKey(projectedEnd.X)},{RoundKey(projectedEnd.Y)}";
        var reverse = $"{RoundKey(projectedEnd.X)},{RoundKey(projectedEnd.Y)};{RoundKey(projectedStart.X)},{RoundKey(projectedStart.Y)}";
        var key = string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse;
        if (!seenKeys.Add(key))
            return;

        polylines.Add(new DxfPolyline([projectedStart, projectedEnd], IsClosed: false));
    }

    private static Vector3d ApplyOffset(Vector3d value, BodyOffset3D? offset)
        => offset is null
            ? value
            : new Vector3d(value.X + offset.X, value.Y + offset.Y, value.Z + offset.Z);

    private static IReadOnlyList<double> CalculateChunkDistortion(
        MeshBody body,
        MeshFaceChunk chunk,
        string distortionMode)
    {
        if (chunk.TriangleCount <= 0)
            return [];

        var normal = CalculateAreaWeightedNormal(body, chunk);
        if (normal.Length <= 1e-9)
            return Enumerable.Repeat(1.0, Math.Max(0, chunk.TriangleCount * 3)).ToArray();

        normal = Normalize(normal);
        var origin = CalculateChunkCentroid(body, chunk);
        var uAxis = BuildPlaneUAxis(normal);
        var vAxis = Normalize(Vector3d.Cross(normal, uAxis));

        var values = new List<double>(chunk.TriangleCount * 3);
        for (var triangleIndex = chunk.StartTriangleIndex; triangleIndex < chunk.StartTriangleIndex + chunk.TriangleCount; triangleIndex++)
        {
            var triangle = body.Triangles[triangleIndex];
            var a = body.Vertices[triangle.A];
            var b = body.Vertices[triangle.B];
            var c = body.Vertices[triangle.C];
            var distortion = CalculateTriangleProjectionDistortion(
                a,
                b,
                c,
                ProjectToPlane(a, origin, uAxis, vAxis),
                ProjectToPlane(b, origin, uAxis, vAxis),
                ProjectToPlane(c, origin, uAxis, vAxis),
                distortionMode);

            values.Add(distortion);
            values.Add(distortion);
            values.Add(distortion);
        }

        return values;
    }

    private static Vector3d CalculateAreaWeightedNormal(MeshBody body, MeshFaceChunk chunk)
    {
        var normal = new Vector3d(0.0, 0.0, 0.0);
        for (var triangleIndex = chunk.StartTriangleIndex; triangleIndex < chunk.StartTriangleIndex + chunk.TriangleCount; triangleIndex++)
        {
            var triangle = body.Triangles[triangleIndex];
            var a = body.Vertices[triangle.A];
            var b = body.Vertices[triangle.B];
            var c = body.Vertices[triangle.C];
            normal += Vector3d.Cross(b - a, c - a);
        }

        return normal;
    }

    private static Vector3d CalculateChunkCentroid(MeshBody body, MeshFaceChunk chunk)
    {
        var sum = new Vector3d(0.0, 0.0, 0.0);
        var count = 0;
        for (var triangleIndex = chunk.StartTriangleIndex; triangleIndex < chunk.StartTriangleIndex + chunk.TriangleCount; triangleIndex++)
        {
            var triangle = body.Triangles[triangleIndex];
            sum += body.Vertices[triangle.A];
            sum += body.Vertices[triangle.B];
            sum += body.Vertices[triangle.C];
            count += 3;
        }

        return count == 0 ? sum : sum / count;
    }

    private static Vector3d BuildPlaneUAxis(Vector3d normal)
    {
        var reference = Math.Abs(normal.Z) < 0.9
            ? new Vector3d(0.0, 0.0, 1.0)
            : new Vector3d(0.0, 1.0, 0.0);
        var axis = Vector3d.Cross(reference, normal);
        return axis.Length <= 1e-9
            ? new Vector3d(1.0, 0.0, 0.0)
            : Normalize(axis);
    }

    private static DxfPoint ProjectToPlane(Vector3d point, Vector3d origin, Vector3d uAxis, Vector3d vAxis)
    {
        var delta = point - origin;
        return new DxfPoint(Dot(delta, uAxis), Dot(delta, vAxis));
    }

    private static double CalculateTriangleProjectionDistortion(
        Vector3d a,
        Vector3d b,
        Vector3d c,
        DxfPoint projectedA,
        DxfPoint projectedB,
        DxfPoint projectedC,
        string distortionMode)
    {
        var ab = Distance(a, b);
        var ac = Distance(a, c);
        var bc = Distance(b, c);
        var projectedAb = Distance(projectedA, projectedB);
        var projectedAc = Distance(projectedA, projectedC);
        var projectedBc = Distance(projectedB, projectedC);

        if (ab <= 1e-9 || ac <= 1e-9 || bc <= 1e-9)
            return 1.0;

        var edgeDistortion = Math.Max(
            RelativeDelta(ab, projectedAb),
            Math.Max(RelativeDelta(ac, projectedAc), RelativeDelta(bc, projectedBc)));
        var areaDistortion = RelativeDelta(TriangleArea(a, b, c), TriangleArea(projectedA, projectedB, projectedC));
        var angleDistortion = MaxAngleDelta(TriangleAngles(ab, ac, bc), TriangleAngles(projectedAb, projectedAc, projectedBc)) / Math.PI;

        var value = distortionMode.ToLowerInvariant() switch
        {
            "equal-area" => areaDistortion,
            "equidistant" => edgeDistortion,
            "balanced" => (areaDistortion + edgeDistortion + angleDistortion) / 3.0,
            _ => angleDistortion,
        };

        return Math.Clamp(value, 0.0, 1.0);
    }

    private static IReadOnlyList<DxfPoint> FlattenTriangle(Vector3d a, Vector3d b, Vector3d c, double xOffset)
    {
        var ab = Distance(a, b);
        var ac = Distance(a, c);
        var bc = Distance(b, c);
        if (ab <= 1e-9 || ac <= 1e-9 || bc <= 1e-9)
            return [];

        var x = ((ac * ac) + (ab * ab) - (bc * bc)) / (2.0 * ab);
        var ySquared = Math.Max(0.0, (ac * ac) - (x * x));
        var y = Math.Sqrt(ySquared);
        return
        [
            new DxfPoint(xOffset, 0.0),
            new DxfPoint(xOffset + ab, 0.0),
            new DxfPoint(xOffset + x, y),
        ];
    }

    private static double TriangleWidth(IReadOnlyList<DxfPoint> points)
        => points.Count == 0 ? 0.0 : points.Max(point => point.X) - points.Min(point => point.X);

    private static double TriangleArea(Vector3d a, Vector3d b, Vector3d c)
        => Vector3d.Cross(b - a, c - a).Length / 2.0;

    private static double TriangleArea(DxfPoint a, DxfPoint b, DxfPoint c)
        => Math.Abs(((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X))) / 2.0;

    private static double Distance(Vector3d left, Vector3d right)
        => (left - right).Length;

    private static double Distance(DxfPoint left, DxfPoint right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static Vector3d Normalize(Vector3d value)
        => value.Length <= 1e-9 ? new Vector3d(0.0, 0.0, 0.0) : value / value.Length;

    private static double Dot(Vector3d left, Vector3d right)
        => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static double RelativeDelta(double expected, double actual)
        => expected <= 1e-9 ? 0.0 : Math.Abs(actual - expected) / expected;

    private static double[] TriangleAngles(double ab, double ac, double bc)
        =>
        [
            TriangleAngle(ab, ac, bc),
            TriangleAngle(ab, bc, ac),
            TriangleAngle(ac, bc, ab),
        ];

    private static double TriangleAngle(double adjacentA, double adjacentB, double opposite)
    {
        if (adjacentA <= 1e-9 || adjacentB <= 1e-9)
            return 0.0;

        var cosine = ((adjacentA * adjacentA) + (adjacentB * adjacentB) - (opposite * opposite)) / (2.0 * adjacentA * adjacentB);
        return Math.Acos(Math.Clamp(cosine, -1.0, 1.0));
    }

    private static double MaxAngleDelta(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        var count = Math.Min(expected.Count, actual.Count);
        var maximum = 0.0;
        for (var i = 0; i < count; i++)
            maximum = Math.Max(maximum, Math.Abs(actual[i] - expected[i]));

        return maximum;
    }

    private static void ExpandBounds(Vector3d point, double[] min, double[] max)
    {
        min[0] = Math.Min(min[0], point.X);
        min[1] = Math.Min(min[1], point.Y);
        min[2] = Math.Min(min[2], point.Z);
        max[0] = Math.Max(max[0], point.X);
        max[1] = Math.Max(max[1], point.Y);
        max[2] = Math.Max(max[2], point.Z);
    }

    private static string CreateGeneratedOutputPath(string prefix)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
        Directory.CreateDirectory(outputDirectory);
        return Path.Combine(outputDirectory, $"{prefix}_{Guid.NewGuid():N}.dxf");
    }

    private static string RoundKey(double value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

    private static bool TryParseDouble(string raw, out double value)
        => double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private sealed record OpenGeometryViewportScene(string ViewportJson, IReadOnlyList<Body3D> Bodies);

    private sealed record ViewportBodyPayload(
        int body_index,
        string name,
        IReadOnlyList<ViewportFacePayload> faces,
        IReadOnlyList<ViewportEdgePayload> edges);

    private sealed record ViewportFacePayload(
        int face_index,
        string type,
        double area,
        IReadOnlyList<double> vertices,
        IReadOnlyList<int> indices);

    private sealed record ViewportEdgePayload(
        int edge_index,
        IReadOnlyList<double> vertices,
        IReadOnlyList<int> faces);

    private sealed record MeshWorkspace([property: JsonPropertyName("bodies")] IReadOnlyList<MeshBody> Bodies);

    private sealed record MeshBody(
        [property: JsonPropertyName("body_index")] int BodyIndex,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("source_path")] string SourcePath,
        [property: JsonPropertyName("vertices")] IReadOnlyList<Vector3d> Vertices,
        [property: JsonPropertyName("triangles")] IReadOnlyList<TriangleIndex> Triangles,
        [property: JsonPropertyName("face_chunks")] IReadOnlyList<MeshFaceChunk> FaceChunks)
    {
        public static MeshBody FromMesh(int bodyIndex, string name, string sourcePath, MeshData mesh)
        {
            var chunks = new List<MeshFaceChunk>();
            var faceIndex = 0;
            for (var start = 0; start < mesh.Triangles.Count; start += TrianglesPerViewportFace)
            {
                chunks.Add(new MeshFaceChunk(
                    FaceIndex: faceIndex++,
                    StartTriangleIndex: start,
                    TriangleCount: Math.Min(TrianglesPerViewportFace, mesh.Triangles.Count - start)));
            }

            return new MeshBody(bodyIndex, string.IsNullOrWhiteSpace(name) ? $"Body {bodyIndex + 1}" : name, sourcePath, mesh.Vertices, mesh.Triangles, chunks);
        }
    }

    private sealed record MeshData(IReadOnlyList<Vector3d> Vertices, IReadOnlyList<TriangleIndex> Triangles);

    private sealed record MeshFaceChunk(
        [property: JsonPropertyName("face_index")] int FaceIndex,
        [property: JsonPropertyName("start_triangle_index")] int StartTriangleIndex,
        [property: JsonPropertyName("triangle_count")] int TriangleCount);

    private readonly record struct TriangleIndex(
        [property: JsonPropertyName("a")] int A,
        [property: JsonPropertyName("b")] int B,
        [property: JsonPropertyName("c")] int C);

    private readonly record struct Vector3d(
        [property: JsonPropertyName("x")] double X,
        [property: JsonPropertyName("y")] double Y,
        [property: JsonPropertyName("z")] double Z)
    {
        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public static Vector3d Cross(Vector3d left, Vector3d right)
            => new(
                (left.Y * right.Z) - (left.Z * right.Y),
                (left.Z * right.X) - (left.X * right.Z),
                (left.X * right.Y) - (left.Y * right.X));

        public static Vector3d operator -(Vector3d left, Vector3d right)
            => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static Vector3d operator +(Vector3d left, Vector3d right)
            => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        public static Vector3d operator *(Vector3d left, double right)
            => new(left.X * right, left.Y * right, left.Z * right);

        public static Vector3d operator /(Vector3d left, double right)
            => new(left.X / right, left.Y / right, left.Z / right);
    }

    private readonly record struct ProjectionBasis(Vector3d Origin, Vector3d UAxis, Vector3d VAxis)
    {
        public static ProjectionBasis FromPlaneName(string? planeName, double offset)
            => planeName?.ToUpperInvariant() switch
            {
                "XZ" => new ProjectionBasis(new Vector3d(0.0, offset, 0.0), new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 0.0, 1.0)),
                "YZ" => new ProjectionBasis(new Vector3d(offset, 0.0, 0.0), new Vector3d(0.0, 1.0, 0.0), new Vector3d(0.0, 0.0, 1.0)),
                "XY" or null or "" => new ProjectionBasis(new Vector3d(0.0, 0.0, offset), new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 1.0, 0.0)),
                _ => throw new NotSupportedException($"Unsupported projection plane type: {planeName}."),
            };

        public DxfPoint Project(Vector3d point)
        {
            var delta = point - Origin;
            return new DxfPoint(Dot(delta, UAxis), Dot(delta, VAxis));
        }

        private static double Dot(Vector3d left, Vector3d right)
            => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }
}
