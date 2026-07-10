using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class Editor3DView : EditorInteractionControlBase
{
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#0D0D10"));
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#151A26"));

    public Editor3DView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewportWebView.NavigationCompleted += OnViewportNavigationCompleted;
        ViewportWebView.WebMessageReceived += OnViewportWebMessageReceived;
        DragDrop.AddDragEnterHandler(WorkspaceViewportSurface, OnDragEnter);
        DragDrop.AddDragLeaveHandler(WorkspaceViewportSurface, OnDragLeave);
        DragDrop.AddDragOverHandler(WorkspaceViewportSurface, OnDragOver);
        DragDrop.AddDropHandler(WorkspaceViewportSurface, OnDrop);
        SetDropState(false);
    }

    public void FrameHome()
    {
        if (DataContext is EditorPageViewModel viewModel)
            ExecuteViewportScript(viewModel.GetHomeFrameScript());
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel)
            return;

        viewModel.ViewportScriptRequested -= OnViewportScriptRequested;
        viewModel.ViewportScriptRequested += OnViewportScriptRequested;
        ViewportWebView.NavigateToString(viewModel.ViewportHtml, viewModel.ViewportBaseUri);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ViewportScriptRequested -= OnViewportScriptRequested;
    }

    private void OnDragEnter(object? sender, DragEventArgs e) => SetDropState(IsFileDrop(e));

    private void OnDragLeave(object? sender, DragEventArgs e) => SetDropState(false);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var acceptsFiles = IsFileDrop(e);
        e.DragEffects = acceptsFiles ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropState(acceptsFiles);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropState(false);
        if (!IsFileDrop(e) || DataContext is not EditorPageViewModel viewModel)
            return;

        var filePaths = e.DataTransfer.TryGetFiles()
            ?.Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray()
            ?? [];
        await viewModel.OpenSourceModelsAsync(filePaths).ConfigureAwait(true);
    }

    private void OnViewportNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.OnViewportNavigationCompleted(e.IsSuccess);
    }

    private void OnViewportWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.OnViewportMessageReceived(e.Body ?? string.Empty);
    }

    private void OnViewportScriptRequested(string script) => ExecuteViewportScript(script);

    private void ExecuteViewportScript(string script)
    {
        try
        {
            ViewportWebView.InvokeScript(script);
        }
        catch
        {
            // The shell tolerates an unloaded or unsupported WebView state.
        }
    }

    private static bool IsFileDrop(DragEventArgs e) => e.DataTransfer.Formats.Contains(DataFormat.File);

    private void SetDropState(bool active) => WorkspaceViewportSurface.Background = active ? ActiveBrush : NormalBrush;
}
