using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private readonly SemaphoreSlim _unsavedChangesPromptGate = new(1, 1);
    private long _documentRevision;
    private long _savedDocumentRevision;
    private int _documentDirtyTrackingSuppressionCount;
    private bool _isDirty;
    private bool _isSaving;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!SetProperty(ref _isDirty, value))
                return;

            SaveDocumentCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (!SetProperty(ref _isSaving, value))
                return;

            SaveDocumentCommand.NotifyCanExecuteChanged();
            SaveAndCloseDocumentCommand.NotifyCanExecuteChanged();
            CloseDocumentCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSaveDocument() => ProjectSession is not null && IsDirty && !IsSaving;

    private bool CanSaveAndCloseDocument() => ProjectSession is not null && !IsSaving;

    private bool CanCloseDocument() => ProjectSession is not null && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSaveDocument))]
    public async Task SaveDocumentAsync()
        => await SaveDocumentCoreAsync(CancellationToken.None).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanSaveAndCloseDocument))]
    public async Task SaveAndCloseDocumentAsync()
    {
        if (!await SaveDocumentCoreAsync(CancellationToken.None).ConfigureAwait(true))
            return;

        NavigateHome();
    }

    [RelayCommand(CanExecute = nameof(CanCloseDocument))]
    public Task CloseDocumentAsync()
    {
        NavigateHome();
        return Task.CompletedTask;
    }

    private async Task<bool> SaveDocumentCoreAsync(CancellationToken cancellationToken)
    {
        if (ProjectSession is null)
            return false;

        if (!IsDirty)
            return true;

        var stateBeingSaved = CaptureProjectState();
        var revisionBeingSaved = _documentRevision;
        IsSaving = true;
        ErrorMessage = null;
        StatusText = "Saving project";

        try
        {
            await PersistDocumentAsync(stateBeingSaved, cancellationToken).ConfigureAwait(true);
            _savedDocumentRevision = revisionBeingSaved;
            RefreshDocumentDirtyState();
            StatusText = IsDirty ? "Project saved; newer changes remain" : "Project saved";
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Project save cancelled";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save project {ProjectPath}", ProjectSession.ProjectFilePath);
            StatusText = "Project save failed";
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task<bool> ConfirmCanLeaveDocumentAsync(CancellationToken cancellationToken)
    {
        if (!IsDirty)
            return true;

        await _unsavedChangesPromptGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (!IsDirty)
                return true;

            var result = await _unsavedChangesPromptService
                .PromptToSaveAsync(ProjectTitle, cancellationToken)
                .ConfigureAwait(true);

            switch (result)
            {
                case UnsavedChangesPromptResult.Save:
                    return await SaveDocumentCoreAsync(cancellationToken).ConfigureAwait(true);
                case UnsavedChangesPromptResult.Discard:
                    return true;
                case UnsavedChangesPromptResult.Cancel:
                default:
                    return false;
            }
        }
        finally
        {
            _unsavedChangesPromptGate.Release();
        }
    }

    protected override void BeforePageLeave(object recipient, BeforeNavigationChangeMessage message)
    {
        if (message.HasReceivedResponse)
            return;

        var cancellationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        message.Reply(cancellationSource);
        _ = ResolveNavigationPreviewAsync(cancellationSource);
    }

    private async Task ResolveNavigationPreviewAsync(TaskCompletionSource<bool> cancellationSource)
    {
        try
        {
            var canLeave = await ConfirmCanLeaveDocumentAsync(CancellationToken.None).ConfigureAwait(true);
            cancellationSource.TrySetResult(!canLeave);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview document navigation");
            cancellationSource.TrySetResult(true);
        }
    }

    private void OnPreviewApplicationClosing(object recipient, PreviewApplicationClosingMessage message)
    {
        if (message.HasReceivedResponse)
            return;

        var cancellationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        message.Reply(cancellationSource);
        _ = ResolveApplicationClosingPreviewAsync(cancellationSource);
    }

    private async Task ResolveApplicationClosingPreviewAsync(TaskCompletionSource<bool> cancellationSource)
    {
        try
        {
            var canClose = await ConfirmCanLeaveDocumentAsync(CancellationToken.None).ConfigureAwait(true);
            cancellationSource.TrySetResult(!canClose);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview application shutdown");
            cancellationSource.TrySetResult(true);
        }
    }

    private void NavigateHome()
        => WeakReferenceMessenger.Default.Send(
            new NavigationChangeRequestMessage(NavigationAddressBook.HomePage));

    private void MarkDocumentDirty()
    {
        if (_documentDirtyTrackingSuppressionCount > 0)
            return;

        _documentRevision++;
        RefreshDocumentDirtyState();
    }

    private void EstablishCleanDocumentBaseline()
    {
        _savedDocumentRevision = _documentRevision;
        RefreshDocumentDirtyState();
    }

    private void RefreshDocumentDirtyState()
        => IsDirty = _documentRevision != _savedDocumentRevision;

    private IDisposable SuppressDocumentDirtyTracking()
    {
        _documentDirtyTrackingSuppressionCount++;
        return new DirtyTrackingSuppression(this);
    }

    private sealed class DirtyTrackingSuppression(EditorPageViewModel owner) : IDisposable
    {
        private EditorPageViewModel? _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is { } currentOwner)
                currentOwner._documentDirtyTrackingSuppressionCount--;
        }
    }
}
