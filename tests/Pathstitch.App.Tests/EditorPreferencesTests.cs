using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.VisualTree;
using Domain.App.Models;
using Pathstitch.App.Dialogs;
using Pathstitch.App.Controls;
using Pathstitch.App.Tests.Fixtures;

namespace Pathstitch.App.Tests;

public sealed class EditorPreferencesTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public async Task PreferencesDialog_TogglesReversePanDirection()
    {
        DxfPreviewCanvas.ReversePanDirection = false;
        var dialog = await _ui.RunAsync(() => new PreferencesDialog());
        await _ui.RunAsync(() =>
        {
            dialog.Show();
            dialog.UpdateLayout();
            var toggle = _ui.FindByAutomationId<CheckBox>(dialog, "dialog.preferences.reverse-pan");
            toggle.IsChecked = true;
        });

        Assert.True(DxfPreviewCanvas.ReversePanDirection);
        await _ui.RunAsync(dialog.Close);
        DxfPreviewCanvas.ReversePanDirection = false;
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
