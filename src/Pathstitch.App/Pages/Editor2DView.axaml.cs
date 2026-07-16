using System;
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
    public Editor2DView()
    {
        InitializeComponent();
        TwoDPreviewCanvas.ReferenceImageTransformChanged += OnReferenceImageTransformChanged;
        TwoDPreviewCanvas.TransformPrecisionRequested += OnTransformPrecisionRequested;
        TwoDPreviewCanvas.TransformPrecisionDismissed += OnTransformPrecisionDismissed;
        WorkspaceRoot.AddHandler(
            InputElement.PointerPressedEvent,
            OnWorkspacePointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
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

    public void CancelActiveInteraction() => TwoDPreviewCanvas.CancelActiveInteraction();

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
        if (!GizmoDimensionPill.IsVisible
            || e.Source is not Visual source
            || IsDescendantOf(source, GizmoDimensionPill))
        {
            return;
        }

        TwoDPreviewCanvas.DismissTransformPrecisionInput();
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
