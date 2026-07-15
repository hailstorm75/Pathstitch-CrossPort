using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.VisualTree;
using Domain.App.Models;
using Pathstitch.App.Dialogs;
using Pathstitch.App.Tests.Fixtures;

namespace Pathstitch.App.Tests;

public sealed class EditorPreferencesTests
{
    private readonly HeadlessUiFixture _ui = new();

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
