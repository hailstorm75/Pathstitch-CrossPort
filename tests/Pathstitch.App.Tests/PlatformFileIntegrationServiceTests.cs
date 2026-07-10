using System.Diagnostics;
using Domain.App.Services;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class PlatformFileIntegrationServiceTests
{
    [Fact]
    public void Factory_SelectsWindowsAndMacOsImplementationsExplicitly()
    {
        var processLauncher = new RecordingProcessLauncher();

        Assert.IsType<WindowsExplorerFileIntegrationService>(
            DesktopFileIntegrationServiceFactory.Create(DesktopPlatform.Windows, processLauncher));
        Assert.IsType<MacOsFinderFileIntegrationService>(
            DesktopFileIntegrationServiceFactory.Create(DesktopPlatform.MacOS, processLauncher));
    }

    [Fact]
    public void Factory_SelectsTheCurrentOperatingSystem()
    {
        var service = DesktopFileIntegrationServiceFactory.CreateForCurrentPlatform(
            new RecordingProcessLauncher());

        if (OperatingSystem.IsWindows())
            Assert.IsType<WindowsExplorerFileIntegrationService>(service);
        else if (OperatingSystem.IsMacOS())
            Assert.IsType<MacOsFinderFileIntegrationService>(service);
        else
            throw new Xunit.Sdk.XunitException("This test project targets the supported desktop platforms.");
    }

    [Fact]
    public async Task WindowsIntegration_UsesExplorerSelectionAndShellFileOpen()
    {
        using var target = TemporaryFile.Create("drawing.dxf");
        var processLauncher = new RecordingProcessLauncher();
        var service = new WindowsExplorerFileIntegrationService(processLauncher);

        await service.RevealFileAsync(target.FilePath);
        await service.OpenFileAsync(target.FilePath);

        Assert.Collection(
            processLauncher.Starts,
            reveal =>
            {
                Assert.Equal("explorer.exe", reveal.FileName);
                Assert.False(reveal.UseShellExecute);
                Assert.Equal($"/select,{target.FilePath}", Assert.Single(reveal.ArgumentList));
            },
            open =>
            {
                Assert.Equal(target.FilePath, open.FileName);
                Assert.True(open.UseShellExecute);
                Assert.Empty(open.ArgumentList);
            });
    }

    [Fact]
    public async Task MacOsIntegration_UsesFinderRevealAndOpenCommands()
    {
        using var target = TemporaryFile.Create("drawing.dxf");
        var processLauncher = new RecordingProcessLauncher();
        var service = new MacOsFinderFileIntegrationService(processLauncher);

        await service.RevealFileAsync(target.FilePath);
        await service.OpenFileAsync(target.FilePath);

        Assert.Collection(
            processLauncher.Starts,
            reveal =>
            {
                Assert.Equal("/usr/bin/open", reveal.FileName);
                Assert.False(reveal.UseShellExecute);
                Assert.Equal(["-R", target.FilePath], reveal.ArgumentList);
            },
            open =>
            {
                Assert.Equal("/usr/bin/open", open.FileName);
                Assert.Equal(target.FilePath, Assert.Single(open.ArgumentList));
            });
    }

    [Theory]
    [InlineData(DesktopPlatform.Windows)]
    [InlineData(DesktopPlatform.MacOS)]
    public async Task RevealMissingFile_OpensItsExistingContainingDirectory(DesktopPlatform platform)
    {
        using var target = TemporaryFile.Create("existing.dxf");
        File.Delete(target.FilePath);
        var missingPath = Path.Combine(target.DirectoryPath, "not-created.dxf");
        var processLauncher = new RecordingProcessLauncher();
        var service = DesktopFileIntegrationServiceFactory.Create(platform, processLauncher);

        await service.RevealFileAsync(missingPath);

        var start = Assert.Single(processLauncher.Starts);
        Assert.Equal(target.DirectoryPath, Assert.Single(start.ArgumentList));
        Assert.Equal(
            platform == DesktopPlatform.Windows ? "explorer.exe" : "/usr/bin/open",
            start.FileName);
    }

    [Fact]
    public async Task EditorOutputLauncher_DelegatesToSharedFileIntegrationContract()
    {
        var integration = new RecordingFileIntegrationService();
        var service = new EditorOutputLauncherService(integration);

        await service.OpenOutputAsync("output.dxf");
        await service.RevealOutputAsync("output.dxf");

        Assert.Equal("output.dxf", integration.OpenedPath);
        Assert.Equal("output.dxf", integration.RevealedPath);
    }

    [Fact]
    public void SharedIntegrationCode_DoesNotHardcodeAWindowsExecutable()
    {
        var sharedFiles = new[]
        {
            RepositoryFile("src", "Domain", "Domain.App", "Services", "IFileIntegrationService.cs"),
            RepositoryFile("src", "Pathstitch.App", "Services", "EditorOutputLauncherService.cs"),
            RepositoryFile("src", "Pathstitch.App", "Services", "DesktopFileIntegrationServiceFactory.cs"),
            RepositoryFile("src", "Pathstitch.App", "App.axaml.cs"),
        };

        Assert.All(sharedFiles, file =>
            Assert.DoesNotContain("explorer.exe", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            "explorer.exe",
            File.ReadAllText(RepositoryFile(
                "src", "Pathstitch.App", "Services", "WindowsExplorerFileIntegrationService.cs")),
            StringComparison.OrdinalIgnoreCase);
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

    private sealed class RecordingProcessLauncher : IProcessLauncher
    {
        public List<ProcessStartInfo> Starts { get; } = [];

        public void Start(ProcessStartInfo startInfo) => Starts.Add(startInfo);
    }

    private sealed class RecordingFileIntegrationService : IFileIntegrationService
    {
        public string? OpenedPath { get; private set; }
        public string? RevealedPath { get; private set; }

        public Task OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            OpenedPath = filePath;
            return Task.CompletedTask;
        }

        public Task RevealFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            RevealedPath = filePath;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryFile : IDisposable
    {
        private TemporaryFile(string directoryPath, string filePath)
        {
            DirectoryPath = directoryPath;
            FilePath = filePath;
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }

        public static TemporaryFile Create(string fileName)
        {
            var directoryPath = Path.Combine(Path.GetTempPath(), $"pathstitch-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryPath);
            var filePath = Path.Combine(directoryPath, fileName);
            File.WriteAllText(filePath, "fixture");
            return new(directoryPath, filePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
