using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Domain.App.Models;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class EditorShellView : EditorInteractionControlBase
{
    public EditorShellView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        KeyDown += OnEditorKeyDown;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => Focus();

    private void OnHomeFrameClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel)
            return;

        if (viewModel.ActiveEditorMode == EditorMode.TwoD)
            viewModel.FrameTwoDToContent();
        else if (viewModel.ActiveEditorMode == EditorMode.ThreeD)
            ThreeDWorkspace.FrameHome();
    }

    private void OnToggleSnappingClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ToggleTwoDSnapping();
    }

    private async void OnShowBatchWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.SetActiveEditorModeAsync(EditorMode.Batch);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || IsShortcutSuppressedByFocusedElement()
            || e.KeyModifiers != KeyModifiers.None)
            return;

        var shortcutToken = GetShortcutText(e.Key);

        if (shortcutToken is null)
            return;

        if (shortcutToken.Equals("N", System.StringComparison.OrdinalIgnoreCase)
            && viewModel.ActiveEditorMode == EditorMode.TwoD)
        {
            viewModel.ToggleTwoDSnapping();
            e.Handled = true;
            return;
        }

        if (shortcutToken == "escape" && viewModel.ActiveEditorMode == EditorMode.TwoD)
            TwoDWorkspace.CancelActiveInteraction();

        if (!viewModel.TryActivateEditorShortcut(shortcutToken)
            && !(shortcutToken == "escape" && viewModel.ActiveEditorMode == EditorMode.TwoD))
            return;

        e.Handled = true;
    }

    private static string? GetShortcutText(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return key.ToString();

        return key switch
        {
            Key.D0 or Key.NumPad0 => "0",
            Key.D1 or Key.NumPad1 => "1",
            Key.D2 or Key.NumPad2 => "2",
            Key.D3 or Key.NumPad3 => "3",
            Key.D4 or Key.NumPad4 => "4",
            Key.D5 or Key.NumPad5 => "5",
            Key.D6 or Key.NumPad6 => "6",
            Key.D7 or Key.NumPad7 => "7",
            Key.D8 or Key.NumPad8 => "8",
            Key.D9 or Key.NumPad9 => "9",
            Key.Delete or Key.Back => "delete-selection",
            Key.Escape => "escape",
            _ => null,
        };
    }

    private bool IsShortcutSuppressedByFocusedElement()
    {
        var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focusedElement is TextBox or ComboBox or NativeWebView;
    }
}
