using System.Globalization;
using Domain.App.Models;
using PlaneSelectionMode = Domain.App.Models.PlaneSelectionModeType;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public bool IsPlaneSelectionActive
    {
        get => _isPlaneSelectionActive;
        private set
        {
            if (!SetWorkspaceFacadeValue(_isPlaneSelectionActive, value, updated => _isPlaneSelectionActive = updated))
                return;

            OnPropertyChanged(nameof(ShowProjectionStartAction));
            OnPropertyChanged(nameof(CanStartPlaneSelection));
            OnPropertyChanged(nameof(CanConfirmProjection));
            OnPropertyChanged(nameof(CanEditProjectionOffset));
            OnPropertyChanged(nameof(IsProjectionOriginMode));
            OnPropertyChanged(nameof(IsProjectionFaceMode));
            OnPropertyChanged(nameof(IsProjectionXYSelected));
            OnPropertyChanged(nameof(IsProjectionXZSelected));
            OnPropertyChanged(nameof(IsProjectionYZSelected));
            OnPropertyChanged(nameof(CanSelectProjectionOriginPlane));
            OnPropertyChanged(nameof(CanUseCurrentSelectionAsProjectionFace));
            OnPropertyChanged(nameof(ProjectionToolHint));
            OnPropertyChanged(nameof(ProjectionOriginPlaneSummary));
            OnPropertyChanged(nameof(ProjectionFaceSelectionActionLabel));
            OnPropertyChanged(nameof(ProjectionFaceSelectionActionHint));
            OnPropertyChanged(nameof(ProjectionFaceRequirementSummary));
            OnPropertyChanged(nameof(ProjectionOffsetHint));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public PlaneSelectionMode PlaneSelectionModeType
    {
        get => _planeSelectionModeType;
        private set
        {
            if (!SetWorkspaceFacadeValue(_planeSelectionModeType, value, updated => _planeSelectionModeType = updated))
                return;

            OnPropertyChanged(nameof(CanConfirmProjection));
            OnPropertyChanged(nameof(CanEditProjectionOffset));
            OnPropertyChanged(nameof(IsProjectionOriginMode));
            OnPropertyChanged(nameof(IsProjectionFaceMode));
            OnPropertyChanged(nameof(IsProjectionXYSelected));
            OnPropertyChanged(nameof(IsProjectionXZSelected));
            OnPropertyChanged(nameof(IsProjectionYZSelected));
            OnPropertyChanged(nameof(CanSelectProjectionOriginPlane));
            OnPropertyChanged(nameof(CanUseCurrentSelectionAsProjectionFace));
            OnPropertyChanged(nameof(ProjectionToolHint));
            OnPropertyChanged(nameof(ProjectionOriginPlaneSummary));
            OnPropertyChanged(nameof(ProjectionFaceSelectionActionLabel));
            OnPropertyChanged(nameof(ProjectionFaceSelectionActionHint));
            OnPropertyChanged(nameof(ProjectionFaceRequirementSummary));
            OnPropertyChanged(nameof(ProjectionOffsetHint));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public bool IsProjectionOriginMode => IsPlaneSelectionActive && PlaneSelectionModeType == PlaneSelectionMode.Origin;

    public bool IsProjectionFaceMode => IsPlaneSelectionActive && PlaneSelectionModeType == PlaneSelectionMode.Face;

    public bool IsProjectionXYSelected => IsProjectionOriginMode && string.Equals(SelectedProjectionPlane, "XY", StringComparison.OrdinalIgnoreCase);

    public bool IsProjectionXZSelected => IsProjectionOriginMode && string.Equals(SelectedProjectionPlane, "XZ", StringComparison.OrdinalIgnoreCase);

    public bool IsProjectionYZSelected => IsProjectionOriginMode && string.Equals(SelectedProjectionPlane, "YZ", StringComparison.OrdinalIgnoreCase);

    public bool ShowProjectionStartAction => !IsPlaneSelectionActive;

    public bool HasProjectionSourceBodies => HasLoadedModel && VisibleBodyCount > 0;

    public bool CanStartPlaneSelection => ShowProjectionStartAction && HasProjectionSourceBodies && HasUsableSourceModelAsset;

    public bool CanSelectProjectionOriginPlane => IsProjectionOriginMode && HasProjectionSourceBodies && HasUsableSourceModelAsset;

    public bool CanUseCurrentSelectionAsProjectionFace
        => IsProjectionFaceMode
           && HasProjectionSourceBodies
           && HasUsableSourceModelAsset
           && SelectedFaceDetails.Count == 1;

    public string? SelectedProjectionPlane
    {
        get => _selectedProjectionPlane;
        private set
        {
            if (!SetWorkspaceFacadeValue(_selectedProjectionPlane, value, updated => _selectedProjectionPlane = updated))
                return;

            OnPropertyChanged(nameof(CanConfirmProjection));
            OnPropertyChanged(nameof(CanEditProjectionOffset));
            OnPropertyChanged(nameof(IsProjectionXYSelected));
            OnPropertyChanged(nameof(IsProjectionXZSelected));
            OnPropertyChanged(nameof(IsProjectionYZSelected));
            OnPropertyChanged(nameof(ProjectionToolHint));
            OnPropertyChanged(nameof(ProjectionSelectionSummary));
            OnPropertyChanged(nameof(ProjectionOriginPlaneSummary));
            OnPropertyChanged(nameof(SelectedProjectionFaceType));
            OnPropertyChanged(nameof(IsSelectedProjectionFacePlanar));
            OnPropertyChanged(nameof(ProjectionFaceRequirementSummary));
            OnPropertyChanged(nameof(ProjectionOffsetHint));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public int? SelectedProjectionFaceIndex
    {
        get => _selectedProjectionFaceIndex;
        private set
        {
            if (!SetWorkspaceFacadeValue(_selectedProjectionFaceIndex, value, updated => _selectedProjectionFaceIndex = updated))
                return;

            OnPropertyChanged(nameof(CanConfirmProjection));
            OnPropertyChanged(nameof(CanEditProjectionOffset));
            OnPropertyChanged(nameof(ProjectionToolHint));
            OnPropertyChanged(nameof(ProjectionSelectionSummary));
            OnPropertyChanged(nameof(SelectedProjectionFaceType));
            OnPropertyChanged(nameof(IsSelectedProjectionFacePlanar));
            OnPropertyChanged(nameof(ProjectionFaceRequirementSummary));
            OnPropertyChanged(nameof(ProjectionOffsetHint));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public int? SelectedProjectionBodyIndex
    {
        get => _selectedProjectionBodyIndex;
        private set
        {
            if (!SetWorkspaceFacadeValue(_selectedProjectionBodyIndex, value, updated => _selectedProjectionBodyIndex = updated))
                return;

            OnPropertyChanged(nameof(CanConfirmProjection));
            OnPropertyChanged(nameof(CanEditProjectionOffset));
            OnPropertyChanged(nameof(ProjectionToolHint));
            OnPropertyChanged(nameof(ProjectionSelectionSummary));
            OnPropertyChanged(nameof(SelectedProjectionFaceType));
            OnPropertyChanged(nameof(IsSelectedProjectionFacePlanar));
            OnPropertyChanged(nameof(ProjectionFaceRequirementSummary));
            OnPropertyChanged(nameof(ProjectionOffsetHint));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public double PlaneOffset
    {
        get => _planeOffset;
        private set
        {
            if (!SetWorkspaceFacadeValue(_planeOffset, value, updated => _planeOffset = updated))
                return;

            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public bool IsPlaneOffsetTextValid
    {
        get => _isPlaneOffsetTextValid;
        private set
        {
            if (!SetWorkspaceFacadeValue(_isPlaneOffsetTextValid, value, updated => _isPlaneOffsetTextValid = updated))
                return;

            OnPropertyChanged(nameof(CanConfirmProjection));
            OnPropertyChanged(nameof(ProjectionOffsetHint));
        }
    }

    public string PlaneOffsetText
    {
        get => _planeOffsetText;
        set
        {
            if (!SetWorkspaceFacadeValue(_planeOffsetText, value, updated => _planeOffsetText = updated))
                return;

            if (TryParsePlaneOffsetText(value, out var parsed))
            {
                IsPlaneOffsetTextValid = true;
                PlaneOffset = parsed;
                UpdateProjectionSummary();
                RequestPlaneSelectionStateSync();
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
                return;
            }

            IsPlaneOffsetTextValid = false;
        }
    }

    public string ProjectionSelectionSummary
    {
        get => _projectionSelectionSummary;
        private set => SetWorkspaceFacadeValue(_projectionSelectionSummary, value, updated => _projectionSelectionSummary = updated);
    }

    public string ProjectionOriginPlaneSummary => !IsProjectionOriginMode
        ? string.Empty
        : string.IsNullOrWhiteSpace(SelectedProjectionPlane)
            ? "No origin plane selected yet."
            : $"{SelectedProjectionPlane} origin plane ready.";

    public string ProjectionFaceSelectionActionLabel => CanUseCurrentSelectionAsProjectionFace
        ? $"Use {SelectionSummary}"
        : "Use Selected Face";

    public string ProjectionFaceSelectionActionHint => !IsProjectionFaceMode
        ? string.Empty
        : SelectedFaceDetails.Count switch
        {
            0 => "Select one mesh face from the body list or viewport, then use it as the projection plane.",
            1 => "Use the current single-face selection as the projection plane.",
            _ => "Projection from a face requires exactly one selected face.",
        };

    public string? SelectedProjectionFaceType => TryGetSelectedProjectionFaceDetails(out var face)
        ? face.FaceType
        : null;

    public bool IsSelectedProjectionFacePlanar
        => string.Equals(SelectedProjectionFaceType, "Plane", StringComparison.OrdinalIgnoreCase)
           || string.Equals(SelectedProjectionFaceType, "Mesh", StringComparison.OrdinalIgnoreCase);

    public bool CanEditProjectionOffset
        => HasUsableSourceModelAsset
           && HasProjectionSourceBodies
           && IsPlaneSelectionActive
           && (PlaneSelectionModeType == PlaneSelectionMode.Face
               ? string.Equals(SelectedProjectionPlane, "face", StringComparison.OrdinalIgnoreCase)
                 && SelectedProjectionFaceIndex is not null
                 && SelectedProjectionBodyIndex is not null
                 && IsSelectedProjectionFacePlanar
               : !string.IsNullOrWhiteSpace(SelectedProjectionPlane));

    public bool CanConfirmProjection => CanEditProjectionOffset && IsPlaneOffsetTextValid;

    public string ProjectionToolHint => !HasLoadedModel
        ? "Load one or more source models before creating a projection sketch."
        : !HasUsableSourceModelAsset
            ? "This restored 3D workspace is view-only. Re-import the model or reopen a .stch with embedded 3D data to create projection sketches."
        : VisibleBodyCount == 0
            ? "Make at least one body visible before creating a projection sketch."
        : PlaneSelectionModeType == PlaneSelectionMode.Face
            ? SelectedProjectionFaceIndex is null || SelectedProjectionBodyIndex is null
                ? "Pick one mesh face from the viewport or face list, adjust the offset if needed, then confirm the projection."
                : IsSelectedProjectionFacePlanar
                    ? "The selected face defines the projection plane. Adjust the offset if needed, then confirm the projection."
                    : $"Projection from a face currently requires a mesh or planar face. The selected face is {SelectedProjectionFaceType ?? "unknown"}."
            : "Pick an origin plane, adjust the offset if needed, then confirm the projection.";

    public string ProjectionBodyScopeSummary => VisibleBodyCount switch
    {
        0 when Bodies.Count == 0 => "Projection has no loaded bodies yet.",
        0 => "Projection currently has no visible bodies to process.",
        _ when HiddenBodyCount == 0 => VisibleBodyCount == 1
            ? "Projection uses the 1 visible body."
            : $"Projection uses all {VisibleBodyCount} visible bodies.",
        _ => $"Projection uses {VisibleBodyCount} visible bodies and ignores {HiddenBodyCount} hidden bodies.",
    };

    public string ProjectionFaceRequirementSummary
    {
        get
        {
            if (!IsProjectionFaceMode)
                return "Face-based projection uses a selected 3D source face.";

            if (SelectedProjectionFaceIndex is null || SelectedProjectionBodyIndex is null)
                return "No face selected yet. Choose one mesh face to enable projection.";

            return IsSelectedProjectionFacePlanar
                ? $"Projection face ready: {SelectedProjectionFaceType}."
                : $"The current face is {SelectedProjectionFaceType ?? "unknown"} and cannot define a projection plane.";
        }
    }

    public string ProjectionOffsetHint => !CanEditProjectionOffset
        ? HasUsableSourceModelAsset
            ? "Select a usable origin plane or mesh face before adjusting the offset."
            : "Projection requires a 3D source asset. Re-import the model or reopen a .stch with embedded 3D data."
        : !IsPlaneOffsetTextValid
            ? "Enter a valid numeric offset in millimeters."
            : "Positive values move along the projection normal; negative values move opposite.";

    public void SetPlaneSelectionMode(string mode)
        => SetPlaneSelectionMode(ParsePlaneSelectionModeType(mode));

    public void SetPlaneSelectionMode(PlaneSelectionMode mode)
    {
        if (PlaneSelectionModeType == mode)
            return;

        ResetProjectionSelection(mode);
        if (mode == PlaneSelectionMode.Face)
            TryInitializeProjectionFaceFromCurrentSelection();
        RequestPlaneSelectionStateSync();
    }

    public void SelectProjectionOriginPlane(string planeName)
    {
        if (!CanSelectProjectionOriginPlane)
            return;

        var normalizedPlane = NormalizeProjectionOriginPlane(planeName);
        if (normalizedPlane is null)
            return;

        PlaneSelectionModeType = PlaneSelectionMode.Origin;
        SelectedProjectionPlane = normalizedPlane;
        SelectedProjectionFaceIndex = null;
        SelectedProjectionBodyIndex = null;
        UpdateProjectionSummary();
        RequestPlaneSelectionStateSync();
        StatusText = $"Projection plane selected: {normalizedPlane}";
    }

    public void UseCurrentSelectionAsProjectionFace()
    {
        if (!CanUseCurrentSelectionAsProjectionFace)
            return;

        PlaneSelectionModeType = PlaneSelectionMode.Face;
        TryInitializeProjectionFaceFromCurrentSelection();
        RequestPlaneSelectionStateSync();

        StatusText = IsSelectedProjectionFacePlanar
            ? $"Projection face selected: {SelectionSummary}"
            : $"Projection face must be a mesh or planar face. Current face is {SelectedProjectionFaceType ?? "unknown"}.";
    }

    public void CancelPlaneSelection()
    {
        ResetProjectionSelection();
        ActivateSelectTool();
    }

    public void ConfirmPlaneProjection()
    {
        if (!CanConfirmProjection)
            return;

        StatusText = "Projection camera animation requested";
        RequestViewportScript("animateCameraToPlane();");
    }

    private async Task ExecuteProjectionAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConfirmProjection)
            return;

        CancelLiveRecompute();
        IsLoading = true;
        StatusText = "Projection running";
        ErrorMessage = null;

        try
        {
            var existingDxfPath = await StageExistingTwoDDocumentAsync(cancellationToken).ConfigureAwait(true);
            EditorOperationResult result;
            try
            {
                result = await _threeDWorkspace.ProjectAsync(
                    BuildProjectionRequest(existingDxfPath),
                    cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                DeleteStagedTwoDDocument(existingDxfPath);
            }

            StatusText = result.IsSuccess ? "Projection completed" : "Projection failed";
            ViewportStateText = result.Message;
            ErrorMessage = result.IsSuccess ? null : result.Message;

            if (result.IsSuccess)
            {
                await HandleSuccessfulGeneratedOutputAsync(
                    result.OutputPath,
                    BuildProjectionOutputContext(),
                    cancellationToken).ConfigureAwait(true);
                RecordActivity("Project 3D Geometry", ProjectionSelectionSummary);
                IsPlaneSelectionActive = false;
                ResetProjectionSelection();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string GetProjectionPlaneTypeValue() => string.Equals(SelectedProjectionPlane, "face", StringComparison.OrdinalIgnoreCase)
        ? "face"
        : SelectedProjectionPlane ?? "XY";

    private EditorProjectionRequest BuildProjectionRequest(string? existingDxfPath)
        => new(
            SourceModelPath: _sourceModelPath,
            PlaneType: GetProjectionPlaneTypeValue(),
            Offset: PlaneOffset,
            FaceIndex: SelectedProjectionFaceIndex,
            FaceBodyIndex: SelectedProjectionBodyIndex,
            VisibleBodyIndices: Bodies.Where(x => x.Visible).Select(x => x.BodyIndex).ToArray(),
            BodyOffsets: BodyOffsets,
            FaceId: SelectedProjectionBodyIndex is { } bodyIndex && SelectedProjectionFaceIndex is { } faceIndex
                ? CreateSelectedFace(bodyIndex, faceIndex).FaceId
                : null,
            VisibleBodyIds: Bodies.Where(x => x.Visible)
                .Select(body => _stepTopology is not null && body.BodyIndex >= 0 && body.BodyIndex < _stepTopology.Bodies.Count
                    ? _stepTopology.Bodies[body.BodyIndex].Id
                    : null)
                .Where(id => id is not null)
                .Cast<string>()
                .ToArray(),
            ExistingDxfPath: existingDxfPath);

    private string BuildSetPlaneSelectionStateScript()
    {
        var selectedPlane = EscapeForJavaScriptString(SelectedProjectionPlane ?? string.Empty);
        var faceIndex = SelectedProjectionFaceIndex ?? -1;
        var bodyIndex = SelectedProjectionBodyIndex ?? -1;
        var offset = PlaneOffset.ToString(CultureInfo.InvariantCulture);
        return $"setPlaneSelectionState({(IsPlaneSelectionActive ? "true" : "false")}, '{GetPlaneSelectionModeValue(PlaneSelectionModeType)}', '{selectedPlane}', {faceIndex}, {bodyIndex}, {offset});";
    }

    private EditorProjectionWorkspaceState BuildPersistedProjectionWorkspaceState()
        => new(
            GetPlaneSelectionModeValue(PlaneSelectionModeType),
            SelectedProjectionPlane,
            SelectedProjectionFaceIndex,
            SelectedProjectionBodyIndex,
            PlaneOffset);

    private void ApplyPersistedProjectionWorkspaceState(EditorProjectionWorkspaceState? state)
    {
        if (state is null)
        {
            ResetProjectionSelection();
            return;
        }

        PlaneSelectionModeType = ParsePlaneSelectionModeType(state.PlaneSelectionModeType);
        SelectedProjectionPlane = state.SelectedProjectionPlane;
        SelectedProjectionFaceIndex = state.SelectedProjectionFaceIndex;
        SelectedProjectionBodyIndex = state.SelectedProjectionBodyIndex;
        PlaneOffset = state.PlaneOffset;
        IsPlaneOffsetTextValid = true;
        PlaneOffsetText = state.PlaneOffset.ToString("0.###", CultureInfo.InvariantCulture);

        if (PlaneSelectionModeType == PlaneSelectionMode.Face)
        {
            if (SelectedProjectionBodyIndex is null
                || SelectedProjectionFaceIndex is null
                || !TryGetSelectedProjectionFaceDetails(out _))
            {
                SelectedProjectionPlane = null;
                SelectedProjectionFaceIndex = null;
                SelectedProjectionBodyIndex = null;
            }
            else
            {
                SelectedProjectionPlane = "face";
            }
        }
        else if (!string.Equals(SelectedProjectionPlane, "XY", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(SelectedProjectionPlane, "XZ", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(SelectedProjectionPlane, "YZ", StringComparison.OrdinalIgnoreCase))
        {
            SelectedProjectionPlane = null;
        }

        UpdateProjectionSummary();
    }

    private void ResetProjectionSelection(PlaneSelectionMode mode = PlaneSelectionMode.Origin)
    {
        PlaneSelectionModeType = mode;
        SelectedProjectionPlane = null;
        SelectedProjectionFaceIndex = null;
        SelectedProjectionBodyIndex = null;
        PlaneOffset = 0.0;
        IsPlaneOffsetTextValid = true;
        PlaneOffsetText = "0";
        UpdateProjectionSummary();
        OnPropertyChanged(nameof(ProjectionToolHint));
    }

    private void RequestPlaneSelectionStateSync() => RequestViewportScript(BuildSetPlaneSelectionStateScript());

    private void UpdateProjectionSummary()
    {
        if (!IsPlaneSelectionActive)
        {
            ProjectionSelectionSummary = "Selected: None";
            return;
        }

        if (PlaneSelectionModeType == PlaneSelectionMode.Face)
        {
            ProjectionSelectionSummary = SelectedProjectionFaceIndex is not null && SelectedProjectionBodyIndex is not null
                ? $"Selected Face: B{SelectedProjectionBodyIndex + 1} : F{SelectedProjectionFaceIndex} ({SelectedProjectionFaceType ?? "Unknown"})"
                : "Selected: None";
        }
        else
        {
            ProjectionSelectionSummary = string.IsNullOrWhiteSpace(SelectedProjectionPlane)
                ? "Selected: None"
                : $"Selected: {SelectedProjectionPlane} Plane";
        }
    }

    private void SyncProjectionFaceSelectionFromCurrentSelection()
    {
        if (!IsPlaneSelectionActive || PlaneSelectionModeType != PlaneSelectionMode.Face)
            return;

        if (SelectedFaces.Count != 1)
        {
            SelectedProjectionPlane = null;
            SelectedProjectionFaceIndex = null;
            SelectedProjectionBodyIndex = null;
            UpdateProjectionSummary();
            RequestPlaneSelectionStateSync();
            return;
        }

        var selected = SelectedFaces[0];
        SelectedProjectionPlane = "face";
        SelectedProjectionFaceIndex = selected.FaceIndex;
        SelectedProjectionBodyIndex = selected.BodyIndex;
        UpdateProjectionSummary();
        RequestPlaneSelectionStateSync();
    }

    private void TryInitializeProjectionFaceFromCurrentSelection()
    {
        if (PlaneSelectionModeType != PlaneSelectionMode.Face || SelectedFaces.Count != 1)
            return;

        var selected = SelectedFaces[0];
        SelectedProjectionPlane = "face";
        SelectedProjectionFaceIndex = selected.FaceIndex;
        SelectedProjectionBodyIndex = selected.BodyIndex;
        UpdateProjectionSummary();
    }

    private bool HasProjectionWorkspaceSetup()
        => !string.IsNullOrWhiteSpace(SelectedProjectionPlane)
           || SelectedProjectionFaceIndex is not null
           || SelectedProjectionBodyIndex is not null
           || Math.Abs(PlaneOffset) > 1e-9;

    private void EnsureProjectionWorkspaceInitializedForPlaneTool()
    {
        if (!HasProjectionWorkspaceSetup())
        {
            PlaneSelectionModeType = SelectedFaces.Count == 1 ? PlaneSelectionMode.Face : PlaneSelectionMode.Origin;
            if (PlaneSelectionModeType == PlaneSelectionMode.Face)
                TryInitializeProjectionFaceFromCurrentSelection();
        }

        UpdateProjectionSummary();
    }

    private static PlaneSelectionMode ParsePlaneSelectionModeType(string? value)
        => string.Equals(value, "face", StringComparison.OrdinalIgnoreCase)
            ? PlaneSelectionMode.Face
            : PlaneSelectionMode.Origin;

    private static string GetPlaneSelectionModeValue(PlaneSelectionMode mode)
        => mode == PlaneSelectionMode.Face ? "face" : "origin";

    private static string? NormalizeProjectionOriginPlane(string? planeName)
        => planeName?.ToUpperInvariant() switch
        {
            "XY" => "XY",
            "XZ" or "ZX" => "XZ",
            "YZ" or "ZY" => "YZ",
            _ => null,
        };

    private static bool TryParsePlaneOffsetText(string rawValue, out double value)
    {
        value = 0.0;
        return !string.IsNullOrWhiteSpace(rawValue)
               && (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   || double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out value));
    }

    private EditorGeneratedOutputContext BuildProjectionOutputContext()
        => new(
            SourceTool: "Projection Sketch",
            TriggerLabel: "Confirm Projection",
            ScopeSummary: ProjectionBodyScopeSummary,
            ConfigurationSummary: string.IsNullOrWhiteSpace(SelectedProjectionPlane)
                ? "Projection plane: not captured."
                : $"{ProjectionSelectionSummary} / Offset {PlaneOffset.ToString("0.###", CultureInfo.InvariantCulture)} mm",
            CreatedUtc: DateTimeOffset.UtcNow);

    private bool TryGetSelectedProjectionFaceDetails(out SelectedFaceDetails face)
    {
        face = default!;
        if (SelectedProjectionBodyIndex is null || SelectedProjectionFaceIndex is null)
            return false;

        var body = Bodies.FirstOrDefault(x => x.BodyIndex == SelectedProjectionBodyIndex.Value && x.Visible);
        var selectedFace = body?.Faces.FirstOrDefault(x => x.FaceIndex == SelectedProjectionFaceIndex.Value);
        if (body is null || selectedFace is null)
            return false;

        face = new SelectedFaceDetails(body.BodyIndex, body.Name, selectedFace.FaceIndex, selectedFace.Type, selectedFace.Area);
        return true;
    }
}
