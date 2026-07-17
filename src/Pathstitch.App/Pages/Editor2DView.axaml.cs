using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Pages;

public partial class Editor2DView : EditorInteractionControlBase
{
    private string? _dimensionExpressionMeasurementId;
    private string? _rejectedDimensionExpressionText;
    private TopLevel? _pointerTopLevel;

    public Editor2DView()
    {
        InitializeComponent();
        TwoDPreviewCanvas.ReferenceImageTransformChanged += OnReferenceImageTransformChanged;
        TwoDPreviewCanvas.ReferenceImageTransformStarted += OnReferenceImageTransformStarted;
        TwoDPreviewCanvas.ReferenceImageTransformCompleted += OnReferenceImageTransformCompleted;
        TwoDPreviewCanvas.ReferenceImageTransformCanceled += OnReferenceImageTransformCanceled;
        TwoDPreviewCanvas.MeasurementEditStarted += OnMeasurementEditStarted;
        TwoDPreviewCanvas.MeasurementEditCompleted += OnMeasurementEditCompleted;
        TwoDPreviewCanvas.TransformPrecisionRequested += OnTransformPrecisionRequested;
        TwoDPreviewCanvas.TransformPrecisionDismissed += OnTransformPrecisionDismissed;
        TwoDPreviewCanvas.DimensionExpressionRequested += OnDimensionExpressionRequested;
        TwoDPreviewCanvas.DimensionExpressionDismissed += OnDimensionExpressionDismissed;
        TwoDPreviewCanvas.SelectionTransformRequested += OnSelectionTransformRequested;
        TwoDPreviewCanvas.PathReplacementRequested += OnPathReplacementRequested;
        TwoDPreviewCanvas.VertexEditRequested += OnVertexEditRequested;
        TwoDPreviewCanvas.ReferenceCalibrationRequested += OnReferenceCalibrationRequested;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _pointerTopLevel = TopLevel.GetTopLevel(this);
        _pointerTopLevel?.AddHandler(
            InputElement.PointerPressedEvent,
            OnWorkspacePointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _pointerTopLevel?.RemoveHandler(InputElement.PointerPressedEvent, OnWorkspacePointerPressed);
        _pointerTopLevel = null;
    }

    private void OnReferenceImageTransformChanged(
        string layerId,
        double x,
        double y,
        double width,
        double height,
        double rotationDegrees)
        => (DataContext as Domain.App.ViewModels.EditorPageViewModel)?.UpdateTwoDReferenceImageTransform(
            layerId, x, y, width, height, rotationDegrees);

    private void OnReferenceImageTransformStarted(string layerId)
        => (DataContext as Domain.App.ViewModels.EditorPageViewModel)?.BeginTwoDReferenceImageTransform(layerId);

    private void OnReferenceImageTransformCompleted()
        => (DataContext as Domain.App.ViewModels.EditorPageViewModel)?.CommitTwoDReferenceImageTransform();

    private void OnReferenceImageTransformCanceled()
        => (DataContext as Domain.App.ViewModels.EditorPageViewModel)?.CancelTwoDReferenceImageTransform();

    private void OnMeasurementEditStarted()
        => (DataContext as Domain.App.ViewModels.EditorPageViewModel)?.BeginTwoDMeasurementEdit();

    private void OnMeasurementEditCompleted()
        => (DataContext as Domain.App.ViewModels.EditorPageViewModel)?.EndTwoDMeasurementEdit();

    public void CancelActiveInteraction() => TwoDPreviewCanvas.CancelActiveInteraction();

    private void OnSelectionTransformRequested(DxfCanvasSelectionTransformEventArgs request)
    {
        if (DataContext is not Domain.App.ViewModels.EditorPageViewModel viewModel
            || !viewModel.ApplyTwoDSelectionTransform(request.Transform, request.CreateCopy)
            || viewModel.TwoDDocument is not { } document)
        {
            return;
        }

        request.Complete(document, viewModel.TwoDSelectedPathIds);
    }

    private void OnPathReplacementRequested(DxfCanvasPathReplacementEventArgs request)
    {
        if (DataContext is not Domain.App.ViewModels.EditorPageViewModel viewModel
            || !viewModel.ReplaceTwoDPath(request.SourcePathId, request.Replacements)
            || viewModel.TwoDDocument is not { } document)
        {
            return;
        }
        request.Complete(document);
    }

    private void OnVertexEditRequested(DxfCanvasVertexEditEventArgs request)
    {
        if (DataContext is not Domain.App.ViewModels.EditorPageViewModel viewModel
            || !viewModel.UpdateTwoDPathVertex(request.PathId, request.VertexIndex, request.Point)
            || viewModel.TwoDDocument is not { } document)
        {
            return;
        }

        request.Complete(document);
    }

    private void OnReferenceCalibrationRequested(DxfCanvasReferenceCalibrationRequest request)
    {
        if (DataContext is not Domain.App.ViewModels.EditorPageViewModel viewModel)
            return;
        viewModel.CaptureTwoDReferenceCalibrationPoints(request.Start, request.End);
        ReferenceCalibrationInput.Text = request.MeasuredDistance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Canvas.SetLeft(ReferenceCalibrationPill, Math.Clamp(request.Anchor.X - 55, 0, Math.Max(TwoDPreviewCanvas.Bounds.Width - 110, 0)));
        Canvas.SetTop(ReferenceCalibrationPill, Math.Clamp(request.Anchor.Y - 42, 0, Math.Max(TwoDPreviewCanvas.Bounds.Height - 30, 0)));
        Dispatcher.UIThread.Post(() =>
        {
            ReferenceCalibrationInput.Focus();
            ReferenceCalibrationInput.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnReferenceCalibrationInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not Domain.App.ViewModels.EditorPageViewModel viewModel)
            return;
        if (e.Key == Key.Enter)
        {
            viewModel.TwoDReferenceCalibrationTargetText = ReferenceCalibrationInput.Text ?? string.Empty;
            if (viewModel.CommitTwoDReferencePointCalibration())
            {
                TwoDPreviewCanvas.Focus();
            }
            else
                ReferenceCalibrationInput.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CancelTwoDReferencePointCalibration();
            TwoDPreviewCanvas.Focus();
            e.Handled = true;
        }
    }

    private void OnTransformPrecisionRequested(DxfCanvasTransformPrecisionRequest request)
    {
        GizmoDimensionInput.Text = request.Text;
        GizmoDimensionInput.IsReadOnly = !request.Focus;
        GizmoDimensionUnit.Text = request.Unit;
        AutomationProperties.SetName(GizmoDimensionInput, request.AutomationName);
        GizmoDimensionPill.IsHitTestVisible = request.Focus;
        GizmoDimensionPill.IsVisible = true;

        const double width = 84.0;
        const double height = 28.0;
        var left = Math.Clamp(request.Anchor.X - (width / 2.0), 0.0, Math.Max(TwoDPreviewCanvas.Bounds.Width - width, 0.0));
        var top = Math.Clamp(request.Anchor.Y - 28.0 - (height / 2.0), 0.0, Math.Max(TwoDPreviewCanvas.Bounds.Height - height, 0.0));
        Canvas.SetLeft(GizmoDimensionPill, left);
        Canvas.SetTop(GizmoDimensionPill, top);

        if (!request.Focus)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            GizmoDimensionInput.Focus();
            GizmoDimensionInput.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnDimensionExpressionRequested(DxfCanvasDimensionExpressionRequest request)
    {
        _dimensionExpressionMeasurementId = request.MeasurementId;
        _rejectedDimensionExpressionText = null;
        DimensionExpressionInput.Text = request.Text;
        var rawExpression = string.IsNullOrWhiteSpace(request.RawExpression)
            ? request.Text
            : request.RawExpression;
        ToolTip.SetTip(DimensionExpressionInput, rawExpression);
        AutomationProperties.SetName(
            DimensionExpressionInput,
            $"Dimension expression: {rawExpression}");
        DimensionExpressionPill.IsVisible = true;

        const double width = 150.0;
        const double estimatedHeight = 50.0;
        Canvas.SetLeft(
            DimensionExpressionPill,
            Math.Clamp(request.Anchor.X - (width / 2.0), 0.0, Math.Max(TwoDPreviewCanvas.Bounds.Width - width, 0.0)));
        Canvas.SetTop(
            DimensionExpressionPill,
            Math.Clamp(request.Anchor.Y - estimatedHeight - 8.0, 0.0, Math.Max(TwoDPreviewCanvas.Bounds.Height - estimatedHeight, 0.0)));

        if (DataContext is Domain.App.ViewModels.EditorPageViewModel viewModel)
            viewModel.ClearTwoDMeasurementExpressionError();
        DimensionExpressionInput.Focus();
        DimensionExpressionInput.SelectAll();
        Dispatcher.UIThread.Post(() =>
        {
            DimensionExpressionInput.Focus();
            DimensionExpressionInput.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnDimensionExpressionDismissed()
    {
        _dimensionExpressionMeasurementId = null;
        _rejectedDimensionExpressionText = null;
        DimensionExpressionPill.IsVisible = false;
        if (DataContext is Domain.App.ViewModels.EditorPageViewModel viewModel)
            viewModel.ClearTwoDMeasurementExpressionError();
    }

    private void OnDimensionExpressionInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab
            && _dimensionExpressionMeasurementId is { } tabMeasurementId
            && DataContext is Domain.App.ViewModels.EditorPageViewModel tabViewModel
            && TryGetCreationPrecisionMeasurement(tabViewModel, tabMeasurementId, out var tabMeasurement))
        {
            e.Handled = true;
            var submittedExpression = DimensionExpressionInput.Text ?? string.Empty;
            if (!tabViewModel.TryCommitTwoDMeasurementExpression(
                    tabMeasurementId,
                    submittedExpression,
                    out _))
            {
                _rejectedDimensionExpressionText = DimensionExpressionInput.Text ?? string.Empty;
                KeepDimensionExpressionInputFocused();
                return;
            }

            var dimensionType = tabMeasurement.DimensionType!.Trim();
            if (dimensionType.Equals("height", StringComparison.OrdinalIgnoreCase))
            {
                FinishDimensionExpressionInput(tabViewModel);
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!dimensionType.Equals("width", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var refreshed = tabViewModel.TwoDMeasurements.FirstOrDefault(item =>
                            item.Id.Equals(tabMeasurementId, StringComparison.Ordinal));
                        if (refreshed is null || !TwoDPreviewCanvas.RequestDimensionExpressionInput(refreshed.Id))
                        {
                            FinishDimensionExpressionInput(tabViewModel);
                            return;
                        }
                        tabViewModel.TwoDSelectedMeasurementId = refreshed.Id;
                        DimensionExpressionInput.Text = submittedExpression;
                        DimensionExpressionInput.SelectAll();
                    }, DispatcherPriority.Input);
                    return;
                }

                var nextMeasurement = tabViewModel.TwoDMeasurements.FirstOrDefault(item =>
                        item.IsAutoDimension
                        && string.Equals(item.EntityPathId, tabMeasurement.EntityPathId, StringComparison.Ordinal)
                        && item.DimensionType?.Trim().Equals("height", StringComparison.OrdinalIgnoreCase) == true);
                if (nextMeasurement is null || !TwoDPreviewCanvas.RequestDimensionExpressionInput(nextMeasurement.Id))
                {
                    FinishDimensionExpressionInput(tabViewModel);
                    return;
                }
                tabViewModel.TwoDSelectedMeasurementId = nextMeasurement.Id;
            }, DispatcherPriority.Input);
            return;
        }

        if (e.Key == Key.Enter
            && _dimensionExpressionMeasurementId is { } measurementId
            && DataContext is Domain.App.ViewModels.EditorPageViewModel viewModel)
        {
            var isCreationPrecision = TryGetCreationPrecisionMeasurement(viewModel, measurementId, out _);
            if (viewModel.TryCommitTwoDMeasurementExpression(
                    measurementId,
                    DimensionExpressionInput.Text ?? string.Empty,
                    out _))
            {
                if (isCreationPrecision)
                    FinishDimensionExpressionInput(viewModel);
                else
                {
                    viewModel.TwoDSelectedMeasurementId = null;
                    TwoDPreviewCanvas.DismissDimensionExpressionInput();
                    TwoDPreviewCanvas.Focus();
                }
            }
            else
            {
                _rejectedDimensionExpressionText = DimensionExpressionInput.Text ?? string.Empty;
                KeepDimensionExpressionInputFocused();
            }
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        TwoDPreviewCanvas.DismissDimensionExpressionInput();
        if (DataContext is Domain.App.ViewModels.EditorPageViewModel escapeViewModel)
        {
            escapeViewModel.TwoDSelectedMeasurementId = null;
            escapeViewModel.TwoDActiveTool = Domain.App.Models.Editor2DTool.Select;
        }
        TwoDPreviewCanvas.Focus();
        e.Handled = true;
    }

    private static bool TryGetCreationPrecisionMeasurement(
        Domain.App.ViewModels.EditorPageViewModel viewModel,
        string measurementId,
        out Domain.App.Models.Editor2DMeasurement measurement)
    {
        measurement = viewModel.TwoDMeasurements.FirstOrDefault(item =>
            item.Id == measurementId
            && item.IsAutoDimension
            && (item.DimensionType?.Trim().Equals("length", StringComparison.OrdinalIgnoreCase) == true
                || item.DimensionType?.Trim().Equals("radius", StringComparison.OrdinalIgnoreCase) == true
                || item.DimensionType?.Trim().Equals("width", StringComparison.OrdinalIgnoreCase) == true
                || item.DimensionType?.Trim().Equals("height", StringComparison.OrdinalIgnoreCase) == true))!;
        return measurement is not null;
    }

    private void KeepDimensionExpressionInputFocused()
        => Dispatcher.UIThread.Post(() =>
        {
            DimensionExpressionInput.Focus();
            DimensionExpressionInput.SelectAll();
        }, DispatcherPriority.Input);

    private void FinishDimensionExpressionInput(
        Domain.App.ViewModels.EditorPageViewModel viewModel)
    {
        TwoDPreviewCanvas.DismissDimensionExpressionInput();
        viewModel.TwoDSelectedMeasurementId = null;
        viewModel.TwoDActiveTool = Domain.App.Models.Editor2DTool.Select;
        TwoDPreviewCanvas.Focus();
    }

    private void OnDimensionExpressionInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var rawExpression = textBox.Text ?? string.Empty;
        ToolTip.SetTip(textBox, rawExpression);
        AutomationProperties.SetName(textBox, $"Dimension expression: {rawExpression}");
        if (DataContext is Domain.App.ViewModels.EditorPageViewModel viewModel
            && viewModel.HasTwoDMeasurementExpressionError)
        {
            if (!string.Equals(rawExpression, _rejectedDimensionExpressionText, StringComparison.Ordinal))
            {
                _rejectedDimensionExpressionText = null;
                viewModel.ClearTwoDMeasurementExpressionError();
            }
        }
    }

    private void OnTransformPrecisionDismissed()
    {
        GizmoDimensionPill.IsVisible = false;
        GizmoDimensionPill.IsHitTestVisible = false;
    }

    private void OnGizmoDimensionInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (!TwoDPreviewCanvas.TryApplyTransformPrecisionInput(GizmoDimensionInput.Text))
                GizmoDimensionInput.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        TwoDPreviewCanvas.DismissTransformPrecisionInput(exitToSelect: true);
        TwoDPreviewCanvas.Focus();
        e.Handled = true;
    }

    private void OnWorkspacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;

        if ((DimensionExpressionPill.IsVisible && IsDescendantOf(source, DimensionExpressionPill))
            || (GizmoDimensionPill.IsVisible && IsDescendantOf(source, GizmoDimensionPill)))
            return;

        var shouldConsume = IsDescendantOf(source, WorkspaceRoot);

        if (DimensionExpressionPill.IsVisible)
        {
            var expressionViewModel = DataContext as Domain.App.ViewModels.EditorPageViewModel;
            var isCreationPrecision = _dimensionExpressionMeasurementId is { } measurementId
                && expressionViewModel is not null
                && TryGetCreationPrecisionMeasurement(expressionViewModel, measurementId, out _);
            if (isCreationPrecision)
                FinishDimensionExpressionInput(expressionViewModel!);
            else
                TwoDPreviewCanvas.DismissDimensionExpressionInput();
            e.Handled = shouldConsume;
        }
        if (GizmoDimensionPill.IsVisible)
        {
            TwoDPreviewCanvas.DismissTransformPrecisionInput();
            e.Handled = shouldConsume;
        }
    }

    private void OnGizmoDimensionInputLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!GizmoDimensionPill.IsVisible)
                return;

            var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (focusedElement is Visual focusedVisual
                && IsDescendantOf(focusedVisual, GizmoDimensionPill))
            {
                return;
            }

            TwoDPreviewCanvas.DismissTransformPrecisionInput();
        }, DispatcherPriority.Input);
    }

    private static bool IsDescendantOf(Visual source, Visual ancestor)
    {
        for (Visual? current = source; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }
}
