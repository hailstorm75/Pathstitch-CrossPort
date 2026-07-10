using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public bool DuplicateTwoDSelection(double offsetX = 10.0, double offsetY = 10.0)
    {
        if (TwoDDocument is not { } document || TwoDSelectedPathIds.Count == 0)
            return false;

        var selectedIds = TwoDSelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var copies = document.Paths
            .Where(path => selectedIds.Contains(path.Id))
            .Select(path => TransformPath(
                path,
                point => new Editor2DPoint(point.X + offsetX, point.Y + offsetY),
                $"{path.Id}:copy:{Guid.NewGuid():N}"))
            .ToArray();
        if (copies.Length == 0)
            return false;

        TwoDDocument = CreateUpdatedTwoDDocument(
            document,
            [.. document.Paths, .. copies]);
        TwoDSelectedPathIds = copies.Select(path => path.Id).ToArray();
        StatusText = copies.Length == 1 ? "Duplicated 1 entity" : $"Duplicated {copies.Length} entities";
        return true;
    }

    public bool FlipTwoDSelection(bool horizontal)
    {
        if (TwoDDocument is not { } document || TwoDSelectedPathIds.Count == 0)
            return false;

        var selectedIds = TwoDSelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var selected = document.Paths.Where(path => selectedIds.Contains(path.Id)).ToArray();
        var referencePoints = selected
            .SelectMany(GetPathReferencePoints)
            .ToArray();
        if (referencePoints.Length == 0)
            return false;

        var pivotX = (referencePoints.Min(point => point.X) + referencePoints.Max(point => point.X)) / 2.0;
        var pivotY = (referencePoints.Min(point => point.Y) + referencePoints.Max(point => point.Y)) / 2.0;
        Editor2DPoint Flip(Editor2DPoint point) => horizontal
            ? new Editor2DPoint((2.0 * pivotX) - point.X, point.Y)
            : new Editor2DPoint(point.X, (2.0 * pivotY) - point.Y);

        var nextPaths = document.Paths
            .Select(path => selectedIds.Contains(path.Id) ? TransformPath(path, Flip, path.Id, horizontal) : path)
            .ToArray();
        TwoDDocument = CreateUpdatedTwoDDocument(document, nextPaths);
        TwoDSelectedPathIds = selectedIds.ToArray();
        StatusText = horizontal ? "Flipped selection horizontally" : "Flipped selection vertically";
        return true;
    }

    private static IEnumerable<Editor2DPoint> GetPathReferencePoints(Editor2DPreviewPath path)
    {
        foreach (var point in path.Points)
            yield return point;

        if (path.Start is { } start)
            yield return start;
        if (path.Center is { } center)
            yield return center;
    }

    private static Editor2DPreviewPath TransformPath(
        Editor2DPreviewPath path,
        Func<Editor2DPoint, Editor2DPoint> transform,
        string id,
        bool? horizontalFlip = null)
        => path with
        {
            Id = id,
            Points = path.Points.Select(transform).ToArray(),
            Start = path.Start is null ? null : transform(path.Start),
            Center = path.Center is null ? null : transform(path.Center),
            RotationDegrees = horizontalFlip switch
            {
                true when path.RotationDegrees is { } rotation => 180.0 - rotation,
                false when path.RotationDegrees is { } rotation => -rotation,
                _ => path.RotationDegrees,
            },
        };
}
