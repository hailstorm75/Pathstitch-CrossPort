using System.Buffers.Binary;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Services;
using Microsoft.Extensions.Logging;

namespace Pathstitch.App.Services;

public sealed class PackagedPdfVectorImportService(
    ILogger<PackagedPdfVectorImportService> logger,
    IGeometryWorkerRuntimeResolver runtimeResolver) : IPdfVectorImportService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Stream? _input;
    private Stream? _output;
    private long _nextRequestId;

    public async Task<string> ConvertToDxfAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("PDF drawing was not found.", sourcePath);

        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Imported");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"pdf-{Guid.NewGuid():N}.dxf");
        try
        {
            var response = await SendAsync(sourcePath, outputPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(response.GetProperty("status").GetString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                var message = response.TryGetProperty("message", out var error)
                    ? error.GetString()
                    : "PDF worker returned an unknown error.";
                throw new InvalidDataException(message);
            }
            if (!File.Exists(outputPath))
                throw new InvalidDataException("PDF worker did not create a DXF drawing.");
            return outputPath;
        }
        catch
        {
            try { File.Delete(outputPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private async Task<JsonElement> SendAsync(string sourcePath, string outputPath, CancellationToken cancellationToken)
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
                op = "import_pdf",
                args = new { input = sourcePath, output = outputPath },
            });
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
            await _input!.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _input.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);

            await ReadExactAsync(_output!, header, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length <= 0 || length > 64 * 1024 * 1024)
                throw new InvalidDataException($"PDF worker returned invalid frame length {length}.");
            var response = new byte[length];
            await ReadExactAsync(_output!, response, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement.Clone();
            if (!root.TryGetProperty("id", out var id) || id.GetInt64() != requestId)
                throw new InvalidDataException("PDF worker response did not match request.");
            return root;
        }
        catch (OperationCanceledException)
        {
            StopWorker();
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF vector import worker failed.");
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
            ?? throw new InvalidOperationException("Packaged PDF vector import runtime was not found.");
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
        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start packaged PDF vector import worker.");
        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                logger.LogDebug("PDF vector import worker: {Message}", eventArgs.Data);
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
                throw new EndOfStreamException("PDF vector import worker closed its output stream.");
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
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
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
