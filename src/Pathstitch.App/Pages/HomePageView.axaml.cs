using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Pathstitch.App.Services;
using UI.Navigation;

namespace Pathstitch.App.Pages;

public partial class HomePageView : BasePageView
{
    private static readonly IBrush DropSurfaceNormalBrush = new SolidColorBrush(Color.Parse("#141418"));
    private static readonly IBrush DropSurfaceNormalBorderBrush = new SolidColorBrush(Color.Parse("#1FFFFFFF"));
    private static readonly IBrush DropSurfaceActiveBrush = new SolidColorBrush(Color.Parse("#1C1C22"));
    private static readonly IBrush DropSurfaceActiveBorderBrush = new SolidColorBrush(Color.Parse("#4D7FFF"));
    private readonly UserPreferencesStore _preferencesStore = new();
    private readonly IFileIntegrationService _fileIntegrationService;

    public HomePageView(IServiceProvider serviceProvider) : base(serviceProvider, NavigationAddressBook.HomePage)
    {
        _fileIntegrationService = serviceProvider.GetRequiredService<IFileIntegrationService>();
        InitializeComponent();
        VersionText.Text = AppVersionInfo.HomeLabel;
        GettingStartedCard.IsVisible = !_preferencesStore.Load().GettingStartedDismissed;
        SupportCard.IsVisible = !_preferencesStore.Load().SupportCardDismissed;
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

    private async void OnRevealRecentProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not RecentProjectSummary recentProject)
            return;

        await _fileIntegrationService.RevealFileAsync(recentProject.ProjectFilePath);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Focus();
    }

    private void OnDismissGettingStartedClicked(object? sender, RoutedEventArgs e)
    {
        GettingStartedCard.IsVisible = false;
        _preferencesStore.Save(_preferencesStore.Load() with { GettingStartedDismissed = true });
    }

    private void OnDismissSupportCardClicked(object? sender, RoutedEventArgs e)
    {
        SupportCard.IsVisible = false;
        _preferencesStore.Save(_preferencesStore.Load() with { SupportCardDismissed = true });
    }

    private static void OnSupportCardClicked(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://buymeacoffee.com/masonchen",
            UseShellExecute = true,
        });
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
