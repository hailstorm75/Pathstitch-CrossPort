using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.VisualTree;
using Domain.App.Models;
using Pathstitch.App.Dialogs;
using Pathstitch.App.Controls;
using Pathstitch.App.Services;
using Pathstitch.App.Tests.Fixtures;

namespace Pathstitch.App.Tests;

public sealed class EditorPreferencesTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public async Task PreferencesDialog_TogglesReversePanDirection()
    {
        DxfPreviewCanvas.ReversePanDirection = false;
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ui-preferences-{Guid.NewGuid():N}.json");
        var dialog = await _ui.RunAsync(() => new PreferencesDialog(null, new UserPreferencesStore(path)));
        await _ui.RunAsync(() =>
        {
            dialog.Show();
            dialog.UpdateLayout();
            var toggle = _ui.FindByAutomationId<CheckBox>(dialog, "dialog.preferences.reverse-pan");
            toggle.IsChecked = true;
            var appearance = _ui.FindByAutomationId<ComboBox>(dialog, "dialog.preferences.appearance");
            appearance.SelectedIndex = 2;
        });

        Assert.True(DxfPreviewCanvas.ReversePanDirection);
        var persisted = new UserPreferencesStore(path).Load();
        Assert.Equal("Dark", persisted.Appearance);
        Assert.True(persisted.ReversePanDirection);
        await _ui.RunAsync(() =>
            _ui.FindByAutomationId<ComboBox>(dialog, "dialog.preferences.appearance").SelectedIndex = 0);
        await _ui.RunAsync(dialog.Close);
        DxfPreviewCanvas.ReversePanDirection = false;
        File.Delete(path);
    }

    [Fact]
    public async Task PreferencesDialog_TogglesSvgStrokeConsolidation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ui-svg-preferences-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            store.Save(new UserPreferences(ConsolidateSvgStrokes: false));
            var dialog = await _ui.RunAsync(() => new PreferencesDialog(null, store));
            await _ui.RunAsync(() =>
            {
                dialog.Show();
                dialog.UpdateLayout();
                var toggle = _ui.FindByAutomationId<CheckBox>(dialog, "dialog.preferences.consolidate-svg-strokes");
                toggle.IsChecked = true;
            });

            Assert.True(store.Load().ConsolidateSvgStrokes);
            await _ui.RunAsync(dialog.Close);
        }
        finally
        {
            SvgPreviewDocumentParser.ImportThickness = 0;
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PreferencesDialog_SelectsSvgFillMode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ui-svg-fill-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            var dialog = await _ui.RunAsync(() => new PreferencesDialog(null, store));
            await _ui.RunAsync(() =>
            {
                dialog.Show();
                dialog.UpdateLayout();
                _ui.FindByAutomationId<ComboBox>(dialog, "dialog.preferences.svg-fill-mode").SelectedIndex = 1;
            });

            Assert.Equal("preserve", store.Load().SvgFillMode);
            await _ui.RunAsync(dialog.Close);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PreferencesDialog_UpdatesSvgImportThickness()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ui-svg-thickness-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            var dialog = await _ui.RunAsync(() => new PreferencesDialog(null, store));
            await _ui.RunAsync(() =>
            {
                dialog.Show();
                dialog.UpdateLayout();
                _ui.FindByAutomationId<TextBox>(dialog, "dialog.preferences.svg-import-thickness").Text = "5.5";
                _ui.FindByAutomationId<Button>(dialog, "dialog.preferences.apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });

            Assert.Equal(5.5, store.Load().SvgImportThickness, 6);
            await _ui.RunAsync(dialog.Close);
        }
        finally
        {
            SvgPreviewDocumentParser.ImportThickness = 0;
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PreferencesDialog_CanRestoreGettingStartedCard()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ui-preferences-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            store.Save(new UserPreferences(GettingStartedDismissed: true));
            var dialog = await _ui.RunAsync(() => new PreferencesDialog(null, store));
            await _ui.RunAsync(() =>
            {
                dialog.Show();
                dialog.UpdateLayout();
                _ui.FindByAutomationId<Button>(dialog, "dialog.preferences.show-getting-started")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });

            Assert.False(store.Load().GettingStartedDismissed);
            await _ui.RunAsync(dialog.Close);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PreferencesDialog_CanRestoreModeIntroCards()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ui-intros-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            store.Save(new UserPreferences(TwoDIntroDismissed: true, ThreeDIntroDismissed: true, BatchIntroDismissed: true));
            var dialog = await _ui.RunAsync(() => new PreferencesDialog(null, store));
            await _ui.RunAsync(() =>
            {
                dialog.Show();
                dialog.UpdateLayout();
                _ui.FindByAutomationId<Button>(dialog, "dialog.preferences.show-mode-intros")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });

            var restored = store.Load();
            Assert.False(restored.TwoDIntroDismissed);
            Assert.False(restored.ThreeDIntroDismissed);
            Assert.False(restored.BatchIntroDismissed);
            await _ui.RunAsync(dialog.Close);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PreferencesDialog_CanReplayGuidedTutorial()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ui-tutorial-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            store.Save(new UserPreferences(TutorialCompleted: true));
            var dialog = await _ui.RunAsync(() => new PreferencesDialog(null, store));
            await _ui.RunAsync(() =>
            {
                dialog.Show();
                dialog.UpdateLayout();
                _ui.FindByAutomationId<Button>(dialog, "dialog.preferences.replay-tutorial")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });

            Assert.False(store.Load().TutorialCompleted);
            await _ui.RunAsync(dialog.Close);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PreferencesDialog_AppliesShortcutAndResetRestoresActiveModeDefaults()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        var dialog = await _ui.RunAsync(() => new PreferencesDialog(viewModel));
        await _ui.RunAsync(() =>
        {
            dialog.Show();
            dialog.UpdateLayout();
        });

        await _ui.RunAsync(() =>
        {
            var shortcut = dialog.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "preferences.shortcut.2d.circle");
            shortcut.Text = "G";
            _ui.FindByAutomationId<Button>(dialog, "dialog.preferences.apply")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

        Assert.Equal("G", viewModel.ToolCustomizations.Single(item => item.Identifier == "2d.circle").ShortcutText);
        await _ui.RunAsync(dialog.Close);

        var resetDialog = await _ui.RunAsync(() => new PreferencesDialog(viewModel));
        await _ui.RunAsync(() =>
        {
            resetDialog.Show();
            resetDialog.UpdateLayout();
        });
        await _ui.RunAsync(() =>
            _ui.FindByAutomationId<Button>(resetDialog, "dialog.preferences.reset")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

        Assert.Equal("C", viewModel.ToolCustomizations.Single(item => item.Identifier == "2d.circle").ShortcutText);
        await _ui.RunAsync(resetDialog.Close);
    }

    [Fact]
    public async Task PreferencesDialog_ResetAllRestoresShortcutsAcrossModes()
    {
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.CustomizeTool("2d.circle", 99, "G");
        viewModel.CustomizeTool("3d.move", 99, "Z");

        var dialog = await _ui.RunAsync(() => new PreferencesDialog(viewModel));
        await _ui.RunAsync(() =>
        {
            dialog.Show();
            dialog.UpdateLayout();
            _ui.FindByAutomationId<Button>(dialog, "dialog.preferences.reset-all")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

        Assert.Equal("C", viewModel.ToolCustomizations.Single(item => item.Identifier == "2d.circle").ShortcutText);
        Assert.Equal("2", viewModel.ToolCustomizations.Single(item => item.Identifier == "3d.move").ShortcutText);
        await _ui.RunAsync(dialog.Close);
    }

    [Fact]
    public async Task PreferencesDialog_PersistsAppCommandShortcutAndUpdatesPalette()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ui-app-shortcuts-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
            await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
            var dialog = await _ui.RunAsync(() => new PreferencesDialog(viewModel, store));
            await _ui.RunAsync(() =>
            {
                dialog.Show();
                dialog.UpdateLayout();
                _ui.FindByAutomationId<TextBox>(dialog, "preferences.shortcut.file.new").Text = "Ctrl+Shift+N";
                _ui.FindByAutomationId<Button>(dialog, "dialog.preferences.apply")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });

            Assert.Equal("Primary+Shift+N", store.Load().AppCommandShortcuts!["file.new"]);
            viewModel.CommandSearchQuery = "New Project";
            Assert.Equal(
                "Ctrl+Shift+N",
                viewModel.CommandSearchResults.Single(item => item.Identifier == "file.new").ShortcutText);
            await _ui.RunAsync(dialog.Close);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PreferencesDialog_RejectsThenReassignsAppShortcutThatConflictsWithTool()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-ui-shortcut-conflict-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserPreferencesStore(path);
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
            await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
            var dialog = await _ui.RunAsync(() => new PreferencesDialog(viewModel, store));
            await _ui.RunAsync(() =>
            {
                dialog.Show();
                dialog.UpdateLayout();
                _ui.FindByAutomationId<TextBox>(dialog, "preferences.shortcut.file.new").Text = "C";
                _ui.FindByAutomationId<Button>(dialog, "dialog.preferences.apply")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });

            var status = await _ui.RunAsync(() => dialog.FindControl<TextBlock>("StatusText")!.Text);
            Assert.Contains("conflicts", status, StringComparison.OrdinalIgnoreCase);
            Assert.Null(store.Load().AppCommandShortcuts);
            Assert.Equal("C", viewModel.ToolCustomizations.Single(item => item.Identifier == "2d.circle").ShortcutText);
            await _ui.RunAsync(() =>
                _ui.FindByAutomationId<Button>(dialog, "dialog.preferences.shortcut-reassign")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            Assert.Equal("C", NormalizeStored(store.Load().AppCommandShortcuts!["file.new"]));
            Assert.Null(viewModel.ToolCustomizations.Single(item => item.Identifier == "2d.circle").ShortcutText);
            await _ui.RunAsync(dialog.Close);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string? NormalizeStored(string value)
    {
        Assert.True(EditorShortcutGesture.TryNormalize(value, out var normalized));
        return normalized;
    }
}
