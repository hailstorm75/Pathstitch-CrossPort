using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal enum DxfCanvasTranslationHandle
{
    None,
    Free,
    X,
    Y,
}

internal readonly record struct DxfCanvasTranslationHandleGeometry(
    Point Free,
    Point X,
    Point Y);

internal static class DxfCanvasTranslationInteraction
{
    internal const double AxisHandleDistancePixels = 70.0;

    public static DxfCanvasTranslationHandleGeometry GetHandleGeometry(Point pivot)
        => new(
            Free: pivot,
            X: new Point(pivot.X + AxisHandleDistancePixels, pivot.Y),
            Y: new Point(pivot.X, pivot.Y - AxisHandleDistancePixels));

    public static DxfCanvasTranslationHandle HitTest(
        Point pointer,
        DxfCanvasTranslationHandleGeometry handles,
        double tolerancePixels)
    {
        if (!double.IsFinite(tolerancePixels) || tolerancePixels < 0.0)
            return DxfCanvasTranslationHandle.None;

        if (IsWithinTolerance(pointer, handles.Free, tolerancePixels))
            return DxfCanvasTranslationHandle.Free;
        if (IsWithinTolerance(pointer, handles.X, tolerancePixels))
            return DxfCanvasTranslationHandle.X;
        if (IsWithinTolerance(pointer, handles.Y, tolerancePixels))
            return DxfCanvasTranslationHandle.Y;
        return DxfCanvasTranslationHandle.None;
    }

    public static Editor2DPoint ScreenToWorldDelta(
        Vector screenDelta,
        double zoom,
        DxfCanvasTranslationHandle handle)
    {
        if (!double.IsFinite(zoom) || zoom <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(zoom), "Zoom must be finite and greater than zero.");

        var deltaX = screenDelta.X / zoom;
        var deltaY = -screenDelta.Y / zoom;
        return handle switch
        {
            DxfCanvasTranslationHandle.Free => new Editor2DPoint(deltaX, deltaY),
            DxfCanvasTranslationHandle.X => new Editor2DPoint(deltaX, 0.0),
            DxfCanvasTranslationHandle.Y => new Editor2DPoint(0.0, deltaY),
            _ => new Editor2DPoint(0.0, 0.0),
        };
    }

    public static bool TryGetSelectionPivot(
        IReadOnlyList<Editor2DPreviewPath> paths,
        IReadOnlyList<string> selectedIds,
        out Editor2DPoint pivot)
    {
        var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        var points = paths
            .Where(path => selected.Contains(path.Id))
            .SelectMany(path => path.Points.Count > 0
                ? path.Points
                : new[] { path.Start, path.Center }.OfType<Editor2DPoint>())
            .ToArray();
        if (points.Length == 0)
        {
            pivot = new Editor2DPoint(0, 0);
            return false;
        }

        pivot = new Editor2DPoint(
            points.Average(point => point.X),
            points.Average(point => point.Y));
        return true;
    }

    public static bool ShouldCommit(
        double dragDistancePixels,
        double minimumDragPixels,
        Editor2DPoint delta)
        => double.IsFinite(dragDistancePixels)
            && double.IsFinite(minimumDragPixels)
            && dragDistancePixels >= minimumDragPixels
            && (Math.Abs(delta.X) > 1e-12 || Math.Abs(delta.Y) > 1e-12);

    private static bool IsWithinTolerance(Point pointer, Point handle, double tolerancePixels)
    {
        var deltaX = pointer.X - handle.X;
        var deltaY = pointer.Y - handle.Y;
        return (deltaX * deltaX) + (deltaY * deltaY) <= tolerancePixels * tolerancePixels;
    }
}
