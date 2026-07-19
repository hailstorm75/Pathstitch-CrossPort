using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Domain.App.Services;
using Pathstitch.App.Dialogs;

namespace Pathstitch.App.Services;

public sealed class AvaloniaProjectOpenDispositionPromptService(
    IDocumentWindowContext? windowContext = null) : IProjectOpenDispositionPromptService
{
    public async Task<ProjectOpenDisposition> PromptAsync(
        string incomingProjectName,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ProjectOpenDisposition.Cancel;
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => PromptAsync(incomingProjectName, cancellationToken));

        var owner = windowContext?.Owner
            ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
            return ProjectOpenDisposition.Cancel;

        var dialog = new ProjectOpenDispositionDialog();
        dialog.SetProjectName(incomingProjectName);
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        var result = await dialog.ShowDialog<ProjectOpenDisposition?>(owner);
        return result ?? dialog.Result ?? ProjectOpenDisposition.Cancel;
    }
}
