using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Tests;

public sealed class DxfCanvasCircularTrimGeometryTests
{
    [Fact]
    public void CircleTrim_RemovesClickedAngularSpanAndKeepsSemanticArc()
    {
        var circle = Circle("circle", new(0, 0), 10) with { IsConstruction = true };
        var document = Document(circle, Line("left", -5, -20, -5, 20), Line("right", 5, -20, 5, 20));

        Assert.True(DxfCanvasCircularTrimGeometry.TryBuildTarget(document, new(0, 10), 0.5, out var target));

        var survivor = Assert.Single(target.ReplacementPaths);
        Assert.Equal("ARC", survivor.EntityType);
        Assert.Equal(120, survivor.StartAngleDegrees!.Value, 6);
        Assert.Equal(60, survivor.EndAngleDegrees!.Value, 6);
        Assert.Equal(circle.Center, survivor.Center);
        Assert.Equal(circle.Radius, survivor.Radius);
        Assert.True(survivor.IsConstruction);
        Assert.False(survivor.IsClosed);
        Assert.True(target.PreviewPoints.Count > 2);
        Assert.All(target.PreviewPoints, point => Assert.Equal(10, Distance(circle.Center!, point), 5));
    }

    [Fact]
    public void ArcTrim_UsesOnlyCutsInsideSweepAndCanProduceTwoFragments()
    {
        var arc = Arc("arc", new(0, 0), 10, 0, 180);
        var outsideArcCutter = Arc("outside", new(10, 0), 10, 180, 360);
        var document = Document(
            arc,
            Line("left", -5, -20, -5, 20),
            Line("right", 5, -20, 5, 20),
            outsideArcCutter);

        Assert.True(DxfCanvasCircularTrimGeometry.TryBuildTarget(document, new(0, 10), 0.5, out var target));

        Assert.Equal(2, target.ReplacementPaths.Count);
        Assert.Collection(target.ReplacementPaths,
            first =>
            {
                Assert.Equal(0, first.StartAngleDegrees!.Value, 6);
                Assert.Equal(60, first.EndAngleDegrees!.Value, 6);
            },
            second =>
            {
                Assert.Equal(120, second.StartAngleDegrees!.Value, 6);
                Assert.Equal(180, second.EndAngleDegrees!.Value, 6);
            });
        Assert.False(DxfCanvasCircularTrimGeometry.TryBuildTarget(document, new(0, -10), 0.5, out _));
    }

    [Fact]
    public void TangencyDoesNotBoundTrimAndCircleIntersectionDoes()
    {
        var circle = Circle("circle", new(0, 0), 10);
        var tangentDocument = Document(circle, Line("tangent", -20, 10, 20, 10));

        Assert.True(DxfCanvasCircularTrimGeometry.TryBuildTarget(tangentDocument, new(10, 0), 0.5, out var tangentTarget));
        Assert.Empty(tangentTarget.ReplacementPaths);

        var crossingCircle = Circle("crossing", new(10, 0), 10);
        var crossingDocument = Document(circle, crossingCircle);
        Assert.True(DxfCanvasCircularTrimGeometry.TryBuildTarget(crossingDocument, new(10, 0), 0.5, out var crossingTarget));
        var survivor = Assert.Single(crossingTarget.ReplacementPaths);
        Assert.Equal(60, survivor.StartAngleDegrees!.Value, 6);
        Assert.Equal(300, survivor.EndAngleDegrees!.Value, 6);
    }

    [Fact]
    public void ArcCutter_ContributesOnlyIntersectionsInsideItsSweep()
    {
        var host = Circle("host", new(0, 0), 10);
        var line = Line("line", -5, -20, -5, 20);
        var upperArc = Arc("upper-cutter", new(10, 0), 10, 90, 180);
        var document = Document(host, line, upperArc);

        Assert.True(DxfCanvasCircularTrimGeometry.TryBuildTarget(document, new(0, 10), 0.5, out var target));

        var survivor = Assert.Single(target.ReplacementPaths);
        Assert.Equal(120, survivor.StartAngleDegrees!.Value, 6);
        Assert.Equal(60, survivor.EndAngleDegrees!.Value, 6);
    }

    [Fact]
    public void WorkspaceReplacement_PreservesSourceLayerDetachesOwnershipAndUndoesOnce()
    {
        var before = Line("before", -20, 0, -15, 0);
        var circle = Circle("circle", new(0, 0), 10);
        var after = Line("after", 15, 0, 20, 0);
        var left = Line("left", -5, -20, -5, 20);
        var right = Line("right", 5, -20, 5, 20);
        var document = Document(before, circle, after, left, right);
        Assert.True(DxfCanvasCircularTrimGeometry.TryBuildTarget(document, new(0, 10), 0.5, out var target));
        var sourceLayer = new Editor2DLayer("source-layer", "Source", [before.Id, circle.Id, after.Id]);
        var activeLayer = new Editor2DLayer("active-layer", "Active", [left.Id, right.Id], Order: 1);
        var measurement = new Editor2DMeasurement("measure", new(0, 0), new(10, 0), EntityPathId: circle.Id);
        var corner = new Editor2DCornerParameter(
            "corner", circle.Id, 0, Editor2DCornerKind.Chamfer, 1, [new(0, 0), new(1, 0), new(1, 1)]);
        var convert = new Editor2DConvertLineGroup(
            "convert", "dashed", new Dictionary<string, double>(),
            [new Editor2DConvertLineSource(
                Line("convert-source", 0, 0, 10, 0), sourceLayer.Id, 1, 1, [circle.Id])]);
        var sewing = new Editor2DSewingHoleOperation(
            "sewing", [circle.Id], [left.Id], Editor2DSewingHoleParameters.Default);
        var import = new Editor2DImportGroup(
            "import", "source.dxf", 1.0, [circle.Id], sourceLayer.Id, 1, 1, []);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(new Editor2DWorkspaceState(
            document,
            Measurements: [measurement],
            Layers: [sourceLayer, activeLayer],
            ActiveLayerId: activeLayer.Id,
            CornerParameters: [corner],
            SewingHoleOperations: [sewing],
            ConvertLineGroups: [convert],
            ImportGroups: [import]), recordHistory: false);
        workspace.ClearHistory();

        Assert.True(workspace.ReplacePath(circle.Id, target.ReplacementPaths));

        var replacementIds = target.ReplacementPaths.Select(path => path.Id).ToArray();
        Assert.Equal([before.Id, .. replacementIds, after.Id], workspace.Layers.Single(layer => layer.Id == sourceLayer.Id).PathIds);
        Assert.Equal([left.Id, right.Id], workspace.Layers.Single(layer => layer.Id == activeLayer.Id).PathIds);
        Assert.Empty(workspace.Measurements);
        Assert.Empty(workspace.CornerParameters);
        Assert.Empty(workspace.ConvertLineGroups);
        Assert.Empty(workspace.SewingHoleOperations);
        Assert.Empty(workspace.ImportGroups);
        Assert.True(workspace.CanUndo);

        Assert.True(workspace.Undo());
        Assert.False(workspace.CanUndo);
        Assert.Contains(workspace.Document.Paths, path => path.Id == circle.Id && path.EntityType == "CIRCLE");
        Assert.Equal(sourceLayer.PathIds, workspace.Layers.Single(layer => layer.Id == sourceLayer.Id).PathIds);
        Assert.Single(workspace.Measurements);
        Assert.Single(workspace.CornerParameters);
        Assert.Single(workspace.ConvertLineGroups);
        Assert.Single(workspace.SewingHoleOperations);
        Assert.Single(workspace.ImportGroups);

        Assert.True(workspace.Redo());
        Assert.False(workspace.CanRedo);
        Assert.DoesNotContain(workspace.Document.Paths, path => path.Id == circle.Id);
        Assert.Equal(replacementIds, workspace.Layers.Single(layer => layer.Id == sourceLayer.Id).PathIds.Skip(1).Take(replacementIds.Length));
        Assert.Empty(workspace.Measurements);
        Assert.Empty(workspace.CornerParameters);
        Assert.Empty(workspace.ConvertLineGroups);
        Assert.Empty(workspace.SewingHoleOperations);
        Assert.Empty(workspace.ImportGroups);
    }

    [Fact]
    public void Canvas_RoutesCircularTrimThroughWorkspaceReplacement()
    {
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");
        var view = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DView.axaml.cs");

        Assert.Contains("DxfCanvasCircularTrimGeometry.TryBuildTarget", canvas, StringComparison.Ordinal);
        Assert.Contains("PathReplacementRequested?.Invoke(request)", canvas, StringComparison.Ordinal);
        Assert.Contains("PathReplacementRequested += OnPathReplacementRequested", view, StringComparison.Ordinal);
        Assert.Contains("ReplaceTwoDPath(request.SourcePathId, request.Replacements)", view, StringComparison.Ordinal);
    }

    private static Editor2DPreviewPath Circle(string id, Editor2DPoint center, double radius)
        => new(id, "CIRCLE", DxfCanvasGeometryEditor.BuildCircle(center, radius), true,
            Center: center, Radius: radius, StartAngleDegrees: 0, EndAngleDegrees: 360);

    private static Editor2DPreviewPath Arc(
        string id, Editor2DPoint center, double radius, double start, double end)
        => new(id, "ARC", Editor2DGeometry.BuildArcPoints(center, radius, start, end), false,
            Center: center, Radius: radius, StartAngleDegrees: start, EndAngleDegrees: end);

    private static Editor2DPreviewPath Line(string id, double x1, double y1, double x2, double y2)
        => new(id, "LINE", [new(x1, y1), new(x2, y2)], false);

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        return new(
            paths,
            new Editor2DBounds(
                points.Min(point => point.X), points.Min(point => point.Y),
                points.Max(point => point.X), points.Max(point => point.Y)),
            paths.GroupBy(path => path.EntityType).ToDictionary(group => group.Key, group => group.Count()),
            []);
    }

    private static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(right.X - left.X, 2) + Math.Pow(right.Y - left.Y, 2));

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
