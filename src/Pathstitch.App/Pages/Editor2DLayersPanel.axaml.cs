using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Converters;

namespace Pathstitch.App.Pages;

public partial class Editor2DLayersPanel : UserControl
{
    private CancellationTokenSource? _layerColorCommitCancellation;
    private string? _editingLayerColorId;
    private string? _editingReferenceOpacityLayerId;
    private Button? _pressedReferenceNudgeButton;
    private KeyModifiers _pressedReferenceNudgeModifiers;

    public Editor2DLayersPanel()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            CommitPendingLayerColorEdit();
            CommitPendingReferenceOpacityEdit();
        };
    }

    private void OnCreateLayerClicked(object? sender, RoutedEventArgs e) => ViewModel?.CreateTwoDLayer();

    private void OnCreateFolderClicked(object? sender, RoutedEventArgs e) => ViewModel?.CreateTwoDFolder();

    private void OnCreateSubfolderClicked(object? sender, RoutedEventArgs e)
        => WithFolder(sender, id => ViewModel?.CreateTwoDFolder(id));

    private void OnToggleFolderExpandedClicked(object? sender, RoutedEventArgs e)
        => WithFolder(sender, id => ViewModel?.ToggleTwoDFolderExpanded(id));

    private void OnRenameFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { Tag: string folderId })
            return;
        var textBox = (sender as Control)?.GetLogicalAncestors().OfType<StackPanel>().FirstOrDefault()?
            .GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(control => Equals(control.Tag, folderId));
        if (textBox is not null)
            ViewModel.RenameTwoDFolder(folderId, textBox.Text ?? string.Empty);
    }

    private void OnDeleteFolderClicked(object? sender, RoutedEventArgs e) => WithFolder(sender, id => ViewModel?.DeleteTwoDFolder(id));

    private void OnMoveFolderUpClicked(object? sender, RoutedEventArgs e)
        => WithFolder(sender, id => ViewModel?.MoveTwoDFolder(id, -1));

    private void OnMoveFolderDownClicked(object? sender, RoutedEventArgs e)
        => WithFolder(sender, id => ViewModel?.MoveTwoDFolder(id, 1));

    private void OnMoveFolderToRootClicked(object? sender, RoutedEventArgs e)
        => WithFolder(sender, id => ViewModel?.MoveTwoDFolderToFolder(id, null));

    private void OnFolderParentChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null
            && sender is ComboBox { Tag: string folderId }
            && e.AddedItems.Count > 0)
            ViewModel.MoveTwoDFolderToFolder(folderId, (e.AddedItems[0] as Editor2DLayerFolder)?.Id);
    }

    private void OnMoveLayerToRootClicked(object? sender, RoutedEventArgs e)
        => WithLayer(sender, id => ViewModel?.MoveTwoDLayerToFolder(id, null));

    private async void OnHierarchyDragStarted(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: Editor2DLayerHierarchyItem item }
            || !e.GetCurrentPoint((Control)sender).Properties.IsLeftButtonPressed)
            return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(item.Id));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private static void OnHierarchyDragOver(object? sender, DragEventArgs e)
    {
        var sourceId = e.DataTransfer.TryGetText();
        e.DragEffects = sender is Control { DataContext: Editor2DLayerHierarchyItem target }
            && !string.IsNullOrWhiteSpace(sourceId)
            && !string.Equals(sourceId, target.Id, StringComparison.Ordinal)
                ? DragDropEffects.Move
                : DragDropEffects.None;
    }

    private void OnHierarchyDrop(object? sender, DragEventArgs e)
    {
        var sourceId = e.DataTransfer.TryGetText();
        if (ViewModel is not null
            && sender is Control { DataContext: Editor2DLayerHierarchyItem target }
            && !string.IsNullOrWhiteSpace(sourceId))
            ViewModel.ReorderTwoDHierarchyItem(sourceId, target.Id);
    }

    private void OnLayerFolderChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null
            && sender is ComboBox { Tag: string layerId } combo
            && e.AddedItems.Count > 0)
            ViewModel.MoveTwoDLayerToFolder(layerId, (e.AddedItems[0] as Editor2DLayerFolder)?.Id);
    }

    private void OnMergeSelectedLayersClicked(object? sender, RoutedEventArgs e) => ViewModel?.MergeTwoDSelectedLayers();

    private async void OnImportReferenceImageClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            await ViewModel.ImportTwoDReferenceImageAsync();
    }

    private void OnSelectLayerClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.SelectTwoDLayer(id));

    private void OnToggleVisibilityClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.ToggleTwoDLayerVisibility(id));

    private void OnToggleLockClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.ToggleTwoDLayerLock(id));

    private void OnMoveLayerUpClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.MoveTwoDLayer(id, -1));

    private void OnMoveLayerDownClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.MoveTwoDLayer(id, 1));

    private void OnMergeLayerWithBelowClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.MergeTwoDLayerWithBelow(id));

    private void OnAssignSelectionClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.AssignTwoDSelectionToLayer(id));

    private void OnRenameLayerClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { Tag: string layerId })
            return;
        var textBox = (sender as Control)?.GetLogicalAncestors().OfType<Border>().FirstOrDefault()?
            .GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(control => Equals(control.Tag, layerId));
        if (textBox is not null)
            ViewModel.RenameTwoDLayer(layerId, textBox.Text ?? string.Empty);
    }

    private void OnDeleteLayerClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.DeleteTwoDLayer(id));

    private void OnSetLayerColorClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { Tag: string layerId })
            return;
        var textBox = (sender as Control)?.GetLogicalAncestors().OfType<Border>().FirstOrDefault()?
            .GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(control => Equals(control.Tag, layerId)
                && control.Width < 100);
        if (textBox is not null)
            CommitLayerColorText(textBox, layerId);
    }

    private async void OnLayerColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel
            || sender is not ColorPicker { Tag: string layerId } colorPicker
            || (!colorPicker.IsKeyboardFocusWithin && !colorPicker.IsPointerOver))
            return;
        var colorHex = LayerColorHexToColorConverter.ToHex(e.NewColor);
        if (string.Equals(
                viewModel.TwoDLayers.FirstOrDefault(layer => layer.Id == layerId)?.ColorHex,
                colorHex,
                StringComparison.OrdinalIgnoreCase))
            return;
        if (!string.Equals(_editingLayerColorId, layerId, StringComparison.Ordinal))
        {
            CommitPendingLayerColorEdit();
            if (!viewModel.BeginTwoDLayerColorEdit(layerId))
                return;
            _editingLayerColorId = layerId;
        }
        if (!viewModel.PreviewTwoDLayerColor(layerId, colorHex))
            return;

        _layerColorCommitCancellation?.Cancel();
        _layerColorCommitCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _layerColorCommitCancellation = cancellation;
        try
        {
            await Task.Delay(350, cancellation.Token);
            if (!cancellation.IsCancellationRequested)
                CommitPendingLayerColorEdit();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private void OnLayerColorTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string layerId } textBox)
            return;
        if (e.Key == Key.Enter)
        {
            CommitLayerColorText(textBox, layerId);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && ViewModel?.TwoDLayers.FirstOrDefault(layer => layer.Id == layerId) is { } layer)
        {
            textBox.Text = layer.ColorHex;
            SetLayerColorTextValidity(textBox, true);
            e.Handled = true;
        }
    }

    private void OnLayerColorTextLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: string layerId } textBox)
            CommitLayerColorText(textBox, layerId);
    }

    private void CommitLayerColorText(TextBox textBox, string layerId)
    {
        CommitPendingLayerColorEdit();
        if (!TryNormalizeLayerColorHex(textBox.Text, out var normalized))
        {
            SetLayerColorTextValidity(textBox, false);
            return;
        }
        ViewModel?.SetTwoDLayerColor(layerId, normalized);
        textBox.Text = ViewModel?.TwoDLayers.FirstOrDefault(layer => layer.Id == layerId)?.ColorHex ?? normalized;
        SetLayerColorTextValidity(textBox, true);
    }

    private void CommitPendingLayerColorEdit()
    {
        _layerColorCommitCancellation?.Cancel();
        _layerColorCommitCancellation?.Dispose();
        _layerColorCommitCancellation = null;
        var editingLayerColorId = _editingLayerColorId;
        _editingLayerColorId = null;
        ViewModel?.CommitTwoDLayerColorEdit(editingLayerColorId);
    }

    private static void SetLayerColorTextValidity(TextBox textBox, bool isValid)
    {
        textBox.Classes.Set("invalid", !isValid);
        ToolTip.SetTip(textBox, isValid ? null : "Use an opaque color in #RRGGBB format");
    }

    internal static bool TryNormalizeLayerColorHex(string? value, out string normalized)
    {
        normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized.Length == 7
            && normalized[0] == '#'
            && normalized.Skip(1).All(Uri.IsHexDigit);
    }

    private void OnReferencePositionTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string layerId } textBox)
            return;

        if (e.Key == Key.Enter)
        {
            CommitReferencePositionText(textBox, layerId);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RestoreReferencePositionText(textBox, layerId);
            e.Handled = true;
        }
    }

    private void OnReferencePositionTextLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: string layerId } textBox)
            CommitReferencePositionText(textBox, layerId);
    }

    private void CommitReferencePositionText(TextBox textBox, string layerId)
    {
        var viewModel = ViewModel;
        var image = viewModel?.TwoDLayers.FirstOrDefault(layer => layer.Id == layerId)?.ReferenceImage;
        if (viewModel is null || image is null)
            return;

        if (!TryParseReferencePosition(textBox.Text, out var value))
        {
            SetReferencePositionTextValidity(textBox, false);
            return;
        }

        var editsX = textBox.Classes.Contains("reference-position-x");
        var currentValue = editsX ? image.X : image.Y;
        if (!value.Equals(currentValue) && viewModel.BeginTwoDReferenceImageTransform(layerId))
        {
            viewModel.UpdateTwoDReferenceImageTransform(
                layerId,
                editsX ? value : image.X,
                editsX ? image.Y : value,
                image.Width,
                image.Height,
                image.RotationDegrees);
            viewModel.CommitTwoDReferenceImageTransform();
        }

        textBox.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
        SetReferencePositionTextValidity(textBox, true);
    }

    private void RestoreReferencePositionText(TextBox textBox, string layerId)
    {
        var image = ViewModel?.TwoDLayers.FirstOrDefault(layer => layer.Id == layerId)?.ReferenceImage;
        if (image is null)
            return;

        var value = textBox.Classes.Contains("reference-position-x") ? image.X : image.Y;
        textBox.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
        SetReferencePositionTextValidity(textBox, true);
    }

    private static void SetReferencePositionTextValidity(TextBox textBox, bool isValid)
    {
        textBox.Classes.Set("invalid", !isValid);
        ToolTip.SetTip(textBox, isValid ? null : "Enter a finite position in millimeters");
    }

    internal static bool TryParseReferencePosition(string? value, out double position)
        => double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out position)
           && double.IsFinite(position);

    private void OnReferenceNudgePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedReferenceNudgeButton = sender as Button;
        _pressedReferenceNudgeModifiers = e.KeyModifiers;
    }

    private double ConsumeReferenceNudgeStep(object? sender)
    {
        var modifiers = ReferenceEquals(sender, _pressedReferenceNudgeButton)
            ? _pressedReferenceNudgeModifiers
            : KeyModifiers.None;
        _pressedReferenceNudgeButton = null;
        _pressedReferenceNudgeModifiers = KeyModifiers.None;
        return GetReferenceNudgeStep(modifiers);
    }

    internal static double GetReferenceNudgeStep(KeyModifiers modifiers)
        => modifiers.HasFlag(KeyModifiers.Shift) ? 10.0 : 1.0;

    private void OnReferenceMoveLeftClicked(object? sender, RoutedEventArgs e)
    {
        var step = ConsumeReferenceNudgeStep(sender);
        WithLayer(sender, id => ViewModel?.MoveTwoDReferenceImage(id, -step, 0));
    }

    private void OnReferenceMoveRightClicked(object? sender, RoutedEventArgs e)
    {
        var step = ConsumeReferenceNudgeStep(sender);
        WithLayer(sender, id => ViewModel?.MoveTwoDReferenceImage(id, step, 0));
    }

    private void OnReferenceMoveUpClicked(object? sender, RoutedEventArgs e)
    {
        var step = ConsumeReferenceNudgeStep(sender);
        WithLayer(sender, id => ViewModel?.MoveTwoDReferenceImage(id, 0, step));
    }

    private void OnReferenceMoveDownClicked(object? sender, RoutedEventArgs e)
    {
        var step = ConsumeReferenceNudgeStep(sender);
        WithLayer(sender, id => ViewModel?.MoveTwoDReferenceImage(id, 0, -step));
    }

    private void OnReferenceScaleDownClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.ScaleTwoDReferenceImage(id, 0.9));

    private void OnReferenceScaleUpClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.ScaleTwoDReferenceImage(id, 1.1));

    private void OnReferenceRotateClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.RotateTwoDReferenceImage(id, 5));

    private void OnReferenceDepthBackClicked(object? sender, RoutedEventArgs e)
        => WithLayer(sender, id => ViewModel?.SetTwoDReferenceImageDepth(id, Editor2DReferenceImageDepth.Back));

    private void OnReferenceDepthFrontClicked(object? sender, RoutedEventArgs e)
        => WithLayer(sender, id => ViewModel?.SetTwoDReferenceImageDepth(id, Editor2DReferenceImageDepth.Front));

    private void OnReferenceFadeClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.AdjustTwoDReferenceImageOpacity(id, -0.1));

    private void OnReferenceBrightenClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.AdjustTwoDReferenceImageOpacity(id, 0.1));

    private void OnReferenceOpacitySliderLostFocus(object? sender, RoutedEventArgs e)
        => CommitPendingReferenceOpacityEdit();

    private void OnReferenceOpacitySliderPointerReleased(object? sender, PointerReleasedEventArgs e)
        => CommitPendingReferenceOpacityEdit();

    private void OnReferenceOpacitySliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider { Tag: string layerId } slider)
            return;
        if (_editingReferenceOpacityLayerId != layerId
            && (slider.IsKeyboardFocusWithin || slider.IsPointerOver))
            BeginReferenceOpacityEdit(slider);
        if (_editingReferenceOpacityLayerId == layerId)
            ViewModel?.SetTwoDReferenceImageOpacity(layerId, e.NewValue);
    }

    private void BeginReferenceOpacityEdit(object? sender)
    {
        if (sender is not Slider { Tag: string layerId }
            || _editingReferenceOpacityLayerId == layerId)
            return;

        CommitPendingReferenceOpacityEdit();
        if (ViewModel?.BeginTwoDReferenceImageTransform(layerId) == true)
            _editingReferenceOpacityLayerId = layerId;
    }

    private void CommitPendingReferenceOpacityEdit()
    {
        if (_editingReferenceOpacityLayerId is null)
            return;
        _editingReferenceOpacityLayerId = null;
        ViewModel?.CommitTwoDReferenceImageTransform();
    }

    private void OnReferenceOpacity10Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 0.1);
    private void OnReferenceOpacity25Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 0.25);
    private void OnReferenceOpacity50Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 0.5);
    private void OnReferenceOpacity75Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 0.75);
    private void OnReferenceOpacity100Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 1.0);

    private void OnReferenceThresholdDownClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.AdjustTwoDReferenceTraceThreshold(id, -0.05));

    private void OnReferenceThresholdUpClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.AdjustTwoDReferenceTraceThreshold(id, 0.05));

    private void OnTraceReferenceImageClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.TraceTwoDReferenceImage(id));

    private void OnCommitReferenceTraceClicked(object? sender, RoutedEventArgs e)
        => ViewModel?.CommitTwoDReferenceTrace();

    private void OnCancelReferenceTraceClicked(object? sender, RoutedEventArgs e)
        => ViewModel?.CancelTwoDReferenceTrace();

    private void OnRemoveReferenceBackgroundClicked(object? sender, RoutedEventArgs e)
        => WithLayer(sender, id => ViewModel?.RemoveTwoDReferenceImageBackground(id));

    private void OnRestoreReferenceBackgroundClicked(object? sender, RoutedEventArgs e)
        => WithLayer(sender, id => ViewModel?.RestoreTwoDReferenceImageBackground(id));

    private void OnCalibrateReferenceImageClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.TwoDActiveLayerId is { } layerId)
            ViewModel.CalibrateTwoDReferenceImage(layerId);
    }

    private void OnBeginReferencePointCalibrationClicked(object? sender, RoutedEventArgs e)
        => ViewModel?.BeginTwoDReferencePointCalibration();

    private void OnCancelReferencePointCalibrationClicked(object? sender, RoutedEventArgs e)
        => ViewModel?.CancelTwoDReferencePointCalibration();

    private EditorPageViewModel? ViewModel => DataContext as EditorPageViewModel;

    private static void WithLayer(object? sender, Action<string>? action)
    {
        if (sender is Button { Tag: string layerId })
            action?.Invoke(layerId);
    }

    private void SetReferenceOpacity(object? sender, double opacity)
        => WithLayer(sender, id => ViewModel?.SetTwoDReferenceImageOpacity(id, opacity));

    private static void WithFolder(object? sender, Action<string>? action)
    {
        if (sender is Button { Tag: string folderId })
            action?.Invoke(folderId);
    }
}
