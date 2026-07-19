using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Domain.App.Services;
using Pathstitch.App.Dialogs;

namespace Pathstitch.App.Services;

public sealed class AvaloniaUnsavedChangesPromptService(IDocumentWindowContext? windowContext = null) : IUnsavedChangesPromptService
{
    public async Task<UnsavedChangesPromptResult> PromptToSaveAsync(
        string documentName,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return UnsavedChangesPromptResult.Cancel;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => PromptToSaveAsync(documentName, cancellationToken));
        }

        var owner = windowContext?.Owner
            ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
        {
            return UnsavedChangesPromptResult.Cancel;
        }

        var dialog = new UnsavedChangesDialog();
        dialog.SetDocumentName(documentName);

        using var registration = cancellationToken.Register(
            () => Dispatcher.UIThread.Post(dialog.Close));

        var result = await dialog.ShowDialog<UnsavedChangesPromptResult?>(owner);
        return result ?? dialog.Result ?? UnsavedChangesPromptResult.Cancel;
    }
}
