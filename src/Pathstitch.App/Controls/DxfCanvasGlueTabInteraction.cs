using System;
using Avalonia;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal enum DxfCanvasGlueTabHandle
{
    None,
    Start,
    End,
}

internal readonly record struct DxfCanvasGlueTabHandles(
    Editor2DPoint Start,
    Editor2DPoint End,
    double LineLength);

internal static class DxfCanvasGlueTabInteraction
{
    public static bool TryGetHandles(
        Editor2DPreviewPath path,
        double startOffset,
        double endOffset,
        out DxfCanvasGlueTabHandles handles)
    {
        handles = default;
        if (!Editor2DGeometry.IsGlueTabSourcePath(path) || path.Points.Count < 2)
            return false;

        var start = path.Start ?? path.Points[0];
        var end = path.Points[^1];
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length < 1.0)
            return false;

        var normalizedStart = Math.Clamp(startOffset, 0.0, Math.Max(0.0, length - 1.0));
        var normalizedEnd = Math.Clamp(endOffset, 0.0, Math.Max(0.0, length - normalizedStart - 1.0));
        var unitX = deltaX / length;
        var unitY = deltaY / length;
        handles = new DxfCanvasGlueTabHandles(
            new Editor2DPoint(start.X + (normalizedStart * unitX), start.Y + (normalizedStart * unitY)),
            new Editor2DPoint(end.X - (normalizedEnd * unitX), end.Y - (normalizedEnd * unitY)),
            length);
        return true;
    }

    public static DxfCanvasGlueTabHandle HitTest(
        Point pointer,
        Point startHandle,
        Point endHandle,
        double tolerance)
    {
        if (tolerance < 0.0)
            return DxfCanvasGlueTabHandle.None;
        if (Distance(pointer, startHandle) <= tolerance)
            return DxfCanvasGlueTabHandle.Start;
        return Distance(pointer, endHandle) <= tolerance
            ? DxfCanvasGlueTabHandle.End
            : DxfCanvasGlueTabHandle.None;
    }

    public static double ProjectOffset(
        Editor2DPreviewPath path,
        Editor2DPoint pointer,
        DxfCanvasGlueTabHandle handle,
        double otherOffset)
    {
        if (handle == DxfCanvasGlueTabHandle.None || path.Points.Count < 2)
            return 0.0;

        var start = path.Start ?? path.Points[0];
        var end = path.Points[^1];
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length <= 1e-9)
            return 0.0;
        var unitX = deltaX / length;
        var unitY = deltaY / length;
        var projection = handle == DxfCanvasGlueTabHandle.Start
            ? ((pointer.X - start.X) * unitX) + ((pointer.Y - start.Y) * unitY)
            : ((end.X - pointer.X) * unitX) + ((end.Y - pointer.Y) * unitY);
        return Math.Clamp(projection, 0.0, Math.Max(0.0, length - Math.Max(0.0, otherOffset) - 1.0));
    }

    private static double Distance(Point left, Point right)
        => Math.Sqrt(Math.Pow(right.X - left.X, 2) + Math.Pow(right.Y - left.Y, 2));
}
