namespace Domain.App.Models;

public sealed record Project3DState(
    string? ViewportJson,
    IReadOnlyList<Body3D> Bodies,
    IReadOnlyList<BodyOffset3D> BodyOffsets,
    string? SourceModelPath = null,
    string? GeneratedOutputPath = null,
    string? GeneratedOutputDataBase64 = null,
    EditorGeneratedOutputContext? GeneratedOutputContext = null,
    EditorUnfoldWorkspaceState? UnfoldWorkspaceState = null,
    EditorProjectionWorkspaceState? ProjectionWorkspaceState = null,
    EditorWorkspaceState? WorkspaceState = null,
    Editor2DWorkspaceState? TwoDWorkspaceState = null,
    Editor3DWorkspaceState? ThreeDWorkspaceState = null,
    StepGeometryDocument? StepTopology = null,
    EditorBatchWorkspaceState? BatchWorkspaceState = null)
{
    public static Project3DState Empty { get; } = new(null, [], [], null, null, null, null, null, null, null, Editor2DWorkspaceState.Empty);

    public bool HasModel => !string.IsNullOrWhiteSpace(ViewportJson);

    public bool HasGeneratedOutput => !string.IsNullOrWhiteSpace(GeneratedOutputPath);
}
