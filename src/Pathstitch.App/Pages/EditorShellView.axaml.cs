using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Dialogs;
using Pathstitch.App.Services;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Pathstitch.App.Pages;

public partial class EditorShellView : EditorInteractionControlBase
{
    public EditorShellView()
    {
        InitializeComponent();
        NewProjectMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.N);
        OpenProjectMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.O);
        ImportFilesMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.I, shift: true);
        SaveMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.S);
        SaveAsMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.S, shift: true);
        SaveAndCloseMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.W, shift: true);
        CloseDocumentMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.W);
        ExportDxfMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.E);
        ExportSvgMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.E, shift: true);
        SearchCommandsMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.K);
        Loaded += OnLoaded;
        KeyDown += OnEditorKeyDown;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Focus();
        if (DataContext is EditorPageViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _ = ShowModeIntroIfNeededAsync(viewModel);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorPageViewModel.ActiveEditorMode)
            && sender is EditorPageViewModel viewModel)
            _ = ShowModeIntroIfNeededAsync(viewModel);
    }

    private async Task ShowModeIntroIfNeededAsync(EditorPageViewModel viewModel)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        var store = new UserPreferencesStore();
        var preferences = store.Load();
        if (!preferences.TutorialCompleted)
        {
            await new TutorialDialog(store).ShowDialog(owner);
            preferences = store.Load();
        }

        var dismissed = viewModel.ActiveEditorMode switch
        {
            EditorMode.TwoD => preferences.TwoDIntroDismissed,
            EditorMode.ThreeD => preferences.ThreeDIntroDismissed,
            _ => preferences.BatchIntroDismissed,
        };
        if (dismissed)
            return;

        await new ModeIntroDialog(viewModel.ActiveEditorMode, store).ShowDialog(owner);
    }

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

    private void OnZoomInClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ZoomTwoDIn();
    }

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ZoomTwoDOut();
    }

    private void OnZoomToFitClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.FrameTwoDToContent();
    }

    private void OnSearchCommandsClicked(object? sender, RoutedEventArgs e)
        => CommandPalette.FocusSearch();

    private void OnToolsSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        const int staticItemCount = 2;
        while (ToolsMenu.Items.Count > staticItemCount)
            ToolsMenu.Items.RemoveAt(ToolsMenu.Items.Count - 1);

        if (DataContext is not EditorPageViewModel viewModel)
            return;

        var addedTool = false;
        foreach (var tool in viewModel.SidebarTools.Where(tool => tool.Tool is not null || tool.TwoDTool is not null))
        {
            if (tool.StartsSection && addedTool)
                ToolsMenu.Items.Add(new Separator());

            var menuItem = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(tool.ShortcutText)
                    ? tool.Label
                    : $"{tool.Label}\t{tool.ShortcutText}",
                IsEnabled = tool.IsEnabled,
                IsChecked = tool.IsActive,
                ToggleType = MenuItemToggleType.Radio,
                Tag = tool.Key,
            };
            AutomationProperties.SetAutomationId(menuItem, $"editor.menu.tools.{tool.Identifier}");
            menuItem.Click += OnCatalogToolClicked;
            ToolsMenu.Items.Add(menuItem);
            addedTool = true;
        }
    }

    private void OnModifySubmenuOpened(object? sender, RoutedEventArgs e)
    {
        ModifyMenu.Items.Clear();
        if (DataContext is not EditorPageViewModel viewModel)
            return;

        foreach (var tool in viewModel.SidebarTools.Where(tool =>
                     tool.Action is EditorSidebarAction.FlipSelectionHorizontal or EditorSidebarAction.FlipSelectionVertical))
        {
            var menuItem = new MenuItem
            {
                Header = tool.Label,
                IsEnabled = tool.IsEnabled,
                Tag = tool.Key,
            };
            AutomationProperties.SetAutomationId(menuItem, $"editor.menu.modify.{tool.Identifier}");
            menuItem.Click += OnCatalogToolClicked;
            ModifyMenu.Items.Add(menuItem);
        }
    }

    private void OnCatalogToolClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel
            && sender is MenuItem { Tag: string toolKey, IsEnabled: true })
        {
            viewModel.ActivateSidebarItem(toolKey);
        }
    }

    private async void OnAboutClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var dialog = new AboutDialog(Ioc.Default.GetRequiredService<IAppUpdateService>());
        await dialog.ShowDialog(owner);
    }

    private void OnCheckForUpdatesClicked(object? sender, RoutedEventArgs e)
        => Ioc.Default.GetRequiredService<IAppUpdateService>().CheckForUpdates();

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
        var commandModifier = DesktopPrimaryShortcut.Matches(e.KeyModifiers);
        var commandShiftModifier = DesktopPrimaryShortcut.Matches(e.KeyModifiers, shift: true);
        var validModifiedShortcut = (e.Key == Key.K && commandModifier)
            || (e.Key == Key.D && commandModifier)
            || (e.Key == Key.Z && (commandModifier || commandShiftModifier))
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

        if (e.Key == Key.Z && (commandModifier || commandShiftModifier))
        {
            var command = commandShiftModifier ? viewModel.RedoCommand : viewModel.UndoCommand;
            if (command.CanExecute(null))
                command.Execute(null);
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
