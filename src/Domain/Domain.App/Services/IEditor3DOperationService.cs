using Domain.App.Models;

namespace Domain.App.Services;

public interface IEditor3DOperationService
{
    Task<EditorModelLoadResult> LoadModelAsync(string sourceModelPath, CancellationToken cancellationToken = default);

    Task<EditorModelLoadResult> LoadModelsAsync(
        IReadOnlyList<string> sourceModelPaths,
        string? existingSourceModelPath = null,
        CancellationToken cancellationToken = default);

    Task<EditorFaceDistortionResult> ComputeFaceDistortionAsync(
        string? sourceModelPath,
        SelectedFace3D selectedFace,
        string distortionMode,
        CancellationToken cancellationToken = default);

    Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default);

    Task<EditorOperationResult> ProjectEdgesAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default);
}
