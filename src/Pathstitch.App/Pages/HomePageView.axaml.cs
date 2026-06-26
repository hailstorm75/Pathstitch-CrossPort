using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Domain.App.Models;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using UI.Navigation;

namespace Pathstitch.App.Pages;

public partial class HomePageView : BasePageView
{
    private static readonly IBrush DropSurfaceNormalBrush = new SolidColorBrush(Color.Parse("#141418"));
    private static readonly IBrush DropSurfaceNormalBorderBrush = new SolidColorBrush(Color.Parse("#1FFFFFFF"));
    private static readonly IBrush DropSurfaceActiveBrush = new SolidColorBrush(Color.Parse("#1C1C22"));
    private static readonly IBrush DropSurfaceActiveBorderBrush = new SolidColorBrush(Color.Parse("#4D7FFF"));

    public HomePageView(IServiceProvider serviceProvider) : base(serviceProvider, NavigationAddressBook.HomePage)
    {
        InitializeComponent();
        Focusable = true;
        Loaded += OnLoaded;
        KeyDown += OnHomePageKeyDown;
        DragDrop.AddDragEnterHandler(FileDropSurface, OnFileDropSurfaceDragEnter);
        DragDrop.AddDragLeaveHandler(FileDropSurface, OnFileDropSurfaceDragLeave);
        DragDrop.AddDragOverHandler(FileDropSurface, OnFileDropSurfaceDragOver);
        DragDrop.AddDropHandler(FileDropSurface, OnFileDropSurfaceDrop);
        ResetDropSurfaceState();
    }

    private void OnFileDropSurfaceDragEnter(object? sender, DragEventArgs e)
    {
        SetDropSurfaceState(IsFileDrop(e));
    }

    private void OnFileDropSurfaceDragLeave(object? sender, DragEventArgs e)
    {
        ResetDropSurfaceState();
    }

    private void OnFileDropSurfaceDragOver(object? sender, DragEventArgs e)
    {
        var acceptsFiles = IsFileDrop(e);
        e.DragEffects = acceptsFiles ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropSurfaceState(acceptsFiles);
    }

    private async void OnFileDropSurfaceDrop(object? sender, DragEventArgs e)
    {
        ResetDropSurfaceState();

        if (!IsFileDrop(e))
            return;

        var filePaths = e.DataTransfer.TryGetFiles()
            ?.Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray()
            ?? [];

        if (filePaths.Length == 0 || DataContext is not HomePageViewModel viewModel)
            return;

        await viewModel.OpenFilesAsync(filePaths);
    }

    private void OnRecentProjectPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not HomePageViewModel viewModel
            || sender is not Control control
            || control.DataContext is not RecentProjectSummary recentProject)
            return;

        viewModel.SelectRecentProjectCard(recentProject);
    }

    private async void OnRecentProjectDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not HomePageViewModel viewModel
            || sender is not Control control
            || control.DataContext is not RecentProjectSummary recentProject)
            return;

        viewModel.SelectRecentProjectCard(recentProject);
        await viewModel.OpenRecentProjectCardAsync(recentProject);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Focus();
    }

    private async void OnHomePageKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not HomePageViewModel viewModel || IsTextInputOrigin(e.Source))
            return;

        switch (e.Key)
        {
            case Key.Left:
                viewModel.SelectPreviousRecentProject();
                e.Handled = true;
                break;

            case Key.Right:
                viewModel.SelectNextRecentProject();
                e.Handled = true;
                break;

            case Key.Enter:
                if (!viewModel.HasSelectedRecentProject)
                    return;

                await viewModel.OpenSelectedRecentProjectAsync();
                e.Handled = true;
                break;
        }
    }

    private static bool IsFileDrop(DragEventArgs e)
        => e.DataTransfer.Formats.Contains(DataFormat.File);

    private static bool IsTextInputOrigin(object? source)
        => source is TextBox
           || source is ComboBox
           || source is AutoCompleteBox;

    private void SetDropSurfaceState(bool isActive)
    {
        if (FileDropSurface is null)
            return;

        FileDropSurface.Background = isActive ? DropSurfaceActiveBrush : DropSurfaceNormalBrush;
        FileDropSurface.BorderBrush = isActive ? DropSurfaceActiveBorderBrush : DropSurfaceNormalBorderBrush;
    }

    private void ResetDropSurfaceState()
    {
        SetDropSurfaceState(false);
    }
}
