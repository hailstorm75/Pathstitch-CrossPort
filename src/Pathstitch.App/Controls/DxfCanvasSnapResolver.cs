using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal enum DxfCanvasSnapKind
{
    Endpoint,
    Intersection,
    Midpoint,
    Center,
    Coincident,
}

internal sealed record DxfCanvasSnapResult(
    Editor2DPoint ModelPoint,
    Point ScreenPoint,
    DxfCanvasSnapKind Kind)
{
    public string Label => Kind.ToString();
}

internal sealed class DxfCanvasSnapResolver
{
    private const double Epsilon = 1e-8;
    private const int IntersectionSegmentLimit = 600;

    public DxfCanvasSnapResult? Resolve(
        Editor2DPreviewDocument? document,
        IReadOnlyList<string> hiddenPathIds,
        Editor2DPoint queryPoint,
        Point queryScreenPoint,
        Func<Editor2DPoint, Point> worldToScreen,
        double tolerancePixels = 12.0,
        IReadOnlyList<Editor2DCornerParameter>? cornerParameters = null)
    {
        if (document is null || tolerancePixels <= 0)
            return null;

        var hidden = new HashSet<string>(hiddenPathIds, StringComparer.Ordinal);
        var visiblePaths = document.Paths.Where(path => !hidden.Contains(path.Id)).ToArray();
        var candidates = new List<SnapCandidate>();
        var segments = new List<Segment>();

        foreach (var path in visiblePaths)
            AddPathCandidates(path, queryPoint, candidates, segments, cornerParameters ?? []);

        if (segments.Count <= IntersectionSegmentLimit)
            AddIntersections(segments, candidates);

        return candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Screen = worldToScreen(candidate.Point),
            })
            .Select(item => new
            {
                item.Candidate,
                item.Screen,
                Distance = Distance(item.Screen, queryScreenPoint),
            })
            .Where(item => item.Distance <= tolerancePixels)
            .OrderBy(item => Priority(item.Candidate.Kind))
            .ThenBy(item => item.Distance)
            .Select(item => new DxfCanvasSnapResult(item.Candidate.Point, item.Screen, item.Candidate.Kind))
            .FirstOrDefault();
    }

    public static Editor2DPoint ApplyOrthogonalConstraint(
        Editor2DPoint reference,
        Editor2DPoint point,
        double thresholdDegrees = 7.0)
    {
        var dx = point.X - reference.X;
        var dy = point.Y - reference.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= Epsilon)
            return point;

        var angle = Math.Atan2(dy, dx);
        var quarterTurn = Math.PI / 2.0;
        var snappedAngle = Math.Round(angle / quarterTurn) * quarterTurn;
        var angularDistance = Math.Abs(NormalizeRadians(angle - snappedAngle));
        if (angularDistance >= thresholdDegrees * Math.PI / 180.0)
            return point;

        return new Editor2DPoint(
            reference.X + (Math.Cos(snappedAngle) * length),
            reference.Y + (Math.Sin(snappedAngle) * length));
    }

    private static void AddPathCandidates(
        Editor2DPreviewPath path,
        Editor2DPoint queryPoint,
        ICollection<SnapCandidate> candidates,
        ICollection<Segment> segments,
        IReadOnlyList<Editor2DCornerParameter> cornerParameters)
    {
        if (path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
            && path.Center is { } circleCenter
            && path.Radius is > Epsilon and var circleRadius)
        {
            candidates.Add(new SnapCandidate(circleCenter, DxfCanvasSnapKind.Center));
            candidates.Add(new SnapCandidate(ClosestPointOnCircle(circleCenter, circleRadius, queryPoint), DxfCanvasSnapKind.Coincident));
            return;
        }

        if (path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
            && path.Center is { } arcCenter
            && path.Radius is > Epsilon and var arcRadius
            && path.StartAngleDegrees is { } startAngle
            && path.EndAngleDegrees is { } endAngle)
        {
            var start = PointOnCircle(arcCenter, arcRadius, startAngle);
            var end = PointOnCircle(arcCenter, arcRadius, endAngle);
            var sweep = PositiveSweep(startAngle, endAngle);
            candidates.Add(new SnapCandidate(arcCenter, DxfCanvasSnapKind.Center));
            candidates.Add(new SnapCandidate(start, DxfCanvasSnapKind.Endpoint));
            candidates.Add(new SnapCandidate(end, DxfCanvasSnapKind.Endpoint));
            candidates.Add(new SnapCandidate(PointOnCircle(arcCenter, arcRadius, startAngle + (sweep / 2.0)), DxfCanvasSnapKind.Midpoint));
            candidates.Add(new SnapCandidate(ClosestPointOnArc(arcCenter, arcRadius, startAngle, sweep, queryPoint), DxfCanvasSnapKind.Coincident));
            return;
        }

        var points = path.Points.Count > 0
            ? path.Points
            : path.Start is { } fallbackStart
                ? new[] { fallbackStart }
                : Array.Empty<Editor2DPoint>();
        if (points.Count == 0)
            return;

        var pathCornerParameters = cornerParameters.Where(parameter => parameter.PathId == path.Id).ToArray();
        var isParametricCornerPath = pathCornerParameters.Length > 0;
        if (isParametricCornerPath)
            AddParametricCornerCandidates(path, pathCornerParameters, candidates);
        else
            foreach (var point in points)
                candidates.Add(new SnapCandidate(point, DxfCanvasSnapKind.Endpoint));

        var segmentCount = path.IsClosed && points.Count > 2 ? points.Count : points.Count - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            var first = points[index];
            var second = points[(index + 1) % points.Count];
            if (Distance(first, second) <= Epsilon)
                continue;

            if (!isParametricCornerPath)
                candidates.Add(new SnapCandidate(Midpoint(first, second), DxfCanvasSnapKind.Midpoint));
            candidates.Add(new SnapCandidate(ClosestPointOnSegment(first, second, queryPoint), DxfCanvasSnapKind.Coincident));
            segments.Add(new Segment(path.Id, first, second));
        }
    }

    private static void AddParametricCornerCandidates(
        Editor2DPreviewPath path,
        IReadOnlyList<Editor2DCornerParameter> parameters,
        ICollection<SnapCandidate> candidates)
    {
        var source = parameters[0].SourcePoints;
        var byIndex = parameters.ToDictionary(parameter => parameter.CornerIndex);
        for (var index = 0; index < source.Count; index++)
        {
            if (!byIndex.TryGetValue(index, out var parameter)
                || (!path.IsClosed && (index == 0 || index == source.Count - 1)))
            {
                candidates.Add(new SnapCandidate(source[index], DxfCanvasSnapKind.Endpoint));
                continue;
            }

            var previous = source[index == 0 ? source.Count - 1 : index - 1];
            var corner = source[index];
            var next = source[index == source.Count - 1 ? 0 : index + 1];
            if (!TryBuildCornerSnapPoints(previous, corner, next, parameter, out var tangentStart, out var tangentEnd, out var center))
            {
                candidates.Add(new SnapCandidate(corner, DxfCanvasSnapKind.Endpoint));
                continue;
            }

            candidates.Add(new SnapCandidate(tangentStart, DxfCanvasSnapKind.Endpoint));
            candidates.Add(new SnapCandidate(tangentEnd, DxfCanvasSnapKind.Endpoint));
            candidates.Add(new SnapCandidate(center, DxfCanvasSnapKind.Midpoint));
        }
    }

    private static bool TryBuildCornerSnapPoints(
        Editor2DPoint previous,
        Editor2DPoint corner,
        Editor2DPoint next,
        Editor2DCornerParameter parameter,
        out Editor2DPoint tangentStart,
        out Editor2DPoint tangentEnd,
        out Editor2DPoint center)
    {
        var towardPrevious = Normalize(previous.X - corner.X, previous.Y - corner.Y);
        var towardNext = Normalize(next.X - corner.X, next.Y - corner.Y);
        var dot = Math.Clamp((towardPrevious.X * towardNext.X) + (towardPrevious.Y * towardNext.Y), -1, 1);
        var angle = Math.Acos(dot);
        var maximum = Math.Min(Distance(previous, corner), Distance(corner, next)) * 0.5;
        if (angle <= 1e-3 || angle >= Math.PI - 1e-3 || maximum <= Epsilon)
        {
            tangentStart = tangentEnd = center = corner;
            return false;
        }

        var setback = parameter.Kind == Editor2DCornerKind.Chamfer
            ? Math.Min(parameter.Value, maximum)
            : Math.Min(parameter.Value / Math.Tan(angle / 2.0), maximum);
        if (setback <= Epsilon)
        {
            tangentStart = tangentEnd = center = corner;
            return false;
        }

        tangentStart = new Editor2DPoint(corner.X + (towardPrevious.X * setback), corner.Y + (towardPrevious.Y * setback));
        tangentEnd = new Editor2DPoint(corner.X + (towardNext.X * setback), corner.Y + (towardNext.Y * setback));
        if (parameter.Kind == Editor2DCornerKind.Chamfer)
        {
            center = Midpoint(tangentStart, tangentEnd);
            return true;
        }

        var bisector = Normalize(towardPrevious.X + towardNext.X, towardPrevious.Y + towardNext.Y);
        var radius = setback * Math.Tan(angle / 2.0);
        var centerDistance = radius / Math.Sin(angle / 2.0);
        center = new Editor2DPoint(corner.X + (bisector.X * centerDistance), corner.Y + (bisector.Y * centerDistance));
        return true;
    }

    private static void AddIntersections(IReadOnlyList<Segment> segments, ICollection<SnapCandidate> candidates)
    {
        for (var firstIndex = 0; firstIndex < segments.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < segments.Count; secondIndex++)
            {
                var first = segments[firstIndex];
                var second = segments[secondIndex];
                if (SharesEndpoint(first, second))
                    continue;

                if (TryIntersect(first, second, out var intersection))
                    candidates.Add(new SnapCandidate(intersection, DxfCanvasSnapKind.Intersection));
            }
        }
    }

    private static bool TryIntersect(Segment first, Segment second, out Editor2DPoint intersection)
    {
        var rx = first.End.X - first.Start.X;
        var ry = first.End.Y - first.Start.Y;
        var sx = second.End.X - second.Start.X;
        var sy = second.End.Y - second.Start.Y;
        var denominator = Cross(rx, ry, sx, sy);
        if (Math.Abs(denominator) <= Epsilon)
        {
            intersection = first.Start;
            return false;
        }

        var qpx = second.Start.X - first.Start.X;
        var qpy = second.Start.Y - first.Start.Y;
        var t = Cross(qpx, qpy, sx, sy) / denominator;
        var u = Cross(qpx, qpy, rx, ry) / denominator;
        if (t < -Epsilon || t > 1 + Epsilon || u < -Epsilon || u > 1 + Epsilon)
        {
            intersection = first.Start;
            return false;
        }

        intersection = new Editor2DPoint(first.Start.X + (t * rx), first.Start.Y + (t * ry));
        return true;
    }

    private static Editor2DPoint ClosestPointOnSegment(Editor2DPoint start, Editor2DPoint end, Editor2DPoint point)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var denominator = (dx * dx) + (dy * dy);
        if (denominator <= Epsilon)
            return start;
        var t = Math.Clamp((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / denominator, 0.0, 1.0);
        return new Editor2DPoint(start.X + (t * dx), start.Y + (t * dy));
    }

    private static Editor2DPoint ClosestPointOnCircle(Editor2DPoint center, double radius, Editor2DPoint point)
    {
        var angle = Math.Atan2(point.Y - center.Y, point.X - center.X);
        return new Editor2DPoint(center.X + (radius * Math.Cos(angle)), center.Y + (radius * Math.Sin(angle)));
    }

    private static Editor2DPoint ClosestPointOnArc(Editor2DPoint center, double radius, double start, double sweep, Editor2DPoint point)
    {
        var angle = NormalizeDegrees(Math.Atan2(point.Y - center.Y, point.X - center.X) * 180.0 / Math.PI);
        var relative = NormalizeDegrees(angle - NormalizeDegrees(start));
        if (relative <= sweep)
            return PointOnCircle(center, radius, start + relative);

        var startPoint = PointOnCircle(center, radius, start);
        var endPoint = PointOnCircle(center, radius, start + sweep);
        return Distance(point, startPoint) <= Distance(point, endPoint) ? startPoint : endPoint;
    }

    private static Editor2DPoint PointOnCircle(Editor2DPoint center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Editor2DPoint(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }

    private static double PositiveSweep(double start, double end) => NormalizeDegrees(end - start);
    private static double NormalizeDegrees(double degrees) => (degrees % 360.0 + 360.0) % 360.0;
    private static double NormalizeRadians(double radians) => Math.Atan2(Math.Sin(radians), Math.Cos(radians));
    private static double Cross(double ax, double ay, double bx, double by) => (ax * by) - (ay * bx);
    private static (double X, double Y) Normalize(double x, double y)
    {
        var length = Math.Sqrt((x * x) + (y * y));
        return length <= Epsilon ? (0, 0) : (x / length, y / length);
    }
    private static Editor2DPoint Midpoint(Editor2DPoint a, Editor2DPoint b) => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
    private static double Distance(Editor2DPoint a, Editor2DPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static bool SharesEndpoint(Segment a, Segment b) =>
        Distance(a.Start, b.Start) <= Epsilon || Distance(a.Start, b.End) <= Epsilon
        || Distance(a.End, b.Start) <= Epsilon || Distance(a.End, b.End) <= Epsilon;
    private static int Priority(DxfCanvasSnapKind kind) => kind switch
    {
        DxfCanvasSnapKind.Endpoint => 0,
        DxfCanvasSnapKind.Intersection => 1,
        DxfCanvasSnapKind.Midpoint => 2,
        DxfCanvasSnapKind.Center => 3,
        _ => 4,
    };

    private sealed record SnapCandidate(Editor2DPoint Point, DxfCanvasSnapKind Kind);
    private sealed record Segment(string PathId, Editor2DPoint Start, Editor2DPoint End);
}
