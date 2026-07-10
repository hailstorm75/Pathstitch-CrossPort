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
    public void QuickLookExtensionPlistsSupportEveryRegisteredContentType(
        string fileName,
        string extensionPoint,
        string principalClass)
    {
        var plist = LoadPlist("scripts", "macos", "quicklook", fileName);
        var values = StringValues(plist);

        Assert.Contains(extensionPoint, values);
        Assert.Contains(principalClass, values);
        Assert.All(ContentTypes, type => Assert.Contains(type, values));
        Assert.Contains("13.0", values);
    }

    [Fact]
    public void PreviewAndThumbnailProvidersGenerateContentInsteadOfReturningPlaceholders()
    {
        var preview = Read("scripts", "macos", "quicklook", "PreviewProvider.swift");
        Assert.Contains("QLPreviewingController", preview, StringComparison.Ordinal);
        Assert.Contains("preparePreviewOfFile", preview, StringComparison.Ordinal);
        Assert.Contains("Data(contentsOf:", preview, StringComparison.Ordinal);
        Assert.Contains("Pathstitch project archive", preview, StringComparison.Ordinal);

        var thumbnail = Read("scripts", "macos", "quicklook", "ThumbnailProvider.swift");
        Assert.Contains("QLThumbnailProvider", thumbnail, StringComparison.Ordinal);
        Assert.Contains("QLThumbnailReply(contextSize:", thumbnail, StringComparison.Ordinal);
        Assert.Contains("NSBezierPath", thumbnail, StringComparison.Ordinal);
        Assert.Contains("extensionBadge", thumbnail, StringComparison.Ordinal);
    }

    [Fact]
    public void BundlePipelineCompilesEmbedsSignsAndSmokeTestsQuickLookExtensions()
    {
        var packaging = Read("scripts", "package-avalonia-macos.ps1");
        Assert.Contains("Build-QuickLookExtension", packaging, StringComparison.Ordinal);
        Assert.Contains("xcrun swiftc", packaging, StringComparison.Ordinal);
        Assert.Contains("PathstitchQuickLook.appex", packaging.Replace("$Name", "PathstitchQuickLook"), StringComparison.Ordinal);
        Assert.Contains("codesign --verify --deep", packaging, StringComparison.Ordinal);

        var workflow = Read(".github", "workflows", "macos-release.yml");
        Assert.Contains("lsregister", workflow, StringComparison.Ordinal);
        Assert.Contains("qlmanage -t", workflow, StringComparison.Ordinal);
        Assert.Contains("sample.stch", workflow, StringComparison.Ordinal);
        Assert.Contains("sample.dxf", workflow, StringComparison.Ordinal);
        Assert.Contains("sample.step", workflow, StringComparison.Ordinal);
        Assert.Contains("Quick Look did not generate all fixture thumbnails", workflow, StringComparison.Ordinal);
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
