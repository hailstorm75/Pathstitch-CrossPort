using System;
using Avalonia;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal sealed record DxfCanvasWorldBounds(double Left, double Right, double Bottom, double Top);

internal static class DxfCanvasViewportTransform
{
    public static Point WorldToScreen(
        Editor2DPoint point,
        Size viewportSize,
        double zoom,
        double offsetX,
        double offsetY)
        => new(
            (viewportSize.Width / 2.0) + offsetX + (point.X * zoom),
            (viewportSize.Height / 2.0) + offsetY - (point.Y * zoom));

    public static Editor2DPoint ScreenToWorld(
        Point point,
        Size viewportSize,
        double zoom,
        double offsetX,
        double offsetY)
    {
        var resolvedZoom = Math.Max(zoom, 0.0001);
        return new Editor2DPoint(
            (point.X - (viewportSize.Width / 2.0) - offsetX) / resolvedZoom,
            ((viewportSize.Height / 2.0) + offsetY - point.Y) / resolvedZoom);
    }

    public static double CalculateWorldStep(double targetPixels, double zoom)
    {
        var raw = targetPixels / Math.Max(zoom, 0.0001);
        var magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var factor = normalized switch
        {
            <= 1.0 => 1.0,
            <= 2.0 => 2.0,
            <= 5.0 => 5.0,
            _ => 10.0,
        };
        return factor * magnitude;
    }

    public static DxfCanvasWorldBounds VisibleWorldBounds(
        Size viewportSize,
        double zoom,
        double offsetX,
        double offsetY)
    {
        var topLeft = ScreenToWorld(new Point(0, 0), viewportSize, zoom, offsetX, offsetY);
        var bottomRight = ScreenToWorld(
            new Point(viewportSize.Width, viewportSize.Height),
            viewportSize,
            zoom,
            offsetX,
            offsetY);
        return new(
            Math.Min(topLeft.X, bottomRight.X),
            Math.Max(topLeft.X, bottomRight.X),
            Math.Min(topLeft.Y, bottomRight.Y),
            Math.Max(topLeft.Y, bottomRight.Y));
    }
}
