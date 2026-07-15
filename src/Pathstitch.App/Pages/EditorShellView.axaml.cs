using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Dialogs;

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

    private void OnToggleGridClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ToggleTwoDGrid();
    }

    private void OnToggleChainSelectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ToggleTwoDChainSelection();
    }

    private async void OnAboutClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var dialog = new AboutDialog();
        await dialog.ShowDialog(owner);
    }

    private async void OnPreferencesClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner
            || DataContext is not EditorPageViewModel viewModel)
            return;

        var dialog = new PreferencesDialog(viewModel);
        await dialog.ShowDialog(owner);
    }

    private async void OnDocumentationClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var dialog = new DocumentationDialog();
        await dialog.ShowDialog(owner);
    }

    private async void OnShowBatchWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.SetActiveEditorModeAsync(EditorMode.Batch);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        var commandModifier = e.KeyModifiers is (KeyModifiers.Control or KeyModifiers.Meta);
        var commandShiftModifier = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift)
            || (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Shift)) == (KeyModifiers.Meta | KeyModifiers.Shift);
        var validModifiedShortcut = (e.Key == Key.K && commandModifier)
            || (e.Key == Key.D && commandModifier)
            || (e.Key is Key.H or Key.J && commandShiftModifier)
            || (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Shift);
        if (DataContext is not EditorPageViewModel viewModel
            || IsShortcutSuppressedByFocusedElement()
            || (e.KeyModifiers != KeyModifiers.None
                && !validModifiedShortcut))
            return;

        if (e.Key == Key.K && e.KeyModifiers is (KeyModifiers.Control or KeyModifiers.Meta))
        {
            CommandPalette.FocusSearch();
            e.Handled = true;
            return;
        }

        var shortcutToken = GetShortcutText(e.Key);

        if (shortcutToken is null)
            return;

        if ((e.Key == Key.D
                && e.KeyModifiers is (KeyModifiers.Control or KeyModifiers.Meta)
            || e.Key is Key.H or Key.J
                && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift)
            || e.Key is Key.H or Key.J
                && (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Shift)) == (KeyModifiers.Meta | KeyModifiers.Shift))
            && viewModel.ActiveEditorMode == EditorMode.TwoD)
        {
            var modifier = e.Key == Key.D ? "Ctrl+D" : e.Key == Key.H ? "Ctrl+Shift+H" : "Ctrl+Shift+J";
            if (viewModel.TryActivateEditorShortcut(modifier))
                e.Handled = true;
            return;
        }

        if (shortcutToken.Equals("N", System.StringComparison.OrdinalIgnoreCase)
            && viewModel.ActiveEditorMode == EditorMode.TwoD)
        {
            viewModel.ToggleTwoDSnapping();
            e.Handled = true;
            return;
        }

        if (shortcutToken.Equals("G", System.StringComparison.OrdinalIgnoreCase)
            && e.KeyModifiers == KeyModifiers.Shift
            && viewModel.ActiveEditorMode == EditorMode.TwoD)
        {
            viewModel.ToggleTwoDGrid();
            e.Handled = true;
            return;
        }

        if (shortcutToken.Equals("A", System.StringComparison.OrdinalIgnoreCase)
            && e.KeyModifiers == KeyModifiers.None
            && viewModel.ActiveEditorMode == EditorMode.TwoD)
        {
            viewModel.ToggleTwoDChainSelection();
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
