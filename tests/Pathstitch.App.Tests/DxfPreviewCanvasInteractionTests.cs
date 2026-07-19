namespace Pathstitch.App.Tests;

using System.Reflection;
using Avalonia;
using Avalonia.Input;
using Domain.App.Models;
using Domain.App.Services;
using Pathstitch.App.Controls;
using Pathstitch.App.Tests.Fixtures;

public sealed class DxfPreviewCanvasInteractionTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public async Task ScreenPointToWorld_UsesLiveCanvasZoomAndPan()
    {
        await _ui.RunAsync(() =>
        {
            var canvas = Canvas(Editor2DWorkspaceState.Empty.Document);
            canvas.Measure(new Size(800, 600));
            canvas.Arrange(new Rect(0, 0, 800, 600));
            canvas.Zoom = 2.5;
            canvas.OffsetX = 14;
            canvas.OffsetY = -9;
            var screen = new Point(510, 225);

            var world = canvas.ScreenPointToWorld(screen);
            var expected = DxfCanvasViewportTransform.ScreenToWorld(
                screen,
                canvas.Bounds.Size,
                canvas.Zoom,
                canvas.OffsetX,
                canvas.OffsetY);

            Assert.Equal(expected, world);
        });
    }

    [Theory]
    [InlineData(Key.Enter, 1, 0)]
    [InlineData(Key.Escape, 0, 1)]
    public async Task ReferenceImageTransform_EnterCommitsAndEscapeCancels(
        Key key,
        int expectedCommits,
        int expectedCancels)
    {
        await _ui.RunAsync(() =>
        {
            var canvas = Canvas(Editor2DWorkspaceState.Empty.Document);
            canvas.ReferenceImageTransformEditActive = true;
            var commits = 0;
            var cancels = 0;
            canvas.ReferenceImageTransformCompleted += () => commits++;
            canvas.ReferenceImageTransformCanceled += () => cancels++;
            var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };

            canvas.RaiseEvent(args);

            Assert.True(args.Handled);
            Assert.Equal(expectedCommits, commits);
            Assert.Equal(expectedCancels, cancels);
        });
    }
    [Theory]
    [InlineData(Editor2DTool.AddThickness)]
    [InlineData(Editor2DTool.Cleanup)]
    [InlineData(Editor2DTool.Patterning)]
    [InlineData(Editor2DTool.AddSewingHoles)]
    public async Task ApplyStyleTool_EnterCommitsAndReturnsToSelect(Editor2DTool tool)
    {
        await _ui.RunAsync(() =>
        {
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
                geometryKernelService: new ThicknessGeometryKernelService());
            try
            {
                viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document;
                var sourceId = viewModel.CreateTwoDLine(new(0, 0), new(12, 0))!;
                if (tool == Editor2DTool.Cleanup)
                    viewModel.CreateTwoDLine(new(12, 0), new(24, 0));
                viewModel.TwoDSelectedPathIds = [sourceId];
                viewModel.TwoDPatternCopiesXText = "2";
                viewModel.TwoDPatternCopiesYText = "1";
                viewModel.TwoDPatternSpacingXText = "20";
                viewModel.TwoDPatternSpacingYText = "0";
                viewModel.TwoDAddThicknessWidthText = "2";
                viewModel.TwoDActiveTool = tool;
                viewModel.TwoDWorkspace.ClearHistory();
                var before = viewModel.TwoDDocument!.Paths.ToArray();
                var canvas = Canvas(viewModel.TwoDDocument);
                canvas.DataContext = viewModel;
                canvas.ActiveTool = tool;
                var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };

                canvas.RaiseEvent(args);

                Assert.True(args.Handled);
                Assert.Equal(Editor2DTool.Select, viewModel.TwoDActiveTool);
                Assert.False(before.SequenceEqual(viewModel.TwoDDocument!.Paths));
                Assert.True(viewModel.TwoDWorkspace.CanUndo);
            }
            finally
            {
                viewModel.Dispose();
            }
        });
    }

    [Theory]
    [InlineData(Editor2DTool.AddThickness)]
    [InlineData(Editor2DTool.Cleanup)]
    [InlineData(Editor2DTool.Patterning)]
    [InlineData(Editor2DTool.AddSewingHoles)]
    public async Task ApplyStyleTool_EnterWithoutEligibleGeometryStaysActive(Editor2DTool tool)
    {
        await _ui.RunAsync(() =>
        {
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
                geometryKernelService: new ThicknessGeometryKernelService());
            try
            {
                viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document;
                viewModel.TwoDActiveTool = tool;
                viewModel.TwoDWorkspace.ClearHistory();
                var canvas = Canvas(viewModel.TwoDDocument);
                canvas.DataContext = viewModel;
                canvas.ActiveTool = tool;
                var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };

                canvas.RaiseEvent(args);

                Assert.True(args.Handled);
                Assert.Equal(tool, viewModel.TwoDActiveTool);
                Assert.Empty(viewModel.TwoDDocument!.Paths);
                Assert.False(viewModel.TwoDWorkspace.CanUndo);
            }
            finally
            {
                viewModel.Dispose();
            }
        });
    }

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
    public async Task LineClickRouting_ReverseDirectionKeepsStartAndRequestsAutoLengthPrecision()
    {
        await _ui.RunAsync(() =>
        {
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
            viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document;
            var canvas = Canvas(viewModel.TwoDDocument!);
            canvas.DataContext = viewModel;
            DxfCanvasDimensionExpressionRequest? request = null;
            canvas.DimensionExpressionRequested += value => request = value;

            InvokeLineClick(canvas, Screen(canvas, new(20, 10)));
            InvokeLineClick(canvas, Screen(canvas, new(0, 0)));

            var path = Assert.Single(viewModel.TwoDDocument!.Paths);
            Assert.Equal(20, path.Points[0].X, 8);
            Assert.Equal(10, path.Points[0].Y, 8);
            Assert.Equal(0, path.Points[1].X, 8);
            Assert.Equal(0, path.Points[1].Y, 8);
            Assert.Equal($"{path.Id}:length", request?.MeasurementId);
            Assert.Equal(Math.Sqrt(500).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), request?.Text);
            Assert.True(viewModel.TwoDMeasurements.Single(item => item.Id == request?.MeasurementId).IsAutoDimension);
        });
    }

    [Fact]
    public async Task SketchLine_EnterCommitsPreviewAndRequestsAutoLengthPrecision()
    {
        await _ui.RunAsync(() =>
        {
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
            try
            {
                viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document;
                viewModel.TwoDActiveTool = Editor2DTool.SketchLine;
                viewModel.TwoDWorkspace.ClearHistory();
                var canvas = Canvas(viewModel.TwoDDocument);
                canvas.DataContext = viewModel;
                canvas.ActiveTool = Editor2DTool.SketchLine;
                DxfCanvasDimensionExpressionRequest? request = null;
                canvas.DimensionExpressionRequested += value => request = value;
                var start = new Editor2DPoint(0, 0);
                var end = new Editor2DPoint(12, 0);

                InvokeLineClick(canvas, Screen(canvas, start));
                var session = InteractionSession(canvas);
                session.PendingLineEnd = end;
                var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };

                canvas.RaiseEvent(args);

                var path = Assert.Single(viewModel.TwoDDocument!.Paths);
                Assert.True(args.Handled);
                Assert.Equal(start.X, path.Points[0].X, 8);
                Assert.Equal(start.Y, path.Points[0].Y, 8);
                Assert.Equal(end.X, path.Points[1].X, 8);
                Assert.Equal(end.Y, path.Points[1].Y, 8);
                Assert.Equal([path.Id], viewModel.TwoDSelectedPathIds);
                Assert.Equal($"{path.Id}:length", request?.MeasurementId);
                Assert.Equal("12", request?.Text);
                Assert.Null(session.PendingLineStart);
                Assert.Null(session.PendingLineEnd);
                Assert.True(viewModel.TwoDWorkspace.CanUndo);
                Assert.Equal(Editor2DTool.SketchLine, viewModel.TwoDActiveTool);
            }
            finally
            {
                viewModel.Dispose();
            }
        });
    }

    [Fact]
    public async Task SketchLine_EnterRejectsShortPreviewAndClearsPendingLine()
    {
        await _ui.RunAsync(() =>
        {
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
            try
            {
                viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document;
                viewModel.TwoDActiveTool = Editor2DTool.SketchLine;
                viewModel.TwoDWorkspace.ClearHistory();
                var canvas = Canvas(viewModel.TwoDDocument);
                canvas.DataContext = viewModel;
                canvas.ActiveTool = Editor2DTool.SketchLine;

                InvokeLineClick(canvas, Screen(canvas, new(0, 0)));
                var session = InteractionSession(canvas);
                session.PendingLineEnd = new(2, 0);
                var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };

                canvas.RaiseEvent(args);

                Assert.True(args.Handled);
                Assert.Empty(viewModel.TwoDDocument!.Paths);
                Assert.Null(session.PendingLineStart);
                Assert.Null(session.PendingLineEnd);
                Assert.False(viewModel.TwoDWorkspace.CanUndo);
            }
            finally
            {
                viewModel.Dispose();
            }
        });
    }

    [Fact]
    public async Task SketchLine_EnterWithoutPendingLineDoesNotCreateGeometry()
    {
        await _ui.RunAsync(() =>
        {
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
            try
            {
                viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document;
                viewModel.TwoDActiveTool = Editor2DTool.SketchLine;
                viewModel.TwoDWorkspace.ClearHistory();
                var canvas = Canvas(viewModel.TwoDDocument);
                canvas.DataContext = viewModel;
                canvas.ActiveTool = Editor2DTool.SketchLine;
                var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };

                canvas.RaiseEvent(args);

                var session = InteractionSession(canvas);
                Assert.Empty(viewModel.TwoDDocument!.Paths);
                Assert.Null(session.PendingLineStart);
                Assert.Null(session.PendingLineEnd);
                Assert.False(viewModel.TwoDWorkspace.CanUndo);
            }
            finally
            {
                viewModel.Dispose();
            }
        });
    }

    [Fact]
    public async Task CircleClickRouting_KeepsCenterAndRequestsAutoRadiusPrecision()
    {
        await _ui.RunAsync(() =>
        {
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests();
            viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document;
            var canvas = Canvas(viewModel.TwoDDocument!);
            canvas.DataContext = viewModel;
            DxfCanvasDimensionExpressionRequest? request = null;
            canvas.DimensionExpressionRequested += value => request = value;

            InvokeCircleClick(canvas, Screen(canvas, new(20, 10)));
            InvokeCircleClick(canvas, Screen(canvas, new(20, 25)));

            var path = Assert.Single(viewModel.TwoDDocument!.Paths);
            Assert.Equal(20, path.Center!.X, 8);
            Assert.Equal(10, path.Center.Y, 8);
            Assert.Equal(15, path.Radius!.Value, 8);
            Assert.Equal($"{path.Id}:radius", request?.MeasurementId);
            Assert.Equal("15", request?.Text);
        });
    }

    [Theory]
    [InlineData("LINE", "length")]
    [InlineData("CIRCLE", "radius")]
    public async Task CreationPrecision_DismissesWhenSelectionChangesOrTargetIsRemoved(
        string entityType,
        string dimensionType)
    {
        await _ui.RunAsync(() =>
        {
            var path = entityType == "LINE"
                ? new Editor2DPreviewPath("target", "LINE", [new(0, 0), new(10, 0)], false)
                : new Editor2DPreviewPath(
                    "target", "CIRCLE", Editor2DGeometry.BuildCirclePoints(new(0, 0), 10), true,
                    Center: new(0, 0), Radius: 10);
            var measurement = new Editor2DMeasurement(
                $"{path.Id}:{dimensionType}",
                new(0, 0),
                new(10, 0),
                IsAutoDimension: true,
                EntityPathId: path.Id,
                DimensionType: dimensionType);
            var canvas = Canvas(Document([path]));
            canvas.Measurements = [measurement];
            canvas.SelectedPathIds = [path.Id];
            var dismissals = 0;
            canvas.DimensionExpressionDismissed += () => dismissals++;

            Assert.True(canvas.RequestDimensionExpressionInput(measurement.Id));
            canvas.SelectedPathIds = ["different"];
            Assert.Equal(1, dismissals);

            canvas.SelectedPathIds = [path.Id];
            Assert.True(canvas.RequestDimensionExpressionInput(measurement.Id));
            canvas.Document = Document([]);
            Assert.Equal(2, dismissals);
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

    [Fact]
    public async Task VertexDrag_PreviewsLocallyThenCommitsOneWorkspaceEdit()
    {
        await _ui.RunAsync(() =>
        {
            var workspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
            var pathId = workspace.CreateLine(new(0, 0), new(10, 0), "line")!;
            workspace.ClearHistory();
            var original = workspace.Document;
            var canvas = Canvas(original);
            var session = InteractionSession(canvas);
            session.IsEditingVertex = true;
            session.EditingVertexPathId = pathId;
            session.EditingVertexIndex = 1;
            session.VertexDocumentSnapshot = original;
            session.VertexPreviewPoint = new Editor2DPoint(10, 0);
            canvas.VertexEditRequested += request =>
            {
                if (workspace.UpdatePathVertex(request.PathId, request.VertexIndex, request.Point))
                    request.Complete(workspace.Document);
            };

            InvokeVertexPreview(canvas, Screen(canvas, new(15, 5)));
            InvokeVertexPreview(canvas, Screen(canvas, new(20, 8)));

            Assert.Same(original, canvas.Document);
            Assert.Equal(new Editor2DPoint(20, 8), session.VertexPreviewPoint);
            Assert.False(workspace.CanUndo);
            Assert.True(InvokeVertexCommit(canvas));
            Assert.Equal(new Editor2DPoint(20, 8), workspace.Document.Paths.Single().Points[1]);
            Assert.True(workspace.CanUndo);
            Assert.True(workspace.Undo());
            Assert.Equal(original, workspace.Document);
        });
    }

    [Fact]
    public async Task VertexDrag_CancelDropsPreviewWithoutDocumentHistory()
    {
        await _ui.RunAsync(() =>
        {
            var workspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
            var pathId = workspace.CreateLine(new(0, 0), new(10, 0), "line")!;
            workspace.ClearHistory();
            var original = workspace.Document;
            var canvas = Canvas(original);
            var session = InteractionSession(canvas);
            session.IsEditingVertex = true;
            session.EditingVertexPathId = pathId;
            session.EditingVertexIndex = 1;
            session.VertexDocumentSnapshot = original;
            session.VertexPreviewPoint = new Editor2DPoint(10, 0);

            InvokeVertexPreview(canvas, Screen(canvas, new(25, 12)));
            canvas.CancelActiveInteraction();

            Assert.Same(original, canvas.Document);
            Assert.False(session.IsEditingVertex);
            Assert.Null(session.VertexDocumentSnapshot);
            Assert.Null(session.VertexPreviewPoint);
            Assert.False(workspace.CanUndo);
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

    [Fact]
    public async Task DimensionClickRouting_RectangleEdgesReuseCreationDimensions()
    {
        await _ui.RunAsync(() =>
        {
            var workspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
            var pathId = workspace.CreateRectangle(new(0, 0), new(20, 10), pathId: "rectangle")!;
            var canvas = Canvas(workspace.Document);
            canvas.Measurements = workspace.Measurements;
            canvas.SelectedPathIds = [pathId];
            DxfCanvasDimensionExpressionRequest? request = null;
            canvas.DimensionExpressionRequested += value => request = value;

            InvokeDimensionClick(canvas, Screen(canvas, new(10, 0)));

            Assert.Equal(2, canvas.Measurements.Count);
            Assert.Equal($"{pathId}:width", request?.MeasurementId);
            Assert.Equal(DxfCanvasDimensionEditContext.DimensionTool, request?.Context);

            InvokeDimensionClick(canvas, Screen(canvas, new(20, 5)));

            Assert.Equal(2, canvas.Measurements.Count);
            Assert.Equal($"{pathId}:height", request?.MeasurementId);
        });
    }

    [Theory]
    [InlineData(Editor2DCornerKind.Fillet)]
    [InlineData(Editor2DCornerKind.Chamfer)]
    public async Task DimensionClickRouting_ReverseRoundedRectangleMapsOriginalHorizontalAndVerticalEdges(
        Editor2DCornerKind cornerKind)
    {
        await _ui.RunAsync(() =>
        {
            var workspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
            var pathId = workspace.CreateRectangle(new(30, 40), new(10, 20), initialFilletRadius: 2, pathId: "rectangle")!;
            var corners = workspace.CornerParameters
                .Select(parameter => parameter with { Kind = cornerKind })
                .ToArray();
            var path = workspace.Document.Paths.Single(item => item.Id == pathId);
            var rendered = Editor2DCornerGeometry.Apply(path with { Points = corners[0].SourcePoints }, corners);
            var document = workspace.Document with { Paths = [rendered] };
            var canvas = Canvas(document);
            canvas.CornerParameters = corners;
            canvas.Measurements = workspace.Measurements;
            canvas.SelectedPathIds = [pathId];
            DxfCanvasDimensionExpressionRequest? request = null;
            canvas.DimensionExpressionRequested += value => request = value;

            InvokeDimensionClick(canvas, Screen(canvas, new(20, 20)));
            Assert.Equal($"{pathId}:width", request?.MeasurementId);
            Assert.Equal(2, canvas.Measurements.Count);

            InvokeDimensionClick(canvas, Screen(canvas, new(10, 30)));
            Assert.Equal($"{pathId}:height", request?.MeasurementId);
            Assert.Equal(2, canvas.Measurements.Count);
        });
    }
    [Fact]
    public async Task DimensionClickRouting_DirectPrimitivesReuseCreationDimensions()
    {
        await _ui.RunAsync(() =>
        {
            var lineWorkspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
            var lineId = lineWorkspace.CreateLine(new(0, 0), new(20, 0), "line")!;
            AssertReuses(lineWorkspace, lineId, new(10, 0), "length");

            var circleWorkspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
            var circleId = circleWorkspace.CreateCircle(new(0, 0), new(10, 0), "circle")!;
            AssertReuses(circleWorkspace, circleId, new(10, 0), "radius");

            var polygonWorkspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
            var polygonId = polygonWorkspace.CreateRegularPolygon(new(0, 0), new(10, 0), 5, "polygon")!;
            AssertReuses(polygonWorkspace, polygonId, new(10, 0), "radius");
        });

        static void AssertReuses(
            Domain.App.ViewModels.Editor2DWorkspaceViewModel workspace,
            string pathId,
            Editor2DPoint click,
            string dimensionType)
        {
            var canvas = Canvas(workspace.Document);
            canvas.Measurements = workspace.Measurements;
            canvas.SelectedPathIds = [pathId];
            DxfCanvasDimensionExpressionRequest? request = null;
            canvas.DimensionExpressionRequested += value => request = value;
            var originalCount = canvas.Measurements.Count;

            InvokeDimensionClick(canvas, Screen(canvas, click));

            Assert.Equal(originalCount, canvas.Measurements.Count);
            Assert.Equal($"{pathId}:{dimensionType}", request?.MeasurementId);
        }
    }

    [Fact]
    public async Task DimensionClickRouting_PenRegularPolygonRemainsReferenceOnly()
    {
        await _ui.RunAsync(() =>
        {
            var anchors = Enumerable.Range(0, 5)
                .Select(index => index * Math.PI * 2.0 / 5.0)
                .Select(angle => new Editor2DBezierAnchor(new(
                    10 * Math.Cos(angle),
                    10 * Math.Sin(angle))))
                .ToArray();
            var penPath = new Editor2DPreviewPath(
                "pen",
                "LWPOLYLINE",
                anchors.Select(anchor => anchor.Point).ToArray(),
                IsClosed: true,
                BezierAnchors: anchors);
            var canvas = Canvas(Document([penPath]));

            InvokeDimensionClick(canvas, Screen(canvas, anchors[0].Point));

            Assert.Empty(canvas.Measurements);
            Assert.NotNull(InteractionSession(canvas).PendingDimensionStart);
        });
    }

    [Fact]
    public async Task EditableMeasurementHitTest_IncludesVisibleAutoDimensionOnlyWhileOwnerSelected()
    {
        await _ui.RunAsync(() =>
        {
            var workspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
            var pathId = workspace.CreateRectangle(new(0, 0), new(20, 10), pathId: "rectangle")!;
            var width = workspace.Measurements.Single(item => item.DimensionType == "width");
            var canvas = Canvas(workspace.Document);
            canvas.Measurements = workspace.Measurements;
            var midpoint = new Editor2DPoint(
                (width.Start.X + width.End.X) / 2.0,
                (width.Start.Y + width.End.Y) / 2.0);

            Assert.Null(InvokeEditableMeasurementHitTest(canvas, Screen(canvas, midpoint)));
            canvas.SelectedPathIds = [pathId];
            Assert.Equal(width.Id, InvokeEditableMeasurementHitTest(canvas, Screen(canvas, midpoint)));
        });
    }

    private static string? InvokeEditableMeasurementHitTest(DxfPreviewCanvas canvas, Point point)
        => (string?)typeof(DxfPreviewCanvas)
            .GetMethod("HitTestEditableMeasurementId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, [point]);
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

    private static void InvokeLineClick(DxfPreviewCanvas canvas, Point point)
        => typeof(DxfPreviewCanvas)
            .GetMethod("HandleSketchLineClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, [point]);

    private static void InvokeCircleClick(DxfPreviewCanvas canvas, Point point)
        => typeof(DxfPreviewCanvas)
            .GetMethod("HandleSketchCircleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, [point]);

    private static DxfCanvasInteractionSession InteractionSession(DxfPreviewCanvas canvas)
        => (DxfCanvasInteractionSession)typeof(DxfPreviewCanvas)
            .GetField("_interaction", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(canvas)!;

    private static void InvokeVertexPreview(DxfPreviewCanvas canvas, Point point)
        => typeof(DxfPreviewCanvas)
            .GetMethod("ApplyVertexEdit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, [point]);

    private static bool InvokeVertexCommit(DxfPreviewCanvas canvas)
        => (bool)typeof(DxfPreviewCanvas)
            .GetMethod("CommitVertexEdit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, null)!;

    private static void InvokePenCommit(DxfPreviewCanvas canvas, bool isClosed)
        => typeof(DxfPreviewCanvas)
            .GetMethod("CommitPendingPenPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, [isClosed]);

    private static void InvokePenPress(DxfPreviewCanvas canvas, Point point, KeyModifiers modifiers)
        => typeof(DxfPreviewCanvas)
            .GetMethod("HandlePenPress", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, [point, modifiers, null]);

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
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void PenClick_CompletesOnlyCreationDraft(
        bool isEditing,
        bool closeCompletion,
        bool expected)
    {
        var completion = closeCompletion ? DxfPenCompletion.Closed : DxfPenCompletion.Open;
        Assert.Equal(expected, DxfCanvasPenInteraction.ShouldCompletePath(isEditing, completion));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void PenClick_AppendsOnlyCreationOrOpenEdit(bool isEditing, bool isClosed, bool expected)
        => Assert.Equal(expected, DxfCanvasPenInteraction.ShouldAppendAnchor(isEditing, isClosed));

    [Fact]
    public async Task PenCommit_EditingEarlierPathKeepsEditedPathSelected()
    {
        await _ui.RunAsync(() =>
        {
            var originalAnchors = new[]
            {
                new Editor2DBezierAnchor(new(0, 0)),
                new Editor2DBezierAnchor(new(10, 0)),
            };
            var editedPath = new Editor2DPreviewPath(
                "pen-a",
                "LWPOLYLINE",
                Editor2DBezierGeometry.Flatten(originalAnchors, closed: false),
                false,
                BezierAnchors: originalAnchors);
            var laterPath = new Editor2DPreviewPath("line-b", "LINE", [new(20, 0), new(30, 0)], false);
            var canvas = Canvas(Document([editedPath, laterPath]));
            var session = InteractionSession(canvas);
            session.EditingPenPathId = editedPath.Id;
            session.PendingPenAnchors = originalAnchors
                .Select(anchor => DxfCanvasPenEditing.MoveAnchor(anchor, new(2, 3)))
                .ToArray();

            InvokePenCommit(canvas, isClosed: false);

            Assert.Equal([editedPath.Id], canvas.SelectedPathIds);
            Assert.Equal(laterPath.Id, canvas.Document!.Paths[^1].Id);
            Assert.Equal(new Editor2DPoint(2, 3), canvas.Document.Paths[0].BezierAnchors![0].Point);
        });
    }

    [Fact]
    public async Task PenAltClick_RemovesStagedAnchorWithoutMutatingEditedPathOrAppendingOnBlankCanvas()
    {
        await _ui.RunAsync(() =>
        {
            var anchors = new[]
            {
                new Editor2DBezierAnchor(new(0, 0)),
                new Editor2DBezierAnchor(new(20, 0)),
                new Editor2DBezierAnchor(new(20, 20)),
            };
            var path = new Editor2DPreviewPath(
                "pen-a",
                "LWPOLYLINE",
                Editor2DBezierGeometry.Flatten(anchors, closed: true),
                true,
                BezierAnchors: anchors);
            var canvas = Canvas(Document([path]));
            var session = InteractionSession(canvas);
            session.EditingPenPathId = path.Id;
            session.EditingPenClosed = true;
            session.PendingPenAnchors = anchors;

            InvokePenPress(canvas, Screen(canvas, anchors[0].Point), KeyModifiers.Alt);

            Assert.Equal(2, session.PendingPenAnchors.Count);
            Assert.DoesNotContain(anchors[0], session.PendingPenAnchors);
            Assert.Equal(anchors, canvas.Document!.Paths[0].BezierAnchors);

            InvokePenPress(canvas, new Point(399, 299), KeyModifiers.Alt);

            Assert.Equal(2, session.PendingPenAnchors.Count);
            Assert.Equal(anchors, canvas.Document.Paths[0].BezierAnchors);
        });
    }

    [Fact]
    public async Task PenEdit_ClosedPathIgnoresBlankCanvasPress()
    {
        await _ui.RunAsync(() =>
        {
            var anchors = new[]
            {
                new Editor2DBezierAnchor(new(0, 0)),
                new Editor2DBezierAnchor(new(20, 0)),
                new Editor2DBezierAnchor(new(20, 20)),
            };
            var path = new Editor2DPreviewPath(
                "pen-a",
                "LWPOLYLINE",
                Editor2DBezierGeometry.Flatten(anchors, closed: true),
                true,
                BezierAnchors: anchors);
            var canvas = Canvas(Document([path]));
            var session = InteractionSession(canvas);
            session.EditingPenPathId = path.Id;
            session.EditingPenClosed = true;
            session.PendingPenAnchors = anchors;

            InvokePenPress(canvas, new Point(399, 299), KeyModifiers.None);

            Assert.Equal(anchors, session.PendingPenAnchors);
            Assert.Equal(anchors, canvas.Document!.Paths[0].BezierAnchors);
            Assert.Equal(path.Points, canvas.Document.Paths[0].Points);
        });
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

    private sealed class ThicknessGeometryKernelService : IEditor2DGeometryKernelService
    {
        public Task<Editor2DGeometryKernelResult> BuildCurveOffsetPathsAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double offsetDistance,
            bool offsetOutward,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Editor2DGeometryKernelResult.Success([]));

        public Task<Editor2DGeometryKernelResult> BuildThicknessOutlinesAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double thickness,
            CancellationToken cancellationToken = default)
        {
            var outlines = sourcePaths.Select(path =>
            {
                var start = path.Points[0];
                var end = path.Points[^1];
                var half = thickness / 2.0;
                return new Editor2DPreviewPath(
                    $"{path.Id}:thickness",
                    "LWPOLYLINE",
                    [
                        new(start.X, start.Y - half),
                        new(end.X, end.Y - half),
                        new(end.X, end.Y + half),
                        new(start.X, start.Y + half),
                    ],
                    true);
            }).ToArray();
            return Task.FromResult(Editor2DGeometryKernelResult.Success(outlines));
        }
    }
}
