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
        };

        Assert.True(session.HasActivePointerGesture);
        session.ResetPointerGesture();

        Assert.False(session.HasActivePointerGesture);
        Assert.Null(session.PressedPathId);
        Assert.Null(session.MarqueeStartPoint);
        Assert.False(session.CancelInteractionOnPointerRelease);
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

        var source = File.ReadAllText(RepositoryFile(
            "src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs"));
        Assert.Contains("DxfCanvasViewportTransform", source, StringComparison.Ordinal);
        Assert.Contains("DxfCanvasHitTester", source, StringComparison.Ordinal);
        Assert.Contains("DxfCanvasGeometryEditor", source, StringComparison.Ordinal);
        Assert.Contains("Command = new RelayCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Click +=", source, StringComparison.Ordinal);
        Assert.True(source.Split('\n').Length < 3650);
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
