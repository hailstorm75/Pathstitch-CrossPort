namespace Pathstitch.App.Tests;

using System.Reflection;
using Avalonia;
using Domain.App.Models;
using Pathstitch.App.Controls;

public sealed class DxfPreviewCanvasInteractionTests
{
    [Fact]
    public void PenClick_FirstAnchorClosesAndLastAnchorFinishesOpenPath()
    {
        var points = new[]
        {
            new Editor2DPoint(10, 10),
            new Editor2DPoint(50, 10),
            new Editor2DPoint(50, 50),
        };
        static Point ToScreen(Editor2DPoint point) => new(point.X, point.Y);

        var close = DxfCanvasPenInteraction.GetCompletionForClick(points, new Point(12, 11), ToScreen, 10);
        var finish = DxfCanvasPenInteraction.GetCompletionForClick(points, new Point(49, 52), ToScreen, 10);
        var append = DxfCanvasPenInteraction.GetCompletionForClick(points, new Point(80, 80), ToScreen, 10);

        Assert.Equal(DxfPenCompletion.Closed, close);
        Assert.Equal(DxfPenCompletion.Open, finish);
        Assert.Null(append);
    }

    [Fact]
    public void PenClick_TwoPointDraftFinishesOpenAtEitherEndpoint()
    {
        var points = new[] { new Editor2DPoint(0, 0), new Editor2DPoint(20, 0) };
        static Point ToScreen(Editor2DPoint point) => new(point.X, point.Y);

        Assert.Equal(
            DxfPenCompletion.Open,
            DxfCanvasPenInteraction.GetCompletionForClick(points, new Point(0, 0), ToScreen, 10));
        Assert.Equal(
            DxfPenCompletion.Open,
            DxfCanvasPenInteraction.GetCompletionForClick(points, new Point(20, 0), ToScreen, 10));
    }

    [Theory]
    [InlineData(Editor2DTool.Select)]
    [InlineData(Editor2DTool.Scale)]
    public void SelectionHandles_AreVisibleForSelectionEditingTools(Editor2DTool activeTool)
    {
        Assert.True(DxfCanvasSelectionInteraction.ShouldDrawHandles(activeTool));
    }

    public static IEnumerable<object[]> NonSelectionEditingTools =>
        Enum.GetValues<Editor2DTool>()
            .Where(tool => tool is not Editor2DTool.Select and not Editor2DTool.Scale)
            .Select(tool => new object[] { tool });

    [Theory]
    [MemberData(nameof(NonSelectionEditingTools))]
    public void SelectionHandles_AreHiddenForOtherTools(Editor2DTool activeTool)
    {
        Assert.False(DxfCanvasSelectionInteraction.ShouldDrawHandles(activeTool));
    }

    [Fact]
    public void MoveCopyHelper_PreservesOriginalsAndAssignsNewIds()
    {
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath("shape-1", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(5, 0)], false)],
            new Editor2DBounds(0, 0, 5, 0),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            []);
        var selectedIds = new[] { "shape-1" };

        var method = typeof(DxfPreviewCanvas).GetMethod(
            "DuplicateSelectedPaths",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var arguments = new object[] { document, selectedIds, null! };

        var duplicatedDocument = Assert.IsType<Editor2DPreviewDocument>(method!.Invoke(null, arguments));
        var copiedSelectionIds = Assert.IsAssignableFrom<IReadOnlyList<string>>(arguments[2]);

        Assert.Single(document.Paths);
        Assert.Equal("shape-1", document.Paths[0].Id);
        Assert.Equal(2, duplicatedDocument.Paths.Count);
        Assert.Equal("shape-1", duplicatedDocument.Paths[0].Id);
        var copiedPath = duplicatedDocument.Paths[1];
        Assert.StartsWith("shape-1:copy:", copiedPath.Id, StringComparison.Ordinal);
        Assert.Equal(document.Paths[0].Points, copiedPath.Points);
        Assert.Equal(new[] { copiedPath.Id }, copiedSelectionIds);
    }

    [Fact]
    public void InPlaceMoveHelper_TranslatesSelectedGeometry()
    {
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath("shape-1", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(5, 0)], false)],
            new Editor2DBounds(0, 0, 5, 0),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            []);

        var movedDocument = typeof(DxfCanvasGeometryEditor)
            .GetMethod("Translate", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { document, new[] { "shape-1" }, 10.0, 2.0 });

        var translated = Assert.IsType<Editor2DPreviewDocument>(movedDocument);
        Assert.Equal(new Editor2DPoint(10, 2), translated.Paths[0].Points[0]);
        Assert.Equal(new Editor2DPoint(15, 2), translated.Paths[0].Points[1]);
        Assert.Single(translated.Paths);
    }

    [Fact]
    public void SelectionTransformRequest_ReturnsCanonicalDocumentAndSelection()
    {
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath("shape-1", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(5, 0)], false)],
            new Editor2DBounds(0, 0, 5, 0),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            []);
        var transform = Editor2DAffineTransform.CreateTranslation(10, 5);
        var request = new DxfCanvasSelectionTransformEventArgs(transform, createCopy: true);

        request.Complete(document, ["shape-1"]);

        Assert.Same(transform, request.Transform);
        Assert.True(request.CreateCopy);
        Assert.True(request.Committed);
        Assert.Same(document, request.Document);
        Assert.Equal(["shape-1"], request.SelectedPathIds);
    }
}
