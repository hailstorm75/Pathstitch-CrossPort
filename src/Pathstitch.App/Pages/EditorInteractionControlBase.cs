using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Domain.App.Models;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public abstract class EditorInteractionControlBase : UserControl
{
    protected void OnSidebarToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button { Tag: string itemKey })
            return;

        viewModel.ActivateSidebarItem(itemKey);
    }

    protected void OnMoveToolIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not ToggleSwitch toggleSwitch)
            return;

        if (toggleSwitch.IsChecked == true)
            viewModel.ActivateMoveTool();
        else if (viewModel.IsMoveToolActive)
            viewModel.ActivateSelectTool();
    }

    protected void OnAddPlaneClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivatePlaneTool();
    }

    protected void OnOriginPlaneModeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetPlaneSelectionMode(PlaneSelectionModeType.Origin);
    }

    protected void OnFacePlaneModeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetPlaneSelectionMode(PlaneSelectionModeType.Face);
    }

    protected void OnProjectionXYClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SelectProjectionOriginPlane("XY");
    }

    protected void OnProjectionXZClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SelectProjectionOriginPlane("XZ");
    }

    protected void OnProjectionYZClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SelectProjectionOriginPlane("YZ");
    }

    protected void OnUseSelectedFaceProjectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.UseCurrentSelectionAsProjectionFace();
    }

    protected void OnConfirmProjectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ConfirmPlaneProjection();
    }

    protected async void OnOpenSourceModelClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.OpenSourceModelAsync();
    }

    protected void OnCancelProjectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.CancelPlaneSelection();
    }

    protected void OnClearSelectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearSelectedFaces();
    }

    protected void OnRemoveSelectedFaceClicked(object? sender, RoutedEventArgs e)
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

    protected void OnBodyNudgeClicked(object? sender, RoutedEventArgs e)
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

    protected void OnBodyResetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ResetSelectedBodyPosition();
    }

    protected void OnResetAllBodiesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ResetAllBodyPositions();
    }

    protected void OnSelectMovedBodyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button button
            || button.Tag is not int bodyIndex)
        {
            return;
        }

        viewModel.SelectMovedBody(bodyIndex);
    }

    protected void OnResetMovedBodyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button button
            || button.Tag is not int bodyIndex)
        {
            return;
        }

        viewModel.ResetBodyPosition(bodyIndex);
    }

    protected async void OnUnfoldSelectedClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RequestUnfoldSelectedAsync();
    }

    protected async void OnRefreshFaceDistortionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RefreshFaceDistortionAsync();
    }

    protected async void OnUnfoldEntireBodyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RequestUnfoldEntireBodyAsync();
    }

    protected void OnSelectedUnfoldPreviewScopeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetUnfoldPreviewScope(false);
    }

    protected void OnWholeBodyUnfoldPreviewScopeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetUnfoldPreviewScope(true);
    }

    protected async void OnRefreshUnfoldPreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RefreshActiveUnfoldPreviewAsync();
    }

    protected void OnClearActiveSeamOverridesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearActiveSeamOverrides();
    }

    protected void OnSetSelectedFaceAsAnchorClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetSelectedFaceAsAnchor();
    }

    protected void OnClearAnchorFaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearAnchorFace();
    }

    protected async void OnOpenTwoDClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.OpenGeneratedOutputAsync();
    }

    protected async void OnShow3DWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.Show3DWorkspaceAsync();
    }

    protected async void OnShowTwoDWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ShowTwoDWorkspaceAsync();
    }

    protected void OnIncreaseTwoDPolygonSidesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.IncrementTwoDPolygonSides();
    }

    protected void OnDecreaseTwoDPolygonSidesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.DecrementTwoDPolygonSides();
    }

    protected void OnClearTwoDSelectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearTwoDSelection();
    }

    protected async void OnExportTwoDDxfClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ExportTwoDDxfAsync();
    }

    protected async void OnExportTwoDSvgClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ExportTwoDSvgAsync();
    }

    protected async void OnExportTwoDPngClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ExportTwoDPngAsync();
    }

    protected async void OnExportTwoDPdfClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ExportTwoDPdfAsync();
    }

    protected void OnExpandTwoDRectanglesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ExpandTwoDRectangles();
    }

    protected async void OnApplyTwoDOffsetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ApplyTwoDOffsetAsync();
    }

    protected void OnFlipTwoDOffsetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.FlipTwoDOffsetDirection();
    }

    protected async void OnConfirmTwoDOffsetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ConfirmTwoDOffsetAsync();
    }

    protected void OnCancelTwoDOffsetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.CancelTwoDOffset(exitTool: true);
    }

    protected async void OnTwoDOffsetDistanceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not EditorPageViewModel viewModel)
            return;

        await viewModel.ConfirmTwoDOffsetAsync();
        e.Handled = true;
    }

    protected async void OnApplyTwoDAddThicknessClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ApplyTwoDAddThicknessAsync();
    }

    protected void OnApplyTwoDCleanupClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDCleanup();
    }

    protected async void OnApplyTwoDBooleanClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel
            && sender is Button button
            && button.Tag is string operation)
            await viewModel.ApplyTwoDBooleanAsync(operation);
    }

    protected void OnApplyTwoDStrokeToFillClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDStrokeToFill();
    }

    protected void OnApplyTwoDFillToStrokeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDFillToStroke();
    }

    protected void OnApplyTwoDPatternClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDPattern();
    }

    protected void OnApplyTwoDPreciseTransformClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDPreciseTransform();
    }

    protected void OnToggleTwoDMovePointToPointClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ToggleTwoDMovePointToPoint();
    }

    protected void OnPickTwoDPatternPivotClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.PickTwoDCircularPatternPivot();
    }

    protected void OnPickTwoDScalePivotClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.PickTwoDScalePivot();
    }

    protected void OnApplyTwoDScaleClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDScale();
    }

    protected void OnClearTwoDMirrorObjectsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearTwoDMirrorObjects();
    }

    protected void OnClearTwoDMirrorAxisClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearTwoDMirrorAxis();
    }

    protected void OnConfirmTwoDMirrorClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ConfirmTwoDMirror();
    }

    protected void OnCancelTwoDMirrorClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.CancelTwoDMirror();
    }

    protected void OnApplyTwoDPaperFoldingCreasesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDPaperFoldingCreases();
    }

    protected void OnApplyTwoDGlueTabsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDGlueTabs();
    }

    protected void OnApplyTwoDConvertLinesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDConvertLines();
    }

    protected void OnClearTwoDMeasurementsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearTwoDMeasurements();
    }

    protected void OnPreviewSewingHolesClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Editor2DWorkspaceViewModel workspace })
            workspace.RefreshSewingHolePreview();
    }

    protected void OnCommitSewingHolesClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Editor2DWorkspaceViewModel workspace })
            workspace.CommitSewingHolePreview();
    }

    protected void OnTagSewingAvoidanceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Editor2DWorkspaceViewModel workspace })
            workspace.UseSelectionAsSewingAvoidancePaths();
    }

    protected void OnClearSewingAvoidanceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Editor2DWorkspaceViewModel workspace })
            workspace.ClearSewingAvoidancePaths();
    }

    protected void OnApplyTwoDSelectedTextClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDSelectedText();
    }

    protected void OnTwoDSelectedTextLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    protected void OnTwoDSelectedTextGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.SelectAll();
    }

    protected void OnTwoDSelectedTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || (e.KeyModifiers & KeyModifiers.Shift) != 0)
            return;

        if (DataContext is not EditorPageViewModel viewModel)
            return;

        e.Handled = true;
        viewModel.ApplyTwoDSelectedText();
    }

    protected void OnSelectTwoDCornerParameterClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel && sender is Button { Tag: string parameterId })
            viewModel.SelectTwoDCornerParameter(parameterId);
    }

    protected void OnApplyTwoDCornerParameterClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ApplyTwoDCornerParameterValue();
    }

    protected void OnConfirmTwoDCornerSessionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ConfirmTwoDCornerToolSession(exitTool: true);
    }

    protected void OnCancelTwoDCornerSessionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.CancelTwoDCornerToolSession(exitTool: true);
    }

    protected async void OnRevealTwoDClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RevealGeneratedOutputAsync();
    }

    protected async void OnRefreshTwoDClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RefreshGeneratedOutputAsync();
    }

    protected void OnBodyVisibilityClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not Button button || button.Tag is not int bodyIndex)
            return;

        viewModel.ToggleBodyVisibility(bodyIndex);
    }

    protected void OnBodySelectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not Button button || button.Tag is not int bodyIndex)
            return;

        viewModel.SelectBodyFromPanel(bodyIndex);
    }

    protected void OnClearSelectedBodyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearSelectedBody();
    }

    protected void OnFaceSelectionPressed(object? sender, PointerPressedEventArgs e)
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
}
