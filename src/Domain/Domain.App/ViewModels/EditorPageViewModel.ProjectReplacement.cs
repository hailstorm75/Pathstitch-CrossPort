using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private readonly SemaphoreSlim _projectReplacementGate = new(1, 1);
    private bool _isReplacingProject;

    public bool IsReplacingProject
    {
        get => _isReplacingProject;
        private set
        {
            if (!SetProperty(ref _isReplacingProject, value))
                return;

            NewProjectCommand.NotifyCanExecuteChanged();
            OpenProjectCommand.NotifyCanExecuteChanged();
            ImportFilesCommand.NotifyCanExecuteChanged();
            NotifyCommandPaletteStateChanged();
        }
    }

    private bool CanReplaceProject()
        => ProjectSession is not null
           && _projectSessionService is not null
           && !IsSaving
           && !IsReplacingProject;

    [RelayCommand(CanExecute = nameof(CanReplaceProject))]
    public Task NewProjectAsync(CancellationToken cancellationToken)
        => ReplaceProjectAsync(
            service => service.PrepareCreateTemplateProjectAsync(cancellationToken: cancellationToken),
            "Creating project",
            prepareBeforeConfirmation: false,
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanReplaceProject))]
    public Task OpenProjectAsync(CancellationToken cancellationToken)
        => OpenProjectWithDispositionAsync(cancellationToken);

    private async Task OpenProjectWithDispositionAsync(CancellationToken cancellationToken)
    {
        var service = _projectSessionService;
        if (service is null || !await _projectReplacementGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
            return;

        IsReplacingProject = true;
        ErrorMessage = null;
        try
        {
            StatusText = "Opening project";
            var session = await service.PrepareOpenTemplateProjectAsync(cancellationToken).ConfigureAwait(true);
            if (session is null)
                return;

            if (_documentWindowService is not null)
            {
                var disposition = await _projectOpenDispositionPromptService
                    .PromptAsync(Path.GetFileName(session.ProjectFilePath), cancellationToken)
                    .ConfigureAwait(true);
                if (disposition == ProjectOpenDisposition.Cancel)
                {
                    StatusText = "Open project cancelled";
                    return;
                }
                if (disposition == ProjectOpenDisposition.NewWindow)
                {
                    await _documentWindowService
                        .OpenDocumentAsync(ProjectLaunchRequest.ForProject(session), cancellationToken)
                        .ConfigureAwait(true);
                    StatusText = "Opened project in a new window";
                    return;
                }

                await CombineProjectAsync(session.ProjectFilePath, cancellationToken).ConfigureAwait(true);
                return;
            }

            if (!await ConfirmCanLeaveDocumentAsync(cancellationToken).ConfigureAwait(true))
                return;
            var navigationRequest = CreateEditorNavigationRequest(ProjectLaunchRequest.ForProject(session));
            service.ActivateSession(session);
            _preapprovedNavigationRequest = navigationRequest;
            Messenger.Send(navigationRequest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Open project cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open editor project");
            StatusText = "Project replacement failed";
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsReplacingProject = false;
            _projectReplacementGate.Release();
        }
    }

    public async Task CombineProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var incoming = await _project3DStateService.LoadAsync(projectPath, cancellationToken).ConfigureAwait(true);
        var incomingTwoD = incoming.TwoDWorkspaceState;
        if (incomingTwoD is null)
            throw new InvalidOperationException("The selected project has no 2D drawing to combine.");

        var merged = Editor2DProjectCombiner.Combine(_twoDWorkspace.State, incomingTwoD);
        _twoDWorkspace.Apply(merged, recordHistory: true);
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        MarkDocumentDirty();
        ActiveEditorMode = EditorMode.TwoD;
        StatusText = $"Combined {Path.GetFileName(projectPath)}";
    }

    private async Task ReplaceProjectAsync(
        Func<ProjectSessionService, Task<ProjectSession?>> prepareSession,
        string statusText,
        bool prepareBeforeConfirmation,
        CancellationToken cancellationToken)
    {
        var service = _projectSessionService;
        if (service is null || !await _projectReplacementGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
            return;

        IsReplacingProject = true;
        ErrorMessage = null;
        try
        {
            StatusText = statusText;
            ProjectSession? session;
            if (_documentWindowService is not null)
            {
                session = await prepareSession(service).ConfigureAwait(true);
                if (session is null)
                    return;
                await _documentWindowService
                    .OpenDocumentAsync(ProjectLaunchRequest.ForProject(session), cancellationToken)
                    .ConfigureAwait(true);
                StatusText = "Opened project in a new window";
                return;
            }
            if (prepareBeforeConfirmation)
            {
                session = await prepareSession(service).ConfigureAwait(true);
                if (session is null
                    || !await ConfirmCanLeaveDocumentAsync(cancellationToken).ConfigureAwait(true))
                    return;
            }
            else
            {
                if (!await ConfirmCanLeaveDocumentAsync(cancellationToken).ConfigureAwait(true))
                    return;
                session = await prepareSession(service).ConfigureAwait(true);
            }
            if (session is null)
                return;

            var navigationRequest = CreateEditorNavigationRequest(ProjectLaunchRequest.ForProject(session));
            service.ActivateSession(session);
            _preapprovedNavigationRequest = navigationRequest;
            Messenger.Send(navigationRequest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Project replacement cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to replace editor project");
            StatusText = "Project replacement failed";
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsReplacingProject = false;
            _projectReplacementGate.Release();
        }
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
}
