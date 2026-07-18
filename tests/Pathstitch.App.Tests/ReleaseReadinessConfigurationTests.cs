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
        Assert.Contains("StepGeometry", script, StringComparison.Ordinal);
        Assert.Contains("Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("npm executables", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("readiness-result.json", script, StringComparison.Ordinal);
        Assert.Contains("verification-transcript.txt", script, StringComparison.Ordinal);
        Assert.Contains("package-inventory.json", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("runtime-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("opengeometry_bg.wasm", script, StringComparison.Ordinal);
        Assert.Contains("LICENSE.node.txt", script, StringComparison.Ordinal);
        Assert.Contains("PowerShell 7 or newer", script, StringComparison.Ordinal);
        Assert.Contains("native x64 Windows host", script, StringComparison.Ordinal);
        Assert.Contains("native Apple-silicon host", script, StringComparison.Ordinal);
        Assert.Contains("all-tests.trx", script, StringComparison.Ordinal);
        Assert.Contains("representative-workflows.trx", script, StringComparison.Ordinal);
        Assert.Contains("Build and smoke-test native STEP worker runtime", script, StringComparison.Ordinal);
        Assert.Contains("Validate both release runtime specifications", script, StringComparison.Ordinal);
        Assert.Contains("requires-native-run", script, StringComparison.Ordinal);
        Assert.DoesNotContain("cross-built-only", script, StringComparison.Ordinal);
        Assert.Contains("build-runtime.ps1", script, StringComparison.Ordinal);
        Assert.Contains("environment.lock", script, StringComparison.Ordinal);
        Assert.Contains("runtime-spec.json", script, StringComparison.Ordinal);
        Assert.Contains("runtime-inputs.json", script, StringComparison.Ordinal);
        Assert.Contains("GeometryWorker/$nativeRid/sitecustomize.py", script, StringComparison.Ordinal);
        Assert.Contains("sbom.spdx.json", script, StringComparison.Ordinal);
        Assert.Contains("third-party-notices.json", script, StringComparison.Ordinal);
        Assert.Contains("step-worker-performance.json", script, StringComparison.Ordinal);
        Assert.Contains("startupSeconds = 15.0", script, StringComparison.Ordinal);
        Assert.Contains("representativeImportSeconds = 45.0", script, StringComparison.Ordinal);
        Assert.Contains("peakWorkingSetBytes = 1610612736", script, StringComparison.Ordinal);

        var workerBuilder = File.ReadAllText(Find("geometry-worker", "build-runtime.ps1"));
        var moveIndex = workerBuilder.IndexOf("Move-Item -LiteralPath $staging -Destination $destination", StringComparison.Ordinal);
        var smokeIndex = workerBuilder.IndexOf("Invoke-NativeRuntimeSmoke $destination", StringComparison.Ordinal);
        Assert.True(moveIndex >= 0 && smokeIndex > moveIndex, "The native worker must be smoke-tested after relocation to its shipped path.");
    }

    [Fact]
    public void ReadinessDocumentation_RequiresNativeLaunchOnBothReleasePlatforms()
    {
        var document = File.ReadAllText(Find("docs", "release-readiness.md"));

        Assert.Contains("Native launch from the clean publish directory", document, StringComparison.Ordinal);
        Assert.Contains("signed/notarized macOS bundle", document, StringComparison.Ordinal);
        Assert.Contains("ADR-001", document, StringComparison.Ordinal);
        Assert.Contains("requires-native-run", document, StringComparison.Ordinal);
        Assert.Contains("does not cross-publish", document, StringComparison.Ordinal);
        Assert.Contains("clean Windows x64 runner", document, StringComparison.Ordinal);
        Assert.Contains("clean Apple-silicon macOS runner", document, StringComparison.Ordinal);
        Assert.Contains("readiness-result.json", document, StringComparison.Ordinal);
    }

    [Fact]
    public void BothNativePlatformWorkflows_RunTheUnifiedGateAndUploadEvidence()
    {
        var windows = File.ReadAllText(Find(".github", "workflows", "windows-release.yml"));
        var macos = File.ReadAllText(Find(".github", "workflows", "macos-release.yml"));

        Assert.Contains("windows-2022", windows, StringComparison.Ordinal);
        Assert.Contains("verify-release-readiness.ps1", windows, StringComparison.Ordinal);
        Assert.Contains("artifacts/readiness", windows, StringComparison.Ordinal);
        Assert.Contains("micromamba-version: 2.8.1-0", windows, StringComparison.Ordinal);
        Assert.Contains("macos-15", macos, StringComparison.Ordinal);
        Assert.Contains("verify-release-readiness.ps1", macos, StringComparison.Ordinal);
        Assert.Contains("artifacts/readiness", macos, StringComparison.Ordinal);
        Assert.Contains("micromamba-version: 2.8.1-0", macos, StringComparison.Ordinal);
        Assert.Contains("GeometryWorker/osx-arm64/bin/python3.11", macos, StringComparison.Ordinal);
        Assert.Contains("GeometryWorker/osx-arm64/sitecustomize.py", macos, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmePlatformAndKernelClaimsMatchTheVerifiedReleaseScope()
    {
        var readme = File.ReadAllText(Find("README.md"));

        Assert.Contains("targets `net10.0`", readme, StringComparison.Ordinal);
        Assert.Contains("`win-x64`", readme, StringComparison.Ordinal);
        Assert.Contains("`osx-arm64`", readme, StringComparison.Ordinal);
        Assert.Contains("without a user-installed Node or npm", readme, StringComparison.Ordinal);
        Assert.Contains("packaged Python/OCCT STEP worker", readme, StringComparison.Ordinal);
        Assert.Contains("OpenGeometry%20%2B%20OCCT", readme, StringComparison.Ordinal);
        Assert.Contains("never resolve Node or Python from", readme, StringComparison.Ordinal);
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
