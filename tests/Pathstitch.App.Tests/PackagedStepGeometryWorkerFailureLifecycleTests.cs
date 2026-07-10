using System.Diagnostics;
using System.Runtime.InteropServices;
using Domain.App.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class PackagedStepGeometryWorkerFailureLifecycleTests
{
    [Fact]
    public async Task MalformedStep_ReturnsTypedInvalidInputFailure()
    {
        using var runtime = new FakeWorkerRuntime("invalid-import");
        using var service = CreateService(runtime.Runtime);
        var malformed = runtime.CreateFile("malformed.step", "this is not ISO-10303-21");

        var result = await service.ImportAsync(malformed);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(GeometryKernelFailureCode.InvalidInput, result.Failure.Code);
        Assert.Equal(GeometryKernelOperation.Import, result.Failure.Operation);
        Assert.Contains("Malformed STEP", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkerCrash_NextRequestStartsFreshWorker()
    {
        using var runtime = new FakeWorkerRuntime("crash-once");
        using var service = CreateService(runtime.Runtime);

        await Assert.ThrowsAnyAsync<Exception>(() => service.HandshakeAsync());
        var restarted = await service.HandshakeAsync();

        Assert.Equal(1, restarted.ProtocolVersion);
        Assert.Equal("fake-python-worker", restarted.Backend);
        Assert.True(File.Exists(Path.Combine(runtime.Root, "crashed.marker")));
    }

    [Fact]
    public async Task UnresponsiveWorker_ReturnsTypedTimeoutAndCanRestart()
    {
        using var runtime = new FakeWorkerRuntime("hang");
        using var service = CreateService(runtime.Runtime);
        var input = runtime.CreateFile("valid.step", "ISO-10303-21;END-ISO-10303-21;");
        var timer = Stopwatch.StartNew();

        var result = await service.ImportAsync(input);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(GeometryKernelFailureCode.Timeout, result.Failure.Code);
        Assert.True(result.Failure.IsRetryable);
        Assert.InRange(timer.Elapsed, TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(75));
        runtime.SetMode("normal");
        var restarted = await service.HandshakeAsync();
        Assert.Equal("fake-python-worker", restarted.Backend);
    }

    [Fact]
    public async Task CallerCancellation_StopsWorkerPromptlyAndAllowsRestart()
    {
        using var runtime = new FakeWorkerRuntime("hang");
        using var service = CreateService(runtime.Runtime);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var timer = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.HandshakeAsync(cancellation.Token));

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"Cancellation took {timer.Elapsed.TotalSeconds:F3}s.");
        runtime.SetMode("normal");
        var restarted = await service.HandshakeAsync();
        Assert.Equal("fake-python-worker", restarted.Backend);
    }

    [Fact]
    public async Task ProtocolMismatch_IsRejectedBeforeWorkerIsTrusted()
    {
        using var runtime = new FakeWorkerRuntime("protocol-mismatch");
        using var service = CreateService(runtime.Runtime);

        var result = await service.ImportAsync("protocol-mismatch.step");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(GeometryKernelFailureCode.ProtocolMismatch, result.Failure.Code);
        Assert.Contains("does not match app protocol", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRuntime_ReturnsTypedSourceUnavailableFailure()
    {
        using var service = new PackagedStepGeometryKernelService(
            NullLogger<PackagedStepGeometryKernelService>.Instance,
            new FixedRuntimeResolver(null));

        var result = await service.ImportAsync("missing.step");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(GeometryKernelFailureCode.SourceUnavailable, result.Failure.Code);
        Assert.Contains("app-owned geometry worker runtime is missing", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PackagedStepGeometryKernelService CreateService(GeometryWorkerRuntime runtime)
        => new(
            NullLogger<PackagedStepGeometryKernelService>.Instance,
            new FixedRuntimeResolver(runtime));

    private sealed class FixedRuntimeResolver(GeometryWorkerRuntime? runtime) : IGeometryWorkerRuntimeResolver
    {
        public GeometryWorkerRuntime? Resolve() => runtime;
    }

    private sealed class FakeWorkerRuntime : IDisposable
    {
        private const string WorkerModule = """
import json, os, struct, sys, time

root = os.getcwd()
mode_path = os.path.join(root, "mode.txt")

def read_exact(count):
    data = bytearray()
    while len(data) < count:
        chunk = sys.stdin.buffer.read(count - len(data))
        if not chunk:
            return None
        data.extend(chunk)
    return bytes(data)

def write_frame(value):
    data = json.dumps(value, separators=(",", ":")).encode("utf-8")
    sys.stdout.buffer.write(struct.pack(">I", len(data)) + data)
    sys.stdout.buffer.flush()

while True:
    header = read_exact(4)
    if header is None:
        break
    request = json.loads(read_exact(struct.unpack(">I", header)[0]))
    mode = open(mode_path, encoding="utf-8").read().strip()
    if mode == "hang":
        time.sleep(120)
    if mode == "crash-once" and not os.path.exists(os.path.join(root, "crashed.marker")):
        open(os.path.join(root, "crashed.marker"), "w").close()
        os._exit(17)
    operation = request.get("operation")
    if mode == "invalid-import" and operation == "import":
        response = {"ok": False, "error": {"code": "invalid-input", "message": "Malformed STEP fixture.", "diagnostic": "parser rejected input", "retryable": False}}
    elif operation == "handshake":
        version = 999 if mode == "protocol-mismatch" else 1
        response = {"ok": True, "protocol": {"protocolVersion": version, "backend": "fake-python-worker", "backendVersion": "test", "capabilities": []}}
    else:
        response = {"ok": False, "error": {"code": "invalid-input", "message": "Unsupported fake operation.", "retryable": False}}
    response["id"] = request.get("id")
    response["protocolVersion"] = 1
    write_frame(response)
""";

        public FakeWorkerRuntime(string mode)
        {
            Root = Path.Combine(Path.GetTempPath(), $"pathstitch-fake-worker-{Guid.NewGuid():N}");
            var package = Path.Combine(Root, "pathstitch_core");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "__init__.py"), string.Empty);
            File.WriteAllText(Path.Combine(package, "geometry_worker.py"), WorkerModule);
            SetMode(mode);
            Runtime = new GeometryWorkerRuntime(FindPythonExecutable(), Root);
        }

        public string Root { get; }
        public GeometryWorkerRuntime Runtime { get; }

        public void SetMode(string mode) => File.WriteAllText(Path.Combine(Root, "mode.txt"), mode);

        public string CreateFile(string name, string contents)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private static string FindPythonExecutable()
        {
            var repository = FindRepositoryDirectory();
            var workerRoot = Path.Combine(repository, "artifacts", "geometry-worker", RuntimeInformation.RuntimeIdentifier);
            var packagedPython = OperatingSystem.IsWindows()
                ? Path.Combine(workerRoot, "python.exe")
                : Path.Combine(workerRoot, "bin", "python3.11");
            if (Directory.Exists(workerRoot))
            {
                if (!File.Exists(packagedPython))
                    throw new InvalidOperationException($"Expected packaged test runtime is incomplete: {packagedPython}");
                return packagedPython;
            }

            var executableNames = OperatingSystem.IsWindows()
                ? new[] { "python.exe", "python3.exe" }
                : new[] { "python3", "python" };
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var name in executableNames)
                {
                    var candidate = Path.Combine(directory, name);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            throw new InvalidOperationException(
                "Failure-lifecycle tests require the packaged native worker runtime or a Python 3 interpreter on PATH.");
        }

        private static string FindRepositoryDirectory()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PathstitchCross.slnx")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
