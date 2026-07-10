namespace Domain.App.Models;

public sealed record EditorUnfoldRequest(
    string? SourceModelPath,
    IReadOnlyList<SelectedFace3D> SelectedFaces,
    IReadOnlyList<int> VisibleBodyIndices,
    bool WholeBody,
    string DistortionMode,
    IReadOnlyList<string>? SelectedFaceIds = null,
    IReadOnlyList<string>? VisibleBodyIds = null);
