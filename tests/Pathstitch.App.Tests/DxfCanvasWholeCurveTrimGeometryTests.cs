using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Tests;

public sealed class DxfCanvasWholeCurveTrimGeometryTests
{
    [Fact]
    public void OpenPen_UsesGlobalCutsAndBakesPlainSurvivors()
    {
        var pen = Pen("pen", [new(0, 0), new(10, 0), new(10, 10), new(20, 10)]) with
        {
            Start = new(99, 99), Text = "stale", Center = new(3, 4), Radius = 8,
            StartAngleDegrees = 10, EndAngleDegrees = 20, IsFilled = true,
        };
        var document = Document(
            pen,
            Line("left", 5, -5, 5, 5),
            Line("left-duplicate", 5, -5, 5, 5),
            Polyline("right", [new(15, 5), new(15, 15)]));

        Assert.True(DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(document, new(10, 5), 0.25, out var target));

        Assert.Equal([new(5, 0), new(10, 0), new(10, 10), new(15, 10)], target.PreviewPoints);
        Assert.Collection(target.ReplacementPaths,
            first => Assert.Equal([new(0, 0), new(5, 0)], first.Points),
            second => Assert.Equal([new(15, 10), new(20, 10)], second.Points));
        Assert.All(target.ReplacementPaths, survivor =>
        {
            Assert.Equal("LINE", survivor.EntityType);
            Assert.NotEqual(pen.Id, survivor.Id);
            Assert.Equal(survivor.Points[0], survivor.Start);
            Assert.Null(survivor.BezierAnchors);
            Assert.Null(survivor.Center);
            Assert.Null(survivor.Radius);
            Assert.Null(survivor.StartAngleDegrees);
            Assert.Null(survivor.EndAngleDegrees);
            Assert.Null(survivor.Text);
            Assert.False(survivor.IsFilled);
            Assert.False(survivor.IsClosed);
        });
        Assert.Equal(2, target.ReplacementPaths.Select(path => path.Id).Distinct().Count());
    }

    [Fact]
    public void OpenPen_ZeroCutsDeletesWholeAndOneCutPreservesOppositeSide()
    {
        var pen = Pen("pen", [new(0, 0), new(10, 0), new(20, 0)]);
        Assert.True(DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(Document(pen), new(5, 0), 0.25, out var noCuts));
        Assert.Empty(noCuts.ReplacementPaths);
        Assert.Equal(pen.Points, noCuts.PreviewPoints);

        var oneCut = Document(pen, Line("cut", 15, -5, 15, 5));
        Assert.True(DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(oneCut, new(5, 0), 0.25, out var target));
        var survivor = Assert.Single(target.ReplacementPaths);
        Assert.Equal([new(15, 0), new(20, 0)], survivor.Points);
        Assert.Equal([new(0, 0), new(10, 0), new(15, 0)], target.PreviewPoints);

        Assert.True(DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(oneCut, new(15, 0), 0.25, out var onCut));
        Assert.Empty(onCut.PreviewPoints);
        Assert.Collection(onCut.ReplacementPaths,
            first => Assert.Equal([new(0, 0), new(10, 0), new(15, 0)], first.Points),
            second => Assert.Equal([new(15, 0), new(20, 0)], second.Points));
    }

    [Fact]
    public void ClosedPen_UsesPeriodicComplementAndDeduplicatesVertexCuts()
    {
        var pen = Pen("closed", [new(0, 0), new(10, 0), new(10, 10), new(0, 10)], closed: true);
        var document = Document(pen, Line("across", -5, 5, 15, 5));

        Assert.True(DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(document, new(0, 2), 0.25, out var target));

        var survivor = Assert.Single(target.ReplacementPaths);
        Assert.Equal([new(10, 5), new(10, 10), new(0, 10), new(0, 5)], survivor.Points);
        Assert.Equal("LWPOLYLINE", survivor.EntityType);
        Assert.False(survivor.IsClosed);
        Assert.Null(survivor.BezierAnchors);
        Assert.Equal(survivor.Points[0], survivor.Start);
        Assert.Equal([new(0, 5), new(0, 0), new(10, 0), new(10, 5)], target.PreviewPoints);

        var duplicateSeamCuts = Document(
            pen,
            Line("vertex-a", -5, 0, 5, 0),
            Line("vertex-b", 0, -5, 0, 5));
        Assert.True(DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(duplicateSeamCuts, new(10, 5), 0.25, out var seamTarget));
        Assert.Empty(seamTarget.ReplacementPaths);
    }

    [Fact]
    public void CircularCutters_RejectTangencyAndFilterArcSweep()
    {
        var pen = Pen("pen", [new(-10, 0), new(0, 0), new(10, 0)]);
        var circleDocument = Document(
            pen,
            Circle("crossing", new(0, 0), 5),
            Circle("tangent", new(0, 5), 5));
        Assert.True(DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(circleDocument, new(0, 0), 0.25, out var circleTarget));
        Assert.Equal(2, circleTarget.ReplacementPaths.Count);
        Assert.Equal([new(-5, 0), new(0, 0), new(5, 0)], circleTarget.PreviewPoints);

        var arcDocument = Document(
            pen,
            Arc("quarter", new(0, 0), 5, 0, 90),
            Circle("tangent", new(0, 5), 5));
        Assert.True(DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(arcDocument, new(-7, 0), 0.25, out var arcTarget));
        var survivor = Assert.Single(arcTarget.ReplacementPaths);
        Assert.Equal([new(5, 0), new(10, 0)], survivor.Points);
    }

    [Fact]
    public async Task WorkspaceReplacement_PrunesMirrorLinkUndoesRedoesAndPersistsBakedPaths()
    {
        var pen = Pen("pen", [new(0, 0), new(10, 0), new(10, 10), new(20, 10)]);
        var left = Line("left", 5, -5, 5, 5);
        var right = Line("right", 15, 5, 15, 15);
        var document = Document(pen, left, right);
        Assert.True(DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(document, new(10, 5), 0.25, out var target));

        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Document = document,
            SelectedPathIds = [pen.Id],
            Layers = [new Editor2DLayer("layer", "Layer", [pen.Id, left.Id, right.Id])],
            ActiveLayerId = "layer",
            Measurements = [new Editor2DMeasurement("measurement", new(0, 0), new(1, 0), EntityPathId: pen.Id)],
            CornerParameters = [new Editor2DCornerParameter(
                "corner", pen.Id, 1, Editor2DCornerKind.Chamfer, 1, [new(0, 0), new(10, 0), new(10, 10)])],
            SewingHoleOperations = [new Editor2DSewingHoleOperation(
                "sewing", [pen.Id], [left.Id], Editor2DSewingHoleParameters.Default)],
            ImportGroups = [new Editor2DImportGroup("import", "pen.dxf", 1, [pen.Id], "layer", 1, 1, [])],
        }, recordHistory: false);
        Assert.True(workspace.ApplyMirror(new(30, -10), new(30, 20)).IsSuccess);
        Assert.Equal(2, workspace.MirrorLinks.Count);
        workspace.ClearHistory();

        Assert.True(workspace.ReplacePath(pen.Id, target.ReplacementPaths));
        Assert.Empty(workspace.MirrorLinks);
        Assert.Empty(workspace.Measurements);
        Assert.Empty(workspace.CornerParameters);
        Assert.Empty(workspace.SewingHoleOperations);
        Assert.Empty(workspace.ImportGroups);
        Assert.Equal([.. target.ReplacementPaths.Select(path => path.Id), left.Id, right.Id], workspace.Layers[0].PathIds.Take(4));
        Assert.True(workspace.Undo());
        Assert.Contains(workspace.Document.Paths, path => path.Id == pen.Id && path.BezierAnchors is not null);
        Assert.Single(workspace.Measurements);
        Assert.Single(workspace.CornerParameters);
        Assert.Single(workspace.SewingHoleOperations);
        Assert.Single(workspace.ImportGroups);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.Redo());
        Assert.DoesNotContain(workspace.Document.Paths, path => path.Id == pen.Id);
        Assert.Empty(workspace.Measurements);
        Assert.Empty(workspace.CornerParameters);
        Assert.Empty(workspace.SewingHoleOperations);
        Assert.Empty(workspace.ImportGroups);
        Assert.All(target.ReplacementPaths, expected => Assert.Contains(workspace.Document.Paths, path => path.Id == expected.Id));

        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-WholeTrim", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "trim.stch");
            var service = new Project3DStateService();
            await service.SaveAsync(projectPath, Project3DState.Empty with { TwoDWorkspaceState = workspace.State });
            var restored = (await service.LoadAsync(projectPath)).TwoDWorkspaceState!;
            var restoredSurvivors = restored.Document.Paths.Where(path => target.ReplacementPaths.Any(item => item.Id == path.Id)).ToArray();
            Assert.Equal(2, restoredSurvivors.Length);
            Assert.All(restoredSurvivors, path =>
            {
                Assert.Null(path.BezierAnchors);
                Assert.Contains(path.EntityType, new[] { "LINE", "LWPOLYLINE" });
                Assert.Equal(path.Points[0], path.Start);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Canvas_PrioritizesWholeCurveAndRoutesThroughWorkspaceReplacement()
    {
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");
        var wholeIndex = canvas.IndexOf("DxfCanvasWholeCurveTrimGeometry.TryBuildTarget", StringComparison.Ordinal);
        var genericIndex = canvas.IndexOf("TryBuildTrimTarget(document", StringComparison.Ordinal);

        Assert.True(wholeIndex >= 0 && wholeIndex < genericIndex);
        Assert.Contains("ApplyPathReplacement(wholeCurveTarget.PathId, wholeCurveTarget.ReplacementPaths)", canvas, StringComparison.Ordinal);
        Assert.Contains("PathReplacementRequested?.Invoke(request)", canvas, StringComparison.Ordinal);
    }

    private static Editor2DPreviewPath Pen(string id, IReadOnlyList<Editor2DPoint> points, bool closed = false)
        => new(id, "LWPOLYLINE", points, closed,
            BezierAnchors: [new(points[0]), new(points[^1])]);

    private static Editor2DPreviewPath Line(string id, double x1, double y1, double x2, double y2)
        => new(id, "LINE", [new(x1, y1), new(x2, y2)], false);

    private static Editor2DPreviewPath Polyline(string id, IReadOnlyList<Editor2DPoint> points)
        => new(id, "LWPOLYLINE", points, false);

    private static Editor2DPreviewPath Circle(string id, Editor2DPoint center, double radius)
        => new(id, "CIRCLE", DxfCanvasGeometryEditor.BuildCircle(center, radius), true,
            Center: center, Radius: radius, StartAngleDegrees: 0, EndAngleDegrees: 360);

    private static Editor2DPreviewPath Arc(string id, Editor2DPoint center, double radius, double start, double end)
        => new(id, "ARC", Editor2DGeometry.BuildArcPoints(center, radius, start, end), false,
            Center: center, Radius: radius, StartAngleDegrees: start, EndAngleDegrees: end);

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        return new(
            paths,
            new(points.Min(point => point.X), points.Min(point => point.Y),
                points.Max(point => point.X), points.Max(point => point.Y)),
            paths.GroupBy(path => path.EntityType).ToDictionary(group => group.Key, group => group.Count()),
            []);
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(Path.Combine(pathParts));
    }
}
