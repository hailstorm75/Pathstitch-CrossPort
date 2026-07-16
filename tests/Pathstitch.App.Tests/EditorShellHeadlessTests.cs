using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
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
    public async Task DimensionExpressionField_InvalidStaysFocusedThenValidCommitsAndKeepsToolActive()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var measurement = new Editor2DMeasurement("dimension", new(0, 0), new(12, 0));
        viewModel.TwoDMeasurements = [measurement];
        viewModel.TwoDSelectedMeasurementId = measurement.Id;
        viewModel.TwoDActiveTool = Editor2DTool.Dimension;
        viewModel.TwoDWorkspace.ClearHistory();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);

        await _ui.RunAsync(() =>
        {
            viewModel.TwoDActiveTool = Editor2DTool.Dimension;
            viewModel.TwoDSelectedMeasurementId = measurement.Id;
            var canvas = _ui.FindByAutomationId<DxfPreviewCanvas>(shell, "editor.canvas.2d");
            Assert.True(canvas.RequestDimensionExpressionInput(measurement.Id));
            viewModel.TwoDWorkspace.ClearHistory();
        });
        await _ui.RunAsync(() => { });

        await _ui.RunAsync(() =>
        {
            var input = _ui.FindByAutomationId<TextBox>(shell, "editor.canvas.2d.dimension-expression-input");
            input.Text = "d1";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        });
        await _ui.RunAsync(() => session.Window.UpdateLayout());

        await _ui.RunAsync(() =>
        {
            var input = _ui.FindByAutomationId<TextBox>(shell, "editor.canvas.2d.dimension-expression-input");
            var pill = _ui.FindByAutomationId<Border>(shell, "editor.canvas.2d.dimension-expression");
            Assert.True(_ui.IsEffectivelyVisible(pill));
            Assert.Same(input, TopLevel.GetTopLevel(shell)?.FocusManager?.GetFocusedElement());
            Assert.True(viewModel.HasTwoDMeasurementExpressionError);
            Assert.Equal(measurement, Assert.Single(viewModel.TwoDMeasurements));
            Assert.False(viewModel.TwoDWorkspace.CanUndo);

            input.Text = "5 * 2";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        });

        await _ui.RunAsync(() =>
        {
            var canvas = _ui.FindByAutomationId<DxfPreviewCanvas>(shell, "editor.canvas.2d");
            var pill = _ui.FindByAutomationId<Border>(shell, "editor.canvas.2d.dimension-expression");
            Assert.False(pill.IsVisible);
            Assert.True(canvas.IsFocused);
            Assert.Equal(Editor2DTool.Dimension, viewModel.TwoDActiveTool);
            Assert.Null(viewModel.TwoDSelectedMeasurementId);
            var updated = Assert.Single(viewModel.TwoDMeasurements);
            Assert.Equal("5 * 2", updated.Expression);
            Assert.Equal(10, updated.Distance, 8);
            Assert.True(viewModel.TwoDWorkspace.CanUndo);
        });
    }

    [Fact]
    public async Task DimensionExpressionField_EscapeClosesWithoutMutation()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var measurement = new Editor2DMeasurement("dimension", new(0, 0), new(12, 0));
        viewModel.TwoDMeasurements = [measurement];
        viewModel.TwoDSelectedMeasurementId = measurement.Id;
        viewModel.TwoDActiveTool = Editor2DTool.Dimension;
        viewModel.TwoDWorkspace.ClearHistory();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);

        await _ui.RunAsync(() =>
        {
            viewModel.TwoDActiveTool = Editor2DTool.Dimension;
            viewModel.TwoDSelectedMeasurementId = measurement.Id;
            var canvas = _ui.FindByAutomationId<DxfPreviewCanvas>(shell, "editor.canvas.2d");
            Assert.True(canvas.RequestDimensionExpressionInput(measurement.Id));
            viewModel.TwoDWorkspace.ClearHistory();
            var input = _ui.FindByAutomationId<TextBox>(shell, "editor.canvas.2d.dimension-expression-input");
            input.Text = "99";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

            Assert.False(_ui.FindByAutomationId<Border>(shell, "editor.canvas.2d.dimension-expression").IsVisible);
            Assert.True(canvas.IsFocused);
            Assert.Equal(measurement, Assert.Single(viewModel.TwoDMeasurements));
            Assert.False(viewModel.TwoDWorkspace.CanUndo);
            Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
            Assert.Null(viewModel.TwoDSelectedMeasurementId);
        });
    }

    [Fact]
    public async Task SelectedDimensionExpression_EnterUsesFieldValidationAndReturnsFocusAfterSuccess()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var measurement = new Editor2DMeasurement(
            "dimension", new(0, 0), new(12, 0), VarName: "d1", Expression: "12", IsParametric: true);
        viewModel.TwoDMeasurements = [measurement];
        viewModel.TwoDSelectedMeasurementId = measurement.Id;
        viewModel.TwoDWorkspace.ClearHistory();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);

        await _ui.RunAsync(() =>
        {
            var input = _ui.FindByAutomationId<TextBox>(shell, "editor.2d.dimension.selected-expression");
            viewModel.TwoDSelectedMeasurementId = measurement.Id;
            var driven = _ui.FindByAutomationId<CheckBox>(shell, "editor.2d.dimension.driven");
            driven.IsChecked = true;
            Assert.True(viewModel.TwoDSelectedMeasurementDriven);
            viewModel.TwoDWorkspace.ClearHistory();
            input.Focus();
            input.Text = "d1";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            session.Window.UpdateLayout();

            Assert.True(viewModel.HasTwoDMeasurementExpressionError);
            Assert.True(input.IsFocused);
            var unchangedDriven = Assert.Single(viewModel.TwoDMeasurements);
            Assert.Equal(measurement.Id, unchangedDriven.Id);
            Assert.Equal(measurement.Expression, unchangedDriven.Expression);
            Assert.True(unchangedDriven.Driven);
            Assert.Equal(measurement.Distance, unchangedDriven.Distance, 8);
            Assert.False(viewModel.TwoDWorkspace.CanUndo);

            input.Text = "3 + 3";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

            Assert.False(viewModel.HasTwoDMeasurementExpressionError);
            Assert.True(_ui.FindByAutomationId<DxfPreviewCanvas>(shell, "editor.canvas.2d").IsFocused);
            var updated = Assert.Single(viewModel.TwoDMeasurements);
            Assert.Equal("3 + 3", updated.Expression);
            Assert.True(updated.Driven);
            Assert.Equal(6, updated.EvaluatedValue);
            Assert.Equal(12, updated.Distance, 8);
        });
    }

    [Fact]
    public async Task LiveCanvas_EnterConfirmsStagedScaleExactlyOnce()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var source = new Editor2DPreviewPath("scale-source", "LINE", [new(0, 0), new(4, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScaleFactorText = "2";
        viewModel.TwoDWorkspace.ClearHistory();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);

        await _ui.RunAsync(() =>
        {
            var canvas = _ui.FindByAutomationId<DxfPreviewCanvas>(shell, "editor.canvas.2d");
            canvas.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
            });
        });

        Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
        Assert.Equal(new Editor2DPoint(-2, 0), viewModel.TwoDDocument!.Paths[0].Points[0]);
        Assert.Equal(new Editor2DPoint(6, 0), viewModel.TwoDDocument.Paths[0].Points[1]);
        Assert.True(viewModel.TwoDWorkspace.Undo());
        Assert.Equal(source.Points, viewModel.TwoDDocument.Paths[0].Points);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

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
    public async Task LiveShell_SewingPatternSideAndSaddleControlsTrackWorkspaceState()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        var shell = await _ui.RunAsync(() => new EditorShellView { DataContext = viewModel });
        await using var session = await _ui.MountAsync(shell);
        await SetModeAndLayoutAsync(session, viewModel, EditorMode.TwoD);

        await _ui.RunAsync(() =>
        {
            var line = new Editor2DPreviewPath(
                "line",
                "LINE",
                [new Editor2DPoint(0, 0), new Editor2DPoint(20, 0)],
                false);
            viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [line] };
            viewModel.TwoDSelectedPathIds = [line.Id];
            viewModel.ActivateSidebarItem("add-sewing-holes");
            session.Window.UpdateLayout();

            var pattern = _ui.FindByAutomationId<ComboBox>(shell, "editor.sewing.pattern");
            var side = _ui.FindByAutomationId<ComboBox>(shell, "editor.sewing.side");
            var saddleSpacing = _ui.FindByAutomationId<AutomationSafeNumericUpDown>(shell, "editor.sewing.saddle-spacing");

            Assert.Equal(Editor2DSewingPattern.Single, pattern.SelectedItem);
            Assert.Equal(["Left", "Right", "Both"], Assert.IsAssignableFrom<IEnumerable<string>>(side.ItemsSource));
            Assert.Equal("Left", side.SelectedItem);
            Assert.False(_ui.IsEffectivelyVisible(saddleSpacing));
            Assert.Equal(3.0m, saddleSpacing.Value);

            viewModel.TwoDWorkspace.SewingPattern = Editor2DSewingPattern.Saddle;
            session.Window.UpdateLayout();
            Assert.Equal(Editor2DSewingPattern.Saddle, pattern.SelectedItem);
            Assert.True(_ui.IsEffectivelyVisible(saddleSpacing));

            var circle = new Editor2DPreviewPath(
                "circle",
                "CIRCLE",
                [],
                true,
                Center: new Editor2DPoint(0, 0),
                Radius: 10);
            viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [circle] };
            viewModel.TwoDSelectedPathIds = [circle.Id];
            session.Window.UpdateLayout();

            Assert.Equal(["Outer", "Inner", "Both"], Assert.IsAssignableFrom<IEnumerable<string>>(side.ItemsSource));
            Assert.Equal("Outer", viewModel.TwoDWorkspace.SewingSideSelection);
            Assert.Equal(Editor2DSewingSide.Right, viewModel.TwoDWorkspace.SewingSide);
            viewModel.TwoDWorkspace.SewingSideSelection = "Inner";
            Assert.Equal(Editor2DSewingSide.Left, viewModel.TwoDWorkspace.SewingSide);
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
