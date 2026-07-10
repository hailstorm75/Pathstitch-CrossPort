using System;
using System.Collections.Generic;
using System.Linq;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal static class DxfCanvasGeometryEditor
{
    public static Editor2DPoint[] BuildRectangle(Editor2DPoint start, Editor2DPoint end)
        =>
        [
            new(start.X, start.Y),
            new(end.X, start.Y),
            new(end.X, end.Y),
            new(start.X, end.Y),
        ];

    public static Editor2DPoint[] BuildCircle(Editor2DPoint center, double radius, int segments = 48)
        => Enumerable.Range(0, Math.Max(segments, 3))
            .Select(index => Math.PI * 2.0 * index / Math.Max(segments, 3))
            .Select(angle => new Editor2DPoint(
                center.X + (radius * Math.Cos(angle)),
                center.Y + (radius * Math.Sin(angle))))
            .ToArray();

    public static Editor2DPoint[] BuildPolygon(Editor2DPoint center, Editor2DPoint edge, int sides)
    {
        var radius = Distance(center, edge);
        var resolvedSides = Math.Clamp(sides, 3, 64);
        if (radius <= 1e-6)
            return [];
        var rotation = Math.Atan2(edge.Y - center.Y, edge.X - center.X);
        return Enumerable.Range(0, resolvedSides)
            .Select(index => rotation + (index * Math.PI * 2.0 / resolvedSides))
            .Select(angle => new Editor2DPoint(
                center.X + (radius * Math.Cos(angle)),
                center.Y + (radius * Math.Sin(angle))))
            .ToArray();
    }

    public static Editor2DPreviewDocument Translate(
        Editor2DPreviewDocument document,
        IReadOnlyList<string> selectedIds,
        double deltaX,
        double deltaY)
    {
        var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        return Update(document, document.Paths.Select(path => !selected.Contains(path.Id) ? path : path with
        {
            Start = path.Start is { } start ? new(start.X + deltaX, start.Y + deltaY) : null,
            Center = path.Center is { } center ? new(center.X + deltaX, center.Y + deltaY) : null,
            Points = path.Points.Select(point => new Editor2DPoint(point.X + deltaX, point.Y + deltaY)).ToArray(),
        }).ToArray());
    }

    public static Editor2DPreviewDocument Scale(
        Editor2DPreviewDocument document,
        IReadOnlyList<string> selectedIds,
        Editor2DPoint center,
        double factor)
    {
        var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        var normalized = Math.Max(factor, 0.05);
        return Update(document, document.Paths.Select(path => !selected.Contains(path.Id) ? path : path with
        {
            Start = path.Start is { } start ? ScalePoint(start, center, normalized) : null,
            Center = path.Center is { } pathCenter ? ScalePoint(pathCenter, center, normalized) : null,
            Radius = path.Radius is { } radius ? radius * normalized : null,
            TextHeight = path.TextHeight is { } textHeight ? textHeight * normalized : null,
            Points = path.Points.Select(point => ScalePoint(point, center, normalized)).ToArray(),
        }).ToArray());
    }

    public static Editor2DPreviewDocument Update(
        Editor2DPreviewDocument document,
        IReadOnlyList<Editor2DPreviewPath> paths)
        => document with
        {
            Paths = paths,
            Bounds = MeasureBounds(paths),
            EntityCounts = paths.GroupBy(path => path.EntityType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
        };

    public static Editor2DBounds MeasureBounds(IReadOnlyList<Editor2DPreviewPath> paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        return points.Length == 0
            ? new(0, 0, 0, 0)
            : new(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.X),
                points.Max(point => point.Y));
    }

    public static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));

    private static Editor2DPoint ScalePoint(Editor2DPoint point, Editor2DPoint center, double factor)
        => new(
            center.X + ((point.X - center.X) * factor),
            center.Y + ((point.Y - center.Y) * factor));
}
