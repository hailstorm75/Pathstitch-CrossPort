using Domain.App.Models;

namespace Domain.App.Services;

public interface IStepGeometryKernelService
{
    Task<StepGeometryProtocolInfo> HandshakeAsync(CancellationToken cancellationToken = default);

    Task<StepGeometryImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);

    Task<StepGeometryCombineResult> CombineAsync(
        string existingSourcePath,
        string incomingSourcePath,
        CancellationToken cancellationToken = default);

    Task<EditorOperationResult> ProjectAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default);

    Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default);

    Task<EditorFaceDistortionResult> ComputeDistortionAsync(
        string sourcePath,
        SelectedFace3D face,
        string distortionMode,
        CancellationToken cancellationToken = default);
}
