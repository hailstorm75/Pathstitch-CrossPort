using Domain.App.Models;
using CommunityToolkit.Mvvm.Input;

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
        private set => SetWorkspaceFacadeValue(_viewportStateText, value, updated => _viewportStateText = updated);
    }

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set => SetWorkspaceFacadeValue(_selectionSummary, value, updated => _selectionSummary = updated);
    }

    public string LastViewportEvent
    {
        get => _lastViewportEvent;
        private set => SetWorkspaceFacadeValue(_lastViewportEvent, value, updated => _lastViewportEvent = updated);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool ViewportReady
    {
        get => _viewportReady;
        private set => SetWorkspaceFacadeValue(_viewportReady, value, updated => _viewportReady = updated);
    }

    public int SelectedFaceCount
    {
        get => _selectedFaceCount;
        private set => SetWorkspaceFacadeValue(_selectedFaceCount, value, updated => _selectedFaceCount = updated);
    }

    public string? ViewportJsonContent
    {
        get => _threeDWorkspace.ViewportJsonContent;
        private set
        {
            if (!_threeDWorkspace.SetViewportJson(value))
                return;

            OnPropertyChanged();

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

    public bool HasLoadedModel => Bodies.Count > 0 && !string.IsNullOrWhiteSpace(ViewportJsonContent);

    public bool HasNoLoadedModel => !HasLoadedModel;

    public bool ShowViewportEmptyState => IsShowing3DWorkspace && HasNoLoadedModel;

    public bool CanFrameHome
        => ActiveEditorMode switch
        {
            EditorMode.ThreeD => HasLoadedModel,
            EditorMode.TwoD => HasTwoDWorkspaceDocument,
            _ => false,
        };

    public string ViewportEmptyStateTitle => "DRAG & DROP 3D MODELS";

    public string ViewportEmptyStateDescription
        => "Open one or more .obj, .stl, .step, or .stp files to start the 3D workspace, or switch to the 2D workspace to sketch directly.";

    private EditorWorkspaceState BuildPersistedEditorWorkspaceState()
        => new(
            ActiveTool,
            ThreeDOrthographic,
            IsShowingTwoDWorkspace,
            TwoDActiveTool,
            TwoDPolygonSides,
            TwoDViewportZoom,
            TwoDViewportOffsetX,
            TwoDViewportOffsetY,
            _twoDExpandedRectanglePathIds,
            ActiveEditorMode,
            ToolCustomizations);

    private Editor2DWorkspaceState BuildPersistedTwoDWorkspaceState()
    {
        SyncTwoDWorkspaceState(recordHistory: false);
        return RemoveGeneratedPreviewLayer(_twoDWorkspace.State);
    }

    public bool UndoTwoDWorkspace()
    {
        if (!_twoDWorkspace.Undo())
            return false;

        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        NotifyTwoDHistoryCommands();
        return true;
    }

    public bool RedoTwoDWorkspace()
    {
        if (!_twoDWorkspace.Redo())
            return false;

        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        NotifyTwoDHistoryCommands();
        return true;
    }

    public bool CanUndoTwoDWorkspace => _twoDWorkspace.CanUndo;

    public bool CanRedoTwoDWorkspace => _twoDWorkspace.CanRedo;

    public void BeginTwoDMeasurementEdit() => _twoDWorkspace.BeginMeasurementEdit();

    public void EndTwoDMeasurementEdit()
    {
        _twoDWorkspace.EndMeasurementEdit();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        NotifyTwoDHistoryCommands();
    }

    [RelayCommand(CanExecute = nameof(CanUndoTwoDWorkspace))]
    private void UndoTwoD() => UndoTwoDWorkspace();

    [RelayCommand(CanExecute = nameof(CanRedoTwoDWorkspace))]
    private void RedoTwoD() => RedoTwoDWorkspace();

    private void SyncTwoDWorkspaceState(bool recordHistory)
    {
        if (_isApplyingTwoDWorkspaceState)
            return;

        _twoDWorkspace.Apply(
            _twoDWorkspace.State with
            {
                Document = TwoDDocument ?? Editor2DWorkspaceState.Empty.Document,
                ActiveTool = TwoDActiveTool,
                SelectedPathIds = TwoDSelectedPathIds,
                Measurements = TwoDMeasurements,
                SelectedMeasurementId = TwoDSelectedMeasurementId,
                PolygonSides = TwoDPolygonSides,
                ViewportZoom = TwoDViewportZoom,
                ViewportOffsetX = TwoDViewportOffsetX,
                ViewportOffsetY = TwoDViewportOffsetY,
                ExpandedRectanglePathIds = _twoDExpandedRectanglePathIds,
                IsInitialized = _twoDWorkspace.IsInitialized,
                Layers = _twoDWorkspace.Layers,
                ActiveLayerId = _twoDWorkspace.ActiveLayerId,
                CornerParameters = _twoDWorkspace.CornerParameters,
                SewingHoleParameters = _twoDWorkspace.SewingHoleParameters,
                SewingHoleOperations = _twoDWorkspace.SewingHoleOperations,
                SnapEnabled = _twoDWorkspace.SnapEnabled,
                GridVisible = _twoDWorkspace.GridVisible,
                ChainSelectionEnabled = _twoDWorkspace.ChainSelectionEnabled,
            },
            recordHistory);
        NotifyTwoDHistoryCommands();
    }

    internal void ApplyPersistedTwoDWorkspaceState(Editor2DWorkspaceState? state)
    {
        if (state is null)
            return;

        _twoDWorkspace.Apply(state, recordHistory: false);
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        _twoDWorkspace.ClearHistory();
    }

    private void ApplyTwoDWorkspaceSnapshot(Editor2DWorkspaceState state)
    {
        ReconcileExpandedTwoDFolderIds();
        _isApplyingTwoDWorkspaceState = true;
        try
        {
            _twoDExpandedRectanglePathIds = NormalizeTwoDExpandedRectanglePathIds(
                state.ExpandedRectanglePathIds);
            ApplyTwoDDocument(
                state.IsInitialized ? state.Document : null,
                requestPersistence: false);
            TwoDActiveTool = state.ActiveTool;
            TwoDPolygonSides = state.PolygonSides;
            TwoDSelectedPathIds = state.SelectedPathIds ?? [];
            SyncTwoDConvertLineEditorFromSelection();
            TwoDMeasurements = state.Measurements ?? [];
            TwoDSelectedMeasurementId = state.SelectedMeasurementId;
            var exportPreferences = state.ExportPreferences ?? new Editor2DExportPreferences();
            _twoDExportSelectedOnly = exportPreferences.ExportSelectedOnly;
            _twoDExportMeasurementLines = exportPreferences.IncludeMeasurementLines;
            _twoDSvgPrecisionText = exportPreferences.SvgPrecisionText;
            _twoDSvgStrokeWidthText = exportPreferences.SvgStrokeWidthText;
            _twoDDxfVersion = new Editor2DExportOptions(DxfVersion: exportPreferences.DxfVersion).NormalizedDxfVersion;
            _twoDPngLongestEdgeText = exportPreferences.PngLongestEdgeText;
            _twoDPngTransparent = exportPreferences.PngTransparent;
            ApplyTwoDViewportState(
                state.ViewportZoom,
                state.ViewportOffsetX,
                state.ViewportOffsetY,
                requestPersistence: false);
            NotifyTwoDWorkspaceFacadeProperties();
            NotifyTwoDHistoryCommands();
        }
        finally
        {
            _isApplyingTwoDWorkspaceState = false;
        }
    }

    private void NotifyTwoDHistoryCommands()
    {
        OnPropertyChanged(nameof(CanUndoTwoDWorkspace));
        OnPropertyChanged(nameof(CanRedoTwoDWorkspace));
        UndoTwoDCommand.NotifyCanExecuteChanged();
        RedoTwoDCommand.NotifyCanExecuteChanged();
        NotifyMenuCommands();
    }

    private void NotifyTwoDWorkspaceFacadeProperties()
    {
        SyncSidebarToolStates();
        OnPropertyChanged(nameof(TwoDDocument));
        OnPropertyChanged(nameof(TwoDActiveTool));
        OnPropertyChanged(nameof(TwoDSnapEnabled));
        OnPropertyChanged(nameof(TwoDSnappingSummary));
        OnPropertyChanged(nameof(TwoDGridVisible));
        OnPropertyChanged(nameof(TwoDGridSummary));
        OnPropertyChanged(nameof(TwoDChainSelectionEnabled));
        OnPropertyChanged(nameof(TwoDChainSelectionSummary));
        OnPropertyChanged(nameof(TwoDSelectedPathIds));
        OnPropertyChanged(nameof(MirrorLinks));
        OnPropertyChanged(nameof(HasTwoDMirrorLinkSelection));
        OnPropertyChanged(nameof(HasSelectedTwoDImportGroup));
        OnPropertyChanged(nameof(TwoDMeasurements));
        OnPropertyChanged(nameof(TwoDDimensionParameters));
        OnPropertyChanged(nameof(TwoDSelectedMeasurementId));
        OnPropertyChanged(nameof(TwoDLayers));
        OnPropertyChanged(nameof(TwoDFolders));
        OnPropertyChanged(nameof(TwoDLayerHierarchyItems));
        OnPropertyChanged(nameof(TwoDActiveLayerId));
        OnPropertyChanged(nameof(TwoDActiveLayer));
        OnPropertyChanged(nameof(HasTwoDActiveLayer));
        OnPropertyChanged(nameof(TwoDHiddenPathIds));
        OnPropertyChanged(nameof(TwoDCornerParameters));
        OnPropertyChanged(nameof(HasTwoDWorkspaceDocument));
        OnPropertyChanged(nameof(HasTwoDPreview));
        OnPropertyChanged(nameof(HasNoTwoDPreview));
        OnPropertyChanged(nameof(HasTwoDSelection));
        OnPropertyChanged(nameof(TwoDSelectionCount));
        OnPropertyChanged(nameof(TwoDSelectionSummary));
        OnPropertyChanged(nameof(CanApplyTwoDStrokeToFill));
        OnPropertyChanged(nameof(CanApplyTwoDFillToStroke));
        OnPropertyChanged(nameof(HasTwoDMeasurements));
        OnPropertyChanged(nameof(HasTwoDSelectedMeasurement));
        OnPropertyChanged(nameof(TwoDMeasurementSummary));
        OnPropertyChanged(nameof(TwoDExportSelectedOnly));
        OnPropertyChanged(nameof(TwoDExportMeasurementLines));
        OnPropertyChanged(nameof(TwoDSvgPrecisionText));
        OnPropertyChanged(nameof(TwoDSvgStrokeWidthText));
        OnPropertyChanged(nameof(CanApplyTwoDSvgOptions));
        OnPropertyChanged(nameof(TwoDDxfVersion));
        OnPropertyChanged(nameof(TwoDPngLongestEdgeText));
        OnPropertyChanged(nameof(TwoDPngTransparent));
        OnPropertyChanged(nameof(TwoDAutoDimensionCount));
        OnPropertyChanged(nameof(TwoDViewportSummary));
        OnPropertyChanged(nameof(TwoDToolHint));
        OnPropertyChanged(nameof(ActiveToolLabel));
        NotifyToolbarCollectionsChanged();
        OnPropertyChanged(nameof(CanFrameHome));
        OnPropertyChanged(nameof(WorkspaceModeHint));
        OnPropertyChanged(nameof(IsTwoDSelectToolActive));
        OnPropertyChanged(nameof(IsTwoDMoveToolActive));
        OnPropertyChanged(nameof(IsTwoDPanToolActive));
        OnPropertyChanged(nameof(IsTwoDMeasureToolActive));
        OnPropertyChanged(nameof(IsTwoDDimensionToolActive));
        OnPropertyChanged(nameof(IsTwoDLineToolActive));
        OnPropertyChanged(nameof(IsTwoDRectangleToolActive));
        OnPropertyChanged(nameof(IsTwoDCircleToolActive));
        OnPropertyChanged(nameof(IsTwoDPolygonToolActive));
        OnPropertyChanged(nameof(IsTwoDTextToolActive));
        OnPropertyChanged(nameof(IsTwoDPenToolActive));
        OnPropertyChanged(nameof(IsTwoDScaleToolActive));
        OnPropertyChanged(nameof(IsTwoDMirrorToolActive));
        OnPropertyChanged(nameof(IsTwoDTrimToolActive));
        OnPropertyChanged(nameof(IsTwoDFilletToolActive));
        OnPropertyChanged(nameof(IsTwoDChamferToolActive));
        OnPropertyChanged(nameof(IsTwoDCornerToolActive));
        OnPropertyChanged(nameof(TwoDActiveCornerLabel));
        OnPropertyChanged(nameof(TwoDCornerValueLabel));
        OnPropertyChanged(nameof(IsTwoDConvertLinesToolActive));
        OnPropertyChanged(nameof(HasSingleTwoDConvertedLineGroupSelection));
        OnPropertyChanged(nameof(IsTwoDConvertLinesInspectorVisible));
        OnPropertyChanged(nameof(TwoDConvertLineActionLabel));
        OnPropertyChanged(nameof(CanApplyTwoDConvertLines));
        OnPropertyChanged(nameof(TwoDConvertLineSummary));
        OnPropertyChanged(nameof(TwoDConvertLinePreviewPaths));
        OnPropertyChanged(nameof(IsTwoDOffsetToolActive));
        OnPropertyChanged(nameof(IsTwoDAddThicknessToolActive));
        OnPropertyChanged(nameof(IsTwoDCleanupToolActive));
        OnPropertyChanged(nameof(IsTwoDPatternToolActive));
        OnPropertyChanged(nameof(IsTwoDPaperFoldingToolActive));
    }

    private void NotifyThreeDWorkspaceFacadeProperties()
    {
        SyncSidebarToolStates();
        OnPropertyChanged(nameof(ActiveTool));
        OnPropertyChanged(nameof(Bodies));
        OnPropertyChanged(nameof(SelectedFaces));
        OnPropertyChanged(nameof(SelectedFaceDetails));
        OnPropertyChanged(nameof(SelectedBodyIndex));
        OnPropertyChanged(nameof(BodyOffsets));
        OnPropertyChanged(nameof(ViewportJsonContent));
        OnPropertyChanged(nameof(HasLoadedModel));
        OnPropertyChanged(nameof(HasNoLoadedModel));
        OnPropertyChanged(nameof(HasFaceSelection));
        OnPropertyChanged(nameof(SelectedFacesQueueSummary));
        OnPropertyChanged(nameof(SelectedBodySummary));
        OnPropertyChanged(nameof(BodyInventorySummary));
        OnPropertyChanged(nameof(VisibleBodyCount));
        OnPropertyChanged(nameof(HiddenBodyCount));
        OnPropertyChanged(nameof(CanFrameHome));
        NotifyToolbarCollectionsChanged();
    }

    private void ApplyPersistedEditorWorkspaceState(EditorWorkspaceState? state)
    {
        if (state is null)
            return;

        ApplyToolCustomizations(state.ToolCustomizations, requestPersistence: false);
        ThreeDOrthographic = state.ThreeDOrthographic;
        ActivateTool(state.ActiveTool);
        TwoDActiveTool = state.TwoDActiveTool;
        var previousViewportSuppression = _suppressTwoDViewportPersistence;
        _suppressTwoDViewportPersistence = true;
        try
        {
            TwoDPolygonSides = state.TwoDPolygonSides;
        }
        finally
        {
            _suppressTwoDViewportPersistence = previousViewportSuppression;
        }

        _twoDExpandedRectanglePathIds = NormalizeTwoDExpandedRectanglePathIds(state.TwoDExpandedRectanglePathIds);
        ApplyTwoDDocument(TwoDDocument, requestPersistence: false);
        ApplyTwoDViewportState(
            state.TwoDViewportZoom,
            state.TwoDViewportOffsetX,
            state.TwoDViewportOffsetY,
            requestPersistence: false);

        var persistedMode = state.ActiveEditorMode
            ?? (state.ShowTwoDWorkspace ? EditorMode.TwoD : EditorMode.ThreeD);
        ActiveEditorMode = persistedMode == EditorMode.TwoD && !HasTwoDWorkspaceDocument
            ? EditorMode.ThreeD
            : persistedMode;
    }
}
