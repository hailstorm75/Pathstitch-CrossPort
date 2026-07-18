using System;
using System.Collections.Generic;
using System.Linq;
using Domain.App.Models;
using Domain.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Services;

public sealed class AvaloniaReferenceImageTraceService : IReferenceImageTraceService
{
    public IReadOnlyList<IReadOnlyList<Editor2DPoint>> TraceContours(
        string imageDataBase64,
        Editor2DReferenceImageTraceOptions options)
    {
        if (string.IsNullOrWhiteSpace(imageDataBase64))
            return [];

        var comma = imageDataBase64.IndexOf(',');
        var encoded = imageDataBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0
            ? imageDataBase64[(comma + 1)..]
            : imageDataBase64;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return [];
        }

        using var bitmap = SKBitmap.Decode(bytes);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            return [];

        var pixels = bitmap.Pixels;
        var foreground = new bool[checked(bitmap.Width * bitmap.Height)];
        var cutoff = Math.Clamp(options.Threshold, 0.0, 1.0) * 255.0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = pixels[(y * bitmap.Width) + x];
                var luminance = (0.2126 * pixel.Red) + (0.7152 * pixel.Green) + (0.0722 * pixel.Blue);
                foreground[(y * bitmap.Width) + x] = pixel.Alpha > 10
                    && (options.SilhouetteOnly || luminance <= cutoff);
            }
        }

        var turdSize = Math.Max(0.0, (100.0 - Math.Clamp(options.Tolerance, 1.0, 100.0)) * 0.25);
        var contours = TracePixelBoundaries(foreground, bitmap.Width, bitmap.Height)
            .Where(contour => AbsoluteArea(contour) > turdSize)
            .Select(contour => RefineContour(contour, options))
            .Where(contour => contour.Count >= 3)
            .OrderByDescending(AbsoluteArea)
            .ToArray();
        return options.SilhouetteOnly ? contours.Take(1).ToArray() : contours;
    }

    private static IReadOnlyList<IReadOnlyList<Editor2DPoint>> TracePixelBoundaries(
        bool[] foreground,
        int width,
        int height)
    {
        var edges = new Dictionary<GridPoint, List<GridPoint>>();
        bool IsForeground(int x, int y)
            => x >= 0 && x < width && y >= 0 && y < height && foreground[(y * width) + x];
        void AddEdge(GridPoint start, GridPoint end)
        {
            if (!edges.TryGetValue(start, out var destinations))
            {
                destinations = [];
                edges[start] = destinations;
            }
            destinations.Add(end);
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!IsForeground(x, y))
                    continue;
                if (!IsForeground(x, y - 1)) AddEdge(new(x, y), new(x + 1, y));
                if (!IsForeground(x + 1, y)) AddEdge(new(x + 1, y), new(x + 1, y + 1));
                if (!IsForeground(x, y + 1)) AddEdge(new(x + 1, y + 1), new(x, y + 1));
                if (!IsForeground(x - 1, y)) AddEdge(new(x, y + 1), new(x, y));
            }
        }

        var contours = new List<IReadOnlyList<Editor2DPoint>>();
        while (edges.Count > 0)
        {
            var start = edges.First().Key;
            var current = start;
            var loop = new List<GridPoint>();
            var guard = 0;
            do
            {
                loop.Add(current);
                if (!edges.TryGetValue(current, out var destinations) || destinations.Count == 0)
                    break;
                var next = destinations[^1];
                destinations.RemoveAt(destinations.Count - 1);
                if (destinations.Count == 0)
                    edges.Remove(current);
                current = next;
                guard++;
            }
            while (current != start && guard <= checked(width * height * 4));

            if (current != start || loop.Count < 3)
                continue;
            var simplified = RemoveCollinearPoints(loop);
            if (simplified.Count >= 3)
            {
                contours.Add(simplified
                    .Select(point => new Editor2DPoint(point.X, point.Y))
                    .ToArray());
            }
        }

        return contours
            .OrderByDescending(AbsoluteArea)
            .ToArray();
    }

    private static List<GridPoint> RemoveCollinearPoints(IReadOnlyList<GridPoint> points)
    {
        var result = new List<GridPoint>();
        for (var index = 0; index < points.Count; index++)
        {
            var previous = points[(index - 1 + points.Count) % points.Count];
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            var cross = ((current.X - previous.X) * (next.Y - current.Y))
                - ((current.Y - previous.Y) * (next.X - current.X));
            if (cross != 0)
                result.Add(current);
        }
        return result;
    }

    private static double AbsoluteArea(IReadOnlyList<Editor2DPoint> points)
    {
        var twiceArea = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            twiceArea += (current.X * next.Y) - (next.X * current.Y);
        }
        return Math.Abs(twiceArea) * 0.5;
    }

    private static IReadOnlyList<Editor2DPoint> RefineContour(
        IReadOnlyList<Editor2DPoint> contour,
        Editor2DReferenceImageTraceOptions options)
    {
        var optimization = Math.Clamp(options.PathOptimization, 0.0, 100.0);
        var epsilon = 0.05 + (optimization / 100.0 * 2.95);
        var simplified = SimplifyClosedContour(contour, epsilon);
        if (simplified.Count < 3)
            simplified = contour.ToArray();

        var smoothness = Math.Clamp(options.CornerSmoothness, 0.0, 100.0) / 100.0;
        if (smoothness <= 0.0 || simplified.Count < 3)
            return simplified;

        var weight = smoothness * 0.25;
        return simplified.Select((point, index) =>
        {
            var previous = simplified[(index - 1 + simplified.Count) % simplified.Count];
            var next = simplified[(index + 1) % simplified.Count];
            return new Editor2DPoint(
                point.X * (1.0 - 2.0 * weight) + (previous.X + next.X) * weight,
                point.Y * (1.0 - 2.0 * weight) + (previous.Y + next.Y) * weight);
        }).ToArray();
    }

    private static IReadOnlyList<Editor2DPoint> SimplifyClosedContour(
        IReadOnlyList<Editor2DPoint> points,
        double epsilon)
    {
        if (points.Count <= 3)
            return points.ToArray();

        var split = 1;
        var maximumDistance = 0.0;
        for (var index = 1; index < points.Count; index++)
        {
            var distance = SquaredDistance(points[0], points[index]);
            if (distance > maximumDistance)
            {
                maximumDistance = distance;
                split = index;
            }
        }

        var firstHalf = points.Take(split + 1).ToArray();
        var secondHalf = points.Skip(split).Append(points[0]).ToArray();
        var first = SimplifyOpenPolyline(firstHalf, epsilon);
        var second = SimplifyOpenPolyline(secondHalf, epsilon);
        return first.Take(first.Count - 1).Concat(second.Take(second.Count - 1)).ToArray();
    }

    private static IReadOnlyList<Editor2DPoint> SimplifyOpenPolyline(
        IReadOnlyList<Editor2DPoint> points,
        double epsilon)
    {
        if (points.Count <= 2)
            return points.ToArray();

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        var ranges = new Stack<(int Start, int End)>();
        ranges.Push((0, points.Count - 1));
        while (ranges.Count > 0)
        {
            var (start, end) = ranges.Pop();
            var maximumDistance = 0.0;
            var split = -1;
            for (var index = start + 1; index < end; index++)
            {
                var distance = DistanceToSegment(points[index], points[start], points[end]);
                if (distance > maximumDistance)
                {
                    maximumDistance = distance;
                    split = index;
                }
            }
            if (split < 0 || maximumDistance <= epsilon)
                continue;
            keep[split] = true;
            ranges.Push((start, split));
            ranges.Push((split, end));
        }

        return points.Where((_, index) => keep[index]).ToArray();
    }

    private static double DistanceToSegment(Editor2DPoint point, Editor2DPoint start, Editor2DPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= double.Epsilon)
            return Math.Sqrt(SquaredDistance(point, start));
        var ratio = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0.0, 1.0);
        var projected = new Editor2DPoint(start.X + ratio * dx, start.Y + ratio * dy);
        return Math.Sqrt(SquaredDistance(point, projected));
    }

    private static double SquaredDistance(Editor2DPoint left, Editor2DPoint right)
        => Math.Pow(right.X - left.X, 2) + Math.Pow(right.Y - left.Y, 2);

    private readonly record struct GridPoint(int X, int Y);
}
