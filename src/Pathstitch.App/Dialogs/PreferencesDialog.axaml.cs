using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
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
    private readonly List<ShortcutEditor> _shortcutEditors = [];
    private string? _lastEditedShortcutIdentifier;
    private bool _suppressShortcutEditTracking;

    private sealed record ShortcutEditor(string Identifier, TextBox Shortcut, bool IsAppCommand);

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
        ConsolidateSvgStrokes.IsChecked = _preferencesStore.Load().ConsolidateSvgStrokes;
        var preferences = _preferencesStore.Load();
        SvgFillModeSelector.SelectedIndex = string.Equals(preferences.SvgFillMode, "preserve", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        SvgImportThickness.Text = preferences.SvgImportThickness.ToString("0.###", CultureInfo.InvariantCulture);
        ReversePanDirection.IsCheckedChanged += OnReversePanDirectionChanged;
        ConsolidateSvgStrokes.IsCheckedChanged += OnConsolidateSvgStrokesChanged;
        SvgFillModeSelector.SelectionChanged += OnSvgFillModeChanged;
        SvgImportThickness.TextChanged += OnSvgImportThicknessChanged;
    }

    private void OnReversePanDirectionChanged(object? sender, RoutedEventArgs e)
    {
        DxfPreviewCanvas.ReversePanDirection = ReversePanDirection.IsChecked == true;
        SavePreferences();
    }

    private void OnConsolidateSvgStrokesChanged(object? sender, RoutedEventArgs e)
    {
        SvgPreviewDocumentParser.ConsolidateStrokes = ConsolidateSvgStrokes.IsChecked == true;
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

    private void OnSvgFillModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        SvgPreviewDocumentParser.FillMode = SvgFillModeSelector.SelectedIndex == 1 ? "preserve" : "strokes";
        SavePreferences();
    }

    private void OnSvgImportThicknessChanged(object? sender, TextChangedEventArgs e)
    {
        if (!double.TryParse(SvgImportThickness.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness))
            return;

        SvgPreviewDocumentParser.ImportThickness = Math.Max(0.0, thickness);
        SavePreferences();
    }

    private void SavePreferences()
    {
        var importThickness = ParseImportThickness();
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
            ConsolidateSvgStrokes = ConsolidateSvgStrokes.IsChecked == true,
            SvgFillMode = SvgFillModeSelector.SelectedIndex == 1 ? "preserve" : "strokes",
            SvgImportThickness = importThickness,
        });
    }

    private double ParseImportThickness()
        => double.TryParse(SvgImportThickness.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness)
            ? Math.Max(0.0, thickness)
            : 3.0;

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

    private void OnReplayTutorialClicked(object? sender, RoutedEventArgs e)
    {
        _preferencesStore.Save(_preferencesStore.Load() with { TutorialCompleted = false });
        StatusText.Text = "The guided tutorial will show again when the editor opens.";
    }

    private void BuildShortcutEditors()
    {
        var resolvedAppShortcuts = EditorAppShortcutCatalog.Resolve(_preferencesStore.Load());
        foreach (var category in EditorAppShortcutCatalog.All
                     .GroupBy(definition => definition.Category, StringComparer.Ordinal))
        {
            AddSectionHeader(category.Key);
            foreach (var definition in category)
            {
                AddShortcutEditor(
                    definition.Identifier,
                    definition.Label,
                    EditorShortcutGesture.ToDisplayText(resolvedAppShortcuts[definition.Identifier]),
                    isAppCommand: true);
            }
        }

        if (_viewModel is null)
            return;

        AddSectionHeader($"{_mode} tools");

        foreach (var customization in _viewModel.ToolCustomizations
                     .Where(item => FindDescriptor(item.Identifier) is not null)
                     .OrderBy(item => item.Order)
                     .ThenBy(item => item.Identifier, StringComparer.Ordinal))
        {
            var descriptor = FindDescriptor(customization.Identifier)!;
            AddShortcutEditor(
                customization.Identifier,
                descriptor.Label,
                customization.ShortcutText ?? string.Empty,
                isAppCommand: false);
        }
    }

    private void AddSectionHeader(string title)
        => ToolRows.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = Avalonia.Media.Brushes.Gray,
            Margin = new Thickness(0, 10, 0, 2),
        });

    private void AddShortcutEditor(string identifier, string label, string value, bool isAppCommand)
    {
        var shortcut = new TextBox
        {
            Width = 130,
            Text = value,
            Tag = identifier,
            IsReadOnly = true,
            PlaceholderText = "Unassigned",
        };
        shortcut.KeyDown += OnShortcutEditorKeyDown;
        shortcut.TextChanged += OnShortcutEditorTextChanged;
        AutomationProperties.SetAutomationId(shortcut, $"preferences.shortcut.{identifier}");
        var clear = new Button
        {
            Content = "×",
            Tag = shortcut,
            Padding = new Thickness(7, 2),
        };
        clear.Click += OnClearShortcutClicked;
        AutomationProperties.SetAutomationId(clear, $"preferences.shortcut.{identifier}.clear");
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                shortcut,
                clear,
            },
        };
        Grid.SetColumn(shortcut, 1);
        Grid.SetColumn(clear, 2);
        ToolRows.Children.Add(row);
        _shortcutEditors.Add(new ShortcutEditor(identifier, shortcut, isAppCommand));
    }

    private void OnShortcutEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox shortcut || e.Key == Key.Escape)
            return;
        if (EditorShortcutGesture.TryCapture(e.Key, e.KeyModifiers, out var canonical))
            shortcut.Text = EditorShortcutGesture.ToDisplayText(canonical);
        e.Handled = true;
    }

    private void OnShortcutEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressShortcutEditTracking || sender is not TextBox { Tag: string identifier })
            return;
        _lastEditedShortcutIdentifier = identifier;
        ResolveConflictButton.IsVisible = false;
    }

    private static void OnClearShortcutClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TextBox shortcut })
            shortcut.Text = string.Empty;
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            SvgPreviewDocumentParser.ImportThickness = ParseImportThickness();
            SavePreferences();
            var appShortcuts = new Dictionary<string, string?>(StringComparer.Ordinal);
            var toolShortcutUpdates = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var editor in _shortcutEditors)
            {
                if (!EditorShortcutGesture.TryNormalize(editor.Shortcut.Text, out var normalized))
                    throw new InvalidOperationException($"'{editor.Shortcut.Text}' is not a valid shortcut.");
                if (editor.IsAppCommand)
                    appShortcuts[editor.Identifier] = normalized;
                else
                    toolShortcutUpdates[editor.Identifier] = EditorShortcutGesture.ToToolShortcutText(normalized);
            }

            var allToolShortcuts = _viewModel?.ToolCustomizations
                .Select(customization => toolShortcutUpdates.TryGetValue(customization.Identifier, out var shortcut)
                    ? customization with { ShortcutText = shortcut }
                    : customization)
                .ToArray() ?? [];
            var conflict = EditorShortcutGesture.FindConflict(
                appShortcuts,
                allToolShortcuts,
                identifier => EditorToolCatalog.All.FirstOrDefault(descriptor =>
                    string.Equals(descriptor.Identifier, identifier, StringComparison.Ordinal))?.Mode);
            if (conflict is not null)
            {
                StatusText.Text = $"{conflict} Choose Reassign to replace the previous binding.";
                ResolveConflictButton.IsVisible = _lastEditedShortcutIdentifier is not null;
                return;
            }

            ResolveConflictButton.IsVisible = false;
            _viewModel?.CustomizeToolShortcuts(toolShortcutUpdates);
            _viewModel?.ApplyCommandShortcutOverrides(appShortcuts.ToDictionary(
                    pair => pair.Key,
                    pair => (string?)EditorShortcutGesture.ToDisplayText(pair.Value),
                    StringComparer.Ordinal));
            var persistedShortcuts = appShortcuts.ToDictionary(
                pair => pair.Key,
                pair => pair.Value ?? string.Empty,
                StringComparer.Ordinal);
            _preferencesStore.Save(_preferencesStore.Load() with { AppCommandShortcuts = persistedShortcuts });
            if (_viewModel is null)
                Close();
            else
                Close(true);
        }
        catch (InvalidOperationException exception)
        {
            ResolveConflictButton.IsVisible = false;
            StatusText.Text = exception.Message;
        }
    }

    private void OnResolveConflictClicked(object? sender, RoutedEventArgs e)
    {
        var winner = _shortcutEditors.FirstOrDefault(editor =>
            string.Equals(editor.Identifier, _lastEditedShortcutIdentifier, StringComparison.Ordinal));
        if (winner is null
            || !EditorShortcutGesture.TryNormalize(winner.Shortcut.Text, out var winnerGesture)
            || winnerGesture is null)
            return;

        _suppressShortcutEditTracking = true;
        try
        {
            foreach (var editor in _shortcutEditors)
            {
                if (ReferenceEquals(editor, winner)
                    || !EditorShortcutGesture.TryNormalize(editor.Shortcut.Text, out var otherGesture)
                    || !string.Equals(winnerGesture, otherGesture, StringComparison.Ordinal)
                    || !ShortcutScopesOverlap(winner, editor))
                    continue;
                editor.Shortcut.Text = string.Empty;
            }
        }
        finally
        {
            _suppressShortcutEditTracking = false;
        }
        ResolveConflictButton.IsVisible = false;
        OnApplyClicked(sender, e);
    }

    private bool ShortcutScopesOverlap(ShortcutEditor first, ShortcutEditor second)
    {
        if (first.IsAppCommand || second.IsAppCommand)
            return true;
        return FindDescriptor(first.Identifier)?.Mode == FindDescriptor(second.Identifier)?.Mode;
    }

    private void OnResetClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ResetToolbarCustomizationForActiveMode();
        foreach (var editor in _shortcutEditors.Where(editor => !editor.IsAppCommand))
            editor.Shortcut.Text = FindDescriptor(editor.Identifier)?.ShortcutText ?? string.Empty;
        StatusText.Text = "Toolbar and shortcuts reset for active mode.";
    }

    private void OnResetAllClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ResetToolbarCustomizationForAllModes();
        foreach (var editor in _shortcutEditors)
        {
            editor.Shortcut.Text = editor.IsAppCommand
                ? EditorShortcutGesture.ToDisplayText(EditorAppShortcutCatalog.Find(editor.Identifier)?.DefaultGesture)
                : FindDescriptor(editor.Identifier)?.ShortcutText ?? string.Empty;
        }
        _preferencesStore.Save(_preferencesStore.Load() with { AppCommandShortcuts = null });
        if (_viewModel is not null)
        {
            _viewModel.ApplyCommandShortcutOverrides(EditorAppShortcutCatalog.All.ToDictionary(
                definition => definition.Identifier,
                definition => (string?)EditorShortcutGesture.ToDisplayText(definition.DefaultGesture),
                StringComparer.Ordinal));
        }
        StatusText.Text = "Toolbar and shortcuts reset for all editor modes.";
    }

    private EditorToolDescriptor? FindDescriptor(string identifier)
        => EditorToolCatalog.All.FirstOrDefault(descriptor =>
            descriptor.Mode == _mode
            && string.Equals(descriptor.Identifier, identifier, StringComparison.Ordinal));

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
