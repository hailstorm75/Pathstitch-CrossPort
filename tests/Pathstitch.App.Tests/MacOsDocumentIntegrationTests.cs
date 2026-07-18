using System.Xml.Linq;

namespace Pathstitch.App.Tests;

public sealed class MacOsDocumentIntegrationTests
{
    private static readonly string[] ContentTypes =
    [
        "com.pathstitch.project",
        "com.pathstitch.dxf",
        "com.pathstitch.step",
    ];

    [Fact]
    public void ApplicationPlistRegistersProjectDxfAndStepWithFinderRoles()
    {
        var plist = LoadPlist("scripts", "macos", "Info.plist");
        var values = StringValues(plist);

        Assert.All(ContentTypes, type => Assert.Contains(type, values));
        Assert.Contains("stch", values);
        Assert.Contains("dxf", values);
        Assert.Contains("step", values);
        Assert.Contains("stp", values);
        Assert.Contains("Pathstitch", values);
        Assert.Contains("Editor", values);
        Assert.Contains("Viewer", values);
        Assert.Contains("Owner", values);
        Assert.Contains("Alternate", values);
        Assert.Contains("application/x-pathstitch", values);
        Assert.Contains("model/step", values);
    }

    [Theory]
    [InlineData("PreviewInfo.plist", "com.apple.quicklook.preview", "PathstitchQuickLook.PreviewProvider")]
    [InlineData("ThumbnailInfo.plist", "com.apple.quicklook.thumbnail", "PathstitchThumbnail.ThumbnailProvider")]
    public void QuickLookExtensionPlistsSupportTheVerifiedPreviewContentTypes(
        string fileName,
        string extensionPoint,
        string principalClass)
    {
        var plist = LoadPlist("scripts", "macos", "quicklook", fileName);
        var values = StringValues(plist);

        Assert.Contains(extensionPoint, values);
        Assert.Contains(principalClass, values);
        Assert.Contains("com.pathstitch.dxf", values);
        Assert.Contains("com.pathstitch.step", values);
        Assert.Contains("com.pathstitch.project", values);
        Assert.Contains("13.0", values);
    }

    [Fact]
    public void PreviewAndThumbnailProvidersRenderDxfProjectAndStepGeometry()
    {
        var preview = Read("scripts", "macos", "quicklook", "PreviewProvider.swift");
        Assert.Contains("QLPreviewingController", preview, StringComparison.Ordinal);
        Assert.Contains("preparePreviewOfFile", preview, StringComparison.Ordinal);
        Assert.Contains("loadStepMesh(url:", preview, StringComparison.Ordinal);
        Assert.Contains("renderStepMeshToImage", preview, StringComparison.Ordinal);
        Assert.Contains("renderStepToImage", preview, StringComparison.Ordinal);
        Assert.Contains("renderFileToImage", preview, StringComparison.Ordinal);

        var thumbnail = Read("scripts", "macos", "quicklook", "ThumbnailProvider.swift");
        Assert.Contains("QLThumbnailProvider", thumbnail, StringComparison.Ordinal);
        Assert.Contains("QLThumbnailReply(contextSize:", thumbnail, StringComparison.Ordinal);
        Assert.Contains("loadStepMesh(url:", thumbnail, StringComparison.Ordinal);
        Assert.Contains("renderStepMeshToImage", thumbnail, StringComparison.Ordinal);
        Assert.Contains("renderStepToImage", thumbnail, StringComparison.Ordinal);
        Assert.Contains("renderFileToImage", thumbnail, StringComparison.Ordinal);

        var dxfRenderer = Read("Pathstitch", "DxfPreviewer", "DxfPreviewShared.swift");
        Assert.Contains("DXFParser.parse(url:", dxfRenderer, StringComparison.Ordinal);
        Assert.Contains("stchEmbeddedPreview(url:", dxfRenderer, StringComparison.Ordinal);
        Assert.Contains("stchEmbeddedDXF(url:", dxfRenderer, StringComparison.Ordinal);
        Assert.Contains("renderEntitiesToImage", dxfRenderer, StringComparison.Ordinal);

        var stepRenderer = Read("Pathstitch", "DxfPreviewer", "StepPreviewShared.swift");
        Assert.Contains("step_mesh_load", stepRenderer, StringComparison.Ordinal);
        Assert.Contains("mesh.indices.count / 3", stepRenderer, StringComparison.Ordinal);
        Assert.Contains("ctx.fillPath()", stepRenderer, StringComparison.Ordinal);
    }

    [Fact]
    public void BundlePipelineCompilesEmbedsSignsAndSmokeTestsQuickLookExtensions()
    {
        var packaging = Read("scripts", "package-avalonia-macos.ps1");
        Assert.Contains("Build-QuickLookExtension", packaging, StringComparison.Ordinal);
        Assert.Contains("xcrun swiftc", packaging, StringComparison.Ordinal);
        Assert.Contains("PathstitchQuickLook.appex", packaging.Replace("$Name", "PathstitchQuickLook"), StringComparison.Ordinal);
        Assert.Contains("DxfPreviewShared.swift", packaging, StringComparison.Ordinal);
        Assert.Contains("StepPreviewShared.swift", packaging, StringComparison.Ordinal);
        Assert.Contains("PreviewGeometrySmoke.swift", packaging, StringComparison.Ordinal);
        Assert.Contains("preview-smoke.dxf", packaging, StringComparison.Ordinal);
        Assert.Contains("analytic-multibody-hole.step", packaging, StringComparison.Ordinal);
        Assert.Contains("codesign --verify --deep", packaging, StringComparison.Ordinal);
        Assert.DoesNotContain("codesign --force --deep", packaging, StringComparison.Ordinal);
        Assert.Contains("PathstitchQuickLook.entitlements", packaging, StringComparison.Ordinal);
        Assert.Contains("libPathstitchMacBridge.dylib", packaging, StringComparison.Ordinal);
        Assert.Contains("AppIconLight.png", packaging, StringComparison.Ordinal);
        Assert.Contains("AppIconDark.png", packaging, StringComparison.Ordinal);
        Assert.Contains("Pathstitch.icns", packaging, StringComparison.Ordinal);
        Assert.Contains("iconutil -c icns", packaging, StringComparison.Ordinal);
        Assert.Contains("Assert-Arm64MachO", packaging, StringComparison.Ordinal);
        Assert.Contains("Assert-NoDeveloperRuntimeDependencies", packaging, StringComparison.Ordinal);
        Assert.Contains("stapler validate", packaging, StringComparison.Ordinal);
        Assert.Contains("stapled app archive recreation failed", packaging, StringComparison.Ordinal);
        Assert.Contains("22787ffb59de99e5dc1fbfe80b19c97a904ad48d", packaging, StringComparison.Ordinal);
        Assert.Contains("$zipFoundationSources", packaging, StringComparison.Ordinal);
        Assert.Contains("arm64-apple-macos13.0", packaging, StringComparison.Ordinal);
        Assert.Contains("PruneNativeInputsBeforeArchive", packaging, StringComparison.Ordinal);
        Assert.Contains("Nested Mach-O signing failed", packaging, StringComparison.Ordinal);

        var smoke = Read("scripts", "macos", "quicklook", "PreviewGeometrySmoke.swift");
        Assert.Contains("DXFParser.parse(url:", smoke, StringComparison.Ordinal);
        Assert.Contains("loadStepMesh(url:", smoke, StringComparison.Ordinal);
        Assert.Contains("renderStepMeshToImage", smoke, StringComparison.Ordinal);
        Assert.Contains("dxfInk > 100, stepInk > 100", smoke, StringComparison.Ordinal);
        Assert.Contains("writePNG(dxfImage", smoke, StringComparison.Ordinal);
        Assert.Contains("writePNG(stepImage", smoke, StringComparison.Ordinal);
        Assert.Contains("stchEmbeddedDXF(url:", smoke, StringComparison.Ordinal);
        Assert.Contains("stchInk > 100", smoke, StringComparison.Ordinal);
        Assert.Contains("writePNG(stchImage", smoke, StringComparison.Ordinal);

        var workflow = Read(".github", "workflows", "macos-release.yml");
        Assert.Contains("lsregister", workflow, StringComparison.Ordinal);
        Assert.Contains("pluginkit -m -A -D", workflow, StringComparison.Ordinal);
        Assert.Contains("com.pathstitch.crossport.quicklook", workflow, StringComparison.Ordinal);
        Assert.Contains("com.pathstitch.crossport.thumbnail", workflow, StringComparison.Ordinal);
        Assert.Contains("qlmanage -t", workflow, StringComparison.Ordinal);
        Assert.Contains("sample.dxf", workflow, StringComparison.Ordinal);
        Assert.Contains("sample.step", workflow, StringComparison.Ordinal);
        Assert.Contains("preview-smoke/dxf-preview.png", workflow, StringComparison.Ordinal);
        Assert.Contains("preview-smoke/step-preview.png", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", workflow, StringComparison.Ordinal);
        Assert.Contains("Quick Look did not generate exactly one STCH, DXF, and STEP fixture thumbnail", workflow, StringComparison.Ordinal);
        Assert.Contains("stch-preview.png", workflow, StringComparison.Ordinal);
        Assert.Contains("quicklook-output-map.json", workflow, StringComparison.Ordinal);
        var validator = Read("scripts", "collect-native-macos-evidence.ps1");
        Assert.Contains("schemaVersion = 4", validator, StringComparison.Ordinal);
        Assert.Contains("validationErrors = @($validationErrors)", validator, StringComparison.Ordinal);
        Assert.Contains("status = $status", validator, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_MACOS_FILE_ACTIVATION_OUTPUT", workflow, StringComparison.Ordinal);
        Assert.Contains("open -a $app $fixture", workflow, StringComparison.Ordinal);
        Assert.Contains("file-activation-acceptance.json", workflow, StringComparison.Ordinal);
        Assert.Contains("Running app did not receive the Finder file activation", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void FinderPreferencesAndSpotlightDiscoveryUsePackagedMacIntegration()
    {
        var preferences = Read("scripts", "macos", "quicklook", "QuickLookPreferences.swift");
        Assert.Contains("UserDefaults(suiteName: appGroupIdentifier)", preferences, StringComparison.Ordinal);
        Assert.Contains("quicklook.preview.enabled.dxf", preferences, StringComparison.Ordinal);
        Assert.Contains("quicklook.preview.enabled.step", preferences, StringComparison.Ordinal);
        Assert.Contains("quicklook.preview.enabled.stch", preferences, StringComparison.Ordinal);
        Assert.Contains("object(forKey: key) != nil", preferences, StringComparison.Ordinal);

        var bridge = Read("scripts", "macos", "PathstitchMacBridge.swift");
        Assert.Contains("@_cdecl(\"pathstitch_set_quicklook_preferences\")", bridge, StringComparison.Ordinal);
        Assert.Contains("@_cdecl(\"pathstitch_get_quicklook_preference\")", bridge, StringComparison.Ordinal);
        Assert.Contains("@_cdecl(\"pathstitch_apply_app_icon\")", bridge, StringComparison.Ordinal);
        Assert.Contains("Bundle.main.path(forResource:", bridge, StringComparison.Ordinal);
        Assert.Contains("effectiveAppearance", bridge, StringComparison.Ordinal);

        var appEntitlements = Read("scripts", "macos", "Pathstitch.entitlements");
        var extensionEntitlements = Read(
            "scripts",
            "macos",
            "quicklook",
            "PathstitchQuickLook.entitlements");
        Assert.Contains("group.com.pathstitch.crossport", appEntitlements, StringComparison.Ordinal);
        Assert.Contains("group.com.pathstitch.crossport", extensionEntitlements, StringComparison.Ordinal);
        Assert.Contains("com.apple.security.app-sandbox", extensionEntitlements, StringComparison.Ordinal);

        var discovery = Read(
            "src",
            "Pathstitch.App",
            "Services",
            "MacOSSpotlightProjectDiscoveryProvider.cs");
        Assert.Contains("/usr/bin/mdfind", discovery, StringComparison.Ordinal);
        Assert.Contains("kMDItemFSName == \\\"*.stch\\\"c", discovery, StringComparison.Ordinal);
        Assert.Contains("CancelAfter(DiscoveryTimeout)", discovery, StringComparison.Ordinal);
        Assert.Contains("ArgumentList.Add(\"-0\")", discovery, StringComparison.Ordinal);
        Assert.Contains("WatchAsync", discovery, StringComparison.Ordinal);
        Assert.Contains("PeriodicTimer(RefreshInterval)", discovery, StringComparison.Ordinal);
        Assert.Contains("SnapshotsEqual", discovery, StringComparison.Ordinal);
    }

    private static XDocument LoadPlist(params string[] pathParts)
        => XDocument.Load(RepositoryFile(pathParts));

    private static HashSet<string> StringValues(XDocument document)
        => document.Descendants("string")
            .Select(element => element.Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string Read(params string[] pathParts)
        => File.ReadAllText(RepositoryFile(pathParts));

    private static string RepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(Path.Combine(pathParts));
    }
}
