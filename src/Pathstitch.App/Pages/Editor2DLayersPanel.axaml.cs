using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Domain.App.Models;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class Editor2DLayersPanel : UserControl
{
    public Editor2DLayersPanel() => InitializeComponent();

    private void OnCreateLayerClicked(object? sender, RoutedEventArgs e) => ViewModel?.CreateTwoDLayer();

    private void OnCreateFolderClicked(object? sender, RoutedEventArgs e) => ViewModel?.CreateTwoDFolder();

    private void OnDeleteFolderClicked(object? sender, RoutedEventArgs e) => WithFolder(sender, id => ViewModel?.DeleteTwoDFolder(id));

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
            ViewModel.SetTwoDLayerColor(layerId, textBox.Text ?? string.Empty);
    }

    private void OnReferenceMoveLeftClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.MoveTwoDReferenceImage(id, -5, 0));

    private void OnReferenceMoveRightClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.MoveTwoDReferenceImage(id, 5, 0));

    private void OnReferenceMoveUpClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.MoveTwoDReferenceImage(id, 0, 5));

    private void OnReferenceMoveDownClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.MoveTwoDReferenceImage(id, 0, -5));

    private void OnReferenceScaleDownClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.ScaleTwoDReferenceImage(id, 0.9));

    private void OnReferenceScaleUpClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.ScaleTwoDReferenceImage(id, 1.1));

    private void OnReferenceRotateClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.RotateTwoDReferenceImage(id, 5));

    private void OnReferenceDepthBackClicked(object? sender, RoutedEventArgs e)
        => WithLayer(sender, id => ViewModel?.SetTwoDReferenceImageDepth(id, Editor2DReferenceImageDepth.Back));

    private void OnReferenceDepthFrontClicked(object? sender, RoutedEventArgs e)
        => WithLayer(sender, id => ViewModel?.SetTwoDReferenceImageDepth(id, Editor2DReferenceImageDepth.Front));

    private void OnReferenceFadeClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.AdjustTwoDReferenceImageOpacity(id, -0.1));

    private void OnReferenceBrightenClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.AdjustTwoDReferenceImageOpacity(id, 0.1));

    private void OnReferenceOpacity10Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 0.1);
    private void OnReferenceOpacity25Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 0.25);
    private void OnReferenceOpacity50Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 0.5);
    private void OnReferenceOpacity75Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 0.75);
    private void OnReferenceOpacity100Clicked(object? sender, RoutedEventArgs e) => SetReferenceOpacity(sender, 1.0);

    private void OnReferenceThresholdDownClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.AdjustTwoDReferenceTraceThreshold(id, -0.05));

    private void OnReferenceThresholdUpClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.AdjustTwoDReferenceTraceThreshold(id, 0.05));

    private void OnTraceReferenceImageClicked(object? sender, RoutedEventArgs e) => WithLayer(sender, id => ViewModel?.TraceTwoDReferenceImage(id));

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
