using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public bool CanUseNativeSeparateFlattenSelected
        => HasUsableSourceModelAsset
           && SelectedFaceDetails.Count > 0;

    public bool CanUseNativeSeparateFlattenWholeBody
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
            ? "Separate Pieces runs natively through OpenCASCADE .NET."
            : "Separate Pieces requires a native 3D source asset. Re-import the model or reopen a .stch with embedded 3D data.";

    public string SeparateFlattenSelectionSummary
    {
        get
        {
            if (!HasUsableSourceModelAsset)
                return "Native flattening is unavailable until the 3D source asset is restored.";

            if (SelectedFaceDetails.Count == 0)
                return "Select one or more faces to see whether the current flatten action stays native.";

            return $"{SelectedFaceDetails.Count} selected face(s) will flatten natively as separate pieces.";
        }
    }

    public string SeparateFlattenWholeBodySummary
    {
        get
        {
            if (!HasUsableSourceModelAsset)
                return Bodies.Count == 0
                    ? "Load a model to enable whole-body flattening."
                    : "Whole-body flattening is unavailable until the 3D source asset is restored.";

            var visibleBodies = Bodies.Where(body => body.Visible).ToArray();
            var totalFaceCount = visibleBodies.Sum(body => body.Faces.Count);
            if (totalFaceCount == 0)
                return Bodies.Count == 0
                    ? "Load a model to enable whole-body flattening."
                    : "Make at least one body visible to enable whole-body flattening.";

            return visibleBodies.Length == Bodies.Count
                ? $"{totalFaceCount} face(s) across the visible bodies are available for native separate-piece flattening."
                : $"{totalFaceCount} face(s) across {visibleBodies.Length} visible body/bodies are available for native separate-piece flattening.";
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

            return "Flatten Selected will run through native OpenCASCADE .NET.";
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

            return "Flatten Entire Body will run through native OpenCASCADE .NET.";
        }
    }

    public string UnfoldConfigurationSummary => $"Native separate pieces / {GetDistortionModeLabel()}";

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
                    return "Whole-body preview requires a native 3D source asset.";

                return VisibleBodyCount == 0
                    ? "Make at least one body visible to preview whole-body flattening."
                    : $"{VisibleBodyCount} visible body/bodies will refresh the preview as separate pieces.";
            }

            if (!HasUsableSourceModelAsset)
                return "Selected-face preview requires a native 3D source asset.";

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
            NetLayoutIndex: 1,
            DistortionModeIndex,
            UnrollModeIndex: 0,
            GlobalSeamDecorationIndex: 0,
            SeamControlModeIndex: 0,
            LiveRecomputeEnabled,
            WholeBodyRecompute: _wholeBodyRecompute,
            GlueTabHeightText: "5",
            HoleDiameterText: "1",
            HoleSpacingText: "4",
            HoleMarginText: "2");

    private void ApplyPersistedUnfoldWorkspaceState(EditorUnfoldWorkspaceState? state)
    {
        if (state is null)
            return;

        var nativeState = state.NormalizeForNativeEditor();
        DistortionModeIndex = nativeState.DistortionModeIndex;
        SetUnfoldPreviewScope(
            nativeState.WholeBodyRecompute && CanUnfoldEntireBody,
            requestPersistence: false,
            requestLiveRecompute: false);
        LiveRecomputeEnabled = nativeState.LiveRecomputeEnabled && CanUseLiveRecompute;
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
            DistortionMode: GetDistortionModeValue());

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
