using System.Buffers.Binary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging;

namespace Pathstitch.App.Services;

public sealed class PackagedPsdImportService(
    ILogger<PackagedPsdImportService> logger,
    IGeometryWorkerRuntimeResolver runtimeResolver) : IPsdImportService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Stream? _input;
    private Stream? _output;
    private long _nextRequestId;

    public async Task<PsdImportData> ParseAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Photoshop file was not found.", sourcePath);
        var fullPath = Path.GetFullPath(sourcePath);
        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "PsdImport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var response = await SendAsync(fullPath, outputDirectory, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(response.GetProperty("status").GetString(), "ok", StringComparison.OrdinalIgnoreCase)
                || !response.TryGetProperty("data", out var data))
            {
                var message = response.TryGetProperty("message", out var error)
                    ? error.GetString()
                    : "PSD worker returned an unknown error.";
                throw new InvalidDataException(message);
            }

            var rasterLayers = new List<PsdRasterLayer>();
            var vectorLayers = new List<PsdVectorLayer>();
            if (data.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
            {
                foreach (var layer in layers.EnumerateArray())
                {
                    var name = layer.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "Layer" : "Layer";
                    var visible = !layer.TryGetProperty("visible", out var visibleValue) || visibleValue.GetBoolean();
                    var kind = layer.TryGetProperty("kind", out var kindValue) ? kindValue.GetString() : "raster";
                    if (string.Equals(kind, "vector", StringComparison.OrdinalIgnoreCase))
                    {
                        var entities = ParseVectorEntities(layer);
                        if (entities.Count > 0)
                            vectorLayers.Add(new(name, entities, visible));
                        continue;
                    }

                    if (!layer.TryGetProperty("png_path", out var pngPathValue)
                        || pngPathValue.GetString() is not { Length: > 0 } pngPath
                        || !File.Exists(pngPath))
                        continue;
                    var bytes = await File.ReadAllBytesAsync(pngPath, cancellationToken).ConfigureAwait(false);
                    var width = PositiveInt(layer, "width_px");
                    var height = PositiveInt(layer, "height_px");
                    if (width > 0 && height > 0)
                    {
                        rasterLayers.Add(new(
                            name,
                            Convert.ToBase64String(bytes),
                            width,
                            height,
                            FiniteDouble(layer, "center_x"),
                            FiniteDouble(layer, "center_y"),
                            visible));
                    }
                }
            }

            var compositePath = data.GetProperty("composite_png_path").GetString()
                ?? throw new InvalidDataException("PSD worker omitted the flattened composite image.");
            var compositeBytes = await File.ReadAllBytesAsync(compositePath, cancellationToken).ConfigureAwait(false);
            var result = new PsdImportData(
                fullPath,
                PositiveInt(data, "canvas_width"),
                PositiveInt(data, "canvas_height"),
                Convert.ToBase64String(compositeBytes),
                PositiveInt(data, "composite_width"),
                PositiveInt(data, "composite_height"),
                rasterLayers,
                vectorLayers);
            if (result.CanvasWidth <= 0 || result.CanvasHeight <= 0 || result.TotalLayerCount == 0)
                throw new InvalidDataException("PSD worker returned no importable layers.");
            return result;
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IReadOnlyList<PsdVectorEntity> ParseVectorEntities(JsonElement layer)
    {
        var result = new List<PsdVectorEntity>();
        if (!layer.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var entity in entities.EnumerateArray())
        {
            if (!entity.TryGetProperty("vertices", out var vertices) || vertices.ValueKind != JsonValueKind.Array)
                continue;
            var points = vertices.EnumerateArray()
                .Where(vertex => vertex.ValueKind == JsonValueKind.Array && vertex.GetArrayLength() >= 2)
                .Select(vertex => new Editor2DPoint(vertex[0].GetDouble(), vertex[1].GetDouble()))
                .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
                .ToArray();
            if (points.Length >= 2)
                result.Add(new(points, entity.TryGetProperty("closed", out var closed) && closed.GetBoolean()));
        }
        return result;
    }

    private static int PositiveInt(JsonElement value, string property)
    {
        var number = FiniteDouble(value, property);
        return number > 0 && number <= int.MaxValue ? Math.Max(1, (int)Math.Round(number)) : 0;
    }

    private static double FiniteDouble(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var number) || number.ValueKind != JsonValueKind.Number)
            return 0;
        var result = number.GetDouble();
        return double.IsFinite(result) ? result : 0;
    }

    private async Task<JsonElement> SendAsync(string sourcePath, string outputDirectory, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureWorker();
            var requestId = Interlocked.Increment(ref _nextRequestId);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                id = requestId,
                module = "dxf_ops",
                op = "parse_psd",
                args = new { input = sourcePath, out_dir = outputDirectory },
            });
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
            await _input!.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _input.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
            await ReadExactAsync(_output!, header, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length <= 0 || length > 64 * 1024 * 1024)
                throw new InvalidDataException($"PSD worker returned invalid frame length {length}.");
            var response = new byte[length];
            await ReadExactAsync(_output!, response, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement.Clone();
            if (!root.TryGetProperty("id", out var id) || id.GetInt64() != requestId)
                throw new InvalidDataException("PSD worker response did not match request.");
            return root;
        }
        catch (OperationCanceledException)
        {
            StopWorker();
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PSD import worker failed.");
            StopWorker();
            throw;
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
        var runtime = runtimeResolver.Resolve()
            ?? throw new InvalidOperationException("Packaged PSD import runtime was not found.");
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.PythonExecutable,
            Arguments = "-B -m pathstitch_core.worker",
            WorkingDirectory = runtime.ModuleRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["PYTHONPATH"] = runtime.ModuleRoot;
        startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start packaged PSD import worker.");
        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                logger.LogDebug("PSD import worker: {Message}", args.Data);
        };
        _input = _process.StandardInput.BaseStream;
        _output = _process.StandardOutput.BaseStream;
        _process.BeginErrorReadLine();
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("PSD import worker closed its output stream.");
            total += read;
        }
    }

    private void StopWorker()
    {
        _input?.Dispose();
        _output?.Dispose();
        _input = null;
        _output = null;
        if (_process is not null)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        StopWorker();
        _gate.Dispose();
    }
}
