using System.Diagnostics;
using System.Text.Json;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class PackagedOpenGeometryRuntimeTests
{
    [Fact]
    public async Task PackagedWorker_RunsGeometryCommandWithNoNodeOrNpmOnPath()
    {
        var runtime = OpenGeometryPackagedRuntime.Resolve();
        Assert.NotNull(runtime);
        Assert.True(runtime.IsComplete);
        Assert.True(Path.IsPathFullyQualified(runtime.NodeExecutablePath));
        Assert.StartsWith(
            Path.GetFullPath(AppContext.BaseDirectory),
            runtime.NodeExecutablePath,
            StringComparison.OrdinalIgnoreCase);

        using var workspace = TemporaryWorkspace.Create();
        var requestPath = workspace.Write(
            "request.json",
            """
            {
              "command": "offset-polylines",
              "width": 1.0,
              "miterLimit": 4.0,
              "polylines": [
                {
                  "points": [{ "x": 0, "y": 0 }, { "x": 10, "y": 0 }],
                  "isClosed": false
                }
              ]
            }
            """);
        var responsePath = Path.Combine(workspace.DirectoryPath, "response.json");
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.NodeExecutablePath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add(runtime.WorkerPath);
        startInfo.ArgumentList.Add(requestPath);
        startInfo.ArgumentList.Add(responsePath);
        startInfo.Environment["PATH"] = string.Empty;
        startInfo.Environment.Remove("NODE_PATH");
        startInfo.Environment.Remove("NPM_CONFIG_PREFIX");

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.True(File.Exists(responsePath), await standardError);
        using var response = JsonDocument.Parse(await File.ReadAllTextAsync(responsePath));
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(response.RootElement.GetProperty("regions").GetArrayLength() > 0);
    }

    [Fact]
    public void BuildPinsRuntimeInputsAndContainsNoNpmExecutionTarget()
    {
        var project = File.ReadAllText(RepositoryFile(
            "src", "Pathstitch.App", "Pathstitch.App.csproj"));

        Assert.Contains("<PackagedNodeVersion>22.22.0</PackagedNodeVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<OpenGeometryPackageVersion>2.0.11</OpenGeometryPackageVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<PackagedNodeArchiveSha256", project, StringComparison.Ordinal);
        Assert.Contains("<OpenGeometryPackageSha512>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("npm ci", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FileName = \"node\"", File.ReadAllText(RepositoryFile(
            "src", "Pathstitch.App", "Services", "OpenGeometryKernelBridge.cs")), StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(File.ReadAllText(RepositoryFile(
            "src", "Pathstitch.App", "Assets", "OpenGeometry", "runtime-manifest.json")));
        Assert.Equal("22.22.0", manifest.RootElement.GetProperty("node").GetProperty("version").GetString());
        Assert.Equal("2.0.11", manifest.RootElement.GetProperty("openGeometry").GetProperty("version").GetString());
        Assert.Contains("OCCT", manifest.RootElement.GetProperty("scope").GetString(), StringComparison.Ordinal);
    }

    private static string RepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(pathParts)}");
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string directoryPath) => DirectoryPath = directoryPath;

        public string DirectoryPath { get; }

        public static TemporaryWorkspace Create()
        {
            var path = Path.Combine(Path.GetTempPath(), $"pathstitch-runtime-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new(path);
        }

        public string Write(string fileName, string content)
        {
            var path = Path.Combine(DirectoryPath, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
