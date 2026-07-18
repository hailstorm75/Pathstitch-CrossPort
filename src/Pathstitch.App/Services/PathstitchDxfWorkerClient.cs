using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pathstitch.App.Services;

public sealed class PathstitchDxfWorkerClient(
    ILogger<PathstitchDxfWorkerClient> logger,
    IGeometryWorkerRuntimeResolver runtimeResolver) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Stream? _input;
    private Stream? _output;
    private long _nextRequestId;

    public async Task<JsonElement> SendAsync(
        string operation,
        object arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(arguments);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureWorker();
            var requestId = Interlocked.Increment(ref _nextRequestId);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                id = requestId,
                module = "dxf_ops",
                op = operation,
                args = arguments,
            });
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
            await _input!.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _input.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);

            await ReadExactAsync(_output!, header, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length <= 0 || length > 64 * 1024 * 1024)
                throw new InvalidDataException($"DXF worker returned invalid frame length {length}.");
            var response = new byte[length];
            await ReadExactAsync(_output!, response, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement.Clone();
            if (!root.TryGetProperty("id", out var id) || id.GetInt64() != requestId)
                throw new InvalidDataException("DXF worker response did not match request.");
            return root;
        }
        catch (OperationCanceledException)
        {
            StopWorker();
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Packaged DXF worker operation {Operation} failed.", operation);
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
            ?? throw new InvalidOperationException(
                "The app-owned drawing conversion runtime is missing. Reinstall Pathstitch with the GeometryWorker payload.");
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
            ?? throw new InvalidOperationException("Could not start the packaged drawing conversion worker.");
        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                logger.LogDebug("Packaged DXF worker: {Message}", eventArgs.Data);
        };
        _input = _process.StandardInput.BaseStream;
        _output = _process.StandardOutput.BaseStream;
        _process.BeginErrorReadLine();
    }

    private static async Task ReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Packaged DXF worker closed its output stream.");
            total += read;
        }
    }

    private void StopWorker()
    {
        _input?.Dispose();
        _output?.Dispose();
        _input = null;
        _output = null;
        if (_process is null)
            return;
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        _process.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        StopWorker();
        _gate.Dispose();
    }
}