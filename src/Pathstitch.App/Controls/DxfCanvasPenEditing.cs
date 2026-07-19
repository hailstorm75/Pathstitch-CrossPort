using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal static class DxfCanvasPenEditing
{
    public static bool TryRemoveAnchorAt(
        IReadOnlyList<Editor2DBezierAnchor> anchors,
        Point screenPoint,
        Func<Editor2DPoint, Point> worldToScreen,
        double tolerance,
        out IReadOnlyList<Editor2DBezierAnchor> updatedAnchors)
    {
        updatedAnchors = anchors;
        if (!double.IsFinite(tolerance) || tolerance < 0)
            return false;

        var nearestIndex = -1;
        var nearestDistance = tolerance;
        for (var index = 0; index < anchors.Count; index++)
        {
            var candidate = worldToScreen(anchors[index].Point);
            var distance = Math.Sqrt(
                Math.Pow(screenPoint.X - candidate.X, 2)
                + Math.Pow(screenPoint.Y - candidate.Y, 2));
            if (distance > nearestDistance)
                continue;

            nearestDistance = distance;
            nearestIndex = index;
        }

        if (nearestIndex < 0)
            return false;

        var next = anchors.ToList();
        next.RemoveAt(nearestIndex);
        updatedAnchors = next;
        return true;
    }

    public static Editor2DPoint? GetHandleInForHit(Editor2DBezierAnchor anchor)
        => anchor.HandleIn ?? (anchor.HandleOut is Editor2DPoint handleOut
            ? ReflectHandle(anchor.Point, handleOut)
            : null);

    public static bool TryInsertAnchorAt(
        IReadOnlyList<Editor2DBezierAnchor> anchors,
        Editor2DPoint point,
        bool isClosed,
        double tolerance,
        out IReadOnlyList<Editor2DBezierAnchor> updatedAnchors,
        out int insertedIndex)
    {
        updatedAnchors = anchors;
        insertedIndex = -1;
        if (anchors.Count < 2 || !double.IsFinite(tolerance) || tolerance < 0)
            return false;

        var segmentCount = isClosed ? anchors.Count : anchors.Count - 1;
        var bestDistance = tolerance;
        Editor2DPoint? bestPoint = null;
        var bestSegment = -1;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = anchors[index].Point;
            var end = anchors[(index + 1) % anchors.Count].Point;
            var candidate = ClosestPointOnSegment(point, start, end);
            var distance = Distance(point, candidate);
            if (distance > bestDistance)
                continue;

            bestDistance = distance;
            bestPoint = candidate;
            bestSegment = index;
        }

        if (bestSegment < 0)
            return false;

        var next = anchors.ToList();
        insertedIndex = bestSegment + 1;
        next.Insert(insertedIndex, new Editor2DBezierAnchor(bestPoint ?? throw new InvalidOperationException()));
        updatedAnchors = next;
        return true;
    }

    public static Editor2DBezierAnchor MoveAnchor(Editor2DBezierAnchor anchor, Editor2DPoint delta)
        => new(
            new Editor2DPoint(anchor.Point.X + delta.X, anchor.Point.Y + delta.Y),
            Translate(anchor.HandleIn, delta),
            Translate(anchor.HandleOut, delta));

    public static Editor2DBezierAnchor MoveHandleIn(Editor2DBezierAnchor anchor, Editor2DPoint handle)
        => anchor with
        {
            HandleIn = handle,
            HandleOut = ReflectHandle(anchor.Point, handle),
        };

    public static Editor2DBezierAnchor MoveHandleOut(Editor2DBezierAnchor anchor, Editor2DPoint handle)
        => anchor with { HandleOut = handle };

    public static Editor2DPreviewDocument ReplacePath(
        Editor2DPreviewDocument document,
        string pathId,
        IReadOnlyList<Editor2DBezierAnchor> anchors,
        bool isClosed)
    {
        var points = Editor2DBezierGeometry.Flatten(anchors, isClosed);
        return DxfCanvasGeometryEditor.Update(
            document,
            document.Paths.Select(path => !string.Equals(path.Id, pathId, StringComparison.Ordinal)
                ? path
                : path with
                {
                    Points = points,
                    IsClosed = isClosed,
                    IsAxisAlignedRectangle = Editor2DGeometry.IsAxisAlignedRectangle(points, isClosed),
                    BezierAnchors = anchors.ToArray(),
                }).ToArray());
    }

    private static Editor2DPoint? Translate(Editor2DPoint? point, Editor2DPoint delta)
        => point is Editor2DPoint resolved
            ? new Editor2DPoint(resolved.X + delta.X, resolved.Y + delta.Y)
            : null;

    private static Editor2DPoint ReflectHandle(Editor2DPoint anchor, Editor2DPoint handle)
        => new((2.0 * anchor.X) - handle.X, (2.0 * anchor.Y) - handle.Y);

    private static Editor2DPoint ClosestPointOnSegment(Editor2DPoint point, Editor2DPoint start, Editor2DPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 1e-12)
            return start;

        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0.0, 1.0);
        return new Editor2DPoint(start.X + (dx * t), start.Y + (dy * t));
    }

    private static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));
}
