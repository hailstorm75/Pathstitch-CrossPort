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

public sealed class AvaloniaPsdImportModePromptService : IPsdImportModePromptService
{
    public async Task<PsdImportMode?> PromptAsync(PsdImportData import, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => PromptAsync(import, cancellationToken));
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not Window owner)
            return null;
        var dialog = new PsdImportModeDialog();
        dialog.SetImport(import);
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        return await dialog.ShowDialog<PsdImportMode?>(owner);
    }
}
