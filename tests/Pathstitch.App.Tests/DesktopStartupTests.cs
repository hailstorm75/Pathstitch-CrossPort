namespace Pathstitch.App.Tests;

public sealed class DesktopStartupTests
{
    [Fact]
    public void NormalizeStartupFileArgumentsKeepsExistingUniqueFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pathstitch-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "sample.stch");
            var drawing = Path.Combine(root, "drawing.dxf");
            File.WriteAllText(project, "{}");
            File.WriteAllText(drawing, "0\nEOF\n");

            var normalized = App.NormalizeStartupFileArguments([
                project,
                project.ToUpperInvariant(),
                drawing,
                Path.Combine(root, "missing.step"),
                "",
            ]);

            Assert.Equal([Path.GetFullPath(project), Path.GetFullPath(drawing)], normalized);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileOpenRouter_HoldsColdLaunchFilesUntilReadyAndSuppressesPendingDuplicates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pathstitch-activation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "sample.stch");
            var drawing = Path.Combine(root, "drawing.dxf");
            File.WriteAllText(project, "{}");
            File.WriteAllText(drawing, "0\nEOF\n");
            using var router = new Services.DesktopFileOpenRouter();
            var routed = new List<IReadOnlyList<string>>();

            router.Enqueue([project, project.ToUpperInvariant()]);
            router.Enqueue([project, drawing]);
            Assert.Empty(routed);

            router.SetReady(paths =>
            {
                routed.Add(paths.ToArray());
                return Task.CompletedTask;
            });
            await router.WhenIdleAsync();

            Assert.Equal(2, routed.Count);
            Assert.Equal([Path.GetFullPath(project)], routed[0]);
            Assert.Equal([Path.GetFullPath(drawing)], routed[1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileOpenRouter_SerializesBatchesAndContinuesAfterFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pathstitch-activation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first.dxf");
            var second = Path.Combine(root, "second.dxf");
            File.WriteAllText(first, "0\nEOF\n");
            File.WriteAllText(second, "0\nEOF\n");
            var errors = new List<Exception>();
            using var router = new Services.DesktopFileOpenRouter(errors.Add);
            var routed = new List<string>();
            var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var active = 0;
            var maximumActive = 0;

            router.SetReady(async paths =>
            {
                var current = Interlocked.Increment(ref active);
                maximumActive = Math.Max(maximumActive, current);
                var path = Assert.Single(paths);
                routed.Add(path);
                try
                {
                    if (string.Equals(path, Path.GetFullPath(first), StringComparison.OrdinalIgnoreCase))
                    {
                        firstStarted.SetResult();
                        await releaseFirst.Task;
                        throw new InvalidOperationException("first failed");
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

            router.Enqueue([first]);
            await firstStarted.Task;
            router.Enqueue([second]);
            Assert.Equal(1, maximumActive);
            releaseFirst.SetResult();
            await router.WhenIdleAsync();

            Assert.Equal([Path.GetFullPath(first), Path.GetFullPath(second)], routed);
            Assert.Equal(1, maximumActive);
            Assert.Single(errors);
            Assert.Equal("first failed", errors[0].Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RuntimeActivationUsesAvaloniaFilePayloadAndExistingDocumentDeclarations()
    {
        var app = ReadRepositoryFile("src", "Pathstitch.App", "App.axaml.cs");
        var plist = ReadRepositoryFile("scripts", "macos", "Info.plist");

        Assert.Contains("TryGetFeature(typeof(IActivatableLifetime))", app, StringComparison.Ordinal);
        Assert.Contains("FileActivatedEventArgs", app, StringComparison.Ordinal);
        Assert.Contains("fileActivation.Files", app, StringComparison.Ordinal);
        Assert.Contains("_documentWindowManager.OpenFilesAsync", app, StringComparison.Ordinal);
        Assert.Contains("CreateWelcomeWindow", app, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_MACOS_FILE_ACTIVATION_OUTPUT", app, StringComparison.Ordinal);
        Assert.Contains("CFBundleDocumentTypes", plist, StringComparison.Ordinal);
        Assert.Contains("com.pathstitch.project", plist, StringComparison.Ordinal);
        Assert.Contains("com.pathstitch.dxf", plist, StringComparison.Ordinal);
        Assert.Contains("com.pathstitch.step", plist, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PathstitchCross.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine([root.FullName, .. parts]));
    }
}
