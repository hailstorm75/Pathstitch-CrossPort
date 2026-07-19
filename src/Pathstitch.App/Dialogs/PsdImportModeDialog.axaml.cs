using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.Models;

namespace Pathstitch.App.Dialogs;

public sealed partial class PsdImportModeDialog : Window
{
    public PsdImportModeDialog() => InitializeComponent();

    public void SetImport(PsdImportData import)
    {
        var details = new List<string>();
        if (import.RasterLayers.Count > 0)
            details.Add($"{import.RasterLayers.Count} raster");
        if (import.VectorLayers.Count > 0)
            details.Add($"{import.VectorLayers.Count} vector");
        DescriptionText.Text = $"How would you like to import “{Path.GetFileNameWithoutExtension(import.SourcePath)}”? "
                               + $"{import.TotalLayerCount} layer{(import.TotalLayerCount == 1 ? string.Empty : "s")}"
                               + (details.Count == 0 ? string.Empty : $" ({string.Join(", ", details)})");
    }

    private void OnLoadAsIsClicked(object? sender, RoutedEventArgs e) => Close((PsdImportMode?)PsdImportMode.LoadAsIs);
    private void OnLoadAsOneClicked(object? sender, RoutedEventArgs e) => Close((PsdImportMode?)PsdImportMode.LoadAsOneImage);
    private void OnAutoVectorizeClicked(object? sender, RoutedEventArgs e) => Close((PsdImportMode?)PsdImportMode.AutoVectorize);
    private void OnMergeAndVectorizeClicked(object? sender, RoutedEventArgs e) => Close((PsdImportMode?)PsdImportMode.MergeAndVectorize);
    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close((PsdImportMode?)null);
}
