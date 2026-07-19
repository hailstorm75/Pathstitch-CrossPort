namespace Domain.App.Models;

public sealed record Editor3DWorkspaceState(
    Editor3DTool ActiveTool,
    bool Orthographic,
    string? ViewportJson,
    string? SourceModelPath,
    IReadOnlyList<Body3D> Bodies,
    IReadOnlyList<BodyOffset3D> BodyOffsets,
    IReadOnlyList<SelectedFace3D> SelectedFaces,
    IReadOnlyList<SelectedFaceDetails> SelectedFaceDetails,
    int? SelectedBodyIndex,
    EditorProjectionWorkspaceState Projection,
    EditorUnfoldWorkspaceState Unfold)
{
    public static Editor3DWorkspaceState Empty { get; } = new(
        Editor3DTool.Select,
        false,
        null,
        null,
        [],
        [],
        [],
        [],
        null,
        new EditorProjectionWorkspaceState("origin", null, null, null, 0),
        new EditorUnfoldWorkspaceState(0, 0, 0, 0, 0, false, false, "5", "1", "4", "2"));
}
