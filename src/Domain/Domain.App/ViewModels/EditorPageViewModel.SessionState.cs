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
                        ? "Imported workspace"
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
            RestoreActivityLog([]);
            IsActivityLogExpanded = false;
            LearnModeEnabled = true;
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
            ClearBodyMoveHistory();
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
            SaveDocumentAsCommand.NotifyCanExecuteChanged();
            SaveAndCloseDocumentCommand.NotifyCanExecuteChanged();
            CloseDocumentCommand.NotifyCanExecuteChanged();
            NewProjectCommand.NotifyCanExecuteChanged();
            OpenProjectCommand.NotifyCanExecuteChanged();
            ImportFilesCommand.NotifyCanExecuteChanged();
            NotifyCommandPaletteStateChanged();
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
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                ImportFilesCommand.NotifyCanExecuteChanged();
                NotifyCommandPaletteStateChanged();
            }
        }
    }

    protected override ValueTask<bool> LoadParametersAsync(IReadOnlyDictionary<string, object> parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue(EditorNavigationParameterKeys.ProjectSession, out var value) || value is not ProjectSession session)
        {
            _logger.LogWarning("Editor navigation was missing project session metadata.");
            return ValueTask.FromResult(false);
        }

        ProjectSession = session;
        if (!_isBatchWorkspaceDirtyTrackingSubscribed)
        {
            _batchWorkspace.StateChanged += MarkDocumentDirty;
            _isBatchWorkspaceDirtyTrackingSubscribed = true;
        }
        if (!_isReferenceImageTraceChangedSubscribed)
        {
            _twoDWorkspace.ReferenceImageTraceChanged += OnReferenceImageTraceChanged;
            _isReferenceImageTraceChangedSubscribed = true;
        }
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
        _pendingReferenceImagePaths = parameters.TryGetValue(EditorNavigationParameterKeys.PendingReferenceImagePaths, out var pendingImageValue)
            ? pendingImageValue switch
            {
                IReadOnlyList<string> paths => paths,
                _ => [],
            }
            : [];
        Messenger.Register<PreviewApplicationClosingMessage>(this, OnPreviewApplicationClosing);
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
            RestoreActivityLog(state.ActivityLog);
            LearnModeEnabled = state.LearnModeEnabled;
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
            if (state.TwoDWorkspaceState?.ExportPreferences is null
                && state.LegacyExportMeasurementLines is { } includeMeasurementLines)
            {
                TwoDExportMeasurementLines = includeMeasurementLines;
            }
            ApplyPersistedEditorWorkspaceState(state.WorkspaceState);
            await _batchWorkspace
                .RestoreStateAsync(state.BatchWorkspaceState, ProjectSession.ProjectFilePath, token)
                .ConfigureAwait(true);
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
            await RouteImportedTwoDDrawingsAsync(_pendingTwoDFilePaths, token).ConfigureAwait(true);
            _pendingTwoDFilePaths = [];
        }

        if (_pendingReferenceImagePaths.Count > 0)
        {
            await ImportReferenceImagesAsync(_pendingReferenceImagePaths, insertionPoint: null, token).ConfigureAwait(true);
            _pendingReferenceImagePaths = [];
        }
    }

    private static Editor2DPreviewDocument ScaleImportedTwoDDocument(Editor2DPreviewDocument document, double factor)
    {
        Editor2DPoint Transform(Editor2DPoint point) => new(point.X * factor, point.Y * factor);
        var paths = document.Paths.Select(path => path with
        {
            Points = path.Points.Select(Transform).ToArray(),
            Start = path.Start is { } start ? Transform(start) : null,
            Center = path.Center is { } center ? Transform(center) : null,
            Radius = path.Radius is { } radius ? radius * factor : null,
            TextHeight = path.TextHeight is { } textHeight ? textHeight * factor : null,
            BezierAnchors = path.BezierAnchors?.Select(anchor => anchor with
            {
                Point = Transform(anchor.Point),
                HandleIn = anchor.HandleIn is { } handleIn ? Transform(handleIn) : null,
                HandleOut = anchor.HandleOut is { } handleOut ? Transform(handleOut) : null,
            }).ToArray(),
        }).ToArray();
        var bounds = document.Bounds;
        return document with
        {
            Paths = paths,
            Bounds = new(
                bounds.MinX * factor,
                bounds.MinY * factor,
                bounds.MaxX * factor,
                bounds.MaxY * factor),
        };
    }

}
