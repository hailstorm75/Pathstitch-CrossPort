using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.Models;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using UI.Navigation;
using Avalonia.Input;
using Avalonia.Media;

namespace Pathstitch.App.Pages;

public partial class EditorPageView : BasePageView
{
    private static readonly IBrush ViewportSurfaceNormalBrush = new SolidColorBrush(Color.Parse("#0D0D10"));
    private static readonly IBrush ViewportSurfaceActiveBrush = new SolidColorBrush(Color.Parse("#151A26"));

    public EditorPageView(IServiceProvider serviceProvider) : base(serviceProvider, NavigationAddressBook.EditorPage)
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        KeyDown += OnEditorKeyDown;
        ViewportWebView.NavigationCompleted += OnViewportNavigationCompleted;
        ViewportWebView.WebMessageReceived += OnViewportWebMessageReceived;
        DragDrop.AddDragEnterHandler(WorkspaceViewportSurface, OnViewportSurfaceDragEnter);
        DragDrop.AddDragLeaveHandler(WorkspaceViewportSurface, OnViewportSurfaceDragLeave);
        DragDrop.AddDragOverHandler(WorkspaceViewportSurface, OnViewportSurfaceDragOver);
        DragDrop.AddDropHandler(WorkspaceViewportSurface, OnViewportSurfaceDrop);
        ResetViewportSurfaceState();
    }

    private void OnHomeFrameClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel)
            return;

        if (viewModel.IsShowingGeneratedOutputWorkspace)
        {
            viewModel.FrameGeneratedOutputToContent();
            return;
        }

        ExecuteViewportScript(viewModel.GetHomeFrameScript());
    }

    private void OnSidebarToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button { Tag: string itemKey })
            return;

        viewModel.ActivateSidebarItem(itemKey);
    }

    private void OnMoveToolIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not ToggleSwitch toggleSwitch)
            return;

        if (toggleSwitch.IsChecked == true)
            viewModel.ActivateMoveTool();
        else if (viewModel.IsMoveToolActive)
            viewModel.ActivateSelectTool();
    }

    private void OnAddPlaneClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivatePlaneTool();
    }

    private void OnOriginPlaneModeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetPlaneSelectionMode(PlaneSelectionModeType.Origin);
    }

    private void OnFacePlaneModeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetPlaneSelectionMode(PlaneSelectionModeType.Face);
    }

    private void OnProjectionXYClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SelectProjectionOriginPlane("XY");
    }

    private void OnProjectionXZClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SelectProjectionOriginPlane("XZ");
    }

    private void OnProjectionYZClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SelectProjectionOriginPlane("YZ");
    }

    private void OnUseSelectedFaceProjectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.UseCurrentSelectionAsProjectionFace();
    }

    private void OnConfirmProjectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ConfirmPlaneProjection();
    }

    private async void OnOpenSourceModelClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.OpenSourceModelAsync();
    }

    private void OnCancelProjectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.CancelPlaneSelection();
    }

    private void OnClearSelectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearSelectedFaces();
    }

    private void OnRemoveSelectedFaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button button
            || button.Tag is not string tag)
        {
            return;
        }

        var parts = tag.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var bodyIndex)
            || !int.TryParse(parts[1], out var faceIndex))
        {
            return;
        }

        viewModel.RemoveSelectedFaceFromQueue(bodyIndex, faceIndex);
    }

    private void OnBodyNudgeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button button
            || button.Tag is not string tag)
            return;

        var parts = tag.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var axis)
            || !int.TryParse(parts[1], out var direction))
            return;

        viewModel.NudgeSelectedBody(axis, direction);
    }

    private void OnBodyResetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ResetSelectedBodyPosition();
    }

    private void OnResetAllBodiesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ResetAllBodyPositions();
    }

    private void OnSelectMovedBodyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button button
            || button.Tag is not int bodyIndex)
        {
            return;
        }

        viewModel.SelectMovedBody(bodyIndex);
    }

    private void OnResetMovedBodyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button button
            || button.Tag is not int bodyIndex)
        {
            return;
        }

        viewModel.ResetBodyPosition(bodyIndex);
    }

    private async void OnUnfoldSelectedClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RequestUnfoldSelectedAsync();
    }

    private async void OnRefreshFaceDistortionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RefreshFaceDistortionAsync();
    }

    private async void OnUnfoldEntireBodyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RequestUnfoldEntireBodyAsync();
    }

    private void OnSelectedUnfoldPreviewScopeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetUnfoldPreviewScope(false);
    }

    private void OnWholeBodyUnfoldPreviewScopeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetUnfoldPreviewScope(true);
    }

    private async void OnRefreshUnfoldPreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RefreshActiveUnfoldPreviewAsync();
    }

    private async void OnOpenGeneratedOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.OpenGeneratedOutputAsync();
    }

    private void OnShow3DWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.Show3DWorkspace();
    }

    private void OnShowGeneratedOutputWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ShowGeneratedOutputWorkspace();
    }

    private void OnGeneratedOutputSelectToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputSelectTool();
    }

    private void OnGeneratedOutputMoveToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputMoveTool();
    }

    private void OnGeneratedOutputPanToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputPanTool();
    }

    private void OnGeneratedOutputMeasureToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputMeasureTool();
    }

    private void OnGeneratedOutputDimensionToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputDimensionTool();
    }

    private void OnGeneratedOutputScaleToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputScaleTool();
    }

    private void OnGeneratedOutputMirrorToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputMirrorTool();
    }

    private void OnGeneratedOutputOffsetToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputOffsetTool();
    }

    private void OnGeneratedOutputAddThicknessToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputAddThicknessTool();
    }

    private void OnGeneratedOutputCleanupToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputCleanupTool();
    }

    private void OnGeneratedOutputPatternToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputPatternTool();
    }

    private void OnGeneratedOutputPaperFoldingToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputPaperFoldingTool();
    }

    private void OnGeneratedOutputTrimToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputTrimTool();
    }

    private void OnGeneratedOutputFilletToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputFilletTool();
    }

    private void OnGeneratedOutputChamferToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputChamferTool();
    }

    private void OnGeneratedOutputConvertLinesToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputConvertLinesTool();
    }

    private void OnGeneratedOutputLineToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputLineTool();
    }

    private void OnGeneratedOutputRectangleToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputRectangleTool();
    }

    private void OnGeneratedOutputCircleToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputCircleTool();
    }

    private void OnGeneratedOutputPolygonToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputPolygonTool();
    }

    private void OnGeneratedOutputTextToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputTextTool();
    }

    private void OnGeneratedOutputPenToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateGeneratedOutputPenTool();
    }

    private void OnIncreaseGeneratedOutputPolygonSidesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.IncrementGeneratedOutputPolygonSides();
    }

    private void OnDecreaseGeneratedOutputPolygonSidesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.DecrementGeneratedOutputPolygonSides();
    }

    private void OnClearGeneratedOutputSelectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearGeneratedOutputSelection();
    }

    private void OnExpandGeneratedOutputRectanglesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ExpandGeneratedOutputRectangles();
    }

    private void OnApplyGeneratedOutputOffsetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyGeneratedOutputOffset();
    }

    private void OnApplyGeneratedOutputAddThicknessClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyGeneratedOutputAddThickness();
    }

    private void OnApplyGeneratedOutputCleanupClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyGeneratedOutputCleanup();
    }

    private void OnApplyGeneratedOutputPatternClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyGeneratedOutputPattern();
    }

    private void OnApplyGeneratedOutputPaperFoldingCreasesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyGeneratedOutputPaperFoldingCreases();
    }

    private void OnApplyGeneratedOutputGlueTabsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyGeneratedOutputGlueTabs();
    }

    private void OnApplyGeneratedOutputConvertLinesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyGeneratedOutputConvertLines();
    }

    private void OnClearGeneratedOutputMeasurementsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearGeneratedOutputMeasurements();
    }

    private void OnApplyGeneratedOutputSelectedTextClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyGeneratedOutputSelectedText();
    }

    private async void OnRevealGeneratedOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RevealGeneratedOutputAsync();
    }

    private async void OnRefreshGeneratedOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RefreshGeneratedOutputAsync();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel)
            return;

        viewModel.ViewportScriptRequested -= OnViewportScriptRequested;
        viewModel.ViewportScriptRequested += OnViewportScriptRequested;
        ViewportWebView.Navigate(new Uri(System.IO.Path.Combine(viewModel.ViewportBaseUri.AbsolutePath, "viewport3d.html")));
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
        {
            viewModel.ViewportScriptRequested -= OnViewportScriptRequested;
        }
    }

    private void OnViewportSurfaceDragEnter(object? sender, DragEventArgs e)
    {
        SetViewportSurfaceState(IsFileDrop(e));
    }

    private void OnViewportSurfaceDragLeave(object? sender, DragEventArgs e)
    {
        ResetViewportSurfaceState();
    }

    private void OnViewportSurfaceDragOver(object? sender, DragEventArgs e)
    {
        var acceptsFiles = IsFileDrop(e);
        e.DragEffects = acceptsFiles ? DragDropEffects.Copy : DragDropEffects.None;
        SetViewportSurfaceState(acceptsFiles);
    }

    private async void OnViewportSurfaceDrop(object? sender, DragEventArgs e)
    {
        ResetViewportSurfaceState();

        if (!IsFileDrop(e) || DataContext is not EditorPageViewModel viewModel)
            return;

        var filePaths = e.DataTransfer.TryGetFiles()
            ?.Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray()
            ?? [];

        await viewModel.OpenSourceModelsAsync(filePaths).ConfigureAwait(true);
    }

    private void OnViewportNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.OnViewportNavigationCompleted(e.IsSuccess);
    }

    private void OnViewportWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.OnViewportMessageReceived(e.Body ?? string.Empty);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || IsShortcutSuppressedByFocusedElement()
            || e.KeyModifiers != KeyModifiers.None)
            return;

        var shortcutToken = e.Key switch
        {
            Key.D1 or Key.NumPad1 => "select",
            Key.D2 or Key.NumPad2 => "move",
            Key.D3 or Key.NumPad3 => "project",
            Key.D4 or Key.NumPad4 => "measure",
            Key.D => "dimension",
            Key.M => "mirror",
            Key.S => "scale",
            Key.X => "trim",
            Key.F => "fillet",
            Key.B => "chamfer",
            Key.J => viewModel.IsShowingGeneratedOutputWorkspace ? "cleanup" : null,
            Key.E => "convert-lines",
            Key.D5 or Key.NumPad5 => "unfold",
            Key.D6 or Key.NumPad6 => "output",
            Key.L => "line",
            Key.R => "rectangle",
            Key.C => "circle",
            Key.P => "polygon",
            Key.T => "text",
            Key.N => "pen",
            Key.H => "frame-home",
            Key.O => viewModel.IsShowingGeneratedOutputWorkspace ? "offset" : "camera-mode",
            Key.Delete or Key.Back => "delete-selection",
            Key.Escape => "escape",
            _ => null,
        };

        if (shortcutToken is null)
            return;

        if (shortcutToken == "escape" && viewModel.IsShowingGeneratedOutputWorkspace)
            GeneratedOutputPreviewCanvas.CancelActiveInteraction();

        if (!viewModel.TryActivateEditorShortcut(shortcutToken)
            && !(shortcutToken == "escape" && viewModel.IsShowingGeneratedOutputWorkspace))
        {
            return;
        }

        e.Handled = true;
    }

    private void ExecuteViewportScript(string script)
    {
        try
        {
            ViewportWebView.InvokeScript(script);
        }
        catch
        {
            // The initial port tolerates an unloaded or unsupported WebView state.
        }
    }

    private void OnViewportScriptRequested(string script) => ExecuteViewportScript(script);

    private void OnBodyVisibilityClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not Button button || button.Tag is not int bodyIndex)
            return;

        viewModel.ToggleBodyVisibility(bodyIndex);
    }

    private void OnBodySelectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not Button button || button.Tag is not int bodyIndex)
            return;

        viewModel.SelectBodyFromPanel(bodyIndex);
    }

    private void OnClearSelectedBodyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearSelectedBody();
    }

    private void OnFaceSelectionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not Button button || button.Tag is not string tag)
            return;

        var parts = tag.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var bodyIndex)
            || !int.TryParse(parts[1], out var faceIndex))
            return;

        var isShiftKey = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        viewModel.SelectFaceFromPanel(bodyIndex, faceIndex, isShiftKey);
        e.Handled = true;
    }

    private static bool IsFileDrop(DragEventArgs e)
        => e.DataTransfer.Formats.Contains(DataFormat.File);

    private bool IsShortcutSuppressedByFocusedElement()
    {
        var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focusedElement is TextBox
               || focusedElement is ComboBox
               || focusedElement is NativeWebView;
    }

    private void SetViewportSurfaceState(bool isActive)
    {
        if (WorkspaceViewportSurface is null)
            return;

        WorkspaceViewportSurface.Background = isActive ? ViewportSurfaceActiveBrush : ViewportSurfaceNormalBrush;
    }

    private void ResetViewportSurfaceState() => SetViewportSurfaceState(false);
}
