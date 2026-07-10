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
        var planner = new DxfCanvasRenderPlanner();
        var document = Document(open, closed);

        Assert.Equal([open], planner.VisiblePaths(document, ["closed"]));
        Assert.Equal(DxfCanvasPathVisualRole.Selected, planner.ResolvePathRole(open, new HashSet<string> { "open" }, "open"));
        Assert.Equal(DxfCanvasPathVisualRole.Hovered, planner.ResolvePathRole(open, new HashSet<string>(), "open"));
        Assert.Equal(DxfCanvasPathVisualRole.Closed, planner.ResolvePathRole(closed, new HashSet<string>(), null));
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
    public void InteractionController_RoutesPrimaryToolPressesWithoutControlInputEvents()
    {
        var session = new DxfCanvasInteractionSession();
        var controller = new DxfCanvasInteractionController(session);

        Assert.Equal(DxfCanvasPressRoute.Selection, controller.RoutePrimaryPress(Editor2DTool.Select));
        Assert.Equal(DxfCanvasPressRoute.Move, controller.RoutePrimaryPress(Editor2DTool.Move));
        Assert.Equal(DxfCanvasPressRoute.SketchRectangle, controller.RoutePrimaryPress(Editor2DTool.SketchRectangle));
        Assert.Equal(DxfCanvasPressRoute.Corner, controller.RoutePrimaryPress(Editor2DTool.Fillet));
        Assert.Equal(DxfCanvasPressRoute.None, controller.RoutePrimaryPress(Editor2DTool.AddSewingHoles));

        session.PendingLineStart = new Editor2DPoint(0, 0);
        Assert.Equal(DxfCanvasMoveRoute.LineDraft, controller.RouteMove(Editor2DTool.SketchLine, false));
        session.PendingLineStart = null;
        session.IsMovingSelection = true;
        session.MoveDocumentSnapshot = Document();
        session.MoveStartPoint = new Editor2DPoint(0, 0);
        Assert.Equal(DxfCanvasMoveRoute.MoveSelection, controller.RouteMove(Editor2DTool.Move, true));
        Assert.Equal(DxfCanvasReleaseRoute.MoveSelection, controller.RouteRelease(Editor2DTool.Move, true));
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
