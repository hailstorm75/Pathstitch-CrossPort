using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Tests;

public sealed class Editor2DProjectCombinerTests
{
    [Fact]
    public void CombineRemapsCollidingPathsAndMergesLayersByName()
    {
        var targetPath = Path("shared", 0, 0, 2, 0);
        var incomingPath = Path("shared", -5, -2, -1, -2);
        var target = State(
            [targetPath],
            [new Editor2DLayer("target-layer", "Cut", ["shared"])],
            [new Editor2DMeasurement("target-measure", new(0, 0), new(2, 0), EntityPathId: "shared")]);
        var incoming = State(
            [incomingPath],
            [new Editor2DLayer("incoming-layer", "Cut", ["shared"])],
            [new Editor2DMeasurement("incoming-measure", new(-5, -2), new(-1, -2), EntityPathId: "shared")]);

        var merged = Editor2DProjectCombiner.Combine(target, incoming);

        Assert.Equal(2, merged.Document.Paths.Count);
        var imported = Assert.Single(merged.Document.Paths, path => path.Id != "shared");
        Assert.Equal(10, imported.Points[0].X);
        Assert.Equal(10, imported.Points[0].Y);
        var layer = Assert.Single(merged.Layers!);
        Assert.Equal(2, layer.PathIds.Count);
        var importedMeasurement = Assert.Single(merged.Measurements!, item => item.Id == "incoming-measure");
        Assert.Equal(imported.Id, importedMeasurement.EntityPathId);
        Assert.Equal(10, importedMeasurement.Start.X);
    }

    [Fact]
    public void CombinePreservesTargetSettingsAndSkipsReferenceImagesAndDuplicateMeasurements()
    {
        var target = State(
            [Path("one", 0, 0, 1, 1)],
            [new Editor2DLayer("one-layer", "Target", ["one"])],
            [new Editor2DMeasurement("same", new(0, 0), new(1, 1))]) with
        {
            ActiveTool = Editor2DTool.Trim,
            ViewportZoom = 4.5,
            SelectedPathIds = ["one"],
        };
        var reference = new Editor2DReferenceImage("ref", "ref.png", "AA==", 1, 1, 0, 0, 1, 1);
        var incoming = State(
            [Path("two", 5, 5, 6, 6)],
            [new Editor2DLayer("ref", "Reference", [], Kind: Editor2DLayerKind.ReferenceImage, ReferenceImage: reference)],
            [new Editor2DMeasurement("same", new(5, 5), new(6, 6))]);

        var merged = Editor2DProjectCombiner.Combine(target, incoming);

        Assert.Equal(Editor2DTool.Trim, merged.ActiveTool);
        Assert.Equal(4.5, merged.ViewportZoom);
        Assert.Equal(["one"], merged.SelectedPathIds);
        Assert.DoesNotContain(merged.Layers!, layer => layer.IsReferenceImage);
        Assert.Single(merged.Measurements!);
    }

    private static Editor2DWorkspaceState State(
        IReadOnlyList<Editor2DPreviewPath> paths,
        IReadOnlyList<Editor2DLayer> layers,
        IReadOnlyList<Editor2DMeasurement> measurements)
        => new(
            new Editor2DPreviewDocument(paths, new Editor2DBounds(-5, -2, 6, 6), new Dictionary<string, int>(), []),
            Measurements: measurements,
            Layers: layers,
            IsInitialized: true);

    private static Editor2DPreviewPath Path(string id, double x1, double y1, double x2, double y2)
        => new(id, "LINE", [new(x1, y1), new(x2, y2)], false);
}
