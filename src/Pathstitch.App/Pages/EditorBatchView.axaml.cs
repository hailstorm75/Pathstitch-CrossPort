using System.Linq;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.DependencyInjection;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;

using Pathstitch.App.Services;
namespace Pathstitch.App.Pages;

public partial class EditorBatchView : UserControl
{
    public EditorBatchView()
    {
        InitializeComponent();
        DragDrop.AddDragOverHandler(BatchDropSurface, OnDragOver);
        DragDrop.AddDropHandler(BatchDropSurface, OnDrop);
    }

    private EditorPageViewModel? EditorViewModel
        => (TopLevel.GetTopLevel(this) as MainWindowShell)?.CurrentPageViewModel as EditorPageViewModel;

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

    private async void OnBatchItemAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is not EditorBatchWorkspaceViewModel viewModel
            || sender is not Control { DataContext: EditorBatchItem item }
            || item.Document is not null)
            return;

        try
        {
            await viewModel.EnsureDocumentAsync(item.Id, Ioc.Default.GetRequiredService<IEditorOutputPreviewService>());
        }
        catch (Exception)
        {
            // Card remains usable when an optional thumbnail cannot be loaded.
        }
    }

    private async void OnEditBatchItemClicked(object? sender, RoutedEventArgs e)
    {
        if (EditorViewModel is { } editor
            && sender is Control { DataContext: EditorBatchItem item })
            await editor.BeginBatchItemEditAsync(item.Id);
    }

    private void OnRemoveBatchItemClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorBatchWorkspaceViewModel viewModel
            && sender is Control { DataContext: EditorBatchItem item })
            viewModel.RemoveProject(item.FilePath);
    }
}
