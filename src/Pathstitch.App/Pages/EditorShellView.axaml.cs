using System.ComponentModel;
using System.Collections.Generic;
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
    private readonly UserPreferencesStore _preferencesStore = new();
    private IReadOnlyDictionary<string, string?> _appShortcuts = new Dictionary<string, string?>();

    public EditorShellView()
    {
        InitializeComponent();
        SaveAndCloseMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.W, shift: true);
        CloseDocumentMenuItem.HotKey = DesktopPrimaryShortcut.Create(Key.W);
        RefreshShortcutBindings();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        KeyDown += OnEditorKeyDown;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Focus();
        if (DataContext is EditorPageViewModel viewModel)
        {
            ApplyCommandShortcutOverrides(viewModel);
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.CommandPaletteHostActionRequested += OnCommandPaletteHostActionRequested;
            _ = ShowModeIntroIfNeededAsync(viewModel);
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel)
            return;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.CommandPaletteHostActionRequested -= OnCommandPaletteHostActionRequested;
    }

    private async void OnCommandPaletteHostActionRequested(object? sender, EditorCommandPaletteHostAction action)
    {
        if (action == EditorCommandPaletteHostAction.StartScreen)
        {
            ShowStartScreen();
            return;
        }
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        if (action == EditorCommandPaletteHostAction.Preferences
            && DataContext is EditorPageViewModel viewModel)
        {
            await new PreferencesDialog(viewModel).ShowDialog(owner);
            RefreshShortcutBindings();
            ApplyCommandShortcutOverrides(viewModel);
        }
        else if (action == EditorCommandPaletteHostAction.Documentation)
            await new DocumentationDialog().ShowDialog(owner);
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

    private void OnToggleActivityLogClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ToggleActivityLog();
    }

    private void OnToggleLearnModeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.ToggleLearnMode();
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

    private static void ShowStartScreen()
        => Ioc.Default.GetRequiredService<DesktopDocumentWindowCoordinator>().ShowStartScreen();

    private void OnShowStartScreenClicked(object? sender, RoutedEventArgs e)
        => ShowStartScreen();

    private async void OnPreferencesClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner
            || DataContext is not EditorPageViewModel viewModel)
            return;

        var dialog = new PreferencesDialog(viewModel);
        await dialog.ShowDialog(owner);
        RefreshShortcutBindings();
        ApplyCommandShortcutOverrides(viewModel);
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

    private async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || IsShortcutSuppressedByFocusedElement())
            return;

        if (EditorShortcutGesture.TryCapture(e.Key, e.KeyModifiers, out var canonical)
            && canonical is not null)
        {
            var appCommand = _appShortcuts.FirstOrDefault(pair =>
                string.Equals(pair.Value, canonical, System.StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(appCommand.Key))
            {
                if (string.Equals(appCommand.Key, EditorCommandPaletteCatalog.SearchIdentifier, System.StringComparison.Ordinal))
                    CommandPalette.FocusSearch();
                else
                    await viewModel.ActivateCommandSearchItemAsync(appCommand.Key);
                e.Handled = true;
                return;
            }
        }

        var shortcutToken = e.KeyModifiers == KeyModifiers.None
            ? GetShortcutText(e.Key)
            : EditorShortcutGesture.ToDisplayText(canonical, isMacOS: false);

        if (shortcutToken is null)
            return;

        if (shortcutToken == "escape" && viewModel.ActiveEditorMode == EditorMode.TwoD)
            TwoDWorkspace.CancelActiveInteraction();

        if (!viewModel.TryActivateEditorShortcut(shortcutToken)
            && !(shortcutToken == "escape" && viewModel.ActiveEditorMode == EditorMode.TwoD))
            return;

        e.Handled = true;
    }

    private void RefreshShortcutBindings()
    {
        _appShortcuts = EditorAppShortcutCatalog.Resolve(_preferencesStore.Load());
        SetHotKey(NewProjectMenuItem, EditorCommandPaletteCatalog.NewIdentifier);
        SetHotKey(OpenProjectMenuItem, EditorCommandPaletteCatalog.OpenIdentifier);
        SetHotKey(ImportFilesMenuItem, EditorCommandPaletteCatalog.ImportIdentifier);
        SetHotKey(SaveMenuItem, EditorCommandPaletteCatalog.SaveIdentifier);
        SetHotKey(SaveAsMenuItem, EditorCommandPaletteCatalog.SaveAsIdentifier);
        SetHotKey(ExportDxfMenuItem, EditorCommandPaletteCatalog.ExportDxfIdentifier);
        SetHotKey(ExportSvgMenuItem, EditorCommandPaletteCatalog.ExportSvgIdentifier);
        SetHotKey(ExportPngMenuItem, EditorCommandPaletteCatalog.ExportPngIdentifier);
        SetHotKey(ExportPdfMenuItem, EditorCommandPaletteCatalog.ExportPdfIdentifier);
        SetHotKey(UndoMenuItem, EditorCommandPaletteCatalog.UndoIdentifier);
        SetHotKey(RedoMenuItem, EditorCommandPaletteCatalog.RedoIdentifier);
        SetHotKey(DeleteMenuItem, EditorCommandPaletteCatalog.DeleteIdentifier);
        SetHotKey(SearchCommandsMenuItem, EditorCommandPaletteCatalog.SearchIdentifier);
        SetHotKey(ZoomInMenuItem, EditorCommandPaletteCatalog.ZoomInIdentifier);
        SetHotKey(ZoomOutMenuItem, EditorCommandPaletteCatalog.ZoomOutIdentifier);
        SetHotKey(ZoomToFitMenuItem, EditorCommandPaletteCatalog.ZoomToFitIdentifier);
        SetHotKey(ActivityLogMenuItem, EditorCommandPaletteCatalog.ToggleActivityLogIdentifier);
        SetHotKey(LearnModeMenuItem, EditorCommandPaletteCatalog.ToggleLearnModeIdentifier);
        SetHotKey(StartScreenMenuItem, EditorCommandPaletteCatalog.StartScreenIdentifier);
        SetHotKey(DocumentationMenuItem, EditorCommandPaletteCatalog.DocumentationIdentifier);
        SetHotKey(PreferencesMenuItem, EditorCommandPaletteCatalog.PreferencesIdentifier);
    }

    private void SetHotKey(MenuItem menuItem, string identifier)
    {
        menuItem.HotKey = _appShortcuts.TryGetValue(identifier, out var canonical)
            && EditorShortcutGesture.TryCreateKeyGesture(canonical, out var gesture)
                ? gesture
                : null;
    }

    private void ApplyCommandShortcutOverrides(EditorPageViewModel viewModel)
        => viewModel.ApplyCommandShortcutOverrides(_appShortcuts.ToDictionary(
            pair => pair.Key,
            pair => (string?)EditorShortcutGesture.ToDisplayText(pair.Value),
            System.StringComparer.Ordinal));

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
