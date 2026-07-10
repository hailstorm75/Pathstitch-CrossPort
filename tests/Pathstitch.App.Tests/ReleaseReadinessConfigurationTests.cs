namespace Pathstitch.App.Tests;

public sealed class ReleaseReadinessConfigurationTests
{
    [Fact]
    public void ReadinessScript_CoversEveryRequiredGateAndBothPlatforms()
    {
        var script = File.ReadAllText(Find("scripts", "verify-release-readiness.ps1"));

        Assert.Contains("-warnaserror", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet test", script, StringComparison.Ordinal);
        Assert.Contains("win-x64", script, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", script, StringComparison.Ordinal);
        Assert.Contains("WorkspaceSemanticRoundTripTests", script, StringComparison.Ordinal);
        Assert.Contains("OperationServiceConformanceTests", script, StringComparison.Ordinal);
        Assert.Contains("Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("npm executables", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("readiness-result.json", script, StringComparison.Ordinal);
        Assert.Contains("verification-transcript.txt", script, StringComparison.Ordinal);
        Assert.Contains("package-inventory.json", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("runtime-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("opengeometry_bg.wasm", script, StringComparison.Ordinal);
        Assert.Contains("LICENSE.node.txt", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessDocumentation_RequiresNativeLaunchOnBothReleasePlatforms()
    {
        var document = File.ReadAllText(Find("docs", "release-readiness.md"));

        Assert.Contains("Native launch from the clean publish directory", document, StringComparison.Ordinal);
        Assert.Contains("signed/notarized macOS bundle", document, StringComparison.Ordinal);
        Assert.Contains("ADR-001", document, StringComparison.Ordinal);
        Assert.Contains("cross-built-only", document, StringComparison.Ordinal);
        Assert.Contains("clean Windows x64 runner", document, StringComparison.Ordinal);
        Assert.Contains("clean Apple-silicon macOS runner", document, StringComparison.Ordinal);
        Assert.Contains("readiness-result.json", document, StringComparison.Ordinal);
    }

    [Fact]
    public void BothNativePlatformWorkflows_RunTheUnifiedGateAndUploadEvidence()
    {
        var windows = File.ReadAllText(Find(".github", "workflows", "windows-release.yml"));
        var macos = File.ReadAllText(Find(".github", "workflows", "macos-release.yml"));

        Assert.Contains("windows-2025", windows, StringComparison.Ordinal);
        Assert.Contains("verify-release-readiness.ps1", windows, StringComparison.Ordinal);
        Assert.Contains("artifacts/readiness", windows, StringComparison.Ordinal);
        Assert.Contains("macos-14", macos, StringComparison.Ordinal);
        Assert.Contains("verify-release-readiness.ps1", macos, StringComparison.Ordinal);
        Assert.Contains("artifacts/readiness", macos, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmePlatformAndKernelClaimsMatchTheVerifiedReleaseScope()
    {
        var readme = File.ReadAllText(Find("README.md"));

        Assert.Contains("targets `net10.0`", readme, StringComparison.Ordinal);
        Assert.Contains("`win-x64`", readme, StringComparison.Ordinal);
        Assert.Contains("`osx-arm64`", readme, StringComparison.Ordinal);
        Assert.Contains("without a user-installed Node or npm", readme, StringComparison.Ordinal);
        Assert.Contains("Avalonia/OpenGeometry path is waiting", readme, StringComparison.Ordinal);
        Assert.Contains("docs/release-readiness.md", readme, StringComparison.Ordinal);
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
