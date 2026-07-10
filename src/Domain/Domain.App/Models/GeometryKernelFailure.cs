namespace Domain.App.Models;

public enum GeometryKernelOperation
{
    Import,
    Projection,
    Unfold,
    Distortion,
}

public enum GeometryKernelFailureCode
{
    InvalidInput,
    SourceUnavailable,
    UnsupportedFormat,
    GeometryNotFound,
    BackendFailure,
    ProtocolMismatch,
    Timeout,
    Cancelled,
}

public sealed record GeometryKernelFailure(
    GeometryKernelFailureCode Code,
    GeometryKernelOperation Operation,
    string Message,
    string? Diagnostic = null,
    bool IsRetryable = false);
