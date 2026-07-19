using System;
using System.Collections.Generic;
using System.Linq;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal sealed record DxfCanvasCircularTrimTarget(
    string PathId,
    IReadOnlyList<Editor2DPoint> PreviewPoints,
    IReadOnlyList<Editor2DPreviewPath> ReplacementPaths);

internal static class DxfCanvasCircularTrimGeometry
{
    private const double Epsilon = 1e-7;

    public static bool TryBuildTarget(
        Editor2DPreviewDocument document,
        Editor2DPoint clickPoint,
        double hitTolerance,
        out DxfCanvasCircularTrimTarget target)
    {
        DxfCanvasCircularTrimTarget? best = null;
        var bestDistance = Math.Max(hitTolerance, 0.0);
        foreach (var path in document.Paths)
        {
            if (!TryGetCircularGeometry(path, out var center, out var radius, out var start, out var sweep))
                continue;

            var clickAngle = NormalizeDegrees(Math.Atan2(clickPoint.Y - center.Y, clickPoint.X - center.X) * 180.0 / Math.PI);
            if (path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
                && !AngleIsOnSweep(clickAngle, start, sweep))
            {
                continue;
            }

            var radialDistance = Math.Abs(Distance(center, clickPoint) - radius);
            if (radialDistance > bestDistance)
                continue;

            var cuts = CollectCutAngles(document, path, center, radius)
                .Where(angle => path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
                                || IsInteriorArcAngle(angle, start, sweep))
                .OrderBy(angle => angle)
                .ToArray();
            var spans = BuildSpans(path, start, sweep, clickAngle, cuts, out var killStart, out var killSweep);
            var replacements = spans.Select(span => CreateArc(
                path,
                $"{path.Id}-trim-{Guid.NewGuid():N}",
                span.Start,
                span.Sweep)).ToArray();
            bestDistance = radialDistance;
            best = new DxfCanvasCircularTrimTarget(
                path.Id,
                SampleArc(center, radius, killStart, killSweep),
                replacements);
        }

        if (best is not null)
        {
            target = best;
            return true;
        }

        target = default!;
        return false;
    }

    private static IReadOnlyList<(double Start, double Sweep)> BuildSpans(
        Editor2DPreviewPath path,
        double sourceStart,
        double sourceSweep,
        double clickAngle,
        IReadOnlyList<double> cuts,
        out double killStart,
        out double killSweep)
    {
        if (path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase))
        {
            if (cuts.Count < 2)
            {
                killStart = 0.0;
                killSweep = 360.0;
                return [];
            }

            for (var index = 0; index < cuts.Count; index++)
            {
                var start = cuts[index];
                var end = cuts[(index + 1) % cuts.Count];
                var sweep = PositiveSweep(start, end);
                if (PositiveSweep(start, clickAngle) <= sweep + Epsilon)
                {
                    killStart = start;
                    killSweep = sweep;
                    return sweep >= 360.0 - Epsilon
                        ? []
                        : [(NormalizeDegrees(end), 360.0 - sweep)];
                }
            }
        }
        else
        {
            var offsets = cuts.Select(angle => PositiveSweep(sourceStart, angle))
                .Where(offset => offset > Epsilon && offset < sourceSweep - Epsilon)
                .OrderBy(offset => offset)
                .ToArray();
            var clickOffset = Math.Clamp(PositiveSweep(sourceStart, clickAngle), 0.0, sourceSweep);
            var bounds = new[] { 0.0 }.Concat(offsets).Append(sourceSweep).ToArray();
            for (var index = 0; index < bounds.Length - 1; index++)
            {
                if (clickOffset <= bounds[index + 1] + Epsilon)
                {
                    var low = bounds[index];
                    var high = bounds[index + 1];
                    killStart = NormalizeDegrees(sourceStart + low);
                    killSweep = high - low;
                    var survivors = new List<(double Start, double Sweep)>(2);
                    if (low > Epsilon)
                        survivors.Add((sourceStart, low));
                    if (sourceSweep - high > Epsilon)
                        survivors.Add((NormalizeDegrees(sourceStart + high), sourceSweep - high));
                    return survivors;
                }
            }
        }

        killStart = sourceStart;
        killSweep = sourceSweep;
        return [];
    }

    private static Editor2DPreviewPath CreateArc(Editor2DPreviewPath source, string id, double start, double sweep)
    {
        var points = Editor2DGeometry.BuildArcPoints(
            source.Center!, source.Radius!.Value, start, start + sweep, minimumSegmentCount: 2);
        return source with
        {
            Id = id,
            EntityType = "ARC",
            Points = points,
            IsClosed = false,
            IsAxisAlignedRectangle = false,
            Start = points[0],
            StartAngleDegrees = NormalizeDegrees(start),
            EndAngleDegrees = NormalizeDegrees(start + sweep),
            BezierAnchors = null,
        };
    }

    private static Editor2DPoint[] SampleArc(Editor2DPoint center, double radius, double start, double sweep)
        => Editor2DGeometry.BuildArcPoints(center, radius, start, start + sweep, minimumSegmentCount: 2);

    private static IReadOnlyList<double> CollectCutAngles(
        Editor2DPreviewDocument document,
        Editor2DPreviewPath host,
        Editor2DPoint center,
        double radius)
    {
        var angles = new List<double>();
        foreach (var cutter in document.Paths.Where(path => !path.Id.Equals(host.Id, StringComparison.Ordinal)))
        {
            if (IsPolyline(cutter))
            {
                foreach (var (start, end) in Segments(cutter))
                    angles.AddRange(SegmentCircleAngles(start, end, center, radius));
                continue;
            }

            if (!TryGetCircularGeometry(cutter, out var otherCenter, out var otherRadius, out var otherStart, out var otherSweep))
                continue;
            foreach (var point in CircleCircleIntersections(center, radius, otherCenter, otherRadius))
            {
                var otherAngle = Angle(otherCenter, point);
                if (cutter.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
                    && !AngleIsOnSweep(otherAngle, otherStart, otherSweep))
                {
                    continue;
                }
                angles.Add(Angle(center, point));
            }
        }

        return angles.Select(NormalizeDegrees).OrderBy(angle => angle)
            .Aggregate(new List<double>(), (result, angle) =>
            {
                if (result.Count == 0 || Math.Abs(angle - result[^1]) > 1e-5)
                    result.Add(angle);
                return result;
            });
    }

    private static IEnumerable<double> SegmentCircleAngles(
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
        foreach (var parameter in new[] { (-b - root) / (2.0 * a), (-b + root) / (2.0 * a) })
        {
            if (parameter >= -Epsilon && parameter <= 1.0 + Epsilon)
                yield return Angle(center, new(start.X + (dx * parameter), start.Y + (dy * parameter)));
        }
    }

    private static IEnumerable<Editor2DPoint> CircleCircleIntersections(
        Editor2DPoint left, double leftRadius, Editor2DPoint right, double rightRadius)
    {
        var distance = Distance(left, right);
        if (distance <= Epsilon
            || distance >= leftRadius + rightRadius - Epsilon
            || distance <= Math.Abs(leftRadius - rightRadius) + Epsilon)
        {
            yield break;
        }
        var along = ((leftRadius * leftRadius) - (rightRadius * rightRadius) + (distance * distance)) / (2.0 * distance);
        var heightSquared = (leftRadius * leftRadius) - (along * along);
        if (heightSquared <= Epsilon)
            yield break;
        var height = Math.Sqrt(heightSquared);
        var baseX = left.X + (along * (right.X - left.X) / distance);
        var baseY = left.Y + (along * (right.Y - left.Y) / distance);
        var normalX = -(right.Y - left.Y) / distance;
        var normalY = (right.X - left.X) / distance;
        yield return new(baseX + (height * normalX), baseY + (height * normalY));
        yield return new(baseX - (height * normalX), baseY - (height * normalY));
    }

    private static bool TryGetCircularGeometry(
        Editor2DPreviewPath path, out Editor2DPoint center, out double radius, out double start, out double sweep)
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

    private static bool IsPolyline(Editor2DPreviewPath path)
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

    private static bool IsInteriorArcAngle(double angle, double start, double sweep)
    {
        var offset = PositiveSweep(start, angle);
        return offset > Epsilon && offset < sweep - Epsilon;
    }

    private static bool AngleIsOnSweep(double angle, double start, double sweep)
        => PositiveSweep(start, angle) <= sweep + Epsilon;

    private static Editor2DPoint PointOnCircle(Editor2DPoint center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }

    private static double Angle(Editor2DPoint center, Editor2DPoint point)
        => NormalizeDegrees(Math.Atan2(point.Y - center.Y, point.X - center.X) * 180.0 / Math.PI);

    private static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(right.X - left.X, 2) + Math.Pow(right.Y - left.Y, 2));

    private static double PositiveSweep(double start, double end)
    {
        var sweep = NormalizeDegrees(end) - NormalizeDegrees(start);
        while (sweep <= 0.0)
            sweep += 360.0;
        return sweep;
    }

    private static double NormalizeDegrees(double angle)
    {
        var normalized = angle % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }
}
