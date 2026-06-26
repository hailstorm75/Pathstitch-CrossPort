using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using UI.Navigation;
using Avalonia.Input;
using Avalonia.Media;

namespace Pathstitch.App.Pages;

public partial class EditorPageView : BasePageView
{
    private static readonly IBrush ViewportSurfaceNormalBrush = new SolidColorBrush(Color.Parse("#0D0D10"));
    private static readonly IBrush ViewportSurfaceActiveBrush = new SolidColorBrush(Color.Parse("#151A26"));

    public EditorPageView(IServiceProvider serviceProvider) : base(serviceProvider, NavigationAddressBook.EditorPage)
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewportWebView.NavigationCompleted += OnViewportNavigationCompleted;
        ViewportWebView.WebMessageReceived += OnViewportWebMessageReceived;
        DragDrop.AddDragEnterHandler(WorkspaceViewportSurface, OnViewportSurfaceDragEnter);
        DragDrop.AddDragLeaveHandler(WorkspaceViewportSurface, OnViewportSurfaceDragLeave);
        DragDrop.AddDragOverHandler(WorkspaceViewportSurface, OnViewportSurfaceDragOver);
        DragDrop.AddDropHandler(WorkspaceViewportSurface, OnViewportSurfaceDrop);
        ResetViewportSurfaceState();
    }

    private void OnHomeFrameClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel)
            return;

        ExecuteViewportScript(viewModel.GetHomeFrameScript());
    }

    private void OnSelectToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateSelectTool();
    }

    private void OnMoveToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivateMoveTool();
    }

    private void OnMoveToolIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not ToggleSwitch toggleSwitch)
            return;

        if (toggleSwitch.IsChecked == true)
            viewModel.ActivateMoveTool();
        else if (viewModel.IsMoveToolActive)
            viewModel.ActivateSelectTool();
    }

    private void OnPlaneToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivatePlaneTool();
    }

    private void OnOrthographicToggleClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ToggleOrthographic();
    }

    private void OnAddPlaneClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ActivatePlaneTool();
    }

    private void OnOriginPlaneModeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetPlaneSelectionMode("origin");
    }

    private void OnFacePlaneModeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetPlaneSelectionMode("face");
    }

    private void OnConfirmProjectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ConfirmPlaneProjection();
    }

    private async void OnOpenSourceModelClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.OpenSourceModelAsync();
    }

    private void OnCancelProjectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.CancelPlaneSelection();
    }

    private void OnBodyNudgeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button button
            || button.Tag is not string tag)
            return;

        var parts = tag.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var axis)
            || !int.TryParse(parts[1], out var direction))
            return;

        viewModel.NudgeSelectedBody(axis, direction);
    }

    private void OnBodyResetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ResetSelectedBodyPosition();
    }

    private void OnSetAnchorClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.SetAnchorFromSelection();
    }

    private void OnResetAnchorClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ResetAnchor();
    }

    private async void OnUnfoldSelectedClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RequestUnfoldSelectedAsync();
    }

    private async void OnUnfoldEntireBodyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RequestUnfoldEntireBodyAsync();
    }

    private void OnClearManualCutsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearManualCuts();
    }

    private void OnClearForcedFoldsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ClearForcedFolds();
    }

    private async void OnOpenGeneratedOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.OpenGeneratedOutputAsync();
    }

    private void OnShow3DWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.Show3DWorkspace();
    }

    private void OnShowGeneratedOutputWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ShowGeneratedOutputWorkspace();
    }

    private async void OnRevealGeneratedOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.RevealGeneratedOutputAsync();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel)
            return;

        viewModel.ViewportScriptRequested -= OnViewportScriptRequested;
        viewModel.ViewportScriptRequested += OnViewportScriptRequested;
        viewModel.GeneratedOutputPreviewRequested -= OnGeneratedOutputPreviewRequested;
        viewModel.GeneratedOutputPreviewRequested += OnGeneratedOutputPreviewRequested;
        ViewportWebView.Navigate(new Uri(System.IO.Path.Combine(viewModel.ViewportBaseUri.AbsolutePath, "viewport3d.html")));
        NavigateGeneratedOutputPreview(viewModel.GeneratedOutputPreviewHtml);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
        {
            viewModel.ViewportScriptRequested -= OnViewportScriptRequested;
            viewModel.GeneratedOutputPreviewRequested -= OnGeneratedOutputPreviewRequested;
        }
    }

    private void OnViewportSurfaceDragEnter(object? sender, DragEventArgs e)
    {
        SetViewportSurfaceState(IsFileDrop(e));
    }

    private void OnViewportSurfaceDragLeave(object? sender, DragEventArgs e)
    {
        ResetViewportSurfaceState();
    }

    private void OnViewportSurfaceDragOver(object? sender, DragEventArgs e)
    {
        var acceptsFiles = IsFileDrop(e);
        e.DragEffects = acceptsFiles ? DragDropEffects.Copy : DragDropEffects.None;
        SetViewportSurfaceState(acceptsFiles);
    }

    private async void OnViewportSurfaceDrop(object? sender, DragEventArgs e)
    {
        ResetViewportSurfaceState();

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

    private void ExecuteViewportScript(string script)
    {
        try
        {
            ViewportWebView.InvokeScript(script);
        }
        catch
        {
            // The initial port tolerates an unloaded or unsupported WebView state.
        }
    }

    private void OnViewportScriptRequested(string script) => ExecuteViewportScript(script);

    private void NavigateGeneratedOutputPreview(string html)
    {
        try
        {
            GeneratedOutputPreviewWebView.NavigateToString(
                string.IsNullOrWhiteSpace(html) ? "<html><body style=\"background:#0d0d10\"></body></html>" : html,
                new Uri("about:blank"));
        }
        catch
        {
            // The preview is best-effort; keep the editor usable if the embedded WebView is unavailable.
        }
    }

    private void OnGeneratedOutputPreviewRequested(string html) => NavigateGeneratedOutputPreview(html);

    private void OnBodyVisibilityClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not Button button || button.Tag is not int bodyIndex)
            return;

        viewModel.ToggleBodyVisibility(bodyIndex);
    }

    private void OnFaceSelectionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel || sender is not Button button || button.Tag is not string tag)
            return;

        var parts = tag.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var bodyIndex)
            || !int.TryParse(parts[1], out var faceIndex))
            return;

        var isShiftKey = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        viewModel.SelectFaceFromPanel(bodyIndex, faceIndex, isShiftKey);
        e.Handled = true;
    }

    private static bool IsFileDrop(DragEventArgs e)
        => e.DataTransfer.Formats.Contains(DataFormat.File);

    private void SetViewportSurfaceState(bool isActive)
    {
        if (WorkspaceViewportSurface is null)
            return;

        WorkspaceViewportSurface.Background = isActive ? ViewportSurfaceActiveBrush : ViewportSurfaceNormalBrush;
    }

    private void ResetViewportSurfaceState() => SetViewportSurfaceState(false);
}
