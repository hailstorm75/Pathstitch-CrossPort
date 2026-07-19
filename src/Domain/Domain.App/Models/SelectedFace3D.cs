namespace Domain.App.Models;

public sealed record SelectedFace3D(
    int BodyIndex,
    int FaceIndex,
    string? BodyId = null,
    string? FaceId = null);
