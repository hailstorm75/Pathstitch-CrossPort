namespace Domain.App.Models;

public static class Editor2DBezierGeometry
{
    public const int DefaultSamplesPerCurve = 18;

    public static Editor2DBezierAnchor CreateSmoothAnchor(Editor2DPoint point, Editor2DPoint handleOut)
        => new(point, Reflect(handleOut, point), handleOut);

    public static IReadOnlyList<Editor2DPoint> Flatten(
        IReadOnlyList<Editor2DBezierAnchor> anchors,
        bool closed,
        int samplesPerCurve = DefaultSamplesPerCurve)
    {
        if (anchors.Count < 2)
            return [];

        var points = new List<Editor2DPoint> { anchors[0].Point };
        var segmentCount = closed ? anchors.Count : anchors.Count - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = anchors[index];
            var end = anchors[(index + 1) % anchors.Count];
            if (start.HandleOut is null && end.HandleIn is null)
            {
                AppendDistinct(points, end.Point);
                continue;
            }

            var control1 = start.HandleOut ?? start.Point;
            var control2 = end.HandleIn ?? end.Point;
            for (var sample = 1; sample <= Math.Max(samplesPerCurve, 1); sample++)
                AppendDistinct(points, Cubic(start.Point, control1, control2, end.Point, (double)sample / Math.Max(samplesPerCurve, 1)));
        }

        if (closed && points.Count > 2 && Distance(points[0], points[^1]) <= 1e-6)
            points.RemoveAt(points.Count - 1);
        return points;
    }

    public static Editor2DBezierAnchor Transform(
        Editor2DBezierAnchor anchor,
        Func<Editor2DPoint, Editor2DPoint> transform)
        => new(
            transform(anchor.Point),
            anchor.HandleIn is null ? null : transform(anchor.HandleIn),
            anchor.HandleOut is null ? null : transform(anchor.HandleOut));

    private static Editor2DPoint Cubic(
        Editor2DPoint p0,
        Editor2DPoint p1,
        Editor2DPoint p2,
        Editor2DPoint p3,
        double t)
    {
        var mt = 1.0 - t;
        var a = mt * mt * mt;
        var b = 3.0 * mt * mt * t;
        var c = 3.0 * mt * t * t;
        var d = t * t * t;
        return new(
            (a * p0.X) + (b * p1.X) + (c * p2.X) + (d * p3.X),
            (a * p0.Y) + (b * p1.Y) + (c * p2.Y) + (d * p3.Y));
    }

    private static Editor2DPoint Reflect(Editor2DPoint point, Editor2DPoint center)
        => new((2.0 * center.X) - point.X, (2.0 * center.Y) - point.Y);

    private static void AppendDistinct(List<Editor2DPoint> points, Editor2DPoint point)
    {
        if (Distance(points[^1], point) > 1e-7)
            points.Add(point);
    }

    private static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));
}
