namespace Pathstitch.App.Tests;

public sealed class MacOSDeliveryAcceptanceTests
{
    [Fact]
    public void PackagedAppAcceptance_ExercisesNativeUiMacBridgeStepLifecycleAndStchRoundTrip()
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
        Assert.Contains("MacOSIntegrationService.TrySetQuickLookPreferences", harness, StringComparison.Ordinal);
        Assert.Contains("MacOSIntegrationService.TryGetQuickLookPreference", harness, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Serialize(reopened.StepTopology)", harness, StringComparison.Ordinal);
        Assert.Contains("MacOSIntegrationService.TryApplyDockIcon", harness, StringComparison.Ordinal);
        Assert.Contains("IStepGeometryKernelService", harness, StringComparison.Ordinal);
        Assert.Contains("ImportAsync", harness, StringComparison.Ordinal);
        Assert.Contains("ProjectAsync", harness, StringComparison.Ordinal);
        Assert.Contains("UnfoldAsync", harness, StringComparison.Ordinal);
        Assert.Contains("packaged-projection.dxf", harness, StringComparison.Ordinal);
        Assert.Contains("packaged-unfold.dxf", harness, StringComparison.Ordinal);
        Assert.Contains("ValidateCanonicalMillimeterDxf", harness, StringComparison.Ordinal);
        Assert.Contains("ReadUnitMetadata", harness, StringComparison.Ordinal);
        Assert.Contains("$MEASUREMENT", harness, StringComparison.Ordinal);
        Assert.Contains("DescribeDxfFileAsync", harness, StringComparison.Ordinal);
        Assert.Contains("insUnitsValues.Count != 1", harness, StringComparison.Ordinal);
        Assert.Contains("StepTopology: imported.Document", harness, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_MACOS_ACCEPTANCE_OUTPUT", workflow, StringComparison.Ordinal);
        Assert.Contains("packaged-app-acceptance.json", workflow, StringComparison.Ordinal);
        Assert.Contains("viewportPreserved", workflow, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_MACOS_ACCEPTANCE_STEP_FIXTURE", workflow, StringComparison.Ordinal);
        Assert.Contains("stepWorkflow.imported", workflow, StringComparison.Ordinal);
        Assert.Contains("stepWorkflow.projected", workflow, StringComparison.Ordinal);
        Assert.Contains("stepWorkflow.unfolded", workflow, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_MACOS_ACCEPTANCE_OBJ_FIXTURE", workflow, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_MACOS_ACCEPTANCE_STL_FIXTURE", workflow, StringComparison.Ordinal);
        Assert.Contains("RunMeshWorkflowAsync", harness, StringComparison.Ordinal);
        Assert.Contains("packaged-input.step", harness, StringComparison.Ordinal);
        Assert.Contains("packaged-input.obj", harness, StringComparison.Ordinal);
        Assert.Contains("packaged-input.stl", harness, StringComparison.Ordinal);
        Assert.Contains("packaged-mixed.step", harness, StringComparison.Ordinal);
        Assert.Contains("packaged-obj-connected.dxf", harness, StringComparison.Ordinal);
        Assert.Contains("GLUE_TABS", harness, StringComparison.Ordinal);
        Assert.Contains("SEW_HOLES", harness, StringComparison.Ordinal);
        Assert.Contains("meshWorkflow.mixedBodyCount", workflow, StringComparison.Ordinal);
        Assert.Contains("meshWorkflow.connectedUnfolded", workflow, StringComparison.Ordinal);
        Assert.Contains("meshWorkflow.tabsDecorated", workflow, StringComparison.Ordinal);
        Assert.Contains("meshWorkflow.holesDecorated", workflow, StringComparison.Ordinal);
        Assert.Contains("collect-native-macos-evidence.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("topologyPreserved", workflow, StringComparison.Ordinal);
        Assert.Contains("sourceModelPreserved", workflow, StringComparison.Ordinal);
        Assert.Contains("generatedOutputPreserved", workflow, StringComparison.Ordinal);
        Assert.Contains("unzip -t", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickLookProviders_RenderStchDxfAndFoxtrotStepGeometry()
    {
        var preview = File.ReadAllText(Find("scripts", "macos", "quicklook", "PreviewProvider.swift"));
        var thumbnail = File.ReadAllText(Find("scripts", "macos", "quicklook", "ThumbnailProvider.swift"));
        var interactiveStep = File.ReadAllText(
            Find("scripts", "macos", "quicklook", "InteractiveStepPreview.swift"));
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

        Assert.Contains("makeInteractiveStepPreview", preview, StringComparison.Ordinal);
        Assert.Contains("import SceneKit", interactiveStep, StringComparison.Ordinal);
        Assert.Contains("allowsCameraControl = true", interactiveStep, StringComparison.Ordinal);
        Assert.Contains("smoothPreviewNormals", interactiveStep, StringComparison.Ordinal);
        Assert.Contains("InteractiveStepPreview.swift", package, StringComparison.Ordinal);
        Assert.Contains("DxfPreviewShared.swift", package, StringComparison.Ordinal);
        Assert.Contains("StepPreviewShared.swift", package, StringComparison.Ordinal);
        Assert.Contains("libstep_mesh.a", package, StringComparison.Ordinal);
        Assert.Contains("PreviewGeometrySmoke.swift", package, StringComparison.Ordinal);
        Assert.Contains("22787ffb59de99e5dc1fbfe80b19c97a904ad48d", package, StringComparison.Ordinal);
        Assert.Contains("$zipFoundationSources", package, StringComparison.Ordinal);
        Assert.Contains("arm64-apple-macos13.0", package, StringComparison.Ordinal);
        Assert.Contains("Nested Mach-O signing failed", package, StringComparison.Ordinal);
        Assert.Contains("dxf-preview.png", workflow, StringComparison.Ordinal);
        Assert.Contains("step-preview.png", workflow, StringComparison.Ordinal);
        Assert.Contains("analytic-multibody-hole.step", workflow, StringComparison.Ordinal);
        Assert.Contains("stch-preview.png", workflow, StringComparison.Ordinal);
        Assert.Contains("sample.stch", workflow, StringComparison.Ordinal);
        Assert.Contains("@('.dxf','.step','.stch')", workflow, StringComparison.Ordinal);
        Assert.Contains("Pathstitch-native-evidence-osx-arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("quicklook-output-map.json", workflow, StringComparison.Ordinal);
        var validator = File.ReadAllText(Find("scripts", "collect-native-macos-evidence.ps1"));
        Assert.Contains("native-evidence-manifest.json", validator, StringComparison.Ordinal);
        Assert.Contains("codesign-details.txt", validator, StringComparison.Ordinal);
        Assert.Contains("codesign-verification.txt", validator, StringComparison.Ordinal);
        Assert.Contains("Project round-trip source hashes do not match.", validator, StringComparison.Ordinal);
        Assert.Contains("packaged-input.step", validator, StringComparison.Ordinal);
        Assert.Contains("packaged-input.obj", validator, StringComparison.Ordinal);
        Assert.Contains("packaged-input.stl", validator, StringComparison.Ordinal);
        Assert.Contains("nested-codesign-details.txt", validator, StringComparison.Ordinal);
        Assert.Contains("schemaVersion = 5", validator, StringComparison.Ordinal);
        Assert.Contains("Quick Look PNG SHA-256 mismatch", validator, StringComparison.Ordinal);
        Assert.Contains("sample-dxf-qlmanage.log", validator, StringComparison.Ordinal);
        Assert.Contains("sample-step-qlmanage.log", validator, StringComparison.Ordinal);
        Assert.Contains("sample-stch-qlmanage.log", validator, StringComparison.Ordinal);
        Assert.Contains("com.pathstitch.crossport.quicklook", validator, StringComparison.Ordinal);
        Assert.Contains("com.pathstitch.crossport.thumbnail", validator, StringComparison.Ordinal);
        Assert.Contains("Pathstitch-osx-arm64.zip", validator, StringComparison.Ordinal);
        Assert.Contains("evidenceFiles = $inventory", validator, StringComparison.Ordinal);
        Assert.Contains("dxfUnitEvidence = $dxfUnitEvidence", validator, StringComparison.Ordinal);
        Assert.Contains("embeddedProjectDxfEvidence", validator, StringComparison.Ordinal);
        Assert.Contains("runtimeProbeEvidence = $runtimeProbeEvidence", validator, StringComparison.Ordinal);
        Assert.Contains("exactly one HEADER", validator, StringComparison.Ordinal);
        Assert.Contains("quicklook-preview-runtime-probe.json", validator, StringComparison.Ordinal);
        Assert.Contains("'cameraControlEnabled'", validator, StringComparison.Ordinal);
        Assert.Contains("App-group collector must run in a separate identified app process.", validator, StringComparison.Ordinal);
        var runtimeProbe = File.ReadAllText(Find("scripts", "macos", "quicklook", "QuickLookRuntimeProbe.swift"));
        var sceneInstall = preview.IndexOf("replaceContent(with: sceneView)", StringComparison.Ordinal);
        var previewAttestation = preview.IndexOf("providerKind: \"preview\"", sceneInstall, StringComparison.Ordinal);
        Assert.True(sceneInstall >= 0 && previewAttestation > sceneInstall);
        Assert.Contains("cameraControlEnabled: sceneView.allowsCameraControl", preview, StringComparison.Ordinal);
        Assert.Equal(runtimeProbe.Count(character => character == '{'), runtimeProbe.Count(character => character == '}'));
        Assert.Contains("forSecurityApplicationGroupIdentifier:", runtimeProbe, StringComparison.Ordinal);
        Assert.Contains("quicklook.runtimeProbe.nonce", runtimeProbe, StringComparison.Ordinal);
        Assert.Contains("data.write(to: destination, options: .atomic)", runtimeProbe, StringComparison.Ordinal);
        Assert.Contains("@('-p', $fixture)", workflow, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_MACOS_RUNTIME_PROBE_ACTION", workflow, StringComparison.Ordinal);
        var bridge = File.ReadAllText(Find("scripts", "macos", "PathstitchMacBridge.swift"));
        Assert.Contains("pathstitch_prepare_quicklook_runtime_probe", bridge, StringComparison.Ordinal);
        Assert.Contains("pathstitch_collect_quicklook_runtime_probe", bridge, StringComparison.Ordinal);
        Assert.Contains("pathstitch_cancel_quicklook_runtime_probe", bridge, StringComparison.Ordinal);
        Assert.Contains("runtimeProbeOriginalPreferencesKey", bridge, StringComparison.Ordinal);
        Assert.Contains("\"present\": present", bridge, StringComparison.Ordinal);
        Assert.Contains("defaults.removeObject(forKey: key)", bridge, StringComparison.Ordinal);
        Assert.Contains("pathstitchCleanupRuntimeProbe(defaults, container, nonce) ? 1 : -1", bridge, StringComparison.Ordinal);
        var integration = File.ReadAllText(
            Find("src", "Pathstitch.App", "Services", "MacOSPlatformPreferences.cs"));
        var acceptance = File.ReadAllText(
            Find("src", "Pathstitch.App", "MacOSQuickLookRuntimeProbeAcceptance.cs"));
        Assert.Contains("MacOSQuickLookRuntimeProbeCollectionResult.NotReady", integration, StringComparison.Ordinal);
        Assert.Contains("pathstitch_cancel_quicklook_runtime_probe", integration, StringComparison.Ordinal);
        Assert.Contains("TryCancelQuickLookRuntimeProbe", acceptance, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeEvidenceReview_IsIndependentIdentityBoundAndNonMutating()
    {
        var reviewer = File.ReadAllText(Find("scripts", "verify-native-macos-evidence.ps1"));
        var workflow = File.ReadAllText(Find(".github", "workflows", "macos-release.yml"));

        Assert.Contains("ExpectedCommit", reviewer, StringComparison.Ordinal);
        Assert.Contains("ExpectedWorkflowRunId", reviewer, StringComparison.Ordinal);
        Assert.Contains("Producer inventory SHA-256 differs", reviewer, StringComparison.Ordinal);
        Assert.Contains("ReceiptPath must be outside EvidenceRoot", reviewer, StringComparison.Ordinal);
        Assert.Contains("collect-native-macos-evidence.ps1", reviewer, StringComparison.Ordinal);
        Assert.Contains("Pathstitch-Native-Evidence-Review-", reviewer, StringComparison.Ordinal);
        Assert.Contains("retention-days: 90", workflow, StringComparison.Ordinal);
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
