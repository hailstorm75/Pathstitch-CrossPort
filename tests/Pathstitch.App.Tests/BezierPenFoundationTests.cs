using Domain.App.Models;
using Domain.App.Services;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Tests;

public sealed class BezierPenFoundationTests
{
    [Fact]
    public void PenGesture_RoutesCapturedMoveAndReleaseAsHandleDrag()
    {
        var session = new DxfCanvasInteractionSession
        {
            PendingPenAnchors = [new(new(2, 3))],
            PendingPenDragAnchorIndex = 0,
        };
        var controller = new DxfCanvasInteractionController(session);

        Assert.Equal(DxfCanvasMoveRoute.PenHandleDrag, controller.RouteMove(Editor2DTool.Pen, capturedByCanvas: true));
        Assert.Equal(DxfCanvasReleaseRoute.PenHandleDrag, controller.RouteRelease(Editor2DTool.Pen, capturedByCanvas: true));
        Assert.Equal(DxfCanvasMoveRoute.PenDraft, controller.RouteMove(Editor2DTool.Pen, capturedByCanvas: false));
    }

    [Fact]
    public void SmoothAnchor_ProducesSymmetricHandlesAndFlattenedCubic()
    {
        var first = Editor2DBezierGeometry.CreateSmoothAnchor(new(0, 0), new(0, 10));
        var second = Editor2DBezierGeometry.CreateSmoothAnchor(new(20, 0), new(20, -10));

        var points = Editor2DBezierGeometry.Flatten([first, second], closed: false);

        Assert.Equal(new Editor2DPoint(0, -10), first.HandleIn);
        Assert.Equal(new Editor2DPoint(20, 10), second.HandleIn);
        Assert.Equal(19, points.Count);
        Assert.Equal(first.Point, points[0]);
        Assert.Equal(second.Point, points[^1]);
        Assert.Contains(points, point => Math.Abs(point.Y) > 0.1);
    }

    [Fact]
    public void PenCommit_KeepsEditableAnchorsAndRendererReadySampledGeometry()
    {
        var document = Editor2DWorkspaceState.Empty.Document;
        var anchors = new[]
        {
            Editor2DBezierGeometry.CreateSmoothAnchor(new(0, 0), new(0, 10)),
            new Editor2DBezierAnchor(new(20, 0)),
            new Editor2DBezierAnchor(new(20, 20)),
        };

        var committed = new DxfCanvasToolCommitter(() => "stable").Pen(document, anchors, closed: true);

        var path = Assert.Single(committed!.Paths);
        Assert.Equal("pen-stable", path.Id);
        Assert.True(path.IsClosed);
        Assert.Equal(anchors, path.BezierAnchors);
        Assert.True(path.Points.Count > anchors.Length);
        Assert.Equal(anchors[0].Point, path.Points[0]);
        Assert.NotEqual(path.Points[0], path.Points[^1]);
    }

    [Fact]
    public void CanvasTransforms_MoveAndScaleBezierAnchorsAndHandles()
    {
        var anchor = Editor2DBezierGeometry.CreateSmoothAnchor(new(2, 3), new(4, 7));
        var path = new Editor2DPreviewPath("pen", "LWPOLYLINE", [anchor.Point, new(10, 3)], false,
            BezierAnchors: [anchor, new(new(10, 3))]);
        var document = DxfCanvasGeometryEditor.Update(Editor2DWorkspaceState.Empty.Document, [path]);

        var moved = DxfCanvasGeometryEditor.Translate(document, [path.Id], 5, -2);
        var movedAnchor = Assert.Single(moved.Paths[0].BezierAnchors!.Take(1));
        Assert.Equal(new Editor2DPoint(7, 1), movedAnchor.Point);
        Assert.Equal(new Editor2DPoint(9, 5), movedAnchor.HandleOut);
        Assert.Equal(new Editor2DPoint(5, -3), movedAnchor.HandleIn);

        var scaled = DxfCanvasGeometryEditor.Scale(document, [path.Id], new(0, 0), 2);
        var scaledAnchor = scaled.Paths[0].BezierAnchors![0];
        Assert.Equal(new Editor2DPoint(4, 6), scaledAnchor.Point);
        Assert.Equal(new Editor2DPoint(8, 14), scaledAnchor.HandleOut);
        Assert.Equal(new Editor2DPoint(0, -2), scaledAnchor.HandleIn);
    }

    [Fact]
    public async Task StchRoundTrip_PreservesBezierAnchorsHandlesAndClosedState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-BezierPen", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "bezier.stch");
            var anchors = new[]
            {
                Editor2DBezierGeometry.CreateSmoothAnchor(new(1, 2), new(4, 8)),
                new Editor2DBezierAnchor(new(20, 2), new(18, -4), new(22, 8)),
                new Editor2DBezierAnchor(new(20, 20)),
            };
            var path = new Editor2DPreviewPath(
                "pen", "LWPOLYLINE", Editor2DBezierGeometry.Flatten(anchors, closed: true), true,
                BezierAnchors: anchors);
            var workspace = Editor2DWorkspaceState.Empty with
            {
                IsInitialized = true,
                Document = DxfCanvasGeometryEditor.Update(Editor2DWorkspaceState.Empty.Document, [path]),
            };
            var service = new Project3DStateService();

            await service.SaveAsync(projectPath, Project3DState.Empty with { TwoDWorkspaceState = workspace });
            var restored = await service.LoadAsync(projectPath);

            var restoredPath = Assert.Single(restored.TwoDWorkspaceState!.Document.Paths);
            Assert.True(restoredPath.IsClosed);
            Assert.Equal(anchors, restoredPath.BezierAnchors);
            Assert.Equal(path.Points, restoredPath.Points);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
