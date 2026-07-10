namespace Domain.App.Models;

public sealed record EditorViewportMessage(
    string Operation,
    int? BodyIndex = null,
    int? FaceIndex = null,
    int? EdgeIndex = null,
    string? Plane = null,
    double? Offset = null,
    double? X = null,
    double? Y = null,
    double? Z = null,
    string? RawBody = null,
    string? Error = null,
    bool IsShiftKey = false);
