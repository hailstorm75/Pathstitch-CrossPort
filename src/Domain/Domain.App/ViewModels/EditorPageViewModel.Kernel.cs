using System;
using System.IO;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public string GeometryKernelDisplayName => _geometryKernel.DisplayName;

    public string GeometryKernelImplementationName => _geometryKernel.ImplementationName;

    public string GeometryKernelRuntimeSummary => _geometryKernel.RuntimeSummary;

    public string GeometryKernelCapabilitySummary => _geometryKernel.CapabilitySummary;

    public string GeometryKernelRequirementSummary => _geometryKernel.RequirementSummary;

    public string GeometryKernelSupportedSourceModelSummary => _geometryKernel.SupportedSourceModelSummary;

    public bool HasUsableSourceModelAsset
        => !string.IsNullOrWhiteSpace(_sourceModelPath)
           && File.Exists(_sourceModelPath)
           && IsOpenGeometrySourceAsset(_sourceModelPath);

    public string SourceModelAssetStatusSummary => !HasLoadedModel
        ? $"{GeometryKernelDisplayName}: ready. Import an OpenGeometry mesh source asset to start the 3D workspace."
        : HasUsableSourceModelAsset
            ? $"{GeometryKernelDisplayName} has an OpenGeometry mesh source asset ready for projection, flattening, and distortion analysis."
            : $"{GeometryKernelDisplayName} is available, but the source asset for this restored workspace is missing. Re-import the model or reopen a .stch with embedded 3D data.";

    private void SetSourceModelPath(string? sourceModelPath)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(sourceModelPath)
            ? null
            : Path.GetFullPath(sourceModelPath);

        if (string.Equals(_sourceModelPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            return;

        _sourceModelPath = normalizedPath;
        OnPropertyChanged(nameof(SourceModelWorkflowHint));
        OnPropertyChanged(nameof(HasUsableSourceModelAsset));
        OnPropertyChanged(nameof(SourceModelAssetStatusSummary));
        OnPropertyChanged(nameof(CanInspectFaceDistortion));
        OnPropertyChanged(nameof(CanRefreshFaceDistortion));
        OnPropertyChanged(nameof(FaceDistortionInspectorHint));
        OnPropertyChanged(nameof(FaceDistortionStatusText));
        OnPropertyChanged(nameof(FaceDistortionSurfaceBehaviorSummary));
        OnPropertyChanged(nameof(FaceDistortionModeSummary));
        OnPropertyChanged(nameof(CanStartPlaneSelection));
        OnPropertyChanged(nameof(ProjectionToolHint));
        OnPropertyChanged(nameof(CanEditProjectionOffset));
        OnPropertyChanged(nameof(ProjectionOffsetHint));
        OnPropertyChanged(nameof(CanConfirmProjection));
        OnPropertyChanged(nameof(CanUseLiveRecompute));
        OnPropertyChanged(nameof(CanUnfoldSelected));
        OnPropertyChanged(nameof(CanUnfoldEntireBody));
        OnPropertyChanged(nameof(CanRefreshActiveUnfoldPreview));
        OnPropertyChanged(nameof(UnfoldHintText));
        OnPropertyChanged(nameof(UnfoldEngineSummary));
        OnPropertyChanged(nameof(SeparateFlattenSelectionSummary));
        OnPropertyChanged(nameof(SeparateFlattenWholeBodySummary));
        OnPropertyChanged(nameof(UnfoldSelectedExecutionSummary));
        OnPropertyChanged(nameof(WholeBodyActionExecutionSummary));
        OnPropertyChanged(nameof(UnfoldPreviewScopeSummary));
        OnPropertyChanged(nameof(WorkspaceModeHint));
    }

    private static bool IsOpenGeometrySourceAsset(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedSourceModelExtensions.Contains(extension)
               || extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }
}
