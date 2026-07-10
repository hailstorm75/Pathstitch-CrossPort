using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class EditorCommandPalette : UserControl
{
    public EditorCommandPalette() => InitializeComponent();

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not EditorPageViewModel viewModel)
            return;

        viewModel.CommandSearchQuery = string.Empty;
        e.Handled = true;
    }

    private void OnCommandClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button { Tag: string identifier })
        {
            return;
        }

        viewModel.ActivateCommandSearchItem(identifier);
    }
}
