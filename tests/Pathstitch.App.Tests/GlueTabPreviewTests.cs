using Avalonia;
using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Tests;

public sealed class GlueTabPreviewTests
{
    [Fact]
    public void HandleGeometry_UsesAlongLineOffsets()
    {
        var line = Line("line", 0, 0, 10, 0);

        Assert.True(DxfCanvasGlueTabInteraction.TryGetHandles(line, 2, 3, out var handles));

        Assert.Equal(new Editor2DPoint(2, 0), handles.Start);
        Assert.Equal(new Editor2DPoint(7, 0), handles.End);
        Assert.Equal(10, handles.LineLength, 8);
        Assert.Equal(
            DxfCanvasGlueTabHandle.Start,
            DxfCanvasGlueTabInteraction.HitTest(new Point(2, 1), new Point(2, 0), new Point(7, 0), 2));
        Assert.Equal(
            DxfCanvasGlueTabHandle.End,
            DxfCanvasGlueTabInteraction.HitTest(new Point(7, 1), new Point(2, 0), new Point(7, 0), 2));
    }

    [Fact]
    public void DragProjection_ClampsOffsetsToOneMillimeterRemainingSpan()
    {
        var line = Line("line", 0, 0, 10, 0);

        Assert.Equal(6, DxfCanvasGlueTabInteraction.ProjectOffset(
            line, new Editor2DPoint(20, 4), DxfCanvasGlueTabHandle.Start, otherOffset: 3), 8);
        Assert.Equal(7, DxfCanvasGlueTabInteraction.ProjectOffset(
            line, new Editor2DPoint(-5, -9), DxfCanvasGlueTabHandle.End, otherOffset: 2), 8);
        Assert.Equal(4, DxfCanvasGlueTabInteraction.ProjectOffset(
            line, new Editor2DPoint(4, 100), DxfCanvasGlueTabHandle.Start, otherOffset: 3), 8);
        Assert.Equal(0, DxfCanvasGlueTabInteraction.ProjectOffset(
            line, new Editor2DPoint(-2, 0), DxfCanvasGlueTabHandle.Start, otherOffset: 3), 8);

        var session = new DxfCanvasInteractionSession
        {
            GlueTabDragHandle = DxfCanvasGlueTabHandle.Start,
        };
        var controller = new DxfCanvasInteractionController(session);
        Assert.Equal(DxfCanvasMoveRoute.GlueTabHandle, controller.RouteMove(Editor2DTool.PaperFolding, capturedByCanvas: true));
        Assert.Equal(DxfCanvasReleaseRoute.GlueTabHandle, controller.RouteRelease(Editor2DTool.PaperFolding, capturedByCanvas: true));
    }

    [Fact]
    public async Task Preview_IsReactiveAndGatedToPaperFoldingWithExactlyOneLine()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        await editor.SetActiveEditorModeAsync(EditorMode.TwoD);
        var line = Line("line", 0, 0, 10, 0);
        var other = Line("other", 0, 10, 10, 10);
        editor.TwoDDocument = editor.TwoDDocument! with { Paths = [line, other] };
        editor.TwoDSelectedPathIds = [line.Id];
        editor.TwoDGlueTabType = "Triangle";
        editor.TwoDGlueTabSide = "Left";
        editor.TwoDGlueTabHeightText = "2";
        editor.TwoDGlueTabStartOffsetText = "1";
        editor.TwoDGlueTabEndOffsetText = "2";

        editor.TwoDActiveTool = Editor2DTool.PaperFolding;
        var preview = Assert.Single(editor.TwoDGlueTabPreviewPaths);
        Assert.Equal([new Editor2DPoint(1, 0), new Editor2DPoint(4.5, -2), new Editor2DPoint(8, 0)], preview.Points);

        var changed = new List<string?>();
        editor.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        editor.TwoDGlueTabHeightText = "3";
        Assert.Contains(nameof(editor.TwoDGlueTabPreviewPaths), changed);
        Assert.Equal(-3, Assert.Single(editor.TwoDGlueTabPreviewPaths).Points[1].Y, 8);
        editor.TwoDActiveTool = Editor2DTool.Select;
        Assert.Empty(editor.TwoDGlueTabPreviewPaths);
        editor.TwoDActiveTool = Editor2DTool.PaperFolding;
        editor.TwoDSelectedPathIds = [line.Id, other.Id];
        Assert.Empty(editor.TwoDGlueTabPreviewPaths);
    }

    [Fact]
    public void Canvas_BindsPreviewAndTwoWayOffsets()
    {
        var view = ReadPage("Editor2DView.axaml");

        Assert.Contains("GlueTabPreviewPaths=\"{Binding TwoDGlueTabPreviewPaths}\"", view, StringComparison.Ordinal);
        Assert.Contains("GlueTabStartOffsetText=\"{Binding TwoDGlueTabStartOffsetText, Mode=TwoWay}\"", view, StringComparison.Ordinal);
        Assert.Contains("GlueTabEndOffsetText=\"{Binding TwoDGlueTabEndOffsetText, Mode=TwoWay}\"", view, StringComparison.Ordinal);
    }

    private static Editor2DPreviewPath Line(string id, double x1, double y1, double x2, double y2)
        => new(id, "LINE", [new(x1, y1), new(x2, y2)], false, Start: new(x1, y1));

    private static string ReadPage(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Pathstitch.App", "Pages", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(fileName);
    }
}
