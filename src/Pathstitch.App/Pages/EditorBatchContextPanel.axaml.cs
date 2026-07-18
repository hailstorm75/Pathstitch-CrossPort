using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.DependencyInjection;
using Domain.App.Services;
using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Services;
using System.Globalization;

namespace Pathstitch.App.Pages;

public partial class EditorBatchContextPanel : UserControl
{
    public EditorBatchContextPanel() => InitializeComponent();

    private EditorPageViewModel? EditorViewModel
        => (TopLevel.GetTopLevel(this) as MainWindowShell)?.CurrentPageViewModel as EditorPageViewModel;

    private void OnAddProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorBatchWorkspaceViewModel viewModel)
            viewModel.AddInputFile();
    }

    private async void OnPickBatchInputsClicked(object? sender, RoutedEventArgs e)
    {
        if (EditorViewModel is { } viewModel)
            await viewModel.PickBatchInputFilesAsync();
    }

    private async void OnPickBatchOutputFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (EditorViewModel is { } viewModel)
            await viewModel.ChooseBatchOutputFolderAsync();
    }

    private async void OnRevealBatchOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (EditorViewModel is { } viewModel)
            await viewModel.RevealBatchOutputAsync();
    }

    private async void OnRunBatchClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorBatchWorkspaceViewModel viewModel)
            await viewModel.RunAsync();
    }

    private async void OnExportDxfClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorBatchWorkspaceViewModel viewModel)
            await viewModel.ExportDxfAsync(Ioc.Default.GetRequiredService<IEditorOutputPreviewService>());
    }

    private async void OnApplyOffsetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorBatchWorkspaceViewModel viewModel)
            return;

        var distanceText = this.FindControl<TextBox>("OffsetDistanceText")?.Text;
        if (!double.TryParse(distanceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance)
            || distance <= 0)
            distance = 1.0;

        await viewModel.ApplyOffsetAsync(
            Ioc.Default.GetRequiredService<IEditorOutputPreviewService>(),
            Ioc.Default.GetRequiredService<IEditor2DGeometryKernelService>(),
            distance);
    }

    private async void OnApplySewingHolesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorBatchWorkspaceViewModel viewModel)
            return;

        var diameter = ParsePositive(SewingDiameterText.Text, 1.0);
        var pitch = ParsePositive(SewingPitchText.Text, 4.0);
        var margin = double.TryParse(SewingMarginText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMargin)
            ? parsedMargin
            : 2.0;
        var proximityDistance = ParseNonNegative(SewingProximityDistanceText.Text, 3.0);
        var lineProximityThreshold = ParseNonNegative(SewingLineProximityThresholdText.Text, 1.0);
        await viewModel.ApplySewingHolesAsync(
            Ioc.Default.GetRequiredService<IEditorOutputPreviewService>(),
            new Editor2DSewingHoleParameters(
                Diameter: diameter,
                Pitch: pitch,
                Margin: margin,
                OffsetCornerFillet: SewingOffsetCornerFilletCheck.IsChecked == true,
                ProximityFilterEnabled: SewingProximityFilterCheck.IsChecked == true,
                CornerInterpolationEnabled: SewingCornerInterpolationCheck.IsChecked == true,
                LineProximityFilterEnabled: SewingLineProximityFilterCheck.IsChecked == true,
                LineProximityThreshold: lineProximityThreshold,
                ProximityFilterDistance: proximityDistance));
    }

    private void OnSelectAllClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorBatchWorkspaceViewModel viewModel)
            viewModel.SetAllSelected(true);
    }

    private void OnSelectNoneClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorBatchWorkspaceViewModel viewModel)
            viewModel.SetAllSelected(false);
    }

    private static double ParsePositive(string? value, double fallback)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    private static double ParseNonNegative(string? value, double fallback)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : fallback;

}
