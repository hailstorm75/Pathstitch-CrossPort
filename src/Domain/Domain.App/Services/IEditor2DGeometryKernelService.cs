using Domain.App.Models;

namespace Domain.App.Services;

public interface IEditor2DGeometryKernelService
{
    async Task<Editor2DGeometryKernelResult> BuildBooleanPathsAsync(
        IReadOnlyList<Editor2DPreviewPath> sourcePaths,
        Editor2DBooleanOperation operation,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return Editor2DGeometryKernelResult.Failure("Boolean operations are not supported by this geometry kernel.");
    }
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

public enum Editor2DBooleanOperation
{
    Union,
    Subtract,
    Intersect,
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
