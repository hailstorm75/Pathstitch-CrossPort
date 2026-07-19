using System;
using System.Collections.Generic;
using Avalonia;

namespace Pathstitch.App.Controls;

internal static class DxfCanvasHitTester
{
    public static double DistanceToPath(Point point, IReadOnlyList<Point> points, bool isClosed)
    {
        if (points.Count < 2)
            return double.PositiveInfinity;

        var bestDistance = double.PositiveInfinity;
        for (var index = 0; index < points.Count - 1; index++)
            bestDistance = Math.Min(bestDistance, DistanceToSegment(point, points[index], points[index + 1]));

        if (isClosed)
            bestDistance = Math.Min(bestDistance, DistanceToSegment(point, points[^1], points[0]));

        return bestDistance;
    }

    public static bool PointInPolygon(Point point, IReadOnlyList<Point> polygon)
    {
        var inside = false;
        for (var index = 0; index < polygon.Count; index++)
        {
            var previousIndex = index == 0 ? polygon.Count - 1 : index - 1;
            var current = polygon[index];
            var previous = polygon[previousIndex];
            var intersects = ((current.Y > point.Y) != (previous.Y > point.Y))
                             && (point.X < ((previous.X - current.X) * (point.Y - current.Y)
                                 / ((previous.Y - current.Y) + double.Epsilon)) + current.X);
            if (intersects)
                inside = !inside;
        }

        return inside;
    }

    public static double DistanceToSegment(Point point, Point start, Point end)
    {
        var delta = end - start;
        var lengthSquared = (delta.X * delta.X) + (delta.Y * delta.Y);
        if (lengthSquared <= 1e-9)
            return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));

        var parameter = ((point.X - start.X) * delta.X + (point.Y - start.Y) * delta.Y) / lengthSquared;
        parameter = Math.Clamp(parameter, 0.0, 1.0);
        var projection = new Point(start.X + (delta.X * parameter), start.Y + (delta.Y * parameter));
        return Math.Sqrt(Math.Pow(point.X - projection.X, 2) + Math.Pow(point.Y - projection.Y, 2));
    }
}
