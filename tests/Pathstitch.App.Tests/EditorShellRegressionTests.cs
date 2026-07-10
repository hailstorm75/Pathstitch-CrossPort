using System.Xml.Linq;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pathstitch.App.Tests;

public sealed class EditorShellRegressionTests
{
    [Fact]
    public async Task TwoDMode_HidesBodiesAndEveryThreeDInspector()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.True(viewModel.IsShowingTwoDWorkspace);
        Assert.False(viewModel.IsShowing3DWorkspace);
        Assert.False(viewModel.ShowSelectionPanel);
        Assert.False(viewModel.ShowMoveBodiesPanel);
        Assert.False(viewModel.ShowProjectionPanel);
        Assert.False(viewModel.ShowMeasurePanel);
        Assert.False(viewModel.ShowUnfoldPanel);
        Assert.False(viewModel.ShowOutputPanel);

        var shell = LoadPage("EditorShellView.axaml");
        AssertElementBinding(shell, "editor.context.3d-bodies", "IsVisible", "{Binding IsShowing3DWorkspace}");
        AssertElementBinding(shell, "editor.context.2d-layers", "IsVisible", "{Binding IsShowingTwoDWorkspace}");

        var inspectorBindings = new Dictionary<string, string>
        {
            ["EditorSelectionInspector.axaml"] = "{Binding ShowSelectionPanel}",
            ["EditorMoveInspector.axaml"] = "{Binding ShowMoveBodiesPanel}",
            ["EditorProjectionInspector.axaml"] = "{Binding ShowProjectionPanel}",
            ["EditorMeasureInspector.axaml"] = "{Binding ShowMeasurePanel}",
            ["EditorUnfoldInspector.axaml"] = "{Binding ShowUnfoldPanel}",
            ["EditorOutputInspector.axaml"] = "{Binding ShowOutputPanel}",
        };

        foreach (var (fileName, expectedBinding) in inspectorBindings)
        {
            var inspector = LoadPage(fileName);
            Assert.Contains(
                inspector.Descendants().Attributes("IsVisible"),
                attribute => attribute.Value == expectedBinding);
        }
    }

    [Fact]
    public async Task TwoDTools_AreRenderedByTheSharedCatalogDrivenRail()
    {
        var rail = LoadPage("EditorToolRail.axaml");
        Assert.Contains(
            rail.Descendants().Attributes("ItemsSource"),
            attribute => attribute.Value == "{Binding SidebarTools}");

        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.Equal(EditorToolCatalog.ForMode(EditorMode.TwoD).Count, viewModel.SidebarTools.Count);
        Assert.All(viewModel.SidebarTools, item =>
        {
            Assert.Equal(EditorMode.TwoD, item.Mode);
            Assert.True(item.TwoDTool is not null || item.Action is not null);
            Assert.Null(item.Tool);
        });
    }

    [Theory]
    [InlineData(EditorMode.TwoD)]
    [InlineData(EditorMode.ThreeD)]
    public async Task EveryVisibleTool_HasOneCommandAndActivationSelectsExactlyThatTool(EditorMode mode)
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(mode);
        var visibleTools = viewModel.SidebarTools.Where(item => item.TwoDTool is not null || item.Tool is not null).ToArray();

        Assert.NotEmpty(visibleTools);
        Assert.Equal(visibleTools.Length, visibleTools.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(visibleTools, item => Assert.False(string.IsNullOrWhiteSpace(item.Key)));

        foreach (var expected in visibleTools)
        {
            viewModel.ActivateSidebarItem(expected.Key);

            var activeTools = viewModel.SidebarTools
                .Where(item => item.TwoDTool is not null || item.Tool is not null)
                .Where(item => item.IsActive)
                .ToArray();
            var active = Assert.Single(activeTools);
            Assert.Equal(expected.Identifier, active.Identifier);
        }
    }

    [Fact]
    public async Task SwitchingModes_PreservesEachWorkspaceToolAndRestoresItsActiveRailItem()
    {
        var viewModel = CreateViewModel();

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.ActivateSidebarItem("circle");
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        viewModel.ActivateSidebarItem("move");
        Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);
        Assert.Equal("2d.circle", Assert.Single(viewModel.SidebarTools, item => item.IsActive).Identifier);

        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);
        Assert.Equal("3d.move", Assert.Single(viewModel.SidebarTools, item => item.IsActive).Identifier);
    }

    [Fact]
    public void ToolRail_IsVerticallyScrollableSoAllToolsRemainReachableAtNarrowHeights()
    {
        var rail = LoadPage("EditorToolRail.axaml");
        XNamespace avalonia = "https://github.com/avaloniaui";
        var items = Assert.Single(rail.Descendants(), element => element.Name == avalonia + "ItemsControl");
        var scrollViewer = items.Ancestors(avalonia + "ScrollViewer").FirstOrDefault();

        Assert.NotNull(scrollViewer);
        Assert.NotEqual("Disabled", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
    }

    private static void AssertElementBinding(XDocument document, string automationId, string attributeName, string expected)
    {
        var element = Assert.Single(document.Descendants(), element => element
            .Attributes()
            .Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && attribute.Value == automationId));
        Assert.Equal(expected, (string?)element.Attribute(attributeName));
    }

    private static XDocument LoadPage(string fileName)
        => XDocument.Load(FindRepositoryFile("src", "Pathstitch.App", "Pages", fileName));

    private static string FindRepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(pathParts)}");
    }

    private static EditorPageViewModel CreateViewModel()
        => new(
            NullLogger<EditorPageViewModel>.Instance,
            new StubViewportAssetLocator(),
            new Project3DStateService(),
            new StubProjectFileDialogService(),
            new StubOutputLauncherService(),
            new StubOutputPreviewService(),
            new Stub2DGeometryKernelService(),
            new Stub3DOperationService(),
            new StubGeometryKernelDescriptorProvider());

    private sealed class StubViewportAssetLocator : IEditorViewportAssetLocator
    {
        public string GetViewportHtml() => string.Empty;
        public Uri GetViewportBaseUri() => new("file:///viewport/");
    }

    private sealed class StubProjectFileDialogService : IProjectFileDialogService
    {
        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class StubOutputLauncherService : IEditorOutputLauncherService
    {
        public Task OpenOutputAsync(string outputPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevealOutputAsync(string outputPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubOutputPreviewService : IEditorOutputPreviewService
    {
        public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default) => Task.FromResult<Editor2DPreviewDocument?>(null);
        public Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default) => Task.FromResult<EditorGeneratedOutputSummary?>(null);
    }

    private sealed class Stub2DGeometryKernelService : IEditor2DGeometryKernelService
    {
        public Task<Editor2DGeometryKernelResult> BuildCurveOffsetPathsAsync(IReadOnlyList<Editor2DPreviewPath> sourcePaths, double offsetDistance, bool offsetOutward, CancellationToken cancellationToken = default) => Task.FromResult(Editor2DGeometryKernelResult.Success([]));
        public Task<Editor2DGeometryKernelResult> BuildThicknessOutlinesAsync(IReadOnlyList<Editor2DPreviewPath> sourcePaths, double thickness, CancellationToken cancellationToken = default) => Task.FromResult(Editor2DGeometryKernelResult.Success([]));
    }

    private sealed class Stub3DOperationService : IEditor3DOperationService
    {
        public Task<EditorModelLoadResult> LoadModelAsync(string sourceModelPath, CancellationToken cancellationToken = default) => Task.FromResult(new EditorModelLoadResult(false, "Not used."));
        public Task<EditorModelLoadResult> LoadModelsAsync(IReadOnlyList<string> sourceModelPaths, string? existingSourceModelPath = null, CancellationToken cancellationToken = default) => Task.FromResult(new EditorModelLoadResult(false, "Not used."));
        public Task<EditorFaceDistortionResult> ComputeFaceDistortionAsync(string? sourceModelPath, SelectedFace3D selectedFace, string distortionMode, CancellationToken cancellationToken = default) => Task.FromResult(new EditorFaceDistortionResult(false, "Not used."));
        public Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new EditorOperationResult(false, "Not used."));
        public Task<EditorOperationResult> ProjectEdgesAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new EditorOperationResult(false, "Not used."));
    }

    private sealed class StubGeometryKernelDescriptorProvider : IGeometryKernelDescriptorProvider
    {
        public GeometryKernelDescriptor Current { get; } = new("Test kernel", "Test implementation", "Test runtime", "Test capabilities", "Test requirements", "Test source models");
    }
}
