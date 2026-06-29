using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ViewportHtml => _viewportHtml;

    public Uri ViewportBaseUri => _viewportBaseUri;

    public string ViewportStateText
    {
        get => _viewportStateText;
        private set => SetProperty(ref _viewportStateText, value);
    }

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set => SetProperty(ref _selectionSummary, value);
    }

    public string LastViewportEvent
    {
        get => _lastViewportEvent;
        private set => SetProperty(ref _lastViewportEvent, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool ViewportReady
    {
        get => _viewportReady;
        private set => SetProperty(ref _viewportReady, value);
    }

    public int SelectedFaceCount
    {
        get => _selectedFaceCount;
        private set => SetProperty(ref _selectedFaceCount, value);
    }

    public string? StepJsonContent
    {
        get => _stepJsonContent;
        private set
        {
            if (!SetProperty(ref _stepJsonContent, value))
                return;

            SyncSidebarToolStates();
            OnPropertyChanged(nameof(HasLoadedModel));
            OnPropertyChanged(nameof(HasNoLoadedModel));
            OnPropertyChanged(nameof(ShowViewportEmptyState));
            OnPropertyChanged(nameof(CanFrameHome));
            OnPropertyChanged(nameof(SourceModelActionLabel));
            OnPropertyChanged(nameof(SourceModelWorkflowHint));
            OnPropertyChanged(nameof(SourceModelAssetStatusSummary));
            OnPropertyChanged(nameof(HasProjectionSourceBodies));
            OnPropertyChanged(nameof(CanStartPlaneSelection));
            OnPropertyChanged(nameof(ProjectionToolHint));
            OnPropertyChanged(nameof(CanEditProjectionOffset));
            OnPropertyChanged(nameof(CanConfirmProjection));
            OnPropertyChanged(nameof(ProjectionOffsetHint));
            OnPropertyChanged(nameof(WorkspaceModeHint));
            OnPropertyChanged(nameof(CanUseLiveRecompute));
            OnPropertyChanged(nameof(CanUnfoldSelected));
            OnPropertyChanged(nameof(UnfoldEngineSummary));
            OnPropertyChanged(nameof(UnfoldHintText));
            OnPropertyChanged(nameof(FaceDistortionStatusText));
        }
    }

    public bool HasLoadedModel => Bodies.Count > 0 && !string.IsNullOrWhiteSpace(StepJsonContent);

    public bool HasNoLoadedModel => !HasLoadedModel;

    public bool ShowViewportEmptyState => IsShowing3DWorkspace && HasNoLoadedModel;

    public bool CanFrameHome
        => IsShowing3DWorkspace
            ? HasLoadedModel
            : HasGeneratedOutputWorkspaceDocument;

    public string ViewportEmptyStateTitle => "DRAG & DROP 3D MODELS";

    public string ViewportEmptyStateDescription
        => "Open one or more .step, .stp, .obj, or .stl files to start the 3D workspace, or switch to the 2D workspace to sketch directly.";

    private EditorWorkspaceState BuildPersistedEditorWorkspaceState()
        => new(
            ActiveTool,
            ThreeDOrthographic,
            IsShowingGeneratedOutputWorkspace,
            GeneratedOutputActiveTool,
            GeneratedOutputPolygonSides,
            GeneratedOutputViewportZoom,
            GeneratedOutputViewportOffsetX,
            GeneratedOutputViewportOffsetY,
            _generatedOutputExpandedRectanglePathIds);

    private void ApplyPersistedEditorWorkspaceState(EditorWorkspaceState? state)
    {
        if (state is null)
            return;

        ThreeDOrthographic = state.ThreeDOrthographic;
        ActivateTool(state.ActiveTool);
        GeneratedOutputActiveTool = state.GeneratedOutputActiveTool;
        var previousViewportSuppression = _suppressGeneratedOutputViewportPersistence;
        _suppressGeneratedOutputViewportPersistence = true;
        try
        {
            GeneratedOutputPolygonSides = state.GeneratedOutputPolygonSides;
        }
        finally
        {
            _suppressGeneratedOutputViewportPersistence = previousViewportSuppression;
        }

        _generatedOutputExpandedRectanglePathIds = NormalizeGeneratedOutputExpandedRectanglePathIds(state.GeneratedOutputExpandedRectanglePathIds);
        ApplyGeneratedOutputPreviewDocument(GeneratedOutputPreviewDocument, requestPersistence: false);
        ApplyGeneratedOutputViewportState(
            state.GeneratedOutputViewportZoom,
            state.GeneratedOutputViewportOffsetX,
            state.GeneratedOutputViewportOffsetY,
            requestPersistence: false);

        if (!state.ShowGeneratedOutputWorkspace)
        {
            IsShowingGeneratedOutputWorkspace = false;
            return;
        }

        if (HasGeneratedOutputWorkspaceDocument)
            IsShowingGeneratedOutputWorkspace = true;
    }
}
