namespace Domain.App.Models;

public sealed record EditorModelLoadResult(
    bool IsSuccess,
    string Message,
    string? SourceModelPath = null,
    string? ViewportJson = null,
    IReadOnlyList<Body3D>? Bodies = null,
    GeometryKernelFailure? Failure = null,
    StepGeometryDocument? StepTopology = null);
