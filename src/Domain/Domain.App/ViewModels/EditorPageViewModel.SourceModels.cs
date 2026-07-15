using System.Globalization;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public string SourceModelActionLabel => HasLoadedModel ? "Add 3D Models..." : "Open 3D Model...";

    public string SourceModelWorkflowHint => !HasLoadedModel
        ? "Open STEP/STP B-rep or OBJ/STL mesh files, or drag them into the viewport to start the 3D workspace."
        : !HasUsableSourceModelAsset
            ? "This restored workspace is view-only until you re-import the model or reopen a .stch with embedded 3D data."
            : "Append more STEP/STP or OBJ/STL source files to the current workspace, or drag them into the viewport.";

    public string BodyInventorySummary => Bodies.Count switch
    {
        0 => "No bodies loaded",
        _ when HiddenBodyCount == 0 => Bodies.Count == 1
            ? "1 body loaded, visible"
            : $"{Bodies.Count} bodies loaded, all visible",
        _ => $"{Bodies.Count} bodies loaded, {VisibleBodyCount} visible / {HiddenBodyCount} hidden",
    };

    public int VisibleBodyCount => Bodies.Count(body => body.Visible);

    public int HiddenBodyCount => Bodies.Count - VisibleBodyCount;

    public string VisibleBodyScopeSummary => Bodies.Count switch
    {
        0 => "No visible 3D bodies yet.",
        _ when HiddenBodyCount == 0 => VisibleBodyCount == 1
            ? "1 body is visible in the workspace."
            : $"{VisibleBodyCount} bodies are visible in the workspace.",
        _ => $"{VisibleBodyCount} visible, {HiddenBodyCount} hidden.",
    };

    public IReadOnlyList<Body3D> Bodies
    {
        get => _threeDWorkspace.Bodies;
        private set
        {
            if (!_threeDWorkspace.SetBodies(value))
                return;

            OnPropertyChanged();

            SyncSidebarToolStates();
            OnPropertyChanged(nameof(CanUnfoldEntireBody));
            OnPropertyChanged(nameof(HasLoadedModel));
            OnPropertyChanged(nameof(HasNoLoadedModel));
            OnPropertyChanged(nameof(ShowViewportEmptyState));
            OnPropertyChanged(nameof(CanFrameHome));
            OnPropertyChanged(nameof(SelectedBodySummary));
            OnPropertyChanged(nameof(MovedBodyOffsets));
            OnPropertyChanged(nameof(SourceModelActionLabel));
            OnPropertyChanged(nameof(SourceModelWorkflowHint));
            OnPropertyChanged(nameof(SourceModelAssetStatusSummary));
            OnPropertyChanged(nameof(BodyInventorySummary));
            OnPropertyChanged(nameof(VisibleBodyCount));
            OnPropertyChanged(nameof(HiddenBodyCount));
            OnPropertyChanged(nameof(VisibleBodyScopeSummary));
            OnPropertyChanged(nameof(ProjectionToolHint));
            OnPropertyChanged(nameof(ProjectionBodyScopeSummary));
            OnPropertyChanged(nameof(HasProjectionSourceBodies));
            OnPropertyChanged(nameof(ShowProjectionStartAction));
            OnPropertyChanged(nameof(CanStartPlaneSelection));
            OnPropertyChanged(nameof(ProjectionSelectionSummary));
            OnPropertyChanged(nameof(SelectedProjectionFaceType));
            OnPropertyChanged(nameof(IsSelectedProjectionFacePlanar));
            OnPropertyChanged(nameof(ProjectionFaceRequirementSummary));
            OnPropertyChanged(nameof(CanEditProjectionOffset));
            OnPropertyChanged(nameof(CanConfirmProjection));
            OnPropertyChanged(nameof(WorkspaceModeHint));
            OnPropertyChanged(nameof(CanUseOpenGeometrySeparateFlattenWholeBody));
            OnPropertyChanged(nameof(CanUseLiveRecompute));
            OnPropertyChanged(nameof(CanUnfoldEntireBody));
            OnPropertyChanged(nameof(SeparateFlattenWholeBodySummary));
            OnPropertyChanged(nameof(WholeBodyActionExecutionSummary));
            OnPropertyChanged(nameof(UnfoldHintText));
            OnPropertyChanged(nameof(CanRefreshActiveUnfoldPreview));
            OnPropertyChanged(nameof(UnfoldPreviewScopeSummary));
        }
    }

    public async Task OpenSourceModelAsync(CancellationToken cancellationToken = default)
    {
        var sourceModelPaths = await _projectFileDialogService
            .PickSourceModelFilesAsync(cancellationToken)
            .ConfigureAwait(true);

        await OpenSourceModelsAsync(sourceModelPaths, cancellationToken).ConfigureAwait(true);
    }

    public async Task OpenSourceModelsAsync(
        IReadOnlyList<string> sourceModelPaths,
        CancellationToken cancellationToken = default)
    {
        var normalizedSourceModelPaths = NormalizeSourceModelPaths(sourceModelPaths);
        if (normalizedSourceModelPaths.Count == 0)
            return;

        var unsupportedPaths = normalizedSourceModelPaths
            .Where(path => !SupportedSourceModelExtensions.Contains(Path.GetExtension(path)))
            .ToArray();
        if (unsupportedPaths.Length > 0)
        {
            StatusText = "Only 3D source model files can be imported here";
            ViewportStateText = "Unsupported source model drop";
            ErrorMessage = "The editor accepts .step, .stp, .obj, and .stl 3D source files.";
            return;
        }

        IsLoading = true;
        var isAppendingToWorkspace = !string.IsNullOrWhiteSpace(_sourceModelPath) && Bodies.Count > 0;
        StatusText = isAppendingToWorkspace
            ? "Appending 3D source model"
            : "Loading 3D source model";
        ViewportStateText = $"Preparing {normalizedSourceModelPaths.Count} source model(s)";
        ErrorMessage = null;

        try
        {
            var result = await _threeDWorkspace
                .LoadModelsAsync(normalizedSourceModelPaths, _sourceModelPath, cancellationToken)
                .ConfigureAwait(true);

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.ViewportJson) || result.Bodies is null)
            {
                StatusText = isAppendingToWorkspace
                    ? "3D source model append failed"
                    : "3D source model load failed";
                ViewportStateText = result.Message;
                ErrorMessage = result.Message;
                return;
            }

            await ApplyLoadedSourceModelsAsync(
                result,
                isAppendingToWorkspace,
                isAppendingToWorkspace
                    ? "3D source model appended"
                    : "3D source model loaded",
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPendingSourceModelsAsync(Project3DState existingState, CancellationToken cancellationToken)
    {
        if (_pendingSourceModelPaths.Count == 0)
            return;

        var isAppendingToWorkspace = !string.IsNullOrWhiteSpace(existingState.SourceModelPath)
            && existingState.Bodies.Count > 0;

        StatusText = isAppendingToWorkspace
            ? "Appending imported 3D models"
            : "Importing 3D models";
        ViewportStateText = $"Preparing {_pendingSourceModelPaths.Count} source model(s)";

        var result = await _threeDWorkspace
            .LoadModelsAsync(_pendingSourceModelPaths, existingState.SourceModelPath, cancellationToken)
            .ConfigureAwait(true);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.ViewportJson) || result.Bodies is null)
        {
            StatusText = "3D model import failed";
            ViewportStateText = result.Message;
            ErrorMessage = result.Message;
            return;
        }

        await ApplyLoadedSourceModelsAsync(result, isAppendingToWorkspace, result.Message, cancellationToken).ConfigureAwait(true);
    }

    private Task ApplyLoadedSourceModelsAsync(
        EditorModelLoadResult result,
        bool isAppendingToWorkspace,
        string successStatusText,
        CancellationToken cancellationToken)
    {
        var bodies = PreserveExistingBodyVisibility(result.Bodies!, isAppendingToWorkspace);

        SetSourceModelPath(result.SourceModelPath);
        _stepTopology = result.StepTopology;
        ViewportJsonContent = result.ViewportJson;
        SetDistortionData(string.Empty);
        ClearTwoDState();
        ActiveEditorMode = EditorMode.ThreeD;
        ResetProjectionSelection();
        IsPlaneSelectionActive = IsPlaneToolActive;
        LiveRecomputeEnabled = false;
        SetUnfoldPreviewScope(false, requestPersistence: false, requestLiveRecompute: false);
        Bodies = bodies;
        if (!isAppendingToWorkspace)
        {
            ClearBodyMoveHistory();
            BodyOffsets = [];
            BodyOffsetCount = 0;
        }
        else
        {
            var knownBodyIndices = bodies.Select(x => x.BodyIndex).ToHashSet();
            BodyOffsets = BodyOffsets
                .Where(x => knownBodyIndices.Contains(x.BodyIndex))
                .OrderBy(x => x.BodyIndex)
                .ToArray();
            BodyOffsetCount = BodyOffsets.Count;
        }

        SelectedFaces = [];
        SelectedFaceDetails = [];
        SelectedBodyIndex = null;
        SelectionSummary = "No selection";
        SelectedFaceCount = 0;

        StatusText = successStatusText;
        ViewportStateText = result.Message;
        RequestViewportScript(BuildLoadModelScript(result.ViewportJson!));
        RequestBodyVisibilityStateSync();
        RequestBodyMoveStateSync();
        OnPropertyChanged(nameof(ProjectionToolHint));

        Request3DStatePersistence();
        return Task.CompletedTask;
    }

    private static IReadOnlyList<string> NormalizeSourceModelPaths(IReadOnlyList<string> sourceModelPaths)
        => sourceModelPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<Body3D> PreserveExistingBodyVisibility(IReadOnlyList<Body3D> bodies, bool isAppendingToWorkspace)
    {
        if (!isAppendingToWorkspace || Bodies.Count == 0)
            return bodies;

        var visibilityByBodyIndex = Bodies.ToDictionary(body => body.BodyIndex, body => body.Visible);
        return bodies
            .Select(body => visibilityByBodyIndex.TryGetValue(body.BodyIndex, out var visible)
                ? body with { Visible = visible }
                : body)
            .ToArray();
    }
}
