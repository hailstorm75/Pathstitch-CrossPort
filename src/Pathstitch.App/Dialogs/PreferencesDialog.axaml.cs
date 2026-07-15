using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Controls;
using Pathstitch.App.Services;

namespace Pathstitch.App.Dialogs;

public sealed partial class PreferencesDialog : Window
{
    private readonly EditorPageViewModel? _viewModel;
    private readonly EditorMode _mode;
    private readonly UserPreferencesStore _preferencesStore;
    private readonly List<(string Identifier, TextBox Shortcut)> _shortcutEditors = [];

    public PreferencesDialog()
    {
        _preferencesStore = new UserPreferencesStore();
        InitializeComponent();
        _mode = EditorMode.TwoD;
        InitializeAppearanceSelector();
    }

    public PreferencesDialog(EditorPageViewModel viewModel)
    {
        _preferencesStore = new UserPreferencesStore();
        InitializeComponent();
        _viewModel = viewModel;
        _mode = viewModel.ActiveEditorMode;
        InitializeAppearanceSelector();
        BuildShortcutEditors();
    }

    public PreferencesDialog(EditorPageViewModel? viewModel, UserPreferencesStore preferencesStore)
    {
        _preferencesStore = preferencesStore;
        InitializeComponent();
        _viewModel = viewModel;
        _mode = viewModel?.ActiveEditorMode ?? EditorMode.TwoD;
        InitializeAppearanceSelector();
        BuildShortcutEditors();
    }

    private void InitializeAppearanceSelector()
    {
        var themeKey = Application.Current?.RequestedThemeVariant?.Key;
        AppearanceSelector.SelectedIndex = Equals(themeKey, ThemeVariant.Light.Key)
            ? 1
            : Equals(themeKey, ThemeVariant.Dark.Key) ? 2 : 0;
        AppearanceSelector.SelectionChanged += OnAppearanceChanged;
        ReversePanDirection.IsChecked = DxfPreviewCanvas.ReversePanDirection;
        ReversePanDirection.IsCheckedChanged += OnReversePanDirectionChanged;
    }

    private void OnReversePanDirectionChanged(object? sender, RoutedEventArgs e)
    {
        DxfPreviewCanvas.ReversePanDirection = ReversePanDirection.IsChecked == true;
        SavePreferences();
    }

    private void OnAppearanceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Application.Current is null || AppearanceSelector.SelectedIndex < 0)
            return;

        Application.Current.RequestedThemeVariant = AppearanceSelector.SelectedIndex switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        SavePreferences();
    }

    private void SavePreferences()
    {
        var appearance = AppearanceSelector.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System",
        };
        _preferencesStore.Save(_preferencesStore.Load() with
        {
            Appearance = appearance,
            ReversePanDirection = DxfPreviewCanvas.ReversePanDirection,
        });
    }

    private void OnShowGettingStartedClicked(object? sender, RoutedEventArgs e)
    {
        _preferencesStore.Save(_preferencesStore.Load() with { GettingStartedDismissed = false });
        StatusText.Text = "Getting Started will show again on the Home page.";
    }

    private void OnShowModeIntrosClicked(object? sender, RoutedEventArgs e)
    {
        _preferencesStore.Save(_preferencesStore.Load() with
        {
            TwoDIntroDismissed = false,
            ThreeDIntroDismissed = false,
            BatchIntroDismissed = false,
        });
        StatusText.Text = "Mode intro cards will show again when each workspace opens.";
    }

    private void BuildShortcutEditors()
    {
        if (_viewModel is null)
            return;

        foreach (var customization in _viewModel.ToolCustomizations
                     .Where(item => FindDescriptor(item.Identifier) is not null)
                     .OrderBy(item => item.Order)
                     .ThenBy(item => item.Identifier, StringComparer.Ordinal))
        {
            var descriptor = FindDescriptor(customization.Identifier)!;
            var shortcut = new TextBox
            {
                Width = 90,
                Text = customization.ShortcutText ?? string.Empty,
                Tag = customization.Identifier,
            };
            AutomationProperties.SetAutomationId(shortcut, $"preferences.shortcut.{customization.Identifier}");
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock { Text = descriptor.Label, VerticalAlignment = VerticalAlignment.Center },
                    shortcut,
                },
            };
            Grid.SetColumn(shortcut, 1);
            ToolRows.Children.Add(row);
            _shortcutEditors.Add((customization.Identifier, shortcut));
        }
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            Close();
            return;
        }

        try
        {
            var current = _viewModel.ToolCustomizations.ToDictionary(item => item.Identifier, StringComparer.Ordinal);
            foreach (var (identifier, shortcut) in _shortcutEditors)
            {
                var customization = current[identifier];
                _viewModel.CustomizeTool(identifier, customization.Order, shortcut.Text);
            }
            Close(true);
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void OnResetClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ResetToolbarCustomizationForActiveMode();
        foreach (var (identifier, shortcut) in _shortcutEditors)
            shortcut.Text = FindDescriptor(identifier)?.ShortcutText ?? string.Empty;
        StatusText.Text = "Toolbar and shortcuts reset for active mode.";
    }

    private void OnResetAllClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ResetToolbarCustomizationForAllModes();
        foreach (var (identifier, shortcut) in _shortcutEditors)
            shortcut.Text = FindDescriptor(identifier)?.ShortcutText ?? string.Empty;
        StatusText.Text = "Toolbar and shortcuts reset for all editor modes.";
    }

    private EditorToolDescriptor? FindDescriptor(string identifier)
        => EditorToolCatalog.All.FirstOrDefault(descriptor =>
            descriptor.Mode == _mode
            && string.Equals(descriptor.Identifier, identifier, StringComparison.Ordinal));

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
