namespace Pathstitch.App.Tests;

using Pathstitch.App.Services;

public sealed class ViewportAssetTests
{
    [Fact]
    public async Task OpenGeometryProjection_DoesNotFallbackToRawProjectedSketchOutput()
    {
        var servicePath = FindRepositoryFile("src", "Pathstitch.App", "Services", "OpenGeometryEditor3DOperationService.cs");

        var source = await File.ReadAllTextAsync(servicePath);

        Assert.DoesNotContain("PROJECTED_SKETCH", source, StringComparison.Ordinal);
        Assert.DoesNotContain("raw projected mesh edges", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kernelOffsetResult.IsSuccess", source, StringComparison.Ordinal);
        Assert.Contains("kernelOffsetResult.Error", source, StringComparison.Ordinal);
        Assert.Contains("OPEN_GEOMETRY_OFFSET", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenGeometryWorker_OffsetsProjectedEdgesAsOnePolylineGroup()
    {
        var workerPath = FindRepositoryFile("src", "Pathstitch.App", "Assets", "OpenGeometry", "opengeometry-worker.mjs");

        var source = await File.ReadAllTextAsync(workerPath);

        Assert.Contains("offsetPolylineGroupRegions", source, StringComparison.Ordinal);
        Assert.Contains("JSON.stringify(kernelPolylines)", source, StringComparison.Ordinal);
        Assert.Contains("OGPolyline", source, StringComparison.Ordinal);
        Assert.Contains("get_offset_serialized", source, StringComparison.Ordinal);
        Assert.DoesNotContain("offsetPolylineRegions", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoDAddThickness_RoutesThroughOpenGeometryKernelService()
    {
        var workspaceOperationsPath = FindRepositoryFile("src", "Domain", "Domain.App", "ViewModels", "Editor2DWorkspaceViewModel.Operations.cs");

        var source = await File.ReadAllTextAsync(workspaceOperationsPath);

        Assert.Contains("IEditor2DGeometryKernelService", source, StringComparison.Ordinal);
        Assert.Contains("BuildThicknessOutlinesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryBuildThicknessPath(path", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoDOffset_RoutesThroughOpenGeometryKernelService()
    {
        var workspaceOperationsPath = FindRepositoryFile("src", "Domain", "Domain.App", "ViewModels", "Editor2DWorkspaceViewModel.Operations.cs");

        var source = await File.ReadAllTextAsync(workspaceOperationsPath);

        Assert.Contains("IEditor2DGeometryKernelService", source, StringComparison.Ordinal);
        Assert.Contains("BuildCurveOffsetPathsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryBuildCurveOffsetPath(path", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Viewport_AllowsMeshFaceProjectionSelection()
    {
        var viewportPath = FindRepositoryFile("src", "Pathstitch.App", "Assets", "Web", "viewport3d.html");

        var html = await File.ReadAllTextAsync(viewportPath);

        Assert.DoesNotContain("type !== \"Plane\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type === \"Plane\"", html, StringComparison.Ordinal);
        Assert.Contains("Compute a mesh face normal and origin", html, StringComparison.Ordinal);
        Assert.Contains("Make mesh faces hoverable", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Swift", html, StringComparison.Ordinal);
        Assert.DoesNotContain("python", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewportLocator_LoadsAvaloniaOwnedHtmlWithHostBridge()
    {
        var locator = new EditorViewportAssetLocator();

        var html = locator.GetViewportHtml();
        var baseUri = locator.GetViewportBaseUri();

        Assert.Contains("window.webkit.messageHandlers.pathstitch", html, StringComparison.Ordinal);
        Assert.Contains("vendor/three.min.js", html, StringComparison.Ordinal);
        Assert.Contains("Compute a mesh face normal and origin", html, StringComparison.Ordinal);
        Assert.EndsWith("/Assets/Web/", baseUri.AbsoluteUri.Replace('\\', '/'), StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file {Path.Combine(pathParts)}.");
    }
}
