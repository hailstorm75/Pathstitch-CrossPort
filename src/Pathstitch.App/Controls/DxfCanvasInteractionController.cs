using System;
using Avalonia;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal readonly record struct DxfCanvasViewportUpdate(double Zoom, double OffsetX, double OffsetY);
internal enum DxfCanvasPressRoute
{
    Selection, Move, Measure, Dimension, Trim, Corner, SketchLine, SketchRectangle,
    SketchCircle, SketchPolygon, SketchText, Pen, None,
}
internal enum DxfCanvasMoveRoute
{
    Pan, MoveSelection, ScaleSelection, EditVertex, LineDraft, RectangleDraft, CircleDraft,
    PolygonDraft, TextDraft, PenDraft, MirrorDraft, MeasurementDraft, DimensionDraft,
    ToolPreview, Marquee, Hover,
}
internal enum DxfCanvasReleaseRoute { Cancel, Pan, Context, MoveSelection, ScaleSelection, EditVertex, Selection, None }

/// <summary>Owns pointer-driven viewport and gesture transitions independently of the control.</summary>
internal sealed class DxfCanvasInteractionController(DxfCanvasInteractionSession session)
{
    private const double ZoomStep = 1.1;

    public DxfCanvasViewportUpdate ApplyWheel(
        Point screenPoint,
        Size viewport,
        double zoom,
        double offsetX,
        double offsetY,
        double wheelDelta)
    {
        var currentZoom = zoom <= 0 ? 1.0 : zoom;
        var nextZoom = Math.Clamp(
            currentZoom * (wheelDelta >= 0 ? ZoomStep : 1.0 / ZoomStep),
            0.02,
            2000.0);
        var world = DxfCanvasViewportTransform.ScreenToWorld(
            screenPoint, viewport, currentZoom, offsetX, offsetY);
        return new DxfCanvasViewportUpdate(
            nextZoom,
            screenPoint.X - (viewport.Width / 2.0) - (world.X * nextZoom),
            screenPoint.Y - (viewport.Height / 2.0) + (world.Y * nextZoom));
    }

    public void BeginPan(Point position)
    {
        session.CancelInteractionOnPointerRelease = false;
        session.IsPanning = true;
        session.LastPointerPosition = position;
        session.PointerPressPosition = position;
    }

    public Vector ContinuePan(Point position)
    {
        var delta = position - session.LastPointerPosition;
        session.LastPointerPosition = position;
        return delta;
    }

    public void EndPan() => session.IsPanning = false;

    public DxfCanvasPressRoute RoutePrimaryPress(Editor2DTool tool) => tool switch
    {
        Editor2DTool.Select or Editor2DTool.Scale or Editor2DTool.Mirror or
        Editor2DTool.ConvertLines or Editor2DTool.Offset or Editor2DTool.AddThickness or
        Editor2DTool.Cleanup or Editor2DTool.Patterning or Editor2DTool.PaperFolding or
        Editor2DTool.AddSewingHoles => DxfCanvasPressRoute.Selection,
        Editor2DTool.Move => DxfCanvasPressRoute.Move,
        Editor2DTool.Measure => DxfCanvasPressRoute.Measure,
        Editor2DTool.Dimension => DxfCanvasPressRoute.Dimension,
        Editor2DTool.Trim => DxfCanvasPressRoute.Trim,
        Editor2DTool.Fillet or Editor2DTool.Chamfer => DxfCanvasPressRoute.Corner,
        Editor2DTool.SketchLine => DxfCanvasPressRoute.SketchLine,
        Editor2DTool.SketchRectangle => DxfCanvasPressRoute.SketchRectangle,
        Editor2DTool.SketchCircle => DxfCanvasPressRoute.SketchCircle,
        Editor2DTool.SketchPolygon => DxfCanvasPressRoute.SketchPolygon,
        Editor2DTool.SketchText => DxfCanvasPressRoute.SketchText,
        Editor2DTool.Pen => DxfCanvasPressRoute.Pen,
        _ => DxfCanvasPressRoute.None,
    };

    public DxfCanvasMoveRoute RouteMove(Editor2DTool tool, bool capturedByCanvas)
    {
        if (session.IsPanning) return DxfCanvasMoveRoute.Pan;
        if (session.IsMovingSelection && session.MoveDocumentSnapshot is not null && session.MoveStartPoint is not null) return DxfCanvasMoveRoute.MoveSelection;
        if (session.IsScalingSelection && session.ScaleDocumentSnapshot is not null && session.ScaleCenterPoint is not null) return DxfCanvasMoveRoute.ScaleSelection;
        if (session.IsEditingVertex && session.EditingVertexPathId is not null) return DxfCanvasMoveRoute.EditVertex;
        if (tool == Editor2DTool.SketchLine && session.PendingLineStart is not null) return DxfCanvasMoveRoute.LineDraft;
        if (tool == Editor2DTool.SketchRectangle && session.PendingRectangleStart is not null) return DxfCanvasMoveRoute.RectangleDraft;
        if (tool == Editor2DTool.SketchCircle && session.PendingCircleCenter is not null) return DxfCanvasMoveRoute.CircleDraft;
        if (tool == Editor2DTool.SketchPolygon && session.PendingPolygonCenter is not null) return DxfCanvasMoveRoute.PolygonDraft;
        if (tool == Editor2DTool.SketchText && session.PendingTextStart is not null) return DxfCanvasMoveRoute.TextDraft;
        if (tool == Editor2DTool.Pen && session.PendingPenPoints.Count > 0) return DxfCanvasMoveRoute.PenDraft;
        if (tool == Editor2DTool.Mirror && session.PendingMirrorAxisStart is not null) return DxfCanvasMoveRoute.MirrorDraft;
        if (tool == Editor2DTool.Measure && session.PendingMeasurementStart is not null) return DxfCanvasMoveRoute.MeasurementDraft;
        if (tool == Editor2DTool.Dimension && session.PendingDimensionStart is not null) return DxfCanvasMoveRoute.DimensionDraft;
        if (tool is Editor2DTool.Trim or Editor2DTool.Fillet or Editor2DTool.Chamfer) return DxfCanvasMoveRoute.ToolPreview;
        if (capturedByCanvas && (tool is Editor2DTool.Select or Editor2DTool.ConvertLines or Editor2DTool.Offset or
            Editor2DTool.AddThickness or Editor2DTool.Cleanup or Editor2DTool.Patterning or
            Editor2DTool.PaperFolding or Editor2DTool.AddSewingHoles) &&
            session.MarqueeStartPoint is not null) return DxfCanvasMoveRoute.Marquee;
        return DxfCanvasMoveRoute.Hover;
    }

    public DxfCanvasReleaseRoute RouteRelease(Editor2DTool tool, bool capturedByCanvas)
    {
        if (capturedByCanvas && session.CancelInteractionOnPointerRelease) return DxfCanvasReleaseRoute.Cancel;
        if (session.IsPanning) return DxfCanvasReleaseRoute.Pan;
        if (session.IsAwaitingSecondaryContextClick) return DxfCanvasReleaseRoute.Context;
        if (session.IsMovingSelection) return DxfCanvasReleaseRoute.MoveSelection;
        if (session.IsScalingSelection) return DxfCanvasReleaseRoute.ScaleSelection;
        if (session.IsEditingVertex) return DxfCanvasReleaseRoute.EditVertex;
        return capturedByCanvas && RoutePrimaryPress(tool) == DxfCanvasPressRoute.Selection
            ? DxfCanvasReleaseRoute.Selection : DxfCanvasReleaseRoute.None;
    }
}
