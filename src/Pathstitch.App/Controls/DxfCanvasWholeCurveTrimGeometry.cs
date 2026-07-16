using System;
using System.Collections.Generic;
using System.Linq;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal sealed record DxfCanvasWholeCurveTrimTarget(
    string PathId,
    IReadOnlyList<Editor2DPoint> PreviewPoints,
    IReadOnlyList<Editor2DPreviewPath> ReplacementPaths);

internal static class DxfCanvasWholeCurveTrimGeometry
{
    private const double Epsilon = 1e-7;

    public static bool TryBuildTarget(
        Editor2DPreviewDocument document,
        Editor2DPoint clickPoint,
        double hitTolerance,
        out DxfCanvasWholeCurveTrimTarget target)
    {
        DxfCanvasWholeCurveTrimTarget? best = null;
        var bestDistance = Math.Max(0.0, hitTolerance);
        foreach (var path in document.Paths)
        {
            if (path.BezierAnchors is not { Count: >= 2 } || path.Points.Count < 2)
                continue;

            var edgeCount = path.IsClosed ? path.Points.Count : path.Points.Count - 1;
            var clickPosition = 0.0;
            var found = false;
            for (var edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
            {
                var start = path.Points[edgeIndex];
                var end = path.Points[(edgeIndex + 1) % path.Points.Count];
                var parameter = ProjectParameter(clickPoint, start, end);
                var projected = Lerp(start, end, parameter);
                var distance = Distance(clickPoint, projected);
                if (distance > bestDistance)
                    continue;
                bestDistance = distance;
                clickPosition = edgeIndex + parameter;
                found = true;
            }
            if (!found)
                continue;

            var cuts = CollectCutPositions(document, path)
                .Select(position => NormalizeCutPosition(position, edgeCount, path.IsClosed))
                .Where(position => position is not null)
                .Select(position => position!.Value)
                .OrderBy(position => position)
                .Aggregate(new List<double>(), (result, position) =>
                {
                    if (result.Count == 0 || Math.Abs(position - result[^1]) > 1e-5)
                        result.Add(position);
                    return result;
                });

            IReadOnlyList<Editor2DPoint> killed;
            IReadOnlyList<IReadOnlyList<Editor2DPoint>> survivors;
            if (path.IsClosed)
                BuildClosed(path.Points, edgeCount, clickPosition, cuts, out killed, out survivors);
            else
                BuildOpen(path.Points, edgeCount, clickPosition, cuts, out killed, out survivors);

            best = new DxfCanvasWholeCurveTrimTarget(
                path.Id,
                killed,
                survivors.Where(HasDrawableSegments)
                    .Select(points => Bake(path, points))
                    .ToArray());
        }

        if (best is not null)
        {
            target = best;
            return true;
        }
        target = default!;
        return false;
    }

    private static void BuildOpen(
        IReadOnlyList<Editor2DPoint> points,
        int edgeCount,
        double clickPosition,
        IReadOnlyList<double> cuts,
        out IReadOnlyList<Editor2DPoint> killed,
        out IReadOnlyList<IReadOnlyList<Editor2DPoint>> survivors)
    {
        var low = cuts.Where(position => position <= clickPosition + Epsilon).DefaultIfEmpty(0.0).Max();
        var high = cuts.Where(position => position >= clickPosition - Epsilon).DefaultIfEmpty(edgeCount).Min();
        killed = Subpath(points, edgeCount, low, high);
        var result = new List<IReadOnlyList<Editor2DPoint>>(2);
        if (low > Epsilon)
            result.Add(Subpath(points, edgeCount, 0.0, low));
        if (high < edgeCount - Epsilon)
            result.Add(Subpath(points, edgeCount, high, edgeCount));
        survivors = result;
    }

    private static void BuildClosed(
        IReadOnlyList<Editor2DPoint> points,
        int edgeCount,
        double clickPosition,
        IReadOnlyList<double> cuts,
        out IReadOnlyList<Editor2DPoint> killed,
        out IReadOnlyList<IReadOnlyList<Editor2DPoint>> survivors)
    {
        if (cuts.Count < 2)
        {
            killed = Subpath(points, edgeCount, 0.0, edgeCount);
            survivors = [];
            return;
        }

        var lower = cuts.Where(position => position <= clickPosition + Epsilon).DefaultIfEmpty(cuts[^1]).Max();
        var upper = cuts.Where(position => position > clickPosition + Epsilon).DefaultIfEmpty(cuts[0] + edgeCount).Min();
        if (upper <= lower)
            upper += edgeCount;
        killed = Subpath(points, edgeCount, lower, upper);
        var survivor = Subpath(points, edgeCount, upper, lower + edgeCount);
        survivors = HasDrawableSegments(survivor) ? [survivor] : [];
    }

    private static Editor2DPreviewPath Bake(Editor2DPreviewPath source, IReadOnlyList<Editor2DPoint> points)
    {
        var baked = points.ToArray();
        var isLine = baked.Length == 2;
        return source with
        {
            Id = $"{source.Id}-trim-{Guid.NewGuid():N}",
            EntityType = isLine ? "LINE" : "LWPOLYLINE",
            Points = baked,
            IsClosed = false,
            IsAxisAlignedRectangle = false,
            Start = baked[0],
            Text = null,
            TextHeight = null,
            RotationDegrees = null,
            WidthFactor = null,
            Center = null,
            Radius = null,
            StartAngleDegrees = null,
            EndAngleDegrees = null,
            FontFamily = null,
            CharacterSpacing = 0.0,
            IsBold = false,
            IsItalic = false,
            IsUnderline = false,
            BezierAnchors = null,
            IsFilled = false,
        };
    }

    private static IReadOnlyList<double> CollectCutPositions(
        Editor2DPreviewDocument document,
        Editor2DPreviewPath host)
    {
        var positions = new List<double>();
        var edgeCount = host.IsClosed ? host.Points.Count : host.Points.Count - 1;
        foreach (var cutter in document.Paths.Where(path => !path.Id.Equals(host.Id, StringComparison.Ordinal)))
        {
            for (var edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
            {
                var start = host.Points[edgeIndex];
                var end = host.Points[(edgeIndex + 1) % host.Points.Count];
                if (IsLinear(cutter))
                {
                    foreach (var (cutterStart, cutterEnd) in Segments(cutter))
                    {
                        if (TrySegmentIntersectionParameter(start, end, cutterStart, cutterEnd, out var parameter))
                            positions.Add(edgeIndex + parameter);
                    }
                    continue;
                }

                if (!TryGetCircular(cutter, out var center, out var radius, out var arcStart, out var arcSweep))
                    continue;
                foreach (var parameter in SegmentCircleParameters(start, end, center, radius))
                {
                    var point = Lerp(start, end, parameter);
                    if (cutter.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
                        && AngularOffset(arcStart, Angle(center, point)) > arcSweep + Epsilon)
                    {
                        continue;
                    }
                    positions.Add(edgeIndex + parameter);
                }
            }
        }
        return positions;
    }

    private static double? NormalizeCutPosition(double position, int edgeCount, bool isClosed)
    {
        if (!isClosed)
            return position > Epsilon && position < edgeCount - Epsilon ? position : null;

        if (Math.Abs(position) <= Epsilon || Math.Abs(position - edgeCount) <= Epsilon)
            return 0.0;
        return position > 0.0 && position < edgeCount ? position : null;
    }

    private static IReadOnlyList<Editor2DPoint> Subpath(
        IReadOnlyList<Editor2DPoint> points,
        int edgeCount,
        double low,
        double high)
    {
        if (high - low <= Epsilon)
            return [];
        var result = new List<Editor2DPoint> { PointAt(points, edgeCount, low) };
        for (var boundary = (int)Math.Floor(low) + 1; boundary < high - Epsilon; boundary++)
            AppendUnique(result, points[boundary % points.Count]);
        AppendUnique(result, PointAt(points, edgeCount, high));
        return result;
    }

    private static Editor2DPoint PointAt(IReadOnlyList<Editor2DPoint> points, int edgeCount, double position)
    {
        if (position >= edgeCount - Epsilon && edgeCount == points.Count - 1)
            return points[^1];
        var edge = ((int)Math.Floor(position) % edgeCount + edgeCount) % edgeCount;
        var parameter = position - Math.Floor(position);
        return Lerp(points[edge], points[(edge + 1) % points.Count], parameter);
    }

    private static bool TrySegmentIntersectionParameter(
        Editor2DPoint leftStart,
        Editor2DPoint leftEnd,
        Editor2DPoint rightStart,
        Editor2DPoint rightEnd,
        out double parameter)
    {
        var rx = leftEnd.X - leftStart.X;
        var ry = leftEnd.Y - leftStart.Y;
        var sx = rightEnd.X - rightStart.X;
        var sy = rightEnd.Y - rightStart.Y;
        var denominator = (rx * sy) - (ry * sx);
        if (Math.Abs(denominator) <= Epsilon)
        {
            parameter = 0.0;
            return false;
        }
        var qx = rightStart.X - leftStart.X;
        var qy = rightStart.Y - leftStart.Y;
        var left = ((qx * sy) - (qy * sx)) / denominator;
        var right = ((qx * ry) - (qy * rx)) / denominator;
        parameter = left;
        return left >= -Epsilon && left <= 1.0 + Epsilon
               && right >= -Epsilon && right <= 1.0 + Epsilon;
    }

    private static IEnumerable<double> SegmentCircleParameters(
        Editor2DPoint start, Editor2DPoint end, Editor2DPoint center, double radius)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var a = (dx * dx) + (dy * dy);
        if (a <= Epsilon)
            yield break;
        var fx = start.X - center.X;
        var fy = start.Y - center.Y;
        var b = 2.0 * ((fx * dx) + (fy * dy));
        var c = (fx * fx) + (fy * fy) - (radius * radius);
        var discriminant = (b * b) - (4.0 * a * c);
        if (discriminant <= 1e-9)
            yield break;
        var root = Math.Sqrt(discriminant);
        foreach (var value in new[] { (-b - root) / (2.0 * a), (-b + root) / (2.0 * a) })
        {
            if (value >= -Epsilon && value <= 1.0 + Epsilon)
                yield return value;
        }
    }

    private static bool TryGetCircular(
        Editor2DPreviewPath path,
        out Editor2DPoint center,
        out double radius,
        out double start,
        out double sweep)
    {
        if ((path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
             || path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase))
            && path.Center is { } resolvedCenter && path.Radius is > Epsilon and var resolvedRadius)
        {
            center = resolvedCenter;
            radius = resolvedRadius;
            start = path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
                ? NormalizeDegrees(path.StartAngleDegrees ?? 0.0)
                : 0.0;
            sweep = path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
                ? PositiveSweep(start, path.EndAngleDegrees ?? start)
                : 360.0;
            return true;
        }
        center = default!;
        radius = start = sweep = 0.0;
        return false;
    }

    private static bool IsLinear(Editor2DPreviewPath path)
        => path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(Editor2DPoint Start, Editor2DPoint End)> Segments(Editor2DPreviewPath path)
    {
        for (var index = 0; index < path.Points.Count - 1; index++)
            yield return (path.Points[index], path.Points[index + 1]);
        if (path.IsClosed && path.Points.Count > 2)
            yield return (path.Points[^1], path.Points[0]);
    }

    private static bool HasDrawableSegments(IReadOnlyList<Editor2DPoint> points)
        => points.Count >= 2 && points.Zip(points.Skip(1)).Any(pair => Distance(pair.First, pair.Second) > Epsilon);

    private static void AppendUnique(List<Editor2DPoint> points, Editor2DPoint point)
    {
        if (points.Count == 0 || Distance(points[^1], point) > Epsilon)
            points.Add(point);
    }

    private static double ProjectParameter(Editor2DPoint point, Editor2DPoint start, Editor2DPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        return lengthSquared <= Epsilon
            ? 0.0
            : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0.0, 1.0);
    }

    private static Editor2DPoint Lerp(Editor2DPoint start, Editor2DPoint end, double amount)
        => new(start.X + ((end.X - start.X) * amount), start.Y + ((end.Y - start.Y) * amount));

    private static double Angle(Editor2DPoint center, Editor2DPoint point)
        => NormalizeDegrees(Math.Atan2(point.Y - center.Y, point.X - center.X) * 180.0 / Math.PI);

    private static double PositiveSweep(double start, double end)
    {
        var sweep = NormalizeDegrees(end) - NormalizeDegrees(start);
        while (sweep <= 0.0)
            sweep += 360.0;
        return sweep;
    }

    private static double AngularOffset(double start, double angle)
    {
        var offset = NormalizeDegrees(angle) - NormalizeDegrees(start);
        return offset < 0.0 ? offset + 360.0 : offset;
    }

    private static double NormalizeDegrees(double angle)
    {
        var normalized = angle % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(right.X - left.X, 2) + Math.Pow(right.Y - left.Y, 2));
}
