using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Domain.App.Models;
using Pathstitch.App.Controls;
using Pathstitch.App.Pages;
using Pathstitch.App.Tests.Fixtures;

namespace Pathstitch.App.Tests;

public sealed class EditorShellHeadlessTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public async Task LiveShell_ModeChangesSwapRailWorkspaceContextAndInspectorsByAutomationId()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);

        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);

        await _ui.RunAsync(() =>
        {
            AssertAutomationIdsExist(shell,
                "editor.shell",
                "editor.header",
                "editor.context-panel",
                "editor.workspace-host",
                "editor.canvas.2d",
                "editor.canvas.3d",
                "editor.canvas.3d.webview",
                "editor.dialog.open-3d-model",
                "editor.inspector-host",
                "editor.inspector.measure",
                "editor.inspector.output",
                "editor.inspector.errors");
            AssertVisible(shell, "editor.tool-rail");
            AssertVisible(shell, "editor.workspace.2d");
            AssertHidden(shell, "editor.workspace.3d");
            AssertVisible(shell, "editor.context.2d-layers");
            AssertHidden(shell, "editor.context.3d-bodies");
            AssertVisible(shell, "editor.inspector.2d");
            AssertHidden(shell, "editor.inspector.selection");
            AssertHidden(shell, "editor.inspector.move");
            AssertHidden(shell, "editor.inspector.projection");
            AssertHidden(shell, "editor.inspector.unfold");
            AssertCatalogButtons(shell, EditorMode.TwoD);
        });

        await SetModeAndLayoutAsync(session, viewModel, EditorMode.ThreeD);

        await _ui.RunAsync(() =>
        {
            AssertHidden(shell, "editor.workspace.2d");
            AssertVisible(shell, "editor.workspace.3d");
            AssertHidden(shell, "editor.context.2d-layers");
            AssertVisible(shell, "editor.context.3d-bodies");
            AssertHidden(shell, "editor.inspector.2d");
            AssertVisible(shell, "editor.inspector.selection");
            AssertCatalogButtons(shell, EditorMode.ThreeD);
        });
    }

    [Fact]
    public async Task LiveShell_SwitchingModesRestoresEachWorkspaceActiveTool()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);

        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);
        await _ui.RunAsync(() => viewModel.ActivateSidebarItem("circle"));
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.ThreeD);
        await _ui.RunAsync(() => viewModel.ActivateSidebarItem("move"));
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);

        await _ui.RunAsync(() =>
        {
            Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);
            Assert.Contains("active", _ui.FindByAutomationId<Button>(shell, "2d.circle").Classes);
        });

        await SetModeAndLayoutAsync(session, viewModel, EditorMode.ThreeD);
        await _ui.RunAsync(() =>
        {
            Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);
            Assert.Contains("active", _ui.FindByAutomationId<Button>(shell, "3d.move").Classes);
        });
    }

    [Fact]
    public async Task LiveShell_SnapToggleIsVisibleInTwoDAndUpdatesPersistedWorkspaceState()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);

        await _ui.RunAsync(() =>
        {
            var snap = _ui.FindByAutomationId<Button>(shell, "editor.workspace.snap");
            Assert.True(_ui.IsEffectivelyVisible(snap));
            Assert.Contains("active", snap.Classes);
            snap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            session.Window.UpdateLayout();
            Assert.DoesNotContain("active", snap.Classes);
        });

        Assert.False(viewModel.TwoDSnapEnabled);
        Assert.False(viewModel.TwoDWorkspace.State.SnapEnabled);
    }

    [Fact]
    public async Task LiveShell_GridToggleIsVisibleInTwoDAndUpdatesPersistedWorkspaceState()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);

        await _ui.RunAsync(() =>
        {
            var grid = _ui.FindByAutomationId<Button>(shell, "editor.workspace.grid");
            Assert.True(_ui.IsEffectivelyVisible(grid));
            Assert.Contains("active", grid.Classes);
            grid.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            session.Window.UpdateLayout();
            Assert.DoesNotContain("active", grid.Classes);
        });

        Assert.False(viewModel.TwoDGridVisible);
        Assert.False(viewModel.TwoDWorkspace.State.GridVisible);
    }

    [Fact]
    public async Task LiveShell_SewingInspectorHasReadableNumericFieldsAndResizableRightPanel()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);
        await _ui.RunAsync(() =>
        {
            viewModel.ActivateSidebarItem("add-sewing-holes");
            session.Window.UpdateLayout();

            var splitter = _ui.FindByAutomationId<GridSplitter>(shell, "editor.inspector.resize");
            Assert.True(_ui.IsEffectivelyVisible(splitter));
            Assert.Equal(GridResizeDirection.Columns, splitter.ResizeDirection);
            Assert.Equal(GridResizeBehavior.PreviousAndNext, splitter.ResizeBehavior);

            foreach (var automationId in new[]
                     {
                         "editor.sewing.diameter",
                         "editor.sewing.pitch",
                         "editor.sewing.margin",
                         "editor.sewing.corner-clearance",
                     })
            {
                var field = _ui.FindByAutomationId<AutomationSafeNumericUpDown>(shell, automationId);
                Assert.True(field.Bounds.Width >= 160, $"{automationId} width was {field.Bounds.Width}.");
                var textBox = Assert.Single(field.GetVisualDescendants().OfType<TextBox>());
                Assert.True(textBox.Bounds.Width >= 80, $"{automationId} editor width was {textBox.Bounds.Width}.");
            }

            var regions = _ui.FindByAutomationId<Grid>(shell, "editor.regions");
            var inspector = _ui.FindByAutomationId<EditorInspectorHost>(shell, "editor.inspector-host");
            Assert.True(inspector.Bounds.Width >= 359);
            regions.ColumnDefinitions[5].Width = new GridLength(400);
            session.Window.UpdateLayout();
            Assert.True(inspector.Bounds.Width >= 399);
        });
    }

    [Fact]
    public async Task LiveShell_NarrowWindowKeepsEveryTwoDToolInVerticalScrollableRailWithoutHorizontalOverflow()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell, width: 640, height: 320);
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);

        await _ui.RunAsync(() =>
        {
            var rail = _ui.FindByAutomationId<EditorToolRail>(shell, "editor.tool-rail");
            var scroller = Assert.Single(rail.GetLogicalDescendants().OfType<ScrollViewer>());
            var expected = EditorToolCatalog.ForMode(EditorMode.TwoD);

            Assert.All(expected, descriptor =>
            {
                var button = _ui.FindByAutomationId<Button>(shell, descriptor.Identifier);
                Assert.True(button.Bounds.Width > 0);
                Assert.True(button.Bounds.Height > 0);
            });
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 0.5,
                $"Rail extent {scroller.Extent.Width} exceeded viewport {scroller.Viewport.Width}.");
            Assert.True(rail.Bounds.Width <= 180);
            Assert.True(session.Window.ClientSize.Width <= 640.5);
        });
    }

    [Fact]
    public async Task LiveToolRail_CustomizationControlsReorderAndResetByStableAutomationId()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell, width: 800, height: 600);
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);
        var originalFirst = viewModel.SidebarTools[0].Identifier;
        var movedIdentifier = viewModel.SidebarTools[1].Identifier;

        await _ui.RunAsync(() =>
        {
            var customize = _ui.FindByAutomationId<ToggleButton>(shell, "editor.tool-rail.customize");
            customize.IsChecked = true;
            session.Window.UpdateLayout();
            var moveEarlier = _ui.FindByAutomationId<Button>(shell, $"{movedIdentifier}.move-up");
            moveEarlier.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

        Assert.Equal(movedIdentifier, viewModel.SidebarTools[0].Identifier);
        Assert.Equal(
            movedIdentifier,
            viewModel.ToolCustomizations
                .Where(item => item.Identifier.StartsWith("2d.", StringComparison.Ordinal))
                .OrderBy(item => item.Order)
                .First()
                .Identifier);

        await _ui.RunAsync(() =>
        {
            var reset = _ui.FindByAutomationId<Button>(shell, "editor.tool-rail.reset");
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

        Assert.Equal(originalFirst, viewModel.SidebarTools[0].Identifier);
    }

    private async Task SetModeAndLayoutAsync(
        HeadlessViewSession<EditorShellView> session,
        Domain.App.ViewModels.EditorPageViewModel viewModel,
        EditorMode mode)
    {
        await _ui.RunAsync(() => viewModel.SetActiveEditorModeAsync(mode));
        await _ui.RunAsync(session.Window.UpdateLayout);
    }

    private void AssertVisible(Control root, string automationId)
        => Assert.True(_ui.IsEffectivelyVisible(_ui.FindByAutomationId(root, automationId)), automationId);

    private void AssertHidden(Control root, string automationId)
        => Assert.False(_ui.IsEffectivelyVisible(_ui.FindByAutomationId(root, automationId)), automationId);

    private void AssertCatalogButtons(Control root, EditorMode mode)
    {
        var expected = EditorToolCatalog.ForMode(mode).Select(item => item.Identifier).Order().ToArray();
        var actual = root.GetLogicalDescendants()
            .OfType<Button>()
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => id is not null && expected.Contains(id, StringComparer.Ordinal))
            .Order()
            .ToArray();
        Assert.Equal(expected, actual);
    }

    private void AssertAutomationIdsExist(Control root, params string[] automationIds)
    {
        foreach (var automationId in automationIds)
            _ui.FindByAutomationId(root, automationId);
    }
}
