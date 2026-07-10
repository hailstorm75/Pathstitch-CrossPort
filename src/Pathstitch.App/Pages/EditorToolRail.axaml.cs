using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class EditorToolRail : EditorInteractionControlBase
{
    public EditorToolRail() => InitializeComponent();

    private void OnMoveToolEarlierClicked(object? sender, RoutedEventArgs e)
        => MoveTool(sender, -1);

    private void OnMoveToolLaterClicked(object? sender, RoutedEventArgs e)
        => MoveTool(sender, 1);

    private void MoveTool(object? sender, int direction)
    {
        if (DataContext is EditorPageViewModel viewModel
            && sender is Button { Tag: string identifier })
        {
            viewModel.MoveToolCustomization(identifier, direction);
        }
    }

    private void OnResetToolbarClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ResetToolbarCustomizationForActiveMode();
    }
}
