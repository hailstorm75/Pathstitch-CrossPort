using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Domain.App.Models;
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

    private void OnMoveToolToMainClicked(object? sender, RoutedEventArgs e)
        => MoveToolToContainer(sender, EditorToolbarContainer.Main);

    private void OnMoveToolToShapesClicked(object? sender, RoutedEventArgs e)
        => MoveToolToContainer(sender, EditorToolbarContainer.Shapes);

    private void OnMoveToolToMoreClicked(object? sender, RoutedEventArgs e)
        => MoveToolToContainer(sender, EditorToolbarContainer.More);

    private void MoveToolToContainer(object? sender, EditorToolbarContainer container)
    {
        if (DataContext is EditorPageViewModel viewModel
            && sender is MenuItem { Tag: string identifier })
        {
            viewModel.MoveToolToContainer(identifier, container);
        }
    }

    private async void OnToolbarItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel { IsToolbarCustomizationMode: true }
            || sender is not Control { DataContext: EditorSidebarToolItemViewModel item } control
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(item.Identifier));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnToolbarItemDragOver(object? sender, DragEventArgs e)
    {
        var sourceIdentifier = e.DataTransfer.TryGetText();
        e.DragEffects = DataContext is EditorPageViewModel viewModel
            && sender is Control { DataContext: EditorSidebarToolItemViewModel target }
            && !string.IsNullOrWhiteSpace(sourceIdentifier)
            && !string.Equals(sourceIdentifier, target.Identifier, StringComparison.Ordinal)
            && viewModel.CanPlaceToolInContainer(sourceIdentifier, target.Container)
                ? DragDropEffects.Move
                : DragDropEffects.None;
    }

    private void OnToolbarItemDrop(object? sender, DragEventArgs e)
    {
        var sourceIdentifier = e.DataTransfer.TryGetText();
        if (DataContext is EditorPageViewModel viewModel
            && sender is Control { DataContext: EditorSidebarToolItemViewModel target }
            && !string.IsNullOrWhiteSpace(sourceIdentifier))
        {
            viewModel.MoveToolBefore(sourceIdentifier, target.Identifier);
        }
    }

    private void OnToolbarContainerDragOver(object? sender, DragEventArgs e)
    {
        var sourceIdentifier = e.DataTransfer.TryGetText();
        e.DragEffects = DataContext is EditorPageViewModel viewModel
            && TryGetContainer(sender, out var container)
            && !string.IsNullOrWhiteSpace(sourceIdentifier)
            && viewModel.CanPlaceToolInContainer(sourceIdentifier, container)
                ? DragDropEffects.Move
                : DragDropEffects.None;
    }

    private void OnToolbarContainerDrop(object? sender, DragEventArgs e)
    {
        var sourceIdentifier = e.DataTransfer.TryGetText();
        if (DataContext is EditorPageViewModel viewModel
            && TryGetContainer(sender, out var container)
            && !string.IsNullOrWhiteSpace(sourceIdentifier))
        {
            viewModel.MoveToolToContainer(sourceIdentifier, container);
        }
    }

    private static bool TryGetContainer(object? sender, out EditorToolbarContainer container)
    {
        container = default;
        return sender is Control { Tag: string containerName }
            && Enum.TryParse(containerName, ignoreCase: true, out container);
    }

    private void OnResetToolbarClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ResetToolbarCustomizationForActiveMode();
    }
}
