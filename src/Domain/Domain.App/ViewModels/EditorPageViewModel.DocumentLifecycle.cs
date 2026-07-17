using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Models;
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
    private NavigationChangeRequestMessage? _preapprovedNavigationRequest;

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
            SaveDocumentAsCommand.NotifyCanExecuteChanged();
            SaveAndCloseDocumentCommand.NotifyCanExecuteChanged();
            CloseDocumentCommand.NotifyCanExecuteChanged();
            NewProjectCommand.NotifyCanExecuteChanged();
            OpenProjectCommand.NotifyCanExecuteChanged();
            ImportFilesCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSaveDocument() => ProjectSession is not null && IsDirty && !IsSaving;

    private bool CanSaveDocumentAs() => ProjectSession is not null && !IsSaving;

    private bool CanSaveAndCloseDocument() => ProjectSession is not null && !IsSaving;

    private bool CanCloseDocument() => ProjectSession is not null && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSaveDocument))]
    public async Task SaveDocumentAsync()
        => await SaveDocumentCoreAsync(CancellationToken.None).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanSaveDocumentAs))]
    public async Task SaveDocumentAsAsync()
    {
        var currentSession = ProjectSession;
        if (currentSession is null)
            return;

        var selectedPath = await _projectFileDialogService
            .PickProjectSaveAsFileAsync($"{ProjectName}.stch", CancellationToken.None)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        var targetPath = Path.GetFullPath(selectedPath);
        if (string.IsNullOrEmpty(Path.GetExtension(targetPath)))
            targetPath += ".stch";
        if (!Path.GetExtension(targetPath).Equals(".stch", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Save As requires a .stch project file.";
            return;
        }

        var revisionBeingSaved = _documentRevision;
        IsSaving = true;
        ErrorMessage = null;
        StatusText = "Saving project as";
        try
        {
            var stateBeingSaved = await CaptureProjectStateAsync(CancellationToken.None).ConfigureAwait(true);
            await _project3DStateService.SaveAsAsync(
                currentSession.ProjectFilePath,
                targetPath,
                stateBeingSaved,
                CancellationToken.None).ConfigureAwait(true);
            var replacementName = Path.GetFileNameWithoutExtension(targetPath);
            var replacement = _projectSessionService?.ReplaceSessionPath(
                currentSession,
                targetPath,
                replacementName,
                trackInRecentProjects: true)
                ?? currentSession with
                {
                    ProjectName = replacementName,
                    ProjectFilePath = targetPath,
                    TrackInRecentProjects = true,
                };
            AdoptSaveAsSession(replacement);
            _savedDocumentRevision = revisionBeingSaved;
            RefreshDocumentDirtyState();
            StatusText = IsDirty ? "Project saved as; newer changes remain" : "Project saved as";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save project as {ProjectPath}", targetPath);
            StatusText = "Project Save As failed";
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void AdoptSaveAsSession(ProjectSession replacement)
    {
        if (!SetProperty(ref _projectSession, replacement, nameof(ProjectSession)))
            return;
        using var dirtyTrackingSuppression = SuppressDocumentDirtyTracking();
        ProjectName = replacement.ProjectName;
        ProjectTitle = replacement.ProjectName;
        ProjectSubtitle = replacement.ProjectFilePath;
        Template = replacement.Template;
        SessionOrigin = replacement.Origin;
        SaveDocumentCommand.NotifyCanExecuteChanged();
        SaveDocumentAsCommand.NotifyCanExecuteChanged();
        SaveAndCloseDocumentCommand.NotifyCanExecuteChanged();
        CloseDocumentCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveAndCloseDocument))]
    public async Task SaveAndCloseDocumentAsync()
    {
        if (!await SaveDocumentCoreAsync(CancellationToken.None).ConfigureAwait(true))
            return;

        if (_documentWindowService is not null)
            await _documentWindowService.CloseCurrentDocumentAsync().ConfigureAwait(true);
        else
            NavigateHome();
    }

    [RelayCommand(CanExecute = nameof(CanCloseDocument))]
    public async Task CloseDocumentAsync()
    {
        if (_documentWindowService is not null)
            await _documentWindowService.CloseCurrentDocumentAsync().ConfigureAwait(true);
        else
            NavigateHome();
    }

    private async Task<bool> SaveDocumentCoreAsync(CancellationToken cancellationToken)
    {
        if (ProjectSession is null)
            return false;

        if (!IsDirty)
            return true;

        var revisionBeingSaved = _documentRevision;
        IsSaving = true;
        ErrorMessage = null;
        StatusText = "Saving project";

        try
        {
            var stateBeingSaved = await CaptureProjectStateAsync(cancellationToken).ConfigureAwait(true);
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
                    return await SaveDocumentCoreAsync(cancellationToken).ConfigureAwait(true) && !IsDirty;
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
        if (ReferenceEquals(message.Request, _preapprovedNavigationRequest))
        {
            _preapprovedNavigationRequest = null;
            if (!message.HasReceivedResponse)
            {
                var approved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                approved.SetResult(false);
                message.Reply(approved);
            }

            return;
        }

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
        => Messenger.Send(
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
