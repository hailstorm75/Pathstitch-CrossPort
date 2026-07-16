namespace Pathstitch.App.Tests;

using System.Reflection;
using Avalonia;
using Domain.App.Models;
using Pathstitch.App.Controls;
using Pathstitch.App.Tests.Fixtures;

public sealed class DxfPreviewCanvasInteractionTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public async Task RectangleClickRouting_ReverseRectangleRequestsAutoWidthPrecision()
    {
        await _ui.RunAsync(() =>
        {
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
            viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document;
            var canvas = Canvas(viewModel.TwoDDocument!);
            canvas.DataContext = viewModel;
            DxfCanvasDimensionExpressionRequest? request = null;
            canvas.DimensionExpressionRequested += value => request = value;

            InvokeRectangleClick(canvas, Screen(canvas, new(20, 10)));
            Assert.Empty(viewModel.TwoDDocument!.Paths);
            InvokeRectangleClick(canvas, Screen(canvas, new(0, 0)));

            var path = Assert.Single(viewModel.TwoDDocument!.Paths);
            Assert.Equal(0, viewModel.TwoDDocument.Bounds.MinX, 8);
            Assert.Equal(0, viewModel.TwoDDocument.Bounds.MinY, 8);
            Assert.Equal(20, viewModel.TwoDDocument.Bounds.MaxX, 8);
            Assert.Equal(10, viewModel.TwoDDocument.Bounds.MaxY, 8);
            Assert.NotNull(request);
            Assert.Equal($"{path.Id}:width", request.Value.MeasurementId);
            Assert.Equal("20", request.Value.Text);
            Assert.True(viewModel.TwoDMeasurements.Single(item => item.Id == request.Value.MeasurementId).IsAutoDimension);
        });
    }

    [Fact]
    public async Task DimensionClickRouting_TwoPointsCreatesSeededDrivenReference()
    {
        await _ui.RunAsync(() =>
        {
            var canvas = Canvas(Document([]));
            var requests = 0;
            canvas.DimensionExpressionRequested += _ => requests++;

            InvokeDimensionClick(canvas, Screen(canvas, new(10, 0)));
            Assert.Empty(canvas.Measurements);
            InvokeDimensionClick(canvas, Screen(canvas, new(30, 0)));

            var measurement = Assert.Single(canvas.Measurements);
            Assert.Equal("reference", measurement.DimensionType);
            Assert.Equal("d1", measurement.VarName);
            Assert.True(measurement.IsParametric);
            Assert.True(measurement.Driven);
            Assert.Equal(measurement.Distance.ToString("R", System.Globalization.CultureInfo.InvariantCulture), measurement.Expression);
            Assert.Equal(measurement.Distance, measurement.EvaluatedValue);
            Assert.Equal(1, requests);
        });
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("CIRCLE")]
    public async Task DimensionClickRouting_EntityCreatesSeededDrivingAttachment(string entityType)
    {
        await _ui.RunAsync(() =>
        {
            var path = entityType == "LINE"
                ? new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(20, 0)], false)
                : new Editor2DPreviewPath(
                    "source", "CIRCLE", Editor2DGeometry.BuildCirclePoints(new(40, 0), 10), true,
                    Center: new(40, 0), Radius: 10);
            var existing = new Editor2DMeasurement(
                "existing", new(0, 20), new(5, 20), VarName: "d1", Expression: "5",
                IsParametric: true, EvaluatedValue: 5);
            var canvas = Canvas(Document([path]));
            canvas.Measurements = [existing];
            var worldClick = entityType == "LINE" ? new Editor2DPoint(10, 0) : new Editor2DPoint(50, 0);

            InvokeDimensionClick(canvas, Screen(canvas, worldClick));

            var measurement = Assert.Single(canvas.Measurements, item => item.Id != existing.Id);
            Assert.Equal(path.Id, measurement.EntityPathId);
            Assert.Equal(entityType == "LINE" ? "length" : "radius", measurement.DimensionType);
            Assert.Equal("d2", measurement.VarName);
            Assert.True(measurement.IsParametric);
            Assert.False(measurement.Driven);
            Assert.Equal(measurement.Distance, measurement.EvaluatedValue);
        });
    }

    private static DxfPreviewCanvas Canvas(Editor2DPreviewDocument document)
    {
        var canvas = new DxfPreviewCanvas { Document = document, SnapEnabled = false, Zoom = 1 };
        canvas.Arrange(new Rect(0, 0, 400, 300));
        return canvas;
    }

    private static Point Screen(DxfPreviewCanvas canvas, Editor2DPoint point)
        => DxfCanvasViewportTransform.WorldToScreen(point, canvas.Bounds.Size, canvas.Zoom, canvas.OffsetX, canvas.OffsetY);

    private static void InvokeDimensionClick(DxfPreviewCanvas canvas, Point point)
        => typeof(DxfPreviewCanvas)
            .GetMethod("HandleDimensionClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, [point]);

    private static void InvokeRectangleClick(DxfPreviewCanvas canvas, Point point)
        => typeof(DxfPreviewCanvas)
            .GetMethod("HandleSketchRectangleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, [point]);

    private static Editor2DPreviewDocument Document(IReadOnlyList<Editor2DPreviewPath> paths)
        => new(
            paths,
            new Editor2DBounds(-100, -100, 100, 100),
            paths.GroupBy(path => path.EntityType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            []);

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
