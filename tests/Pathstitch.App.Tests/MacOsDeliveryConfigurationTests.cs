using System.Xml.Linq;

namespace Pathstitch.App.Tests;

public sealed class MacOsDeliveryConfigurationTests
{
    [Fact]
    public void PublishProfile_IsSelfContainedAppleSiliconRelease()
    {
        var profile = XDocument.Load(Find("src", "Pathstitch.App", "Properties", "PublishProfiles", "osx-arm64.pubxml"));

        Assert.Equal("osx-arm64", profile.Descendants("RuntimeIdentifier").Single().Value);
        Assert.Equal("true", profile.Descendants("SelfContained").Single().Value);
        Assert.Equal("Release", profile.Descendants("Configuration").Single().Value);
    }

    [Fact]
    public void PackagingScript_DefinesBundleSigningNotarizationAndOfflineRuntimeLayout()
    {
        var script = File.ReadAllText(Find("scripts", "package-avalonia-macos.ps1"));

        Assert.Contains("Pathstitch.app", script, StringComparison.Ordinal);
        Assert.Contains("Contents", script, StringComparison.Ordinal);
        Assert.Contains("codesign --verify", script, StringComparison.Ordinal);
        Assert.Contains("notarytool submit", script, StringComparison.Ordinal);
        Assert.Contains("stapler staple", script, StringComparison.Ordinal);
        Assert.Contains("PublishProfile=osx-arm64", script, StringComparison.Ordinal);
        Assert.Contains("-r osx-arm64", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MacCi_RestoresTestsPackagesLaunchesAndUploadsArtifact()
    {
        var workflow = File.ReadAllText(Find(".github", "workflows", "macos-release.yml"));

        Assert.Contains("runs-on: macos-15", workflow, StringComparison.Ordinal);
        Assert.Contains("verify-release-readiness.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("package-avalonia-macos.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("libAvaloniaNative.dylib", workflow, StringComparison.Ordinal);
        Assert.Contains("Packaged app WebView, file-dialog, and project round-trip acceptance", workflow, StringComparison.Ordinal);
        Assert.Contains("webView.navigationCompleted", workflow, StringComparison.Ordinal);
        Assert.Contains("fileDialogs.CanOpen", workflow, StringComparison.Ordinal);
        Assert.Contains("fileDialogs.CanSave", workflow, StringComparison.Ordinal);
        Assert.Contains("projectRoundTrip.viewportPreserved", workflow, StringComparison.Ordinal);
        Assert.Contains("packaged-roundtrip.stch", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
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
