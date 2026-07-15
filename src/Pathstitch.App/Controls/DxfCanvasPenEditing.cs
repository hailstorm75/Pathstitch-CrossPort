using System;
using System.Collections.Generic;
using System.Linq;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal static class DxfCanvasPenEditing
{
    public static Editor2DPoint? GetHandleInForHit(Editor2DBezierAnchor anchor)
        => anchor.HandleIn ?? (anchor.HandleOut is Editor2DPoint handleOut
            ? ReflectHandle(anchor.Point, handleOut)
            : null);

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
}
