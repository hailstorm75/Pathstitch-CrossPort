using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Domain.App.Models;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class EditorBatchView : UserControl
{
    public EditorBatchView()
    {
        InitializeComponent();
        DragDrop.AddDragOverHandler(BatchDropSurface, OnDragOver);
        DragDrop.AddDropHandler(BatchDropSurface, OnDrop);
    }

    private static bool IsFileDrop(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?.Any() == true;

    private void OnDragOver(object? sender, DragEventArgs e)
        => e.DragEffects = IsFileDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!IsFileDrop(e) || DataContext is not EditorBatchWorkspaceViewModel viewModel)
            return;
        var paths = e.DataTransfer.TryGetFiles()?
            .Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray() ?? [];
        viewModel.AddFiles(paths);
    }

    private void OnRemoveBatchItemClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorBatchWorkspaceViewModel viewModel
            && sender is Control { DataContext: EditorBatchItem item })
            viewModel.RemoveProject(item.FilePath);
    }
}
