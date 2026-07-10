namespace Domain.App.Models;

public sealed record EditorProjectionRequest(
    string? SourceModelPath,
    string PlaneType,
    double Offset,
    int? FaceIndex,
    int? FaceBodyIndex,
    IReadOnlyList<int> VisibleBodyIndices,
    IReadOnlyList<BodyOffset3D> BodyOffsets,
    string? FaceId = null,
    IReadOnlyList<string>? VisibleBodyIds = null);
