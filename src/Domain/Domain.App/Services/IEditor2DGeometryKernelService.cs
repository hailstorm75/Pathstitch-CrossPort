using Domain.App.Models;

namespace Domain.App.Services;

public interface IEditor2DGeometryKernelService
{
    Task<Editor2DGeometryKernelResult> BuildCurveOffsetPathsAsync(
        IReadOnlyList<Editor2DPreviewPath> sourcePaths,
        double offsetDistance,
        bool offsetOutward,
        CancellationToken cancellationToken = default);

    Task<Editor2DGeometryKernelResult> BuildThicknessOutlinesAsync(
        IReadOnlyList<Editor2DPreviewPath> sourcePaths,
        double thickness,
        CancellationToken cancellationToken = default);
}

public sealed record Editor2DGeometryKernelResult(
    bool IsSuccess,
    IReadOnlyList<Editor2DPreviewPath> Paths,
    string? Error)
{
    public static Editor2DGeometryKernelResult Success(IReadOnlyList<Editor2DPreviewPath> paths)
        => new(true, paths, null);

    public static Editor2DGeometryKernelResult Failure(string error)
        => new(false, [], error);
}
