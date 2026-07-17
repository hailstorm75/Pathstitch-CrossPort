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
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pathstitch.App.Services;

internal sealed class DesktopDocumentWindowManager : IDisposable
{
    private readonly IServiceProvider _rootServices;
    private readonly IClassicDesktopStyleApplicationLifetime? _desktop;
    private readonly List<DocumentWindowHandle> _documents = [];
    private readonly ApplicationCloseCoordinator _applicationCloseCoordinator = new();
    private readonly ILogger<DesktopDocumentWindowManager> _logger;
    private DocumentWindowHandle? _welcome;
    private DocumentWindowHandle? _activeDocument;
    private bool _disposed;
    private bool _shutdownPreviewRunning;
    private bool _shutdownApproved;

    public DesktopDocumentWindowManager(
        IServiceProvider rootServices,
        IClassicDesktopStyleApplicationLifetime? desktop = null)
    {
        _rootServices = rootServices;
        _desktop = desktop;
        _logger = rootServices.GetService<ILogger<DesktopDocumentWindowManager>>()
            ?? NullLogger<DesktopDocumentWindowManager>.Instance;
        if (_desktop is not null)
            _desktop.ShutdownRequested += OnShutdownRequested;
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

    public void ShowStartScreen()
    {
        var window = CreateWelcomeWindow();
        window.Show();
        window.Activate();
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
        if (filePaths.Count == 1
            && Path.GetExtension(filePaths[0]).Equals(".stch", StringComparison.OrdinalIgnoreCase)
            && _activeDocument is not null)
        {
            var prompt = _activeDocument.Scope.ServiceProvider
                .GetService<IProjectOpenDispositionPromptService>();
            var disposition = prompt is null
                ? ProjectOpenDisposition.NewWindow
                : await prompt.PromptAsync(Path.GetFileName(filePaths[0]), cancellationToken).ConfigureAwait(true);
            if (disposition == ProjectOpenDisposition.Cancel)
                return;
            if (disposition == ProjectOpenDisposition.Combine)
            {
                if (_activeDocument.Window.CurrentPageViewModel is EditorPageViewModel editor)
                    await editor.CombineProjectAsync(filePaths[0], cancellationToken).ConfigureAwait(true);
                return;
            }
        }

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

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownApproved)
            return;

        e.Cancel = true;
        if (_shutdownPreviewRunning)
            return;

        _shutdownPreviewRunning = true;
        try
        {
            var targets = BuildApplicationCloseTargets();
            if (!await _applicationCloseCoordinator.TryApproveAsync(targets).ConfigureAwait(true))
                return;
            if (_disposed)
                return;

            _welcome?.Window.ApproveApplicationClose();
            _shutdownApproved = true;
            _desktop?.Shutdown();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Application shutdown preview failed");
        }
        finally
        {
            _shutdownPreviewRunning = false;
        }
    }

    private IReadOnlyList<ApplicationCloseTarget> BuildApplicationCloseTargets()
    {
        var ordered = _activeDocument is null
            ? _documents.ToArray()
            : _documents
                .Where(document => !ReferenceEquals(document, _activeDocument))
                .Prepend(_activeDocument)
                .ToArray();
        return ordered
            .Select(document => new ApplicationCloseTarget(
                document.Scope.ServiceProvider.GetRequiredService<IMessenger>(),
                document.Window.ApproveApplicationClose))
            .ToArray();
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
        if (_desktop is not null)
            _desktop.ShutdownRequested -= OnShutdownRequested;
        foreach (var document in _documents.ToArray())
            document.Scope.Dispose();
        _documents.Clear();
        _welcome?.Scope.Dispose();
        _welcome = null;
        _activeDocument = null;
    }

    private sealed record DocumentWindowHandle(IServiceScope Scope, MainWindowShell Window);
}
