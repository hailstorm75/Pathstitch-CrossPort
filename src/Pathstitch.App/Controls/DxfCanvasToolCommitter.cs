using System;
using System.Collections.Generic;
using System.Linq;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

/// <summary>Creates committed tool geometry without depending on Avalonia input or control state.</summary>
internal sealed class DxfCanvasToolCommitter(Func<string> idFactory)
{
    private const double MinimumSize = 1e-6;

    public DxfCanvasToolCommitter() : this(() => Guid.NewGuid().ToString("N")) { }

    public Editor2DPreviewDocument? Line(Editor2DPreviewDocument? document, Editor2DPoint start, Editor2DPoint end)
        => Append(document, DxfCanvasGeometryEditor.Distance(start, end) <= MinimumSize ? null :
            new($"line-{idFactory()}", "LINE", [start, end], false));

    public Editor2DPreviewDocument? Rectangle(Editor2DPreviewDocument? document, Editor2DPoint start, Editor2DPoint end)
    {
        if (Math.Abs(end.X - start.X) <= MinimumSize || Math.Abs(end.Y - start.Y) <= MinimumSize)
            return null;
        return Append(document, new($"rectangle-{idFactory()}", "LWPOLYLINE",
            DxfCanvasGeometryEditor.BuildRectangle(start, end), true, true));
    }

    public Editor2DPreviewDocument? Circle(Editor2DPreviewDocument? document, Editor2DPoint center, Editor2DPoint edge)
    {
        var radius = DxfCanvasGeometryEditor.Distance(center, edge);
        if (radius <= MinimumSize)
            return null;
        var points = DxfCanvasGeometryEditor.BuildCircle(center, radius);
        return Append(document, new($"circle-{idFactory()}", "CIRCLE", points, true, false,
            Center: center, Radius: radius, StartAngleDegrees: 0, EndAngleDegrees: 360));
    }

    public Editor2DPreviewDocument? Polygon(Editor2DPreviewDocument? document, Editor2DPoint center, Editor2DPoint edge, int sides)
    {
        if (sides < 3 || DxfCanvasGeometryEditor.Distance(center, edge) <= MinimumSize)
            return null;
        var points = DxfCanvasGeometryEditor.BuildPolygon(center, edge, sides);
        return Append(document, new($"polygon-{idFactory()}", "LWPOLYLINE", points, true));
    }

    public Editor2DPreviewDocument? Text(Editor2DPreviewDocument? document, Editor2DPoint start, Editor2DPoint end, string value)
    {
        var height = Math.Abs(end.Y - start.Y);
        var width = Math.Abs(end.X - start.X);
        if (height <= MinimumSize || width <= MinimumSize)
            return null;
        var insertion = new Editor2DPoint(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y));
        var naturalWidth = Math.Max(value.Length * height * .6, height * .6);
        var widthFactor = Math.Max(width / naturalWidth, .1);
        return Append(document, new($"text-{idFactory()}", "TEXT",
            Editor2DGeometry.BuildTextBoundsPoints(insertion, value, height, widthFactor: widthFactor), false,
            Start: insertion, Text: value, TextHeight: height, RotationDegrees: 0, WidthFactor: widthFactor));
    }

    public Editor2DPreviewDocument? Pen(Editor2DPreviewDocument? document, IReadOnlyList<Editor2DBezierAnchor> anchors, bool closed)
    {
        if (anchors.Count < 2)
            return null;
        var points = Editor2DBezierGeometry.Flatten(anchors, closed);
        return Append(document, new($"pen-{idFactory()}", "LWPOLYLINE", points, closed,
            Editor2DGeometry.IsAxisAlignedRectangle(points, closed), BezierAnchors: anchors.ToArray()));
    }

    private static Editor2DPreviewDocument? Append(Editor2DPreviewDocument? document, Editor2DPreviewPath? path)
    {
        if (document is null || path is null)
            return null;
        return DxfCanvasGeometryEditor.Update(document, [.. document.Paths, path]);
    }
}
