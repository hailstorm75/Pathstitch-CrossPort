using Domain.App.Models;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class Editor2DSelectionTransformTests
{
    [Fact]
    public void TransformPath_AppliesAffineTransformToAllPathGeometry()
    {
        var path = new Editor2DPreviewPath(
            "curve",
            "TEXT",
            [new(1, 0), new(2, 0)],
            false,
            Start: new(1, 0),
            RotationDegrees: 0,
            BezierAnchors: [new(new(1, 0), new(0, 0), new(2, 0))]);
        var transform = Editor2DAffineTransform.CreateRotation(new(0, 0), 90)
            .Then(Editor2DAffineTransform.CreateTranslation(5, 3));

        var transformed = Editor2DGeometry.TransformPath(path, transform);

        AssertPoint(new(5, 4), transformed.Points[0]);
        AssertPoint(new(5, 5), transformed.Points[1]);
        AssertPoint(new(5, 4), transformed.Start!);
        Assert.Equal(90, transformed.RotationDegrees!.Value, 8);
        var anchor = Assert.Single(transformed.BezierAnchors!);
        AssertPoint(new(5, 4), anchor.Point);
        AssertPoint(new(5, 3), anchor.HandleIn!);
        AssertPoint(new(5, 5), anchor.HandleOut!);
    }

    [Fact]
    public void ApplySelectionTransform_UpdatesAttachedManualMeasurementAndCornerSources()
    {
        var selected = Line("selected", 0, 0, 2, 0);
        var untouched = Line("untouched", 10, 0, 12, 0);
        var attached = new Editor2DMeasurement(
            "attached", new(0, 2), new(2, 2), EntityPathId: selected.Id,
            DimensionType: "length", RectP1: new(0, 0), RectP2: new(2, 2),
            VarName: "d1", Expression: "2", IsParametric: true, EvaluatedValue: 2);
        var automatic = new Editor2DMeasurement(
            "automatic", new(0, -2), new(2, -2), IsAutoDimension: true, EntityPathId: selected.Id);
        var free = new Editor2DMeasurement("free", new(20, 20), new(21, 20));
        var corner = new Editor2DCornerParameter(
            "corner", selected.Id, 1, Editor2DCornerKind.Chamfer, 1,
            [new(0, 0), new(2, 0), new(2, 2)]);
        var workspace = Workspace(
            [selected, untouched],
            SelectedPathIds: [selected.Id],
            Measurements: [attached, automatic, free],
            CornerParameters: [corner]);

        Assert.True(workspace.ApplySelectionTransform(Editor2DAffineTransform.CreateTranslation(5, -1)));

        var moved = workspace.Document.Paths.Single(path => path.Id == selected.Id);
        AssertPoint(new(5, -1), moved.Points[0]);
        Assert.Same(untouched, workspace.Document.Paths.Single(path => path.Id == untouched.Id));
        var movedMeasurement = workspace.Measurements.Single(item => item.Id == attached.Id);
        AssertPoint(new(5, 1), movedMeasurement.Start);
        AssertPoint(new(7, 1), movedMeasurement.End);
        AssertPoint(new(5, -1), movedMeasurement.RectP1!);
        AssertPoint(new(7, 1), movedMeasurement.RectP2!);
        Assert.Null(movedMeasurement.EvaluatedValue);
        Assert.Equal(automatic, workspace.Measurements.Single(item => item.Id == automatic.Id));
        Assert.Equal(free, workspace.Measurements.Single(item => item.Id == free.Id));
        Assert.Equal(
            [new Editor2DPoint(5, -1), new Editor2DPoint(7, -1), new Editor2DPoint(7, 1)],
            Assert.Single(workspace.CornerParameters).SourcePoints);
    }

    [Fact]
    public void ApplySelectionTransform_FullConvertLineGroupKeepsGroupAndMovesSourceSnapshot()
    {
        var source = Line("source", 0, 0, 24, 0);
        var workspace = Workspace([source], SelectedPathIds: [source.Id]);
        Assert.True(workspace.ApplyConvertedLines("dashed", new Dictionary<string, double>
        {
            ["dash_length"] = 4,
            ["gap"] = 2,
        }).IsSuccess);
        var group = Assert.Single(workspace.ConvertLineGroups);
        workspace.SetSelection(group.GeneratedPathIds);

        Assert.True(workspace.ApplySelectionTransform(Editor2DAffineTransform.CreateTranslation(3, 4)));

        var retained = Assert.Single(workspace.ConvertLineGroups);
        Assert.Equal(group.Id, retained.Id);
        var sourceSnapshot = Assert.Single(retained.Sources).SourcePath;
        AssertPoint(new(3, 4), sourceSnapshot.Points[0]);
        AssertPoint(new(27, 4), sourceSnapshot.Points[1]);
    }

    [Fact]
    public void ApplySelectionTransform_PartialConvertLineGroupDetaches()
    {
        var source = Line("source", 0, 0, 24, 0);
        var workspace = Workspace([source], SelectedPathIds: [source.Id]);
        Assert.True(workspace.ApplyConvertedLines("dashed", new Dictionary<string, double>()).IsSuccess);
        var group = Assert.Single(workspace.ConvertLineGroups);
        workspace.SetSelection([group.GeneratedPathIds[0]]);

        Assert.True(workspace.ApplySelectionTransform(Editor2DAffineTransform.CreateTranslation(2, 0)));

        Assert.Empty(workspace.ConvertLineGroups);
    }

    [Fact]
    public void ApplyScale_ScalesSynchronizedPhysicalMetadataButNotCounts()
    {
        var generated = Line("generated", 0, 0, 4, 0);
        var sourceSnapshot = Line("source", 0, 0, 12, 0);
        var measurement = new Editor2DMeasurement(
            "attached", new(0, 2), new(4, 2), EntityPathId: generated.Id,
            DimensionType: "length", FilletRadius: 1.5, OffsetDistance: 3);
        var corner = new Editor2DCornerParameter(
            "corner", generated.Id, 1, Editor2DCornerKind.Chamfer, 2,
            [new(0, 0), new(4, 0), new(4, 4)]);
        var group = new Editor2DConvertLineGroup(
            "group", "wave", new Dictionary<string, double>
            {
                ["wavelength"] = 6,
                ["amplitude"] = 2,
                ["samples_per_wave"] = 12,
            },
            [new Editor2DConvertLineSource(sourceSnapshot, "layer", 0, 0, [generated.Id])]);
        var workspace = Workspace(
            [generated],
            SelectedPathIds: [generated.Id],
            Measurements: [measurement],
            CornerParameters: [corner],
            ConvertLineGroups: [group]);

        Assert.True(workspace.ApplyScale(2, fromCenter: false).IsSuccess);

        Assert.Equal(4, Assert.Single(workspace.CornerParameters).Value, 8);
        var scaledMeasurement = Assert.Single(workspace.Measurements);
        Assert.Equal(3, scaledMeasurement.FilletRadius, 8);
        Assert.Equal(6, scaledMeasurement.OffsetDistance, 8);
        var scaledGroup = Assert.Single(workspace.ConvertLineGroups);
        Assert.Equal(12, scaledGroup.Settings["wavelength"], 8);
        Assert.Equal(4, scaledGroup.Settings["amplitude"], 8);
        Assert.Equal(12, scaledGroup.Settings["samples_per_wave"], 8);
        AssertPoint(new(24, 0), Assert.Single(scaledGroup.Sources).SourcePath.Points[1]);
    }

    [Fact]
    public void ApplySelectionTransform_CopyClonesEditableMetadataButNotMeasurements()
    {
        var generated = Line("generated", 0, 0, 4, 0);
        var sourceSnapshot = Line("source", 0, 0, 12, 0);
        var measurement = new Editor2DMeasurement(
            "attached", new(0, 2), new(4, 2), EntityPathId: generated.Id, DimensionType: "length");
        var corner = new Editor2DCornerParameter(
            "corner", generated.Id, 1, Editor2DCornerKind.Chamfer, 1,
            [new(0, 0), new(4, 0), new(4, 4)]);
        var group = new Editor2DConvertLineGroup(
            "group", "dashed", new Dictionary<string, double>(),
            [new Editor2DConvertLineSource(sourceSnapshot, "layer", 0, 0, [generated.Id])]);
        var workspace = Workspace(
            [generated],
            SelectedPathIds: [generated.Id],
            Measurements: [measurement],
            CornerParameters: [corner],
            ConvertLineGroups: [group]);
        var persistedGroup = Assert.Single(workspace.ConvertLineGroups);

        Assert.True(workspace.ApplySelectionTransform(
            Editor2DAffineTransform.CreateTranslation(10, 0),
            createCopy: true));

        var copyId = Assert.Single(workspace.SelectedPathIds);
        Assert.NotEqual(generated.Id, copyId);
        AssertPoint(new(10, 0), workspace.Document.Paths.Single(path => path.Id == copyId).Points[0]);
        Assert.Equal([measurement], workspace.Measurements);
        Assert.Equal(2, workspace.CornerParameters.Count);
        var copiedCorner = workspace.CornerParameters.Single(item => item.PathId == copyId);
        Assert.NotEqual(corner.Id, copiedCorner.Id);
        Assert.Equal(
            [new Editor2DPoint(10, 0), new Editor2DPoint(14, 0), new Editor2DPoint(14, 4)],
            copiedCorner.SourcePoints);
        Assert.Equal([generated.Id, copyId], Assert.Single(workspace.Layers).PathIds);
        Assert.Equal(2, workspace.ConvertLineGroups.Count);
        var retainedGroup = workspace.ConvertLineGroups.Single(item => item.Id == persistedGroup.Id);
        Assert.Equal(persistedGroup.Id, retainedGroup.Id);
        Assert.Equal(persistedGroup.Style, retainedGroup.Style);
        Assert.Equal(persistedGroup.Settings.OrderBy(item => item.Key), retainedGroup.Settings.OrderBy(item => item.Key));
        Assert.Equal(Assert.Single(persistedGroup.Sources).SourcePath, Assert.Single(retainedGroup.Sources).SourcePath);
        Assert.Equal(persistedGroup.GeneratedPathIds, retainedGroup.GeneratedPathIds);
        var copiedGroup = workspace.ConvertLineGroups.Single(item => item.Id != persistedGroup.Id);
        Assert.Equal([copyId], copiedGroup.GeneratedPathIds);
        AssertPoint(new(10, 0), Assert.Single(copiedGroup.Sources).SourcePath.Points[0]);
        AssertPoint(new(22, 0), Assert.Single(copiedGroup.Sources).SourcePath.Points[1]);
        Assert.DoesNotContain(workspace.Measurements, item => item.EntityPathId == copyId);
    }

    private static Editor2DWorkspaceViewModel Workspace(
        IReadOnlyList<Editor2DPreviewPath> paths,
        IReadOnlyList<string>? SelectedPathIds = null,
        IReadOnlyList<Editor2DMeasurement>? Measurements = null,
        IReadOnlyList<Editor2DCornerParameter>? CornerParameters = null,
        IReadOnlyList<Editor2DConvertLineGroup>? ConvertLineGroups = null)
    {
        var layer = new Editor2DLayer("layer", "Layer", paths.Select(path => path.Id).ToArray());
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(new Editor2DWorkspaceState(
            Document(paths),
            SelectedPathIds: SelectedPathIds,
            Measurements: Measurements,
            Layers: [layer],
            ActiveLayerId: layer.Id,
            CornerParameters: CornerParameters,
            ConvertLineGroups: ConvertLineGroups), recordHistory: false);
        workspace.ClearHistory();
        return workspace;
    }

    private static Editor2DPreviewPath Line(string id, double x1, double y1, double x2, double y2)
        => new(id, "LINE", [new(x1, y1), new(x2, y2)], false);

    private static Editor2DPreviewDocument Document(IReadOnlyList<Editor2DPreviewPath> paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        return new Editor2DPreviewDocument(
            paths,
            new Editor2DBounds(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.X),
                points.Max(point => point.Y)),
            paths.GroupBy(path => path.EntityType).ToDictionary(group => group.Key, group => group.Count()),
            []);
    }

    private static void AssertPoint(Editor2DPoint expected, Editor2DPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }
}
