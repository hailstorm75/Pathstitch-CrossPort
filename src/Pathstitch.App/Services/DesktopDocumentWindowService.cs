using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Services;

internal sealed class DesktopDocumentWindowCoordinator
{
    private Func<ProjectLaunchRequest, CancellationToken, Task>? _openDocument;
    private Action? _showStartScreen;

    public void Attach(
        Func<ProjectLaunchRequest, CancellationToken, Task> openDocument,
        Action showStartScreen)
    {
        _openDocument = openDocument ?? throw new ArgumentNullException(nameof(openDocument));
        _showStartScreen = showStartScreen ?? throw new ArgumentNullException(nameof(showStartScreen));
    }

    public void Detach()
    {
        _openDocument = null;
        _showStartScreen = null;
    }

    public void ShowStartScreen()
        => (_showStartScreen ?? throw new InvalidOperationException("Document window manager is unavailable."))();

    public Task OpenDocumentAsync(
        ProjectLaunchRequest launchRequest,
        CancellationToken cancellationToken = default)
        => _openDocument?.Invoke(launchRequest, cancellationToken)
           ?? Task.FromException(new InvalidOperationException("Document window manager is unavailable."));
}

internal sealed class DesktopDocumentWindowService(
    DesktopDocumentWindowCoordinator coordinator,
    IDocumentWindowContext windowContext) : IDocumentWindowService
{
    public Task OpenDocumentAsync(
        ProjectLaunchRequest launchRequest,
        CancellationToken cancellationToken = default)
        => coordinator.OpenDocumentAsync(launchRequest, cancellationToken);

    public Task CloseCurrentDocumentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (windowContext.Owner is null)
            return Task.CompletedTask;
        return Dispatcher.UIThread.InvokeAsync(windowContext.Owner.Close).GetTask();
    }
}
