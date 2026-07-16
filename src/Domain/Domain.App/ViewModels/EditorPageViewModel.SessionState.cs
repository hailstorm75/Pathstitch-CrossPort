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
                ImportFilesCommand.NotifyCanExecuteChanged();
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
            await ImportTwoDDrawingsAsync(_pendingTwoDFilePaths, token).ConfigureAwait(true);
            _pendingTwoDFilePaths = [];
        }

        if (_pendingReferenceImagePaths.Count > 0)
        {
            await ImportReferenceImagesAsync(_pendingReferenceImagePaths, token).ConfigureAwait(true);
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

    private static Editor2DPreviewDocument MergeImportedTwoDDocuments(
        IReadOnlyList<Editor2DPreviewDocument> documents,
        bool preserveFirstDocument = false)
    {
        if (documents.Count == 1)
            return documents[0];

        const double gap = 20.0;
        var paths = new List<Editor2DPreviewPath>();
        var entityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cursorX = preserveFirstDocument ? documents[0].Bounds.MaxX + gap : 0.0;

        for (var documentIndex = 0; documentIndex < documents.Count; documentIndex++)
        {
            var document = documents[documentIndex];
            if (documentIndex == 0 && preserveFirstDocument)
            {
                paths.AddRange(document.Paths);
                foreach (var (entityType, count) in document.EntityCounts)
                    entityCounts[entityType] = count;
                foreach (var entityType in document.UnsupportedEntityTypes)
                    unsupported.Add(entityType);
                continue;
            }
            var offsetX = cursorX - document.Bounds.MinX;
            var offsetY = -document.Bounds.MinY;
            for (var pathIndex = 0; pathIndex < document.Paths.Count; pathIndex++)
            {
                var path = document.Paths[pathIndex];
                var id = $"import-{documentIndex + 1}-{pathIndex + 1}-{path.Id}";
                paths.Add(TranslatePath(path, id, offsetX, offsetY));
            }

            foreach (var (entityType, count) in document.EntityCounts)
                entityCounts[entityType] = entityCounts.TryGetValue(entityType, out var existing) ? existing + count : count;
            foreach (var entityType in document.UnsupportedEntityTypes)
                unsupported.Add(entityType);

            cursorX += document.Bounds.Width + gap;
        }

        return new Editor2DPreviewDocument(
            paths,
            MeasureBounds(paths),
            entityCounts,
            unsupported.ToArray());
    }

    private static Editor2DPreviewPath TranslatePath(
        Editor2DPreviewPath path,
        string id,
        double offsetX,
        double offsetY)
    {
        static Editor2DPoint Translate(Editor2DPoint point, double x, double y)
            => new(point.X + x, point.Y + y);

        var translatedAnchors = path.BezierAnchors?
            .Select(anchor => new Editor2DBezierAnchor(
                Translate(anchor.Point, offsetX, offsetY),
                anchor.HandleIn is { } handleIn ? Translate(handleIn, offsetX, offsetY) : null,
                anchor.HandleOut is { } handleOut ? Translate(handleOut, offsetX, offsetY) : null))
            .ToArray();

        return path with
        {
            Id = id,
            Points = path.Points.Select(point => Translate(point, offsetX, offsetY)).ToArray(),
            Start = path.Start is { } start ? Translate(start, offsetX, offsetY) : null,
            Center = path.Center is { } center ? Translate(center, offsetX, offsetY) : null,
            BezierAnchors = translatedAnchors,
        };
    }

    private static Editor2DBounds MeasureBounds(IReadOnlyList<Editor2DPreviewPath> paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        if (points.Length == 0)
            return new Editor2DBounds(0.0, 0.0, 0.0, 0.0);

        return new Editor2DBounds(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }
}
