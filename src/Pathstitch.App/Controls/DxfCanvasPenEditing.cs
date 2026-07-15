using System;
using System.Collections.Generic;
using System.Linq;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal static class DxfCanvasPenEditing
{
    public static Editor2DBezierAnchor MoveAnchor(Editor2DBezierAnchor anchor, Editor2DPoint delta)
        => new(
            new Editor2DPoint(anchor.Point.X + delta.X, anchor.Point.Y + delta.Y),
            Translate(anchor.HandleIn, delta),
            Translate(anchor.HandleOut, delta));

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
}
