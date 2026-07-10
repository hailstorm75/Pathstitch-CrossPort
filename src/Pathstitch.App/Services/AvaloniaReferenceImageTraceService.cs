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
        double threshold)
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
        var cutoff = Math.Clamp(threshold, 0.0, 1.0) * 255.0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = pixels[(y * bitmap.Width) + x];
                var luminance = (0.2126 * pixel.Red) + (0.7152 * pixel.Green) + (0.0722 * pixel.Blue);
                foreground[(y * bitmap.Width) + x] = pixel.Alpha > 0 && luminance <= cutoff;
            }
        }

        return TracePixelBoundaries(foreground, bitmap.Width, bitmap.Height);
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

    private readonly record struct GridPoint(int X, int Y);
}
