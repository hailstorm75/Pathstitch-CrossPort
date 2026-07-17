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
        Assert.DoesNotContain("com.pathstitch.project", values);
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
        Assert.Contains("Assert-Arm64MachO", packaging, StringComparison.Ordinal);
        Assert.Contains("Assert-NoDeveloperRuntimeDependencies", packaging, StringComparison.Ordinal);
        Assert.Contains("stapler validate", packaging, StringComparison.Ordinal);
        Assert.Contains("stapled app archive recreation failed", packaging, StringComparison.Ordinal);

        var smoke = Read("scripts", "macos", "quicklook", "PreviewGeometrySmoke.swift");
        Assert.Contains("DXFParser.parse(url:", smoke, StringComparison.Ordinal);
        Assert.Contains("loadStepMesh(url:", smoke, StringComparison.Ordinal);
        Assert.Contains("renderStepMeshToImage", smoke, StringComparison.Ordinal);
        Assert.Contains("dxfInk > 100, stepInk > 100", smoke, StringComparison.Ordinal);
        Assert.Contains("writePNG(dxfImage", smoke, StringComparison.Ordinal);
        Assert.Contains("writePNG(stepImage", smoke, StringComparison.Ordinal);

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
        Assert.Contains("Quick Look did not generate DXF and STEP fixture thumbnails", workflow, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_MACOS_FILE_ACTIVATION_OUTPUT", workflow, StringComparison.Ordinal);
        Assert.Contains("open -a $app $fixture", workflow, StringComparison.Ordinal);
        Assert.Contains("file-activation-acceptance.json", workflow, StringComparison.Ordinal);
        Assert.Contains("Running app did not receive the Finder file activation", workflow, StringComparison.Ordinal);
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
