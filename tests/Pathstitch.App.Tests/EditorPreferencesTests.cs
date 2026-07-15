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
}
