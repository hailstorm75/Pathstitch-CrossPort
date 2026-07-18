using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pathstitch.App.Tests;

public sealed class NativeMacOSEvidenceValidatorTests
{
    [Fact]
    public async Task CompleteSyntheticBundle_ProducesSelfContainedPassedManifest()
    {
        using var fixture = NativeEvidenceFixture.Create();

        var result = await fixture.RunValidatorAsync();

        Assert.Equal(0, result.ExitCode);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.ManifestPath));
        Assert.Equal("passed", manifest.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(manifest.RootElement.GetProperty("validationErrors").EnumerateArray());
        Assert.True(File.Exists(Path.Combine(fixture.EvidenceRoot, "Pathstitch-osx-arm64.zip")));
        Assert.Equal(3, manifest.RootElement.GetProperty("quickLookThumbnailCount").GetInt32());
        Assert.Equal(7, manifest.RootElement.GetProperty("dxfUnitEvidence").GetArrayLength());
        Assert.Equal(4, manifest.RootElement.GetProperty("embeddedProjectDxfEvidence").GetProperty("insUnitsCode").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("embeddedProjectDxfEvidence").GetProperty("measurementCode").GetInt32());
        var runtimeProbe = manifest.RootElement.GetProperty("runtimeProbeEvidence");
        Assert.Equal("0123456789abcdef0123456789abcdef", runtimeProbe.GetProperty("nonce").GetString());
        Assert.True(runtimeProbe.GetProperty("preview").GetProperty("interactiveSceneKit").GetBoolean());
        Assert.True(runtimeProbe.GetProperty("preview").GetProperty("cameraControlEnabled").GetBoolean());
        Assert.Equal("com.pathstitch.crossport.thumbnail", runtimeProbe.GetProperty("thumbnail").GetProperty("bundleIdentifier").GetString());
    }

    [Fact]
    public async Task IndependentReview_ValidBundle_BindsExpectedRunAndLeavesProducerManifestUntouched()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        using var fixture = NativeEvidenceFixture.Create();
        var collected = await fixture.RunValidatorAsync(commit, "8675309", "2");
        Assert.Equal(0, collected.ExitCode);
        var producerManifestBefore = await File.ReadAllBytesAsync(fixture.ManifestPath);

        var reviewed = await fixture.RunReviewerAsync(commit, "8675309", "2");

        Assert.Equal(0, reviewed.ExitCode);
        Assert.Equal(producerManifestBefore, await File.ReadAllBytesAsync(fixture.ManifestPath));
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.ReviewReceiptPath));
        Assert.Equal("passed", receipt.RootElement.GetProperty("status").GetString());
        Assert.Equal(commit, receipt.RootElement.GetProperty("expectedCommit").GetString());
        Assert.Equal("8675309", receipt.RootElement.GetProperty("expectedWorkflowRunId").GetString());
        Assert.Equal(0, receipt.RootElement.GetProperty("revalidation").GetProperty("collectorExitCode").GetInt32());
    }

    [Theory]
    [InlineData("fedcba9876543210fedcba9876543210fedcba98", false)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    public async Task IndependentReview_WrongIdentityOrTamperedEvidence_CannotPass(
        string reviewedCommit,
        bool tamperEvidence)
    {
        const string producerCommit = "0123456789abcdef0123456789abcdef01234567";
        using var fixture = NativeEvidenceFixture.Create();
        var collected = await fixture.RunValidatorAsync(producerCommit, "8675309", "2");
        Assert.Equal(0, collected.ExitCode);
        if (tamperEvidence)
            fixture.TamperCollectedEvidence("packaged-projection.dxf");

        var reviewed = await fixture.RunReviewerAsync(reviewedCommit, "8675309", "2");

        Assert.NotEqual(0, reviewed.ExitCode);
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.ReviewReceiptPath));
        Assert.Equal("failed", receipt.RootElement.GetProperty("status").GetString());
        Assert.NotEmpty(receipt.RootElement.GetProperty("validationErrors").EnumerateArray());
    }

    [Theory]
    [InlineData("tiny_png")]
    [InlineData("mutated_png")]
    [InlineData("duplicate_map_output")]
    [InlineData("missing_ql_log")]
    [InlineData("missing_zip")]
    [InlineData("mutated_zip")]
    [InlineData("mutated_roundtrip_stch")]
    [InlineData("mismatched_roundtrip_hash")]
    [InlineData("bad_signature_evidence")]
    [InlineData("bad_plugin_inventory")]
    [InlineData("bad_activation")]
    [InlineData("false_package_boolean")]
    [InlineData("trivial_mesh_dxf")]
    [InlineData("missing_measurement")]
    [InlineData("misplaced_measurement")]
    [InlineData("duplicate_insunits")]
    [InlineData("embedded_wrong_units")]
    [InlineData("extra_bad_dxf")]
    [InlineData("missing_runtime_probe")]
    [InlineData("runtime_nonce_mismatch")]
    [InlineData("preview_fallback")]
    [InlineData("app_group_pref_mismatch")]
    public async Task CorruptedSyntheticBundle_CannotProducePassedManifest(string mutation)
    {
        using var fixture = NativeEvidenceFixture.Create();
        fixture.ApplyMutation(mutation);

        var result = await fixture.RunValidatorAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(fixture.ManifestPath), result.StandardError);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.ManifestPath));
        Assert.Equal("failed", manifest.RootElement.GetProperty("status").GetString());
        Assert.NotEmpty(manifest.RootElement.GetProperty("validationErrors").EnumerateArray());
    }

    [Theory]
    [InlineData("packaged-projection.dxf")]
    [InlineData("packaged-unfold.dxf")]
    [InlineData("packaged-obj-projection.dxf")]
    [InlineData("packaged-obj-connected.dxf")]
    [InlineData("packaged-obj-separate.dxf")]
    [InlineData("packaged-obj-tabs.dxf")]
    [InlineData("packaged-obj-holes.dxf")]
    public async Task WrongInsUnits_InAnyRetainedDxf_CannotProducePassedManifest(string fileName)
    {
        using var fixture = NativeEvidenceFixture.Create();
        fixture.CorruptRetainedDxf(fileName, insUnitsCode: 1, measurementCode: 1);

        var result = await fixture.RunValidatorAsync();

        Assert.NotEqual(0, result.ExitCode);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.ManifestPath));
        Assert.Contains(
            manifest.RootElement.GetProperty("validationErrors").EnumerateArray(),
            error => error.GetString()!.Contains("$INSUNITS=4", StringComparison.Ordinal));
    }

    private sealed class NativeEvidenceFixture : IDisposable
    {
        private readonly string _root;
        private readonly string _expectedActivationFixture;
        private readonly string _packageAcceptancePath;
        private readonly string _fileActivationPath;
        private readonly string _quickLookMapPath;
        private readonly string _firstPngPath;
        private readonly string _firstLogPath;
        private readonly string _meshConnectedPath;
        private readonly string _roundTripPath;
        private readonly string _appEntitlementsPath;
        private readonly string _pluginInventoryPath;
        private readonly string _previewRuntimeProbePath;
        private readonly string _thumbnailRuntimeProbePath;

        private NativeEvidenceFixture(string root)
        {
            _root = root;
            EvidenceRoot = Path.Combine(root, "evidence");
            PackageAcceptanceDirectory = Path.Combine(root, "package-acceptance");
            FileActivationDirectory = Path.Combine(root, "file-activation");
            QuickLookDirectory = Path.Combine(root, "quicklook");
            RuntimeProbeDirectory = Path.Combine(root, "runtime-probes");
            PackageZip = Path.Combine(root, "Pathstitch-osx-arm64.zip");
            _expectedActivationFixture = Path.Combine(root, "preview-smoke.dxf");
            Directory.CreateDirectory(EvidenceRoot);
            Directory.CreateDirectory(PackageAcceptanceDirectory);
            Directory.CreateDirectory(FileActivationDirectory);
            Directory.CreateDirectory(QuickLookDirectory);
            Directory.CreateDirectory(RuntimeProbeDirectory);
            File.WriteAllBytes(_expectedActivationFixture, CreateBytes(256, 11));
            CreateZip(PackageZip, "Pathstitch.app/Contents/MacOS/Pathstitch.App", 4096, 17);

            File.WriteAllText(Path.Combine(EvidenceRoot, "codesign-details.txt"), "synthetic signed app");
            File.WriteAllText(Path.Combine(EvidenceRoot, "nested-codesign-details.txt"), "synthetic signed nested binaries");
            File.WriteAllText(
                Path.Combine(EvidenceRoot, "codesign-verification.txt"),
                "Pathstitch.app PathstitchQuickLook.appex PathstitchThumbnail.appex");
            _appEntitlementsPath = Path.Combine(EvidenceRoot, "app-entitlements.plist");
            File.WriteAllText(_appEntitlementsPath, "group.com.pathstitch.crossport");
            foreach (var name in new[]
            {
                "PathstitchQuickLook-entitlements.plist",
                "PathstitchThumbnail-entitlements.plist",
            }) File.WriteAllText(
                Path.Combine(EvidenceRoot, name),
                "group.com.pathstitch.crossport com.apple.security.app-sandbox");

            _pluginInventoryPath = Path.Combine(QuickLookDirectory, "pluginkit-inventory.txt");
            File.WriteAllText(
                _pluginInventoryPath,
                "com.pathstitch.crossport.quicklook com.pathstitch.crossport.thumbnail");
            var quickLookEntries = new List<object>();
            var fixtures = new[] { "sample.dxf", "sample.step", "sample.stch" };
            for (var index = 0; index < fixtures.Length; index++)
            {
                var extension = Path.GetExtension(fixtures[index]).TrimStart('.');
                var png = Path.Combine(QuickLookDirectory, $"sample-{extension}.png");
                File.WriteAllBytes(png, CreateBytes(2048 + index, 30 + index));
                var log = Path.Combine(QuickLookDirectory, $"sample-{extension}-qlmanage.log");
                File.WriteAllText(log, $"qlmanage {fixtures[index]} ok");
                quickLookEntries.Add(new
                {
                    fixture = fixtures[index],
                    output = Path.GetFileName(png),
                    bytes = new FileInfo(png).Length,
                    sha256 = HashFile(png),
                });
            }
            _firstPngPath = Path.Combine(QuickLookDirectory, "sample-dxf.png");
            _firstLogPath = Path.Combine(QuickLookDirectory, "sample-dxf-qlmanage.log");
            _quickLookMapPath = Path.Combine(QuickLookDirectory, "quicklook-output-map.json");
            File.WriteAllText(_quickLookMapPath, JsonSerializer.Serialize(quickLookEntries));
            (_previewRuntimeProbePath, _thumbnailRuntimeProbePath) = WriteRuntimeProbeEvidence();

            var stepInput = WritePackageArtifact("packaged-input.step", 1200, 38);
            var objInput = WritePackageArtifact("packaged-input.obj", 1100, 39);
            var stlInput = WritePackageArtifact("packaged-input.stl", 1150, 40);
            var stepProjection = WritePackageDxfArtifact("packaged-projection.dxf");
            var stepUnfold = WritePackageDxfArtifact("packaged-unfold.dxf");
            var roundTrip = WriteProjectArchiveArtifact("packaged-roundtrip.stch", File.ReadAllBytes((string)stepUnfold["path"]!));
            _roundTripPath = roundTrip["path"] as string
                ?? throw new InvalidOperationException("Synthetic round-trip path is missing.");
            var mixed = WritePackageArtifact("packaged-mixed.step", 1700, 44);
            var meshProjection = WritePackageDxfArtifact("packaged-obj-projection.dxf");
            var meshConnected = WritePackageDxfArtifact("packaged-obj-connected.dxf");
            var meshSeparate = WritePackageDxfArtifact("packaged-obj-separate.dxf");
            var meshTabs = WritePackageDxfArtifact("packaged-obj-tabs.dxf");
            var meshHoles = WritePackageDxfArtifact("packaged-obj-holes.dxf");
            _meshConnectedPath = meshConnected["path"] as string
                ?? throw new InvalidOperationException("Synthetic mesh artifact path is missing.");
            var fixedHash = (string)stepUnfold["sha256"]!;
            var projectRoundTrip = new Dictionary<string, object?>(roundTrip)
            {
                ["viewportPreserved"] = true,
                ["topologyPreserved"] = true,
                ["sourceModelPreserved"] = true,
                ["sourceSha256"] = fixedHash,
                ["reopenedSourceSha256"] = fixedHash,
                ["generatedOutputPreserved"] = true,
                ["generatedOutputSha256"] = fixedHash,
                ["reopenedGeneratedOutputSha256"] = fixedHash,
            };
            var packageAcceptance = new
            {
                schemaVersion = 4,
                status = "passed",
                webView = new { navigationCompleted = true },
                fileDialogs = new { CanOpen = true, CanSave = true },
                macIntegration = new
                {
                    independentFinderPatternApplied = true,
                    independentFinderPatternReadBack = true,
                    finderDefaultsRestored = true,
                    finderDefaultsReadBack = true,
                    lightIconApplied = true,
                    darkIconApplied = true,
                    automaticIconApplied = true,
                },
                stepWorkflow = new
                {
                    imported = true,
                    fixtureEvidence = stepInput,
                    projected = true,
                    unfolded = true,
                    projection = stepProjection,
                    unfold = stepUnfold,
                },
                projectRoundTrip,
                meshWorkflow = new
                {
                    objFixtureEvidence = objInput,
                    stlFixtureEvidence = stlInput,
                    objImported = true,
                    objBodyCount = 1,
                    objFaceCount = 2,
                    objEdgeCount = 5,
                    stlImported = true,
                    stlBodyCount = 1,
                    stlFaceCount = 2,
                    stlEdgeCount = 5,
                    mixedCombined = true,
                    mixedBodyCount = 4,
                    mixed,
                    projected = true,
                    projection = meshProjection,
                    connectedUnfolded = true,
                    connected = meshConnected,
                    separateUnfolded = true,
                    separate = meshSeparate,
                    tabsDecorated = true,
                    tabs = meshTabs,
                    holesDecorated = true,
                    holes = meshHoles,
                },
            };
            _packageAcceptancePath = Path.Combine(PackageAcceptanceDirectory, "packaged-app-acceptance.json");
            File.WriteAllText(_packageAcceptancePath, JsonSerializer.Serialize(packageAcceptance));

            _fileActivationPath = Path.Combine(FileActivationDirectory, "file-activation-acceptance.json");
            File.WriteAllText(_fileActivationPath, JsonSerializer.Serialize(new
            {
                status = "passed",
                files = new[] { _expectedActivationFixture },
                activePage = "Domain.App.ViewModels.EditorPageViewModel",
            }));
        }

        public string EvidenceRoot { get; }
        private string PackageAcceptanceDirectory { get; }
        private string FileActivationDirectory { get; }
        private string QuickLookDirectory { get; }
        private string RuntimeProbeDirectory { get; }
        private string PackageZip { get; }
        public string ManifestPath => Path.Combine(EvidenceRoot, "native-evidence-manifest.json");
        public string ReviewReceiptPath => Path.Combine(_root, "native-evidence-review.json");

        public static NativeEvidenceFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "Pathstitch-Native-Evidence-Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new NativeEvidenceFixture(root);
        }

        public void CorruptRetainedDxf(string fileName, int insUnitsCode, int measurementCode)
        {
            var path = Path.Combine(PackageAcceptanceDirectory, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException(fileName, path);
            File.WriteAllBytes(path, CreateMetricDxfBytes(insUnitsCode, measurementCode));
            RefreshAcceptanceDescriptor(path);
        }

        public void ApplyMutation(string mutation)
        {
            switch (mutation)
            {
                case "tiny_png":
                    File.WriteAllBytes(_firstPngPath, CreateBytes(64, 70));
                    break;
                case "mutated_png":
                    File.WriteAllBytes(_firstPngPath, CreateBytes((int)new FileInfo(_firstPngPath).Length, 71));
                    break;
                case "duplicate_map_output":
                {
                    var map = JsonNode.Parse(File.ReadAllText(_quickLookMapPath))!.AsArray();
                    map[1]!["output"] = map[0]!["output"]!.GetValue<string>();
                    File.WriteAllText(_quickLookMapPath, map.ToJsonString());
                    break;
                }
                case "missing_ql_log":
                    File.Delete(_firstLogPath);
                    break;
                case "missing_zip":
                    File.Delete(PackageZip);
                    break;
                case "mutated_zip":
                    File.WriteAllBytes(PackageZip, File.ReadAllBytes(PackageZip)[..64]);
                    break;
                case "mutated_roundtrip_stch":
                    File.WriteAllBytes(_roundTripPath, File.ReadAllBytes(_roundTripPath)[..64]);
                    break;
                case "mismatched_roundtrip_hash":
                {
                    var json = JsonNode.Parse(File.ReadAllText(_packageAcceptancePath))!;
                    json["projectRoundTrip"]!["reopenedSourceSha256"] = new string('b', 64);
                    File.WriteAllText(_packageAcceptancePath, json.ToJsonString());
                    break;
                }
                case "bad_signature_evidence":
                    File.WriteAllText(_appEntitlementsPath, "synthetic missing entitlement");
                    break;
                case "bad_plugin_inventory":
                    File.WriteAllText(_pluginInventoryPath, "synthetic missing plugins");
                    break;
                case "bad_activation":
                    File.WriteAllText(_fileActivationPath, JsonSerializer.Serialize(new
                    {
                        status = "passed",
                        files = new[] { Path.Combine(_root, "wrong.dxf") },
                        activePage = "HomePageViewModel",
                    }));
                    break;
                case "false_package_boolean":
                {
                    var json = JsonNode.Parse(File.ReadAllText(_packageAcceptancePath))!;
                    json["meshWorkflow"]!["connectedUnfolded"] = false;
                    File.WriteAllText(_packageAcceptancePath, json.ToJsonString());
                    break;
                }
                case "trivial_mesh_dxf":
                    File.WriteAllBytes(_meshConnectedPath, CreateBytes(40, 72));
                    break;
                case "missing_measurement":
                    File.WriteAllBytes(
                        _meshConnectedPath,
                        CreateMetricDxfBytes(includeMeasurement: false));
                    RefreshAcceptanceDescriptor(_meshConnectedPath);
                    break;
                case "misplaced_measurement":
                    File.WriteAllBytes(
                        _meshConnectedPath,
                        CreateMetricDxfBytes(includeMeasurement: false, measurementInEntities: true));
                    RefreshAcceptanceDescriptor(_meshConnectedPath);
                    break;
                case "duplicate_insunits":
                    File.WriteAllBytes(
                        _meshConnectedPath,
                        CreateMetricDxfBytes(duplicateInsUnits: true));
                    RefreshAcceptanceDescriptor(_meshConnectedPath);
                    break;
                case "embedded_wrong_units":
                    File.Delete(_roundTripPath);
                    CreateProjectArchive(_roundTripPath, CreateMetricDxfBytes(insUnitsCode: 1));
                    RefreshAcceptanceDescriptor(_roundTripPath);
                    break;
                case "extra_bad_dxf":
                    File.WriteAllBytes(
                        Path.Combine(PackageAcceptanceDirectory, "future-generated-output.dxf"),
                        CreateMetricDxfBytes(measurementCode: 0));
                    break;
                case "missing_runtime_probe":
                    File.Delete(_previewRuntimeProbePath);
                    break;
                case "runtime_nonce_mismatch":
                {
                    var json = JsonNode.Parse(File.ReadAllText(_thumbnailRuntimeProbePath))!;
                    json["nonce"] = "fedcba9876543210fedcba9876543210";
                    File.WriteAllText(_thumbnailRuntimeProbePath, json.ToJsonString());
                    break;
                }
                case "preview_fallback":
                {
                    var json = JsonNode.Parse(File.ReadAllText(_previewRuntimeProbePath))!;
                    json["interactiveSceneKit"] = false;
                    json["sceneViewInstalled"] = false;
                    json["cameraControlEnabled"] = false;
                    json["fallbackImageInstalled"] = true;
                    File.WriteAllText(_previewRuntimeProbePath, json.ToJsonString());
                    break;
                }
                case "app_group_pref_mismatch":
                {
                    var json = JsonNode.Parse(File.ReadAllText(_previewRuntimeProbePath))!;
                    json["observedPreferences"]!["dxf"] = true;
                    File.WriteAllText(_previewRuntimeProbePath, json.ToJsonString());
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
            }
        }

        private void RefreshAcceptanceDescriptor(string artifactPath)
        {
            var root = JsonNode.Parse(File.ReadAllText(_packageAcceptancePath))
                ?? throw new InvalidOperationException("Package acceptance JSON is missing.");
            if (!RefreshDescriptor(root, artifactPath))
                throw new InvalidOperationException($"No acceptance descriptor references {artifactPath}.");
            File.WriteAllText(_packageAcceptancePath, root.ToJsonString());
        }

        private static bool RefreshDescriptor(JsonNode? node, string artifactPath)
        {
            if (node is JsonObject obj)
            {
                if (obj["path"] is JsonValue pathValue
                    && pathValue.TryGetValue<string>(out var describedPath)
                    && string.Equals(describedPath, artifactPath, StringComparison.Ordinal))
                {
                    obj["bytes"] = new FileInfo(artifactPath).Length;
                    obj["sha256"] = HashFile(artifactPath);
                    return true;
                }
                foreach (var child in obj.Select(pair => pair.Value).ToArray())
                {
                    if (RefreshDescriptor(child, artifactPath))
                        return true;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var child in array)
                {
                    if (RefreshDescriptor(child, artifactPath))
                        return true;
                }
            }
            return false;
        }

        public async Task<ProcessResult> RunValidatorAsync(
            string? commit = null,
            string? workflowRunId = null,
            string? workflowRunAttempt = null)
        {
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = FindRepositoryDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (commit is not null)
                start.Environment["GITHUB_SHA"] = commit;
            if (workflowRunId is not null)
                start.Environment["GITHUB_RUN_ID"] = workflowRunId;
            if (workflowRunAttempt is not null)
                start.Environment["GITHUB_RUN_ATTEMPT"] = workflowRunAttempt;
            foreach (var argument in new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-File",
                FindRepositoryFile("scripts", "collect-native-macos-evidence.ps1"),
                "-EvidenceRoot", EvidenceRoot,
                "-PackageAcceptanceDirectory", PackageAcceptanceDirectory,
                "-FileActivationDirectory", FileActivationDirectory,
                "-QuickLookDirectory", QuickLookDirectory,
                "-RuntimeProbeDirectory", RuntimeProbeDirectory,
                "-PackageZip", PackageZip,
                "-ExpectedActivationFixture", _expectedActivationFixture,
                "-SkipPlatformCollection",
            }) start.ArgumentList.Add(argument);

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }

        public async Task<ProcessResult> RunReviewerAsync(
            string expectedCommit,
            string expectedWorkflowRunId,
            string expectedWorkflowRunAttempt)
        {
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = FindRepositoryDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-File",
                FindRepositoryFile("scripts", "verify-native-macos-evidence.ps1"),
                "-EvidenceRoot", EvidenceRoot,
                "-ExpectedCommit", expectedCommit,
                "-ExpectedWorkflowRunId", expectedWorkflowRunId,
                "-ExpectedWorkflowRunAttempt", expectedWorkflowRunAttempt,
                "-ReceiptPath", ReviewReceiptPath,
            }) start.ArgumentList.Add(argument);

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }

        public void TamperCollectedEvidence(string name)
            => File.AppendAllText(Path.Combine(EvidenceRoot, name), "tampered");

        private (string Preview, string Thumbnail) WriteRuntimeProbeEvidence()
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var fixture = Path.Combine(RuntimeProbeDirectory, "runtime-probe.step");
            File.WriteAllBytes(fixture, CreateBytes(1600, 81));
            var fixtureHash = HashFile(fixture);
            File.WriteAllText(
                Path.Combine(RuntimeProbeDirectory, "app-group-writer-probe.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    status = "passed",
                    action = "prepare",
                    nonce,
                    processIdentifier = 1001,
                    expectedPreferences = new { dxf = false, step = true, stch = false },
                    observedPreferences = new { dxf = false, step = true, stch = false },
                }));
            var preview = Path.Combine(
                RuntimeProbeDirectory,
                "quicklook-preview-runtime-probe.json");
            File.WriteAllText(preview, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                status = "passed",
                providerKind = "preview",
                bundleIdentifier = "com.pathstitch.crossport.quicklook",
                processIdentifier = 2001,
                nonce,
                fixture = Path.GetFileName(fixture),
                fixtureSha256 = fixtureHash,
                observedPreferences = new { dxf = false, step = true, stch = false },
                rendered = true,
                interactiveSceneKit = true,
                sceneViewInstalled = true,
                cameraControlEnabled = true,
                fallbackImageInstalled = false,
                vertexCount = 24,
                triangleCount = 12,
            }));
            var thumbnail = Path.Combine(
                RuntimeProbeDirectory,
                "quicklook-thumbnail-runtime-probe.json");
            File.WriteAllText(thumbnail, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                status = "passed",
                providerKind = "thumbnail",
                bundleIdentifier = "com.pathstitch.crossport.thumbnail",
                processIdentifier = 2002,
                nonce,
                fixture = Path.GetFileName(fixture),
                fixtureSha256 = fixtureHash,
                observedPreferences = new { dxf = false, step = true, stch = false },
                rendered = true,
                interactiveSceneKit = false,
                sceneViewInstalled = false,
                cameraControlEnabled = false,
                fallbackImageInstalled = true,
                vertexCount = 0,
                triangleCount = 0,
            }));
            File.WriteAllText(
                Path.Combine(RuntimeProbeDirectory, "app-group-collector-probe.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    status = "passed",
                    action = "collect",
                    nonce,
                    processIdentifier = 1002,
                    expectedPreferences = new { dxf = false, step = true, stch = false },
                    collectedFiles = new[]
                    {
                        DescribeArtifact(preview),
                        DescribeArtifact(thumbnail),
                    },
                }));
            return (preview, thumbnail);
        }

        private Dictionary<string, object?> WriteProjectArchiveArtifact(string name, byte[] generatedDxf)
        {
            var path = Path.Combine(PackageAcceptanceDirectory, name);
            CreateProjectArchive(path, generatedDxf);
            return DescribeArtifact(path);
        }

        private Dictionary<string, object?> WritePackageDxfArtifact(string name)
        {
            var path = Path.Combine(PackageAcceptanceDirectory, name);
            File.WriteAllBytes(path, CreateMetricDxfBytes());
            return DescribeDxfArtifact(path);
        }

        private Dictionary<string, object?> WritePackageArtifact(string name, int size, int seed)
        {
            var path = Path.Combine(PackageAcceptanceDirectory, name);
            File.WriteAllBytes(path, CreateBytes(size, seed));
            return DescribeArtifact(path);
        }

        private static Dictionary<string, object?> DescribeArtifact(string path)
            => new()
            {
                ["path"] = path,
                ["bytes"] = new FileInfo(path).Length,
                ["sha256"] = HashFile(path),
            };

        private static Dictionary<string, object?> DescribeDxfArtifact(string path)
            => new(DescribeArtifact(path))
            {
                ["insUnitsCode"] = 4,
                ["measurementCode"] = 1,
            };

        private static byte[] CreateMetricDxfBytes(
            int insUnitsCode = 4,
            int measurementCode = 1,
            bool includeInsUnits = true,
            bool includeMeasurement = true,
            bool measurementInEntities = false,
            bool duplicateInsUnits = false)
        {
            var lines = new List<string> { "0", "SECTION", "2", "HEADER" };
            if (includeInsUnits)
                lines.AddRange(["9", "$INSUNITS", "70", insUnitsCode.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            if (duplicateInsUnits)
                lines.AddRange(["9", "$INSUNITS", "70", "1"]);
            if (includeMeasurement)
                lines.AddRange(["9", "$MEASUREMENT", "70", measurementCode.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            lines.AddRange(["999", new string('x', 120), "0", "ENDSEC", "0", "SECTION", "2", "ENTITIES"]);
            if (measurementInEntities)
                lines.AddRange(["9", "$MEASUREMENT", "70", measurementCode.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            lines.AddRange(["0", "ENDSEC", "0", "EOF"]);
            return System.Text.Encoding.ASCII.GetBytes(string.Join("\n", lines) + "\n");
        }

        private static void CreateProjectArchive(string path, byte[] generatedDxf)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            var entry = archive.CreateEntry("project.json", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(JsonSerializer.Serialize(new
            {
                dxfDataBase64 = Convert.ToBase64String(generatedDxf),
            }));
        }

        private static void CreateZip(string path, string entryName, int size, int seed)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var output = entry.Open();
            output.Write(CreateBytes(size, seed));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch { }
        }

        private static byte[] CreateBytes(int size, int seed)
        {
            var bytes = new byte[size];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        private static string HashFile(string path)
            => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        private static string FindRepositoryDirectory()
            => Path.GetDirectoryName(FindRepositoryFile("PathstitchCross.slnx"))!;

        private static string FindRepositoryFile(params string[] parts)
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
