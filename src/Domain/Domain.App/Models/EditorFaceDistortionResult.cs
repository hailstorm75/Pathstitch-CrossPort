namespace Domain.App.Models;

public sealed record EditorFaceDistortionResult(
    bool IsSuccess,
    string Message,
    string? DistortionJson = null,
    GeometryKernelFailure? Failure = null);
