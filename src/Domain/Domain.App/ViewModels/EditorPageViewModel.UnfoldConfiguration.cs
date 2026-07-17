using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public bool CanUseOpenGeometrySeparateFlattenSelected
        => HasUsableSourceModelAsset
           && SelectedFaceDetails.Count > 0;

    public bool CanUseOpenGeometrySeparateFlattenWholeBody
        => HasUsableSourceModelAsset
           && VisibleBodyCount > 0
           && Bodies.Where(body => body.Visible).SelectMany(body => body.Faces).Any();

    public bool CanUseLiveRecompute => HasLoadedModel && HasUsableSourceModelAsset;

    public bool IsSelectedUnfoldPreviewScope => !_wholeBodyRecompute;

    public bool IsWholeBodyUnfoldPreviewScope => _wholeBodyRecompute;

    public bool CanRefreshActiveUnfoldPreview => HasUsableSourceModelAsset
        && (_wholeBodyRecompute ? VisibleBodyCount > 0 : SelectedFaces.Count > 0);

    public string UnfoldModeDescription
        => "Separate Pieces flattens each selected face independently and lays the results out side by side in the 2D output.";

    public string UnfoldEngineSummary => HasUsableSourceModelAsset
            ? "Separate Pieces runs through the active 3D geometry worker."
            : "Separate Pieces requires a 3D source asset. Re-import the model or reopen a .stch with embedded 3D data.";

    public string SeparateFlattenSelectionSummary
    {
        get
        {
            if (!HasUsableSourceModelAsset)
                return "OpenGeometry flattening is unavailable until the mesh source asset is restored.";

            if (SelectedFaceDetails.Count == 0)
                return "Select one or more faces to see whether the current flatten action can run through OpenGeometry.";

            return $"{SelectedFaceDetails.Count} selected face(s) will flatten through the active 3D geometry worker as separate pieces.";
        }
    }

    public string SeparateFlattenWholeBodySummary
    {
        get
        {
            if (!HasUsableSourceModelAsset)
                return Bodies.Count == 0
                    ? "Load a model to enable whole-body flattening."
                    : "Whole-body flattening is unavailable until the mesh source asset is restored.";

            var visibleBodies = Bodies.Where(body => body.Visible).ToArray();
            var totalFaceCount = visibleBodies.Sum(body => body.Faces.Count);
            if (totalFaceCount == 0)
                return Bodies.Count == 0
                    ? "Load a model to enable whole-body flattening."
                    : "Make at least one body visible to enable whole-body flattening.";

            return visibleBodies.Length == Bodies.Count
                ? $"{totalFaceCount} face(s) across the visible bodies are available for OpenGeometry separate-piece flattening."
                : $"{totalFaceCount} face(s) across {visibleBodies.Length} visible body/bodies are available for OpenGeometry separate-piece flattening.";
        }
    }

    public string UnfoldSelectedExecutionSummary
    {
        get
        {
            if (!HasUsableSourceModelAsset)
                return HasLoadedModel
                    ? "Re-import the model or reopen a .stch with embedded 3D data to enable this action."
                    : "Load a model to enable this action.";

            if (SelectedFaces.Count == 0)
                return "Select one or more faces to enable this action.";

        return "Flatten Selected will run through the active 3D geometry worker.";
        }
    }

    public string WholeBodyActionExecutionSummary
    {
        get
        {
            if (!HasUsableSourceModelAsset)
                return HasLoadedModel
                    ? "Re-import the model or reopen a .stch with embedded 3D data to enable this action."
                    : "Load a model to enable this action.";

            if (VisibleBodyCount == 0)
                return Bodies.Count == 0
                    ? "Load a model to enable this action."
                    : "Make at least one body visible to enable this action.";

        return "Flatten Entire Body will run through the active 3D geometry worker.";
        }
    }

    public string UnfoldConfigurationSummary => IsConnectedNetLayout
        ? $"{NetLayoutLabel} / {UnrollModeLabel} / {GetDistortionModeLabel()}"
        : $"{NetLayoutLabel} / {GetDistortionModeLabel()}";

    public bool IsConnectedNetLayout => NetLayoutIndex == 0;

    public bool IsSeparatePiecesLayout => !IsConnectedNetLayout;

    public int NetLayoutIndex
    {
        get => _threeDWorkspace.NetLayoutIndex;
        set
        {
            if (!SetWorkspaceFacadeValue(_threeDWorkspace.NetLayoutIndex, Math.Clamp(value, 0, 1), updated => _threeDWorkspace.SetNetLayoutIndex(updated)))
                return;
            OnPropertyChanged(nameof(NetLayoutLabel));
            OnPropertyChanged(nameof(IsConnectedNetLayout));
            OnPropertyChanged(nameof(IsSeparatePiecesLayout));
            OnPropertyChanged(nameof(UnfoldConfigurationSummary));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
            if (LiveRecomputeEnabled)
                RequestLiveRecompute(TimeSpan.FromMilliseconds(150));
        }
    }

    public int UnrollModeIndex
    {
        get => _threeDWorkspace.UnrollModeIndex;
        set
        {
            if (!SetWorkspaceFacadeValue(_threeDWorkspace.UnrollModeIndex, Math.Clamp(value, 0, 2), updated => _threeDWorkspace.SetUnrollModeIndex(updated)))
                return;
            OnPropertyChanged(nameof(UnrollModeLabel));
            OnPropertyChanged(nameof(UnfoldConfigurationSummary));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
            if (LiveRecomputeEnabled)
                RequestLiveRecompute(TimeSpan.FromMilliseconds(150));
        }
    }

    public string NetLayoutLabel => NetLayoutIndex == 0 ? "Connected Net" : "Separate Pieces";
    public string UnrollModeLabel => UnrollModeIndex switch
    {
        1 => "Strip",
        2 => "Spanning Tree",
        _ => "Radial",
    };

    public int SeamControlModeIndex
    {
        get => _threeDWorkspace.SeamControlModeIndex;
        set
        {
            if (!SetWorkspaceFacadeValue(
                    _threeDWorkspace.SeamControlModeIndex,
                    Math.Clamp(value, 0, 2),
                    updated => _threeDWorkspace.SetSeamControlModeIndex(updated)))
                return;

            OnPropertyChanged(nameof(SeamControlModeLabel));
            RequestSeamControlStateSync();
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
            if (LiveRecomputeEnabled)
                RequestLiveRecompute(TimeSpan.FromMilliseconds(150));
        }
    }

    public string SeamControlModeLabel => SeamControlModeIndex switch
    {
        1 => "Manual (Cuts)",
        2 => "Hybrid (Folds)",
        _ => "Auto",
    };

    public string SeamOverrideSummary => SeamControlModeIndex switch
    {
        1 => $"{ForcedSeams.Count} manual cut(s) selected.",
        2 => $"{ForbiddenSeams.Count} forced fold(s) selected.",
        _ => "Automatic seam selection is active.",
    };

    public bool CanClearActiveSeamOverrides
        => SeamControlModeIndex == 1 ? ForcedSeams.Count > 0
            : SeamControlModeIndex == 2 && ForbiddenSeams.Count > 0;

    public IReadOnlyList<EditorSeamEdge3D> ForcedSeams => _threeDWorkspace.ForcedSeams;

    public IReadOnlyList<EditorSeamEdge3D> ForbiddenSeams => _threeDWorkspace.ForbiddenSeams;

    public int GlobalSeamDecorationIndex
    {
        get => _threeDWorkspace.GlobalSeamDecorationIndex;
        set
        {
            if (!SetWorkspaceFacadeValue(
                    _threeDWorkspace.GlobalSeamDecorationIndex,
                    Math.Clamp(value, 0, 2),
                    updated => _threeDWorkspace.SetGlobalSeamDecorationIndex(updated)))
                return;

            NotifySeamDecorationChanged();
        }
    }

    public string GlobalSeamDecorationLabel => GlobalSeamDecorationIndex switch
    {
        1 => "Glue Tabs",
        2 => "Sew Holes",
        _ => "Plain",
    };

    public string SeamDecorationValue => GlobalSeamDecorationIndex switch
    {
        1 => "tabs",
        2 => "holes",
        _ => "none",
    };

    public SelectedFace3D? AnchorFace => _threeDWorkspace.AnchorFace;

    public string AnchorFaceSummary => AnchorFace is { } anchor
        ? $"Anchor: B{anchor.BodyIndex + 1}:F{anchor.FaceIndex}"
        : "Anchor: automatic (largest face)";

    public bool HasAnchorFace => AnchorFace is not null;

    public IReadOnlyList<string> SeamDecorationOptions { get; } = ["default", "none", "tabs", "holes"];

    public bool CanSetSelectedFaceAsAnchor => SelectedFaces.Count == 1;

    public EditorSeamEdge3D? SelectedSeamEdge => _threeDWorkspace.SelectedSeamEdge;

    public string SelectedSeamDecoration
    {
        get => SelectedSeamEdge is { } edge
            ? _threeDWorkspace.SeamDecorations.FirstOrDefault(item => item.Edge == edge)?.Decoration ?? "default"
            : "default";
        set
        {
            if (SelectedSeamEdge is not { } edge)
                return;

            if (string.Equals(value, "default", StringComparison.OrdinalIgnoreCase))
                _threeDWorkspace.ClearSeamDecoration(edge);
            else
                _threeDWorkspace.SetSeamDecoration(edge, value);
            NotifySeamDecorationChanged();
        }
    }

    public int SelectedSeamDecorationIndex
    {
        get => SelectedSeamDecoration switch
        {
            "none" => 1,
            "tabs" => 2,
            "holes" => 3,
            _ => 0,
        };
        set => SelectedSeamDecoration = value switch
        {
            1 => "none",
            2 => "tabs",
            3 => "holes",
            _ => "default",
        };
    }

    public bool HasSelectedSeamEdge => SelectedSeamEdge is not null;

    public void SetSelectedFaceAsAnchor()
    {
        if (SelectedFaces.Count != 1)
            return;
        _threeDWorkspace.SetAnchorFace(SelectedFaces[0]);
        NotifySeamDecorationChanged();
    }

    public void ClearAnchorFace()
    {
        _threeDWorkspace.SetAnchorFace(null);
        NotifySeamDecorationChanged();
    }

    public void ToggleSeamEdge(int bodyIndex, int edgeIndex)
    {
        _threeDWorkspace.SetSelectedSeamEdge(new EditorSeamEdge3D(bodyIndex, edgeIndex));
        if (SeamControlModeIndex != 0)
            _threeDWorkspace.ToggleSeamEdge(bodyIndex, edgeIndex);
        NotifySeamControlChanged();
        NotifySeamDecorationChanged();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        if (LiveRecomputeEnabled)
            RequestLiveRecompute(TimeSpan.FromMilliseconds(150));
    }

    public void ClearActiveSeamOverrides()
    {
        _threeDWorkspace.ClearActiveSeamOverrides();
        NotifySeamControlChanged();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        if (LiveRecomputeEnabled)
            RequestLiveRecompute(TimeSpan.FromMilliseconds(150));
    }

    public string UnfoldPreviewModeSummary => LiveRecomputeEnabled
        ? _wholeBodyRecompute
            ? "Live recompute refreshes the 2D preview from every visible body."
            : "Live recompute refreshes the 2D preview from the current face selection."
        : "Run Flatten Selected, Flatten Entire Body, or Refresh Preview Now to update the 2D preview.";

    public string UnfoldPreviewRefreshButtonText => _wholeBodyRecompute
        ? "Refresh Whole-Body Preview"
        : "Refresh Selected Preview";

    public string UnfoldPreviewScopeSummary
    {
        get
        {
            if (_wholeBodyRecompute)
            {
                if (!HasUsableSourceModelAsset)
                    return "Whole-body preview requires a 3D source asset.";

                return VisibleBodyCount == 0
                    ? "Make at least one body visible to preview whole-body flattening."
                    : $"{VisibleBodyCount} visible body/bodies will refresh the preview as separate pieces.";
            }

            if (!HasUsableSourceModelAsset)
                    return "Selected-face preview requires a 3D source asset.";

            return SelectedFaces.Count == 0
                ? "Select one or more faces to preview selected-face flattening."
                : $"{SelectedFaces.Count} selected face(s) will refresh the preview as separate pieces.";
        }
    }

    public void SetUnfoldPreviewScope(bool wholeBody, bool requestPersistence = true, bool requestLiveRecompute = true)
    {
        if (_wholeBodyRecompute == wholeBody)
            return;

        _wholeBodyRecompute = wholeBody;
        NotifyUnfoldPreviewScopeChanged();

        if (requestPersistence)
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));

        if (requestLiveRecompute && LiveRecomputeEnabled)
            RequestLiveRecompute(TimeSpan.FromMilliseconds(150));
    }

    private EditorUnfoldWorkspaceState BuildPersistedUnfoldWorkspaceState()
        => new(
            NetLayoutIndex,
            DistortionModeIndex,
            UnrollModeIndex,
            GlobalSeamDecorationIndex,
            SeamControlModeIndex,
            LiveRecomputeEnabled,
            WholeBodyRecompute: _wholeBodyRecompute,
            GlueTabHeightText: "5",
            HoleDiameterText: "1",
            HoleSpacingText: "4",
            HoleMarginText: "2",
            ForcedSeams.Count == 0 ? null : ForcedSeams,
            ForbiddenSeams.Count == 0 ? null : ForbiddenSeams,
            AnchorFace,
            SeamDecorations.Count == 0 ? null : SeamDecorations);

    private void ApplyPersistedUnfoldWorkspaceState(EditorUnfoldWorkspaceState? state)
    {
        if (state is null)
            return;

        var openGeometryState = state.NormalizeForOpenGeometryEditor();
        DistortionModeIndex = openGeometryState.DistortionModeIndex;
        NetLayoutIndex = openGeometryState.NetLayoutIndex;
        UnrollModeIndex = openGeometryState.UnrollModeIndex;
        SeamControlModeIndex = openGeometryState.SeamControlModeIndex;
        _threeDWorkspace.SetForcedSeams(openGeometryState.ForcedSeams ?? []);
        _threeDWorkspace.SetForbiddenSeams(openGeometryState.ForbiddenSeams ?? []);
        _threeDWorkspace.SetGlobalSeamDecorationIndex(openGeometryState.GlobalSeamDecorationIndex);
        _threeDWorkspace.SetAnchorFace(openGeometryState.AnchorFace);
        _threeDWorkspace.SetSeamDecorations(openGeometryState.SeamDecorations ?? []);
        NotifySeamControlChanged();
        NotifySeamDecorationChanged();
        SetUnfoldPreviewScope(
            openGeometryState.WholeBodyRecompute && CanUnfoldEntireBody,
            requestPersistence: false,
            requestLiveRecompute: false);
        LiveRecomputeEnabled = openGeometryState.LiveRecomputeEnabled && CanUseLiveRecompute;
    }

    private string GetDistortionModeLabel() => DistortionModeIndex switch
    {
        0 => "Conformal",
        1 => "Equal-Area",
        2 => "Equidistant",
        3 => "Balanced",
        _ => "Conformal",
    };

    private string GetDistortionModeValue() => DistortionModeIndex switch
    {
        1 => "equal-area",
        2 => "equidistant",
        3 => "balanced",
        _ => "conformal",
    };

    private EditorUnfoldRequest BuildUnfoldRequest(bool wholeBody)
        => new(
            SourceModelPath: _sourceModelPath,
            SelectedFaces: SelectedFaces,
            WholeBody: wholeBody,
            VisibleBodyIndices: Bodies.Where(x => x.Visible).Select(x => x.BodyIndex).ToArray(),
            DistortionMode: GetDistortionModeValue(),
            SelectedFaceIds: SelectedFaces.Where(face => face.FaceId is not null).Select(face => face.FaceId!).ToArray(),
            VisibleBodyIds: Bodies.Where(x => x.Visible)
                .Select(body => _stepTopology is not null && body.BodyIndex >= 0 && body.BodyIndex < _stepTopology.Bodies.Count
                    ? _stepTopology.Bodies[body.BodyIndex].Id
                    : null)
                .Where(id => id is not null)
                .Cast<string>()
                .ToArray(),
            SeamControlMode: GetSeamControlModeValue(),
            ForcedSeams: ForcedSeams,
            ForbiddenSeams: ForbiddenSeams,
            AnchorFace: AnchorFace,
            SeamDecoration: SeamDecorationValue,
            SeamDecorations: SeamDecorations,
            NetLayout: NetLayoutIndex == 0 ? "connected" : "separate",
            UnrollMode: UnrollModeIndex switch
            {
                1 => "strip",
                2 => "spanning",
                _ => "radial",
            });

    private string GetSeamControlModeValue() => SeamControlModeIndex switch
    {
        1 => "manual",
        2 => "hybrid",
        _ => "auto",
    };

    private void NotifySeamControlChanged()
    {
        OnPropertyChanged(nameof(SeamControlModeIndex));
        OnPropertyChanged(nameof(SeamControlModeLabel));
        OnPropertyChanged(nameof(ForcedSeams));
        OnPropertyChanged(nameof(ForbiddenSeams));
        OnPropertyChanged(nameof(SeamOverrideSummary));
        OnPropertyChanged(nameof(CanClearActiveSeamOverrides));
        RequestSeamControlStateSync();
    }

    private IReadOnlyList<EditorSeamDecoration3D> SeamDecorations => _threeDWorkspace.SeamDecorations;

    private void NotifySeamDecorationChanged()
    {
        OnPropertyChanged(nameof(GlobalSeamDecorationIndex));
        OnPropertyChanged(nameof(GlobalSeamDecorationLabel));
        OnPropertyChanged(nameof(SeamDecorationValue));
        OnPropertyChanged(nameof(AnchorFace));
        OnPropertyChanged(nameof(AnchorFaceSummary));
        OnPropertyChanged(nameof(HasAnchorFace));
        OnPropertyChanged(nameof(CanSetSelectedFaceAsAnchor));
        OnPropertyChanged(nameof(SelectedSeamEdge));
        OnPropertyChanged(nameof(SelectedSeamDecoration));
        OnPropertyChanged(nameof(SelectedSeamDecorationIndex));
        OnPropertyChanged(nameof(HasSelectedSeamEdge));
        RequestSeamControlStateSync();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        if (LiveRecomputeEnabled)
            RequestLiveRecompute(TimeSpan.FromMilliseconds(150));
    }

    private void NotifyUnfoldPreviewScopeChanged()
    {
        OnPropertyChanged(nameof(IsSelectedUnfoldPreviewScope));
        OnPropertyChanged(nameof(IsWholeBodyUnfoldPreviewScope));
        OnPropertyChanged(nameof(CanRefreshActiveUnfoldPreview));
        OnPropertyChanged(nameof(UnfoldPreviewModeSummary));
        OnPropertyChanged(nameof(UnfoldPreviewRefreshButtonText));
        OnPropertyChanged(nameof(UnfoldPreviewScopeSummary));
    }
}
