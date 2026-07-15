using System;
using System.Collections.Generic;
using Avalonia;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal sealed class DxfCanvasInteractionSession
{
    internal enum PenDragControl
    {
        Anchor,
        HandleIn,
        HandleOut,
    }

    internal bool IsPanning;
    internal bool IsMarqueeSelecting;
    internal Point LastPointerPosition;
    internal Point PointerPressPosition;
    internal string? HoveredPathId;
    internal string? PressedPathId;
    internal Point? MarqueeStartPoint;
    internal Point? MarqueeCurrentPoint;
    internal Point HoverPointerPosition;
    internal bool HasHoverPointerPosition;
    internal bool CancelInteractionOnPointerRelease;
    internal bool IsMovingSelection;
    internal bool IsScalingSelection;
    internal bool IsAwaitingSecondaryContextClick;
    internal bool IsEditingVertex;
    internal Editor2DPreviewDocument? MoveDocumentSnapshot;
    internal Editor2DPreviewDocument? ScaleDocumentSnapshot;
    internal IReadOnlyList<string> MoveSelectionIds = Array.Empty<string>();
    internal IReadOnlyList<string> ScaleSelectionIds = Array.Empty<string>();
    internal Editor2DPoint? MoveStartPoint;
    internal Editor2DPoint? ScaleCenterPoint;
    internal double ScaleStartDistance;
    internal double ScalePreviewFactor = 1.0;
    internal string? EditingVertexPathId;
    internal int EditingVertexIndex;
    internal bool EditingVertexIsConstrainedRectangle;
    internal string? EditingPenPathId;
    internal bool EditingPenClosed;
    internal Editor2DPoint? PendingLineStart;
    internal Editor2DPoint? PendingLineEnd;
    internal Editor2DPoint? PendingRectangleStart;
    internal Editor2DPoint? PendingRectangleEnd;
    internal Editor2DPoint? PendingCircleCenter;
    internal Editor2DPoint? PendingCircleEdge;
    internal Editor2DPoint? PendingPolygonCenter;
    internal Editor2DPoint? PendingPolygonEdge;
    internal Editor2DPoint? PendingTextStart;
    internal Editor2DPoint? PendingTextEnd;
    internal IReadOnlyList<Editor2DBezierAnchor> PendingPenAnchors = Array.Empty<Editor2DBezierAnchor>();
    internal Editor2DPoint? PendingPenHoverPoint;
    internal int? PendingPenDragAnchorIndex;
    internal PenDragControl PendingPenDragControl = PenDragControl.Anchor;
    internal Editor2DPoint? PendingMirrorAxisStart;
    internal Editor2DPoint? PendingMirrorAxisEnd;
    internal Editor2DPoint? PendingMeasurementStart;
    internal Editor2DPoint? PendingMeasurementEnd;
    internal Editor2DPoint? PendingDimensionStart;
    internal Editor2DPoint? PendingDimensionEnd;
    internal bool PendingFrameToDocument;
    internal double? CornerToolSessionValue;

    public bool HasActivePointerGesture => IsPanning || IsMarqueeSelecting;

    public void ResetPointerGesture()
    {
        IsPanning = false;
        IsMarqueeSelecting = false;
        PressedPathId = null;
        MarqueeStartPoint = null;
        MarqueeCurrentPoint = null;
        CancelInteractionOnPointerRelease = false;
    }

    public void ResetToolDrafts()
    {
        IsMovingSelection = false;
        IsScalingSelection = false;
        IsEditingVertex = false;
        MoveDocumentSnapshot = null;
        ScaleDocumentSnapshot = null;
        MoveSelectionIds = Array.Empty<string>();
        ScaleSelectionIds = Array.Empty<string>();
        MoveStartPoint = null;
        ScaleCenterPoint = null;
        ScaleStartDistance = 0;
        ScalePreviewFactor = 1.0;
        EditingVertexPathId = null;
        EditingPenPathId = null;
        EditingPenClosed = false;
        PendingLineStart = null;
        PendingLineEnd = null;
        PendingRectangleStart = null;
        PendingRectangleEnd = null;
        PendingCircleCenter = null;
        PendingCircleEdge = null;
        PendingPolygonCenter = null;
        PendingPolygonEdge = null;
        PendingTextStart = null;
        PendingTextEnd = null;
        PendingPenAnchors = Array.Empty<Editor2DBezierAnchor>();
        PendingPenHoverPoint = null;
        PendingPenDragAnchorIndex = null;
        PendingPenDragControl = PenDragControl.Anchor;
        PendingMirrorAxisStart = null;
        PendingMirrorAxisEnd = null;
        PendingMeasurementStart = null;
        PendingMeasurementEnd = null;
        PendingDimensionStart = null;
        PendingDimensionEnd = null;
        CornerToolSessionValue = null;
    }
}
