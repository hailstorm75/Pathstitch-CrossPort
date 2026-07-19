using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Domain.App.Models;
using Domain.App.Services;
using Pathstitch.App.Dialogs;

namespace Pathstitch.App.Services;

public sealed class AvaloniaEditorImportUnitsPromptService(IDocumentWindowContext? windowContext = null) : IEditorImportUnitsPromptService
{
    public async Task<double?> PromptAsync(Editor2DImportUnitsInfo info, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => PromptAsync(info, cancellationToken));

        var owner = windowContext?.Owner
            ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
            return null;

        var dialog = new EditorImportUnitsDialog();
        dialog.SetImportInfo(info);
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        return await dialog.ShowDialog<double?>(owner);
    }
}
