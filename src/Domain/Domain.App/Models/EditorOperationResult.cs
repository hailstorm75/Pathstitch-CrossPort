namespace Domain.App.Models;

public sealed record EditorOperationResult(
    bool IsSuccess,
    string Message,
    string? OutputPath = null,
    GeometryKernelFailure? Failure = null,
    StepOperationGeometry? Geometry = null);
