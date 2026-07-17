using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Pathstitch.App.Services;

internal sealed class DesktopDocumentWindowManager : IDisposable
{
    private readonly IServiceProvider _rootServices;
    private readonly IClassicDesktopStyleApplicationLifetime? _desktop;
    private readonly List<DocumentWindowHandle> _documents = [];
    private DocumentWindowHandle? _welcome;
    private DocumentWindowHandle? _activeDocument;
    private bool _disposed;

    public DesktopDocumentWindowManager(
        IServiceProvider rootServices,
        IClassicDesktopStyleApplicationLifetime? desktop = null)
    {
        _rootServices = rootServices;
        _desktop = desktop;
    }

    public MainWindowShell? ActiveWindow => _activeDocument?.Window ?? _welcome?.Window;

    public IServiceProvider? WelcomeServices => _welcome?.Scope.ServiceProvider;

    public int DocumentCount => _documents.Count;

    internal IReadOnlyList<MainWindowShell> DocumentWindows
        => _documents.Select(document => document.Window).ToArray();

    public MainWindowShell CreateWelcomeWindow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_welcome is not null)
            return _welcome.Window;

        var scope = _rootServices.CreateScope();
        var window = new MainWindowShell(scope.ServiceProvider);
        var handle = new DocumentWindowHandle(scope, window);
        _welcome = handle;
        window.Closed += (_, _) => OnWelcomeClosed(handle);
        scope.ServiceProvider.GetRequiredService<IMessenger>()
            .Send(new NavigationChangeRequestMessage(NavigationAddressBook.HomePage));
        return window;
    }

    public Task OpenFilesAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
        => RunOnUiThreadAsync(async () =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalizedPaths = filePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var projectPaths = normalizedPaths
                .Where(path => Path.GetExtension(path).Equals(".stch", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (projectPaths.Length <= 1)
            {
                await OpenFileBatchAsync(normalizedPaths, cancellationToken).ConfigureAwait(true);
                return;
            }

            foreach (var projectPath in projectPaths)
                await OpenFileBatchAsync([projectPath], cancellationToken).ConfigureAwait(true);
            var importPaths = normalizedPaths.Except(projectPaths, StringComparer.OrdinalIgnoreCase).ToArray();
            if (importPaths.Length > 0)
                await OpenFileBatchAsync(importPaths, cancellationToken).ConfigureAwait(true);
        });

    private async Task OpenFileBatchAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        var scope = _rootServices.CreateScope();
        var transferred = false;
        try
        {
            var sessionService = scope.ServiceProvider.GetRequiredService<ProjectSessionService>();
            var launchRequest = await sessionService
                .OpenWorkspaceFilesAsync(filePaths, cancellationToken)
                .ConfigureAwait(true);
            if (launchRequest is null)
                return;

            await OpenDocumentInScopeAsync(scope, launchRequest, cancellationToken).ConfigureAwait(true);
            transferred = true;
        }
        finally
        {
            if (!transferred)
                scope.Dispose();
        }
    }

    public Task OpenDocumentAsync(
        ProjectLaunchRequest launchRequest,
        CancellationToken cancellationToken = default)
        => RunOnUiThreadAsync(async () =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var scope = _rootServices.CreateScope();
            var transferred = false;
            try
            {
                scope.ServiceProvider.GetRequiredService<ProjectSessionService>()
                    .ActivateSession(launchRequest.Session);
                await OpenDocumentInScopeAsync(scope, launchRequest, cancellationToken).ConfigureAwait(true);
                transferred = true;
            }
            finally
            {
                if (!transferred)
                    scope.Dispose();
            }
        });

    private async Task OpenDocumentInScopeAsync(
        IServiceScope scope,
        ProjectLaunchRequest launchRequest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new MainWindowShell(scope.ServiceProvider);
        var handle = new DocumentWindowHandle(scope, window);
        _documents.Add(handle);
        _activeDocument = handle;
        window.Activated += (_, _) => _activeDocument = handle;
        window.Closed += (_, _) => OnDocumentClosed(handle);

        scope.ServiceProvider.GetRequiredService<IMessenger>()
            .Send(CreateEditorNavigationRequest(launchRequest));
        window.Show();
        _welcome?.Window.Hide();
        await window.WhenNavigationIdleAsync().ConfigureAwait(true);
    }

    private void OnDocumentClosed(DocumentWindowHandle handle)
    {
        if (!_documents.Remove(handle))
            return;

        handle.Scope.Dispose();
        _activeDocument = _documents.LastOrDefault();
        if (_documents.Count == 0 && _welcome is not null)
        {
            _welcome.Window.Show();
            _welcome.Window.Activate();
        }
    }

    private void OnWelcomeClosed(DocumentWindowHandle handle)
    {
        if (!ReferenceEquals(_welcome, handle))
            return;
        _welcome = null;
        handle.Scope.Dispose();
        if (_documents.Count == 0)
            _desktop?.Shutdown();
    }

    private static NavigationChangeRequestMessage CreateEditorNavigationRequest(ProjectLaunchRequest launchRequest)
    {
        var parameters = new Dictionary<string, object>
        {
            [EditorNavigationParameterKeys.ProjectSession] = launchRequest.Session,
        };
        if (launchRequest.PendingSourceModelPaths.Count > 0)
            parameters[EditorNavigationParameterKeys.PendingSourceModelPaths] = launchRequest.PendingSourceModelPaths;
        if (launchRequest.PendingTwoDFilePaths.Count > 0)
            parameters[EditorNavigationParameterKeys.PendingTwoDFilePaths] = launchRequest.PendingTwoDFilePaths;
        if (launchRequest.PendingReferenceImagePaths.Count > 0)
            parameters[EditorNavigationParameterKeys.PendingReferenceImagePaths] = launchRequest.PendingReferenceImagePaths;
        return new NavigationChangeRequestMessage(NavigationAddressBook.EditorPage, parameters);
    }

    private static async Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action().ConfigureAwait(true);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await action().ConfigureAwait(true);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        await completion.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var document in _documents.ToArray())
            document.Scope.Dispose();
        _documents.Clear();
        _welcome?.Scope.Dispose();
        _welcome = null;
        _activeDocument = null;
    }

    private sealed record DocumentWindowHandle(IServiceScope Scope, MainWindowShell Window);
}
