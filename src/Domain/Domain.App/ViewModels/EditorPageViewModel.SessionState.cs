using Domain.App.Models;
using Domain.App.Navigation;
using Domain.MVVM.Navigation;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public ProjectSession? ProjectSession
    {
        get => _projectSession;
        private set
        {
            if (!SetProperty(ref _projectSession, value))
                return;

            using var dirtyTrackingSuppression = SuppressDocumentDirtyTracking();
            ProjectName = value?.ProjectName ?? string.Empty;
            Template = value?.Template;
            SessionOrigin = value?.Origin;
            ProjectTitle = value?.ProjectName ?? "Editor";
            ProjectSubtitle = value?.ProjectFilePath ?? "No project file";
            StatusText = value is null
                ? "Waiting for session"
                : value.Origin == ProjectSessionOrigin.Created
                    ? "New template project"
                    : value.Origin == ProjectSessionOrigin.Imported
                        ? "Imported 3D workspace"
                        : "Opened template project";
            ViewportStateText = "Booting viewport";
            SelectionSummary = "No selection";
            LastViewportEvent = "SessionLoaded";
            ErrorMessage = null;
            _threeDWorkspace.ResetViewportLifecycle();
            OnPropertyChanged(nameof(ViewportReady));
            SelectedFaceCount = 0;
            ViewportJsonContent = null;
            SetSourceModelPath(null);
            SetDistortionData(string.Empty);
            ClearTwoDState();
            ActiveEditorMode = EditorMode.ThreeD;
            ActiveTool = Editor3DTool.Select;
            ThreeDOrthographic = false;
            IsPlaneSelectionActive = false;
            PlaneSelectionModeType = Domain.App.Models.PlaneSelectionModeType.Origin;
            SelectedProjectionPlane = null;
            SelectedProjectionFaceIndex = null;
            SelectedProjectionBodyIndex = null;
            PlaneOffset = 0.0;
            PlaneOffsetText = "0";
            ProjectionSelectionSummary = "Selected: None";
            Bodies = [];
            SelectedFaces = [];
            SelectedFaceDetails = [];
            SelectedBodyIndex = null;
            BodyOffsets = [];
            BodyOffsetCount = 0;
            DistortionModeIndex = 0;
            LiveRecomputeEnabled = false;
            SetUnfoldPreviewScope(false, requestPersistence: false, requestLiveRecompute: false);
            SelectedBodyOffsetXText = "0";
            SelectedBodyOffsetYText = "0";
            SelectedBodyOffsetZText = "0";
            BodyMoveStepText = "1";
            SaveDocumentCommand.NotifyCanExecuteChanged();
            SaveAndCloseDocumentCommand.NotifyCanExecuteChanged();
            CloseDocumentCommand.NotifyCanExecuteChanged();
        }
    }

    public string ProjectName
    {
        get => _projectName;
        private set => SetProperty(ref _projectName, value);
    }

    public string ProjectTitle
    {
        get => _projectTitle;
        private set => SetProperty(ref _projectTitle, value);
    }

    public string ProjectSubtitle
    {
        get => _projectSubtitle;
        private set => SetProperty(ref _projectSubtitle, value);
    }

    public ProjectTemplateDefinition? Template
    {
        get => _template;
        private set => SetProperty(ref _template, value);
    }

    public ProjectSessionOrigin? SessionOrigin
    {
        get => _sessionOrigin;
        private set => SetProperty(ref _sessionOrigin, value);
    }

    public override bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    protected override ValueTask<bool> LoadParametersAsync(IReadOnlyDictionary<string, object> parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue(EditorNavigationParameterKeys.ProjectSession, out var value) || value is not ProjectSession session)
        {
            _logger.LogWarning("Editor navigation was missing project session metadata.");
            return ValueTask.FromResult(false);
        }

        ProjectSession = session;
        _pendingSourceModelPaths = parameters.TryGetValue(EditorNavigationParameterKeys.PendingSourceModelPaths, out var pendingValue)
            ? pendingValue switch
            {
                IReadOnlyList<string> paths => paths,
                _ => [],
            }
            : [];
        _pendingTwoDFilePaths = parameters.TryGetValue(EditorNavigationParameterKeys.PendingTwoDFilePaths, out var pendingTwoDValue)
            ? pendingTwoDValue switch
            {
                IReadOnlyList<string> paths => paths,
                _ => [],
            }
            : [];
        WeakReferenceMessenger.Default.Register<PreviewApplicationClosingMessage>(this, OnPreviewApplicationClosing);
        return ValueTask.FromResult(true);
    }

    protected override async ValueTask LoadPageAsync(CancellationToken token)
    {
        if (ProjectSession is null)
            return;

        Project3DState state;
        using (SuppressDocumentDirtyTracking())
        {
            state = await _project3DStateService.LoadAsync(ProjectSession.ProjectFilePath, token).ConfigureAwait(true);
            ViewportJsonContent = state.ViewportJson;
            SetSourceModelPath(state.SourceModelPath);
            _stepTopology = state.StepTopology;
            SetDistortionData(string.Empty);
            Bodies = state.Bodies;
            BodyOffsets = state.BodyOffsets;
            BodyOffsetCount = state.BodyOffsets.Count;
            if (state.HasGeneratedOutput)
            {
                GeneratedOutputContext = state.GeneratedOutputContext;
                await UpdateGeneratedOutputPreviewAsync(
                    state.GeneratedOutputPath,
                    activatePreviewWorkspace: false,
                    token,
                    persistState: false).ConfigureAwait(true);
            }
            else
            {
                ClearTwoDState();
            }
            ApplyPersistedUnfoldWorkspaceState(state.UnfoldWorkspaceState);
            RefreshSelectionState();
            RequestBodyMoveStateSync();

            StatusText = state.HasModel switch
            {
                true when state.HasGeneratedOutput => $"3D model restored ({Bodies.Count} bodies) with generated output",
                true => $"3D model restored ({Bodies.Count} bodies)",
                _ when state.HasGeneratedOutput => "Generated output restored",
                _ => "No persisted 3D model found",
            };

            ViewportStateText = state.HasModel
                ? "Waiting for viewport ready event"
                : state.HasGeneratedOutput
                    ? "Generated output preview restored"
                    : "Viewport loaded without saved model";

            if (state.HasModel)
            {
                RequestViewportScript(BuildLoadModelScript(state.ViewportJson!));
                RequestBodyVisibilityStateSync();
                ApplyPersistedProjectionWorkspaceState(state.ProjectionWorkspaceState);
            }

            ApplyPersistedTwoDWorkspaceState(state.TwoDWorkspaceState);
            ApplyPersistedEditorWorkspaceState(state.WorkspaceState);
            if (state.ThreeDWorkspaceState is { } threeDState)
            {
                _threeDWorkspace.RestoreState(threeDState);
                SelectedFaces = HydrateStableFaceReferences(SelectedFaces);
                ApplyPersistedProjectionWorkspaceState(threeDState.Projection);
                ApplyPersistedUnfoldWorkspaceState(threeDState.Unfold);
                NotifyThreeDWorkspaceFacadeProperties();
            }
        }

        EstablishCleanDocumentBaseline();

        if (_pendingSourceModelPaths.Count > 0)
        {
            await LoadPendingSourceModelsAsync(state, token).ConfigureAwait(true);
            _pendingSourceModelPaths = [];
            MarkDocumentDirty();
        }

        if (_pendingTwoDFilePaths.Count > 0)
        {
            var importedDocument = await _editorOutputPreviewService
                .LoadPreviewDocumentAsync(_pendingTwoDFilePaths[0], token)
                .ConfigureAwait(true);
            if (importedDocument is not null)
            {
                SetTwoDDocument(importedDocument);
                StatusText = $"Imported drawing: {Path.GetFileName(_pendingTwoDFilePaths[0])}";
                MarkDocumentDirty();
            }
            _pendingTwoDFilePaths = [];
        }
    }
}
