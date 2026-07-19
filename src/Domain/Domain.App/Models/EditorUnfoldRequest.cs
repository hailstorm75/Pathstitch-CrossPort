namespace Domain.App.Models;

public sealed record EditorUnfoldRequest(
    string? SourceModelPath,
    IReadOnlyList<SelectedFace3D> SelectedFaces,
    IReadOnlyList<int> VisibleBodyIndices,
    bool WholeBody,
    string DistortionMode,
    IReadOnlyList<string>? SelectedFaceIds = null,
    IReadOnlyList<string>? VisibleBodyIds = null,
    string SeamControlMode = "auto",
    IReadOnlyList<EditorSeamEdge3D>? ForcedSeams = null,
    IReadOnlyList<EditorSeamEdge3D>? ForbiddenSeams = null,
    SelectedFace3D? AnchorFace = null,
    string SeamDecoration = "none",
    IReadOnlyList<EditorSeamDecoration3D>? SeamDecorations = null,
    string NetLayout = "connected",
    string UnrollMode = "radial",
    string? ExistingDxfPath = null,
    double TabHeight = 5.0,
    double HoleDiameter = 1.0,
    double HoleSpacing = 4.0,
    double HoleMargin = 2.0);
