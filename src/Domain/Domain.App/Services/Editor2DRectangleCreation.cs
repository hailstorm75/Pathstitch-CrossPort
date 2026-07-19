using Domain.App.Models;

namespace Domain.App.Services;

public sealed record Editor2DRectangleCreation(
    Editor2DPreviewPath Path,
    IReadOnlyList<Editor2DCornerParameter> CornerParameters);

public static class Editor2DRectangleCreationService
{
    private const double MinimumSize = 1e-6;

    public static Editor2DRectangleCreation? Create(
        string pathId,
        Editor2DPoint start,
        Editor2DPoint end,
        double initialFilletRadius = 0.0,
        Editor2DFilletContinuity continuity = Editor2DFilletContinuity.G1)
    {
        if (string.IsNullOrWhiteSpace(pathId)
            || !IsFinite(start)
            || !IsFinite(end)
            || !double.IsFinite(initialFilletRadius))
        {
            return null;
        }

        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);
        if (width <= MinimumSize || height <= MinimumSize)
            return null;

        var sourcePoints = new Editor2DPoint[]
        {
            new(start.X, start.Y),
            new(end.X, start.Y),
            new(end.X, end.Y),
            new(start.X, end.Y),
        };
        var source = new Editor2DPreviewPath(
            pathId.Trim(),
            "LWPOLYLINE",
            sourcePoints,
            IsClosed: true,
            IsAxisAlignedRectangle: true);
        var radius = Math.Clamp(initialFilletRadius, 0.0, Math.Min(width, height) / 2.0);
        if (radius <= MinimumSize)
            return new Editor2DRectangleCreation(source, []);

        var parameters = Enumerable.Range(0, sourcePoints.Length)
            .Select(index => new Editor2DCornerParameter(
                $"{source.Id}:{index}",
                source.Id,
                index,
                Editor2DCornerKind.Fillet,
                radius,
                sourcePoints.ToArray(),
                continuity))
            .ToArray();
        return new Editor2DRectangleCreation(
            Editor2DCornerGeometry.Apply(source, parameters),
            parameters);
    }

    private static bool IsFinite(Editor2DPoint point)
        => double.IsFinite(point.X) && double.IsFinite(point.Y);
}
