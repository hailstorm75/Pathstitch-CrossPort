namespace Pathstitch.App.Tests;

public sealed class MacOSDeliveryAcceptanceTests
{
    [Fact]
    public void PackagedAppAcceptance_ExercisesWebViewNativeDialogsAndStchRoundTrip()
    {
        var harness = File.ReadAllText(Find("src", "Pathstitch.App", "MacOSPackagedAcceptance.cs"));
        var workflow = File.ReadAllText(Find(".github", "workflows", "macos-release.yml"));

        Assert.Contains("new NativeWebView()", harness, StringComparison.Ordinal);
        Assert.Contains("NavigationCompleted", harness, StringComparison.Ordinal);
        Assert.Contains("ProjectFileDialogService", harness, StringComparison.Ordinal);
        Assert.Contains("StorageProvider", harness, StringComparison.Ordinal);
        Assert.Contains("SaveAsync", harness, StringComparison.Ordinal);
        Assert.Contains("LoadAsync", harness, StringComparison.Ordinal);
        Assert.Contains("packaged-roundtrip.stch", harness, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_MACOS_ACCEPTANCE_OUTPUT", workflow, StringComparison.Ordinal);
        Assert.Contains("packaged-app-acceptance.json", workflow, StringComparison.Ordinal);
        Assert.Contains("viewportPreserved", workflow, StringComparison.Ordinal);
        Assert.Contains("unzip -t", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickLookProviders_RenderDxfAndFoxtrotStepGeometry()
    {
        var preview = File.ReadAllText(Find("scripts", "macos", "quicklook", "PreviewProvider.swift"));
        var thumbnail = File.ReadAllText(Find("scripts", "macos", "quicklook", "ThumbnailProvider.swift"));
        var package = File.ReadAllText(Find("scripts", "package-avalonia-macos.ps1"));
        var workflow = File.ReadAllText(Find(".github", "workflows", "macos-release.yml"));

        foreach (var provider in new[] { preview, thumbnail })
        {
            Assert.Contains("renderFileToImage", provider, StringComparison.Ordinal);
            Assert.Contains("loadStepMesh", provider, StringComparison.Ordinal);
            Assert.Contains("renderStepMeshToImage", provider, StringComparison.Ordinal);
            Assert.DoesNotContain("NSTextView", provider, StringComparison.Ordinal);
            Assert.DoesNotContain("extensionBadge", provider, StringComparison.Ordinal);
        }

        Assert.Contains("DxfPreviewShared.swift", package, StringComparison.Ordinal);
        Assert.Contains("StepPreviewShared.swift", package, StringComparison.Ordinal);
        Assert.Contains("libstep_mesh.a", package, StringComparison.Ordinal);
        Assert.Contains("PreviewGeometrySmoke.swift", package, StringComparison.Ordinal);
        Assert.Contains("dxf-preview.png", workflow, StringComparison.Ordinal);
        Assert.Contains("step-preview.png", workflow, StringComparison.Ordinal);
        Assert.Contains("analytic-multibody-hole.step", workflow, StringComparison.Ordinal);
    }

    private static string Find(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(Path.Combine(parts));
    }
}
