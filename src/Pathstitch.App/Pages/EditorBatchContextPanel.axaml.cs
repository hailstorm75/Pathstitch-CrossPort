using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class EditorBatchContextPanel : UserControl
{
    public EditorBatchContextPanel() => InitializeComponent();

    private void OnAddProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorBatchWorkspaceViewModel viewModel)
            viewModel.AddInputFile();
    }

    private async void OnRunBatchClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorBatchWorkspaceViewModel viewModel)
            await viewModel.RunAsync();
    }
}
