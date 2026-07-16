using System.Reflection;
using Avalonia;
using Domain.App.Models;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Tests;

public sealed class DxfCanvasCollaboratorTests
{
    [Fact]
    public void ViewportTransform_RoundTripsWorldCoordinatesAndCalculatesStableGridStep()
    {
        var size = new Size(800, 600);
        var world = new Editor2DPoint(12.5, -7.25);

        var screen = DxfCanvasViewportTransform.WorldToScreen(world, size, 2.5, 14, -9);
        var restored = DxfCanvasViewportTransform.ScreenToWorld(screen, size, 2.5, 14, -9);

        Assert.InRange(Math.Abs(restored.X - world.X), 0, 1e-9);
        Assert.InRange(Math.Abs(restored.Y - world.Y), 0, 1e-9);
        Assert.Equal(20, DxfCanvasViewportTransform.CalculateWorldStep(40, 2.5));
    }

    [Fact]
    public void HitTester_HandlesSegmentsClosedPathsAndPolygonContainment()
    {
        var square = new[]
        {
            new Point(0, 0),
            new Point(10, 0),
            new Point(10, 10),
            new Point(0, 10),
        };

        Assert.Equal(2, DxfCanvasHitTester.DistanceToSegment(new Point(5, 2), square[0], square[1]), 6);
        Assert.Equal(2, DxfCanvasHitTester.DistanceToPath(new Point(5, 2), square, isClosed: false), 6);
        Assert.True(DxfCanvasHitTester.PointInPolygon(new Point(5, 5), square));
        Assert.False(DxfCanvasHitTester.PointInPolygon(new Point(12, 5), square));
    }

    [Fact]
    public void RenderPlanner_FiltersHiddenPathsAndResolvesVisualPriority()
    {
        var open = Path("open", false, new(0, 0), new(10, 0));
        var closed = Path("closed", true, new(0, 0), new(10, 0), new(10, 10));
        var construction = Path("construction", false, new(0, 5), new(10, 5)) with { IsConstruction = true };
        var planner = new DxfCanvasRenderPlanner();
        var document = Document(open, closed, construction);

        Assert.Equal([open, construction], planner.VisiblePaths(document, ["closed"]));
        Assert.Equal(DxfCanvasPathVisualRole.Selected, planner.ResolvePathRole(open, new HashSet<string> { "open" }, "open"));
        Assert.Equal(DxfCanvasPathVisualRole.Hovered, planner.ResolvePathRole(open, new HashSet<string>(), "open"));
        Assert.Equal(DxfCanvasPathVisualRole.Closed, planner.ResolvePathRole(closed, new HashSet<string>(), null));
        Assert.Equal(DxfCanvasPathVisualRole.Construction, planner.ResolvePathRole(construction, new HashSet<string>(), null));
        Assert.Equal(DxfCanvasPathVisualRole.Hovered, planner.ResolvePathRole(construction, new HashSet<string>(), "construction"));
        Assert.Equal(DxfCanvasPathVisualRole.Selected, planner.ResolvePathRole(construction, new HashSet<string> { "construction" }, "construction"));
    }

    [Fact]
    public void Canvas_DefinesDashedGrayPenForUnselectedConstructionPaths()
    {
        var source = File.ReadAllText(RepositoryFile(
            "src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs"));

        Assert.Contains("Color.Parse(\"#8A909B\")", source, StringComparison.Ordinal);
        Assert.Contains("1.2, dashStyle: new DashStyle([6, 4], 0)", source, StringComparison.Ordinal);
        Assert.Contains("DxfCanvasPathVisualRole.Construction => ConstructionPathPen", source, StringComparison.Ordinal);
        Assert.Contains("path.IsConstruction ? null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractionSession_OwnsAndResetsTransientPointerGesture()
    {
        var session = new DxfCanvasInteractionSession
        {
            IsPanning = true,
            IsMarqueeSelecting = true,
            PressedPathId = "path",
            MarqueeStartPoint = new Point(1, 1),
            MarqueeCurrentPoint = new Point(5, 5),
            CancelInteractionOnPointerRelease = true,
            IsMovingSelection = true,
            PendingLineStart = new Editor2DPoint(1, 1),
            EditingVertexPathId = "path",
            EditingPenPathId = "pen-path",
            EditingPenClosed = true,
        };

        Assert.True(session.HasActivePointerGesture);
        session.ResetPointerGesture();

        Assert.False(session.HasActivePointerGesture);
        Assert.Null(session.PressedPathId);
        Assert.Null(session.MarqueeStartPoint);
        Assert.False(session.CancelInteractionOnPointerRelease);
        session.ResetToolDrafts();
        Assert.False(session.IsMovingSelection);
        Assert.Null(session.PendingLineStart);
        Assert.Null(session.EditingVertexPathId);
        Assert.Null(session.EditingPenPathId);
        Assert.False(session.EditingPenClosed);
    }

    [Fact]
    public void InteractionController_ZoomsAroundPointerAndOwnsPanLifecycle()
    {
        var session = new DxfCanvasInteractionSession();
        var controller = new DxfCanvasInteractionController(session);
        var viewport = new Size(800, 600);
        var pointer = new Point(250, 175);
        var worldBefore = DxfCanvasViewportTransform.ScreenToWorld(pointer, viewport, 2, 12, -8);

        var update = controller.ApplyWheel(pointer, viewport, 2, 12, -8, 1);
        var worldAfter = DxfCanvasViewportTransform.ScreenToWorld(
            pointer, viewport, update.Zoom, update.OffsetX, update.OffsetY);

        Assert.Equal(2.2, update.Zoom, 8);
        Assert.InRange(Math.Abs(worldBefore.X - worldAfter.X), 0, 1e-9);
        Assert.InRange(Math.Abs(worldBefore.Y - worldAfter.Y), 0, 1e-9);

        controller.BeginPan(new Point(10, 20));
        Assert.True(session.IsPanning);
        Assert.Equal(new Vector(5, -3), controller.ContinuePan(new Point(15, 17)));
        controller.EndPan();
        Assert.False(session.IsPanning);
    }

    [Fact]
    public void InteractionController_AppliesPinchZoomAndPrecisePan()
    {
        var session = new DxfCanvasInteractionSession();
        var controller = new DxfCanvasInteractionController(session);
        var viewport = new Size(800, 600);
        var anchor = new Point(300, 200);
        var before = DxfCanvasViewportTransform.ScreenToWorld(anchor, viewport, 2, 12, -8);

        var zoomed = controller.ApplyZoom(anchor, viewport, 2, 12, -8, 1.25);
        var after = DxfCanvasViewportTransform.ScreenToWorld(anchor, viewport, zoomed.Zoom, zoomed.OffsetX, zoomed.OffsetY);

        Assert.Equal(2.5, zoomed.Zoom, 8);
        Assert.InRange(Math.Abs(before.X - after.X), 0, 1e-9);
        Assert.InRange(Math.Abs(before.Y - after.Y), 0, 1e-9);

        var panned = controller.ApplyPan(zoomed.Zoom, zoomed.OffsetX, zoomed.OffsetY, new Vector(7, -4), reverseVertical: false);
        Assert.Equal(zoomed.Zoom, panned.Zoom);
        Assert.Equal(zoomed.OffsetX + 7, panned.OffsetX);
        Assert.Equal(zoomed.OffsetY - 4, panned.OffsetY);
    }

    [Fact]
    public void InteractionController_RoutesPrimaryToolPressesWithoutControlInputEvents()
    {
        var session = new DxfCanvasInteractionSession();
        var controller = new DxfCanvasInteractionController(session);

        Assert.Equal(DxfCanvasPressRoute.Selection, controller.RoutePrimaryPress(Editor2DTool.Select));
        Assert.Equal(DxfCanvasPressRoute.Move, controller.RoutePrimaryPress(Editor2DTool.Move));
        Assert.Equal(DxfCanvasPressRoute.SketchRectangle, controller.RoutePrimaryPress(Editor2DTool.SketchRectangle));
        Assert.Equal(DxfCanvasPressRoute.Corner, controller.RoutePrimaryPress(Editor2DTool.Fillet));
        Assert.Equal(DxfCanvasPressRoute.Selection, controller.RoutePrimaryPress(Editor2DTool.AddSewingHoles));

        session.PendingLineStart = new Editor2DPoint(0, 0);
        Assert.Equal(DxfCanvasMoveRoute.LineDraft, controller.RouteMove(Editor2DTool.SketchLine, false));
        session.PendingLineStart = null;
        session.IsMovingSelection = true;
        session.MoveDocumentSnapshot = Document();
        session.MoveStartPoint = new Editor2DPoint(0, 0);
        Assert.Equal(DxfCanvasMoveRoute.MoveSelection, controller.RouteMove(Editor2DTool.Move, true));
        Assert.Equal(DxfCanvasReleaseRoute.MoveSelection, controller.RouteRelease(Editor2DTool.Move, true));

        session.IsMovingSelection = false;
        session.MoveDocumentSnapshot = null;
        session.IsRotatingSelection = true;
        session.RotateDocumentSnapshot = Document();
        session.RotatePivot = new Editor2DPoint(0, 0);
        Assert.Equal(DxfCanvasMoveRoute.RotateSelection, controller.RouteMove(Editor2DTool.Select, true));
        Assert.Equal(DxfCanvasReleaseRoute.RotateSelection, controller.RouteRelease(Editor2DTool.Select, true));

        session.IsRotatingSelection = false;
        session.RotateDocumentSnapshot = null;
        session.RotatePivot = null;
        session.IsTranslatingSelection = true;
        session.TranslateDocumentSnapshot = Document();
        session.TranslatePivot = new Editor2DPoint(0, 0);
        Assert.Equal(DxfCanvasMoveRoute.TranslateSelection, controller.RouteMove(Editor2DTool.Select, true));
        Assert.Equal(DxfCanvasReleaseRoute.TranslateSelection, controller.RouteRelease(Editor2DTool.Select, true));

        session.IsTranslatingSelection = false;
        session.TranslateDocumentSnapshot = null;
        session.TranslatePivot = null;
        session.IsDraggingCorner = true;
        Assert.Equal(DxfCanvasMoveRoute.Corner, controller.RouteMove(Editor2DTool.Fillet, true));
        Assert.Equal(DxfCanvasReleaseRoute.Corner, controller.RouteRelease(Editor2DTool.Fillet, true));

        session.IsDraggingCorner = false;
        session.IsDraggingSewingHoleMargin = true;
        Assert.Equal(DxfCanvasMoveRoute.SewingHoleMargin, controller.RouteMove(Editor2DTool.AddSewingHoles, true));
        Assert.Equal(DxfCanvasReleaseRoute.SewingHoleMargin, controller.RouteRelease(Editor2DTool.AddSewingHoles, true));

        session.IsDraggingSewingHoleMargin = false;
        session.IsDraggingOffsetHandle = true;
        Assert.Equal(DxfCanvasMoveRoute.OffsetHandle, controller.RouteMove(Editor2DTool.Offset, true));
        Assert.Equal(DxfCanvasReleaseRoute.OffsetHandle, controller.RouteRelease(Editor2DTool.Offset, true));
    }

    [Theory]
    [InlineData((int)Editor2DTool.Select, true)]
    [InlineData((int)Editor2DTool.Move, true)]
    [InlineData((int)Editor2DTool.Measure, true)]
    [InlineData((int)Editor2DTool.Fillet, false)]
    [InlineData((int)Editor2DTool.Chamfer, false)]
    [InlineData((int)Editor2DTool.Scale, false)]
    [InlineData((int)Editor2DTool.Offset, false)]
    [InlineData((int)Editor2DTool.Pan, false)]
    public void SelectionInteraction_ShowsTransformGizmoOutsideConflictingTools(int toolValue, bool expected)
    {
        var tool = (Editor2DTool)toolValue;

        Assert.Equal(expected, DxfCanvasSelectionInteraction.ShouldShowTransformGizmo(tool, true, false));
        Assert.False(DxfCanvasSelectionInteraction.ShouldShowTransformGizmo(tool, false, false));
        Assert.False(DxfCanvasSelectionInteraction.ShouldShowTransformGizmo(tool, true, true));
    }

    [Fact]
    public void ToolCommitter_BuildsCanonicalGeometryAndRejectsDegenerateDrafts()
    {
        var committer = new DxfCanvasToolCommitter(() => "stable");
        var document = Document();

        Assert.Null(committer.Line(document, new(1, 1), new(1, 1)));
        var rectangle = committer.Rectangle(document, new(1, 2), new(5, 8));
        var path = Assert.Single(rectangle!.Paths);
        Assert.Equal("rectangle-stable", path.Id);
        Assert.True(path.IsClosed);
        Assert.True(path.IsAxisAlignedRectangle);
        Assert.Equal(4, path.Points.Count);

        var circle = committer.Circle(document, new(0, 0), new(0, 2));
        Assert.Equal(2, Assert.Single(circle!.Paths).Radius);
        var text = committer.Text(document, new(0, 0), new(10, 2), "Label");
        Assert.Equal("Label", Assert.Single(text!.Paths).Text);
    }

    [Fact]
    public void PenEditing_ReplacesExistingPathAndPreservesItsIdentity()
    {
        var anchors = new[]
        {
            new Editor2DBezierAnchor(new(0, 0)),
            new Editor2DBezierAnchor(new(10, 0)),
            new Editor2DBezierAnchor(new(10, 10)),
        };
        var path = new Editor2DPreviewPath(
            "pen-existing",
            "LWPOLYLINE",
            Editor2DBezierGeometry.Flatten(anchors, closed: true),
            true,
            BezierAnchors: anchors);
        var document = Document(path);
        var movedAnchors = anchors.Select(anchor => DxfCanvasPenEditing.MoveAnchor(anchor, new(2, 3))).ToArray();

        var edited = DxfCanvasPenEditing.ReplacePath(document, path.Id, movedAnchors, isClosed: true);

        var editedPath = Assert.Single(edited.Paths);
        Assert.Equal(path.Id, editedPath.Id);
        Assert.Equal(new Editor2DPoint(2, 3), editedPath.BezierAnchors![0].Point);
        Assert.Equal(new Editor2DPoint(2, 3), editedPath.Points[0]);
        Assert.True(editedPath.IsClosed);
    }

    [Fact]
    public void PenEditing_HandleDragUpdatesHandleWithoutMovingAnchor()
    {
        var anchor = new Editor2DBezierAnchor(new(10, 10), new(7, 10), new(13, 10));

        var movedOut = DxfCanvasPenEditing.MoveHandleOut(anchor, new(18, 14));
        var movedIn = DxfCanvasPenEditing.MoveHandleIn(anchor, new(4, 6));

        Assert.Equal(anchor.Point, movedOut.Point);
        Assert.Equal(new Editor2DPoint(18, 14), movedOut.HandleOut);
        Assert.Equal(new Editor2DPoint(4, 6), movedIn.HandleIn);
        Assert.Equal(new Editor2DPoint(16, 14), movedIn.HandleOut);
    }

    [Fact]
    public void MeasurementEditing_MovesOnlyRequestedEndpoint()
    {
        var measurement = new Editor2DMeasurement(
            "m", new(0, 0), new(10, 0), VarName: "d1", Expression: "10",
            IsParametric: true, EvaluatedValue: 10);

        var movedStart = DxfCanvasMeasurementEditing.MoveEndpoint(measurement, new(2, 3), start: true);
        var movedEnd = DxfCanvasMeasurementEditing.MoveEndpoint(measurement, new(12, 4), start: false);

        Assert.Equal(new Editor2DPoint(2, 3), movedStart.Start);
        Assert.Equal(measurement.End, movedStart.End);
        Assert.Equal(measurement.Start, movedEnd.Start);
        Assert.Equal(new Editor2DPoint(12, 4), movedEnd.End);
        Assert.Null(movedStart.EvaluatedValue);
        Assert.Null(movedEnd.EvaluatedValue);
    }

    [Fact]
    public void GeometryEditor_TransformsOnlySelectedPathsAndRecalculatesDocumentMetadata()
    {
        var selected = Path("selected", false, new(0, 0), new(10, 0));
        var untouched = Path("untouched", false, new(0, 10), new(10, 10));
        var document = Document(selected, untouched);

        var translated = DxfCanvasGeometryEditor.Translate(document, ["selected"], 5, 2);
        var moved = Assert.Single(translated.Paths, path => path.Id == "selected");

        Assert.Equal(new Editor2DPoint(5, 2), moved.Points[0]);
        Assert.Equal(new Editor2DPoint(0, 10), Assert.Single(translated.Paths, path => path.Id == "untouched").Points[0]);
        Assert.Equal(0, translated.Bounds.MinX);
        Assert.Equal(15, translated.Bounds.MaxX);
        Assert.Equal(2, translated.EntityCounts["LINE"]);
    }

    [Fact]
    public void RotationInteraction_UsesFixedHandleAndScreenCardinalAngles()
    {
        var pivot = new Point(250, 200);

        var topHandle = DxfCanvasRotationInteraction.GetHandlePosition(pivot);
        var rightHandle = DxfCanvasRotationInteraction.GetHandlePosition(pivot, 90);
        Assert.Equal(250, topHandle.X, 8);
        Assert.Equal(100, topHandle.Y, 8);
        Assert.Equal(350, rightHandle.X, 8);
        Assert.Equal(200, rightHandle.Y, 8);
        Assert.Equal(0, DxfCanvasRotationInteraction.GetScreenCardinalAngle(pivot, new Point(250, 100)), 8);
        Assert.Equal(90, DxfCanvasRotationInteraction.GetScreenCardinalAngle(pivot, new Point(350, 200)), 8);
        Assert.Equal(180, DxfCanvasRotationInteraction.GetScreenCardinalAngle(pivot, new Point(250, 300)), 8);
        Assert.Equal(270, DxfCanvasRotationInteraction.GetScreenCardinalAngle(pivot, new Point(150, 200)), 8);
    }

    [Fact]
    public void RotationInteraction_WrapsGrabRelativeDeltaAndTestsHitTolerance()
    {
        var pivot = new Point(100, 100);
        var grab = DxfCanvasRotationInteraction.GetHandlePosition(pivot, 355);
        var pointer = DxfCanvasRotationInteraction.GetHandlePosition(pivot, 5);
        var handle = DxfCanvasRotationInteraction.GetHandlePosition(pivot);

        Assert.Equal(10, DxfCanvasRotationInteraction.GetGrabRelativeDelta(pivot, grab, pointer), 8);
        Assert.Equal(-10, DxfCanvasRotationInteraction.GetGrabRelativeDelta(pivot, pointer, grab), 8);
        Assert.True(DxfCanvasRotationInteraction.IsHandleHit(new Point(handle.X + 6, handle.Y + 8), handle, 10));
        Assert.False(DxfCanvasRotationInteraction.IsHandleHit(new Point(handle.X + 6.1, handle.Y + 8), handle, 10));
        Assert.False(DxfCanvasRotationInteraction.IsHandleHit(handle, handle, -1));
    }

    [Fact]
    public void TranslationInteraction_UsesFixedAxisHandleGeometryAndHitPriority()
    {
        var handles = DxfCanvasTranslationInteraction.GetHandleGeometry(new Point(200, 150));

        Assert.Equal(new Point(200, 150), handles.Free);
        Assert.Equal(new Point(270, 150), handles.X);
        Assert.Equal(new Point(200, 80), handles.Y);
        Assert.Equal(
            DxfCanvasTranslationHandle.Free,
            DxfCanvasTranslationInteraction.HitTest(new Point(206, 158), handles, 10));
        Assert.Equal(
            DxfCanvasTranslationHandle.X,
            DxfCanvasTranslationInteraction.HitTest(new Point(276, 158), handles, 10));
        Assert.Equal(
            DxfCanvasTranslationHandle.Y,
            DxfCanvasTranslationInteraction.HitTest(new Point(206, 72), handles, 10));
        Assert.Equal(
            DxfCanvasTranslationHandle.None,
            DxfCanvasTranslationInteraction.HitTest(new Point(211, 150), handles, 10));
        Assert.Equal(
            DxfCanvasTranslationHandle.None,
            DxfCanvasTranslationInteraction.HitTest(handles.Free, handles, -1));
    }

    [Theory]
    [InlineData((int)DxfCanvasTranslationHandle.Free, 12, -8)]
    [InlineData((int)DxfCanvasTranslationHandle.X, 12, 0)]
    [InlineData((int)DxfCanvasTranslationHandle.Y, 0, -8)]
    [InlineData((int)DxfCanvasTranslationHandle.None, 0, 0)]
    public void TranslationInteraction_ConvertsScreenDeltaToConstrainedWorldDelta(
        int handleValue,
        double expectedX,
        double expectedY)
    {
        var handle = (DxfCanvasTranslationHandle)handleValue;
        var worldDelta = DxfCanvasTranslationInteraction.ScreenToWorldDelta(
            new Vector(30, 20),
            zoom: 2.5,
            handle);

        Assert.Equal(expectedX, worldDelta.X, 8);
        Assert.Equal(expectedY, worldDelta.Y, 8);
    }

    [Fact]
    public void TranslationInteraction_RejectsInvalidZoom()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DxfCanvasTranslationInteraction.ScreenToWorldDelta(
                new Vector(1, 1),
                zoom: 0,
                DxfCanvasTranslationHandle.Free));
    }

    [Fact]
    public void TransformPrecision_TranslationTotalsProduceIncrementalAxisCorrections()
    {
        var state = DxfCanvasTransformPrecisionState.Empty;

        Assert.True(state.TryApplyTranslationTotal(DxfCanvasPrecisionAxis.X, "12.50", out state, out var firstX));
        Assert.Equal(12.5, firstX, 8);
        Assert.True(state.TryApplyTranslationTotal(DxfCanvasPrecisionAxis.X, "15.25", out state, out var secondX));
        Assert.Equal(2.75, secondX, 8);
        Assert.True(state.TryApplyTranslationTotal(DxfCanvasPrecisionAxis.Y, "-4", out state, out var firstY));
        Assert.Equal(-4, firstY, 8);
        Assert.Equal(15.25, state.AppliedX, 8);
        Assert.Equal(-4, state.AppliedY, 8);
    }

    [Fact]
    public void TransformPrecision_RotationWrapsAbsoluteTargetAndResetsWithSelection()
    {
        var state = new DxfCanvasTransformPrecisionState(3, -2, 350);

        Assert.True(state.TryApplyAbsoluteRotation("730", out var next, out var correction));
        Assert.Equal(-340, correction, 8);
        Assert.Equal(10, next.CumulativeRotation, 8);
        Assert.Equal(DxfCanvasTransformPrecisionState.Empty, next.ResetForSelection());
        Assert.Equal(350, DxfCanvasTransformPrecisionState.WrapRotation(-10), 8);
        Assert.Equal(10, DxfCanvasTransformPrecisionState.WrapRotation(730), 8);
        Assert.Equal(10, state.RecordRotationDrag(20).CumulativeRotation, 8);
        Assert.Equal(12.5, state.RecordTranslationDrag(DxfCanvasPrecisionAxis.X, 12.5).AppliedX, 8);
    }

    [Fact]
    public void TransformPrecision_FormatsInvariantValuesAndRejectsInvalidInputWithoutMutation()
    {
        var state = new DxfCanvasTransformPrecisionState(1, 2, 45);

        Assert.Equal("12.50", DxfCanvasTransformPrecisionState.FormatTranslation(12.5));
        Assert.Equal("350.0", DxfCanvasTransformPrecisionState.FormatRotation(-10));
        Assert.False(state.TryApplyTranslationTotal(DxfCanvasPrecisionAxis.X, "not-a-number", out var translationNext, out var translationCorrection));
        Assert.False(state.TryApplyAbsoluteRotation("Infinity", out var rotationNext, out var rotationCorrection));
        Assert.Equal(state, translationNext);
        Assert.Equal(state, rotationNext);
        Assert.Equal(0, translationCorrection);
        Assert.Equal(0, rotationCorrection);
    }

    [Fact]
    public void TranslationInteraction_UsesSelectedPointMeanForSharedTransformPivot()
    {
        var selected = Path("selected", false, new Editor2DPoint(0, 0), new Editor2DPoint(9, 0), new Editor2DPoint(9, 3));
        var otherSelected = Path("other", false, new Editor2DPoint(20, 10));
        var untouched = Path("untouched", false, new Editor2DPoint(-100, -100));

        var found = DxfCanvasTranslationInteraction.TryGetSelectionPivot(
            [selected, otherSelected, untouched],
            [selected.Id, otherSelected.Id],
            out var pivot);

        Assert.True(found);
        Assert.Equal(9.5, pivot.X, 8);
        Assert.Equal(3.25, pivot.Y, 8);
    }

    [Fact]
    public void TranslationInteraction_RequiresRealDragBeforeCommit()
    {
        Assert.False(DxfCanvasTranslationInteraction.ShouldCommit(3.99, 4, new Editor2DPoint(5, 0)));
        Assert.False(DxfCanvasTranslationInteraction.ShouldCommit(4, 4, new Editor2DPoint(0, 0)));
        Assert.True(DxfCanvasTranslationInteraction.ShouldCommit(4, 4, new Editor2DPoint(5, 0)));
    }

    [Fact]
    public void RotationInteraction_RequiresRealDragAndAngleBeforeCommit()
    {
        Assert.False(DxfCanvasRotationInteraction.ShouldCommit(3.99, 4, 10, 0.05));
        Assert.False(DxfCanvasRotationInteraction.ShouldCommit(4, 4, 0.05, 0.05));
        Assert.True(DxfCanvasRotationInteraction.ShouldCommit(4, 4, 0.051, 0.05));
    }

    [Fact]
    public void GeometryEditor_RotatesOnlySelectedPathsAndPreservesIdentityAndMetadata()
    {
        var selected = Path("selected", false, new(1, 0), new(2, 0));
        var untouched = Path("untouched", false, new(-3, 4), new(-2, 4));
        var document = Document(selected, untouched);

        var rotated = DxfCanvasGeometryEditor.Rotate(document, [selected.Id], new Editor2DPoint(0, 0), 90);
        var rotatedPath = Assert.Single(rotated.Paths, path => path.Id == selected.Id);

        Assert.Equal(0, rotatedPath.Points[0].X, 8);
        Assert.Equal(1, rotatedPath.Points[0].Y, 8);
        Assert.Equal(0, rotatedPath.Points[1].X, 8);
        Assert.Equal(2, rotatedPath.Points[1].Y, 8);
        Assert.Same(untouched, Assert.Single(rotated.Paths, path => path.Id == untouched.Id));
        Assert.Equal(-3, rotated.Bounds.MinX);
        Assert.Equal(4, rotated.Bounds.MaxY);
        Assert.Equal(2, rotated.EntityCounts["LINE"]);
    }

    [Fact]
    public void CanvasStructure_DelegatesResponsibilitiesAndUsesCommandsForMenuActions()
    {
        var fields = typeof(DxfPreviewCanvas)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ToArray();
        Assert.Contains(typeof(DxfCanvasRenderer), fields);
        Assert.Contains(typeof(DxfCanvasInteractionSession), fields);
        Assert.Contains(typeof(DxfCanvasInteractionController), fields);
        Assert.Contains(typeof(DxfCanvasToolCommitter), fields);

        var source = File.ReadAllText(RepositoryFile(
            "src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs"));
        Assert.Contains("DxfCanvasViewportTransform", source, StringComparison.Ordinal);
        Assert.Contains("DxfCanvasHitTester", source, StringComparison.Ordinal);
        Assert.Contains("DxfCanvasGeometryEditor", source, StringComparison.Ordinal);
        Assert.Contains("Command = new RelayCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Click +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Editor2DPreviewPath", source, StringComparison.Ordinal);
        Assert.Contains("RoutePrimaryPress(ActiveTool)", source, StringComparison.Ordinal);

        var canvasFieldNames = typeof(DxfPreviewCanvas)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToArray();
        Assert.DoesNotContain("_moveDocumentSnapshot", canvasFieldNames);
        Assert.DoesNotContain("_pendingLineStart", canvasFieldNames);
        Assert.DoesNotContain("_editingVertexPathId", canvasFieldNames);

        var sessionFieldNames = typeof(DxfCanvasInteractionSession)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToArray();
        Assert.Contains("MoveDocumentSnapshot", sessionFieldNames);
        Assert.Contains("PendingLineStart", sessionFieldNames);
        Assert.Contains("EditingVertexPathId", sessionFieldNames);
    }

    private static Editor2DPreviewPath Path(string id, bool closed, params Editor2DPoint[] points)
        => new(id, "LINE", points, closed);

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
        => new(paths, DxfCanvasGeometryEditor.MeasureBounds(paths), new Dictionary<string, int> { ["LINE"] = paths.Length }, []);

    private static string RepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = System.IO.Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(System.IO.Path.Combine(pathParts));
    }
}
