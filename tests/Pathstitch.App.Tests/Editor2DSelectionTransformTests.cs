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
            SourceEntityHandle: "A1",
            BezierAnchors: [new(new(1, 0), new(0, 0), new(2, 0))]);
        var transform = Editor2DAffineTransform.CreateRotation(new(0, 0), 90)
            .Then(Editor2DAffineTransform.CreateTranslation(5, 3));

        var transformed = Editor2DGeometry.TransformPath(path, transform);

        AssertPoint(new(5, 4), transformed.Points[0]);
        AssertPoint(new(5, 5), transformed.Points[1]);
        AssertPoint(new(5, 4), transformed.Start!);
        Assert.Equal("A1", transformed.SourceEntityHandle);
        Assert.Equal(90, transformed.RotationDegrees!.Value, 8);
        var anchor = Assert.Single(transformed.BezierAnchors!);
        AssertPoint(new(5, 4), anchor.Point);
        AssertPoint(new(5, 3), anchor.HandleIn!);
        AssertPoint(new(5, 5), anchor.HandleOut!);
    }

    [Fact]
    public void TransformPath_ReflectionPreservesTextGlyphBasisHandedness()
    {
        var path = new Editor2DPreviewPath(
            "text", "TEXT", [], false,
            Start: new(2, 1), Text: "Mirror", TextHeight: 2,
            RotationDegrees: 30, WidthFactor: 0.8);

        var transformed = Editor2DGeometry.TransformPath(
            path,
            Editor2DAffineTransform.CreateReflectionAcrossVerticalAxis(0));

        AssertPoint(new(-2, 1), transformed.Start!);
        Assert.Equal(330, transformed.RotationDegrees!.Value, 8);
        Assert.Equal(-0.8, transformed.WidthFactor!.Value, 8);
        Assert.Equal(2, transformed.TextHeight!.Value, 8);
    }

    [Fact]
    public void TransformPath_NonUniformTextScaleDecomposesHeightAndWidthFactor()
    {
        var path = new Editor2DPreviewPath(
            "text", "TEXT", [], false,
            Start: new(1, 2), Text: "Scale", TextHeight: 2,
            RotationDegrees: 0, WidthFactor: 1.25);
        var transform = new Editor2DAffineTransform(2, 0, 0, 3, 0, 0);

        var transformed = Editor2DGeometry.TransformPath(path, transform);

        AssertPoint(new(2, 6), transformed.Start!);
        Assert.Equal(0, transformed.RotationDegrees!.Value, 8);
        Assert.Equal(5.0 / 6.0, transformed.WidthFactor!.Value, 8);
        Assert.Equal(6, transformed.TextHeight!.Value, 8);
    }
    [Fact]
    public void TransformPath_NewIdentityClearsSourceEntityHandle()
    {
        var path = new Editor2DPreviewPath(
            "source", "LINE", [new(0, 0), new(10, 0)], false,
            SourceLayerName: "CUT", SourceEntityHandle: "BEEF");

        var transformed = Editor2DGeometry.TranslatePath(path, 5, 0, "copy");

        Assert.Null(transformed.SourceEntityHandle);
        Assert.Equal("CUT", transformed.SourceLayerName);
    }

    [Fact]
    public void CurveOffset_NewIdentityClearsSourceEntityHandle()
    {
        var path = new Editor2DPreviewPath(
            "source", "CIRCLE", Editor2DGeometry.BuildCirclePoints(new(0, 0), 10), true,
            Center: new(0, 0), Radius: 10, SourceLayerName: "CUT", SourceEntityHandle: "CAFE");

        Assert.True(Editor2DGeometry.TryBuildCurveOffsetPath(path, 2, true, out var offset));

        Assert.Null(offset.SourceEntityHandle);
        Assert.Equal("CUT", offset.SourceLayerName);
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
        var movedAutomatic = workspace.Measurements.Single(item => item.Id == automatic.Id);
        AssertPoint(new(5, -3), movedAutomatic.Start);
        AssertPoint(new(7, -3), movedAutomatic.End);
        Assert.Equal(free, workspace.Measurements.Single(item => item.Id == free.Id));
        Assert.Equal(
            [new Editor2DPoint(5, -1), new Editor2DPoint(7, -1), new Editor2DPoint(7, 1)],
            Assert.Single(workspace.CornerParameters).SourcePoints);
    }

    [Fact]
    public void ApplySelectionTransform_RebuildsLineAndCircleAutoMeasurementsAcrossTransforms()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var lineId = workspace.CreateLine(new(0, 0), new(3, 4), "line")!;
        var circleId = workspace.CreateCircle(new(10, 0), new(12, 0), "circle")!;
        workspace.SetSelection([lineId, circleId]);
        workspace.ClearHistory();

        Assert.True(workspace.ApplySelectionTransform(Editor2DAffineTransform.CreateTranslation(5, -2)));
        var line = workspace.Measurements.Single(item => item.Id == $"{lineId}:length");
        AssertPoint(new(5, -2), line.Start);
        AssertPoint(new(8, 2), line.End);
        Assert.Equal(5, line.Distance, 8);
        var radius = workspace.Measurements.Single(item => item.Id == $"{circleId}:radius");
        AssertPoint(new(15, -2), radius.Start);
        AssertPoint(new(17, -2), radius.End);
        Assert.Equal(2, radius.Distance, 8);

        Assert.True(workspace.ApplySelectionTransform(Editor2DAffineTransform.CreateRotation(new(0, 0), 90)));
        line = workspace.Measurements.Single(item => item.Id == $"{lineId}:length");
        AssertPoint(new(2, 5), line.Start);
        AssertPoint(new(-2, 8), line.End);
        Assert.Equal(5, line.Distance, 8);
        radius = workspace.Measurements.Single(item => item.Id == $"{circleId}:radius");
        AssertPoint(new(2, 15), radius.Start);
        AssertPoint(new(4, 15), radius.End);
        Assert.Equal(2, radius.Distance, 8);

        var beforeScale = workspace.State;
        Assert.True(workspace.ApplySelectionTransform(Editor2DAffineTransform.CreateScale(new(0, 0), 2)));
        Assert.Equal(10, workspace.Measurements.Single(item => item.Id == $"{lineId}:length").Distance, 8);
        Assert.Equal(4, workspace.Measurements.Single(item => item.Id == $"{circleId}:radius").Distance, 8);
        Assert.True(workspace.Undo());
        Assert.Equal(beforeScale, workspace.State);
        Assert.True(workspace.Redo());
        Assert.Equal(10, workspace.Measurements.Single(item => item.Id == $"{lineId}:length").Distance, 8);
    }

    [Fact]
    public void ApplySelectionTransform_RebuildsRectangleAutoEndpointsAndRectMetadata()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var pathId = workspace.CreateRectangle(new(0, 0), new(20, 10), pathId: "rectangle")!;
        workspace.ClearHistory();

        Assert.True(workspace.ApplySelectionTransform(Editor2DAffineTransform.CreateTranslation(5, 7)));

        var width = workspace.Measurements.Single(item => item.Id == $"{pathId}:width");
        var height = workspace.Measurements.Single(item => item.Id == $"{pathId}:height");
        AssertPoint(new(5, -1), width.Start);
        AssertPoint(new(25, -1), width.End);
        AssertPoint(new(-3, 7), height.Start);
        AssertPoint(new(-3, 17), height.End);
        Assert.Equal(new Editor2DPoint(5, 7), width.RectP1);
        Assert.Equal(new Editor2DPoint(25, 17), width.RectP2);
        Assert.Equal(width.RectP1, height.RectP1);
        Assert.Equal(width.RectP2, height.RectP2);

        Assert.True(workspace.ApplySelectionTransform(Editor2DAffineTransform.CreateScale(new(5, 7), 2)));
        width = workspace.Measurements.Single(item => item.Id == $"{pathId}:width");
        height = workspace.Measurements.Single(item => item.Id == $"{pathId}:height");
        Assert.Equal(40, width.Distance, 8);
        Assert.Equal(20, height.Distance, 8);
        AssertPoint(new(5, -9), width.Start);
        AssertPoint(new(45, -9), width.End);
    }

    [Fact]
    public void ApplySelectionTransform_CopyClonesLineAndCircleAutoMeasurementsWithUniqueIds()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var lineId = workspace.CreateLine(new(0, 0), new(4, 0), "line")!;
        var circleId = workspace.CreateCircle(new(10, 0), new(12, 0), "circle")!;
        workspace.SetSelection([lineId, circleId]);
        workspace.ClearHistory();
        var before = workspace.State;

        Assert.True(workspace.ApplySelectionTransform(
            Editor2DAffineTransform.CreateTranslation(5, 3),
            createCopy: true));

        Assert.Equal(4, workspace.Measurements.Count);
        Assert.Contains(workspace.Measurements, item => item.Id == $"{lineId}:length");
        Assert.Contains(workspace.Measurements, item => item.Id == $"{circleId}:radius");
        var copyIds = workspace.SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(2, copyIds.Count);
        var lineCopy = workspace.Document.Paths.Single(path => copyIds.Contains(path.Id) && path.EntityType == "LINE");
        var circleCopy = workspace.Document.Paths.Single(path => copyIds.Contains(path.Id) && path.EntityType == "CIRCLE");
        var lineMeasurement = workspace.Measurements.Single(item => item.EntityPathId == lineCopy.Id);
        var circleMeasurement = workspace.Measurements.Single(item => item.EntityPathId == circleCopy.Id);
        Assert.Equal($"{lineCopy.Id}:length", lineMeasurement.Id);
        Assert.Equal($"{circleCopy.Id}:radius", circleMeasurement.Id);
        AssertPoint(new(5, 3), lineMeasurement.Start);
        AssertPoint(new(9, 3), lineMeasurement.End);
        AssertPoint(new(15, 3), circleMeasurement.Start);
        AssertPoint(new(17, 3), circleMeasurement.End);
        Assert.Equal(4, Assert.Single(workspace.Layers).PathIds.Count);
        Assert.Equal(4, workspace.Measurements.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.True(workspace.Undo());
        Assert.Equal(before, workspace.State);
        Assert.True(workspace.Redo());
        Assert.Equal(4, workspace.Measurements.Count);
    }

    [Fact]
    public void UpdatePathVertex_CommitsOnceAndPreservesAttachedState()
    {
        var line = new Editor2DPreviewPath(
            "line", "LINE", [new(0, 0), new(10, 0)], false, Start: new(0, 0));
        var measurement = new Editor2DMeasurement(
            "line:length", new(0, 0), new(10, 0),
            IsAutoDimension: true, EntityPathId: line.Id, DimensionType: "length");
        var corner = new Editor2DCornerParameter(
            "corner", line.Id, 1, Editor2DCornerKind.Chamfer, 1,
            [new(0, 0), new(10, 0)]);
        var workspace = Workspace(
            [line],
            SelectedPathIds: [line.Id],
            Measurements: [measurement],
            CornerParameters: [corner]);
        var before = workspace.State;

        Assert.True(workspace.UpdatePathVertex(line.Id, 0, new(3, 4)));

        var updated = Assert.Single(workspace.Document.Paths);
        AssertPoint(new(3, 4), updated.Points[0]);
        AssertPoint(new(3, 4), updated.Start!);
        Assert.Equal([line.Id], workspace.SelectedPathIds);
        Assert.Equal([line.Id], Assert.Single(workspace.Layers).PathIds);
        var rebuiltMeasurement = Assert.Single(workspace.Measurements);
        AssertPoint(new(3, 4), rebuiltMeasurement.Start);
        AssertPoint(new(10, 0), rebuiltMeasurement.End);
        Assert.Equal(Math.Sqrt(65), rebuiltMeasurement.Distance, 8);
        var retainedCorner = Assert.Single(workspace.CornerParameters);
        Assert.Equal(corner.Id, retainedCorner.Id);
        Assert.Equal([new Editor2DPoint(3, 4), new Editor2DPoint(10, 0)], retainedCorner.SourcePoints);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(before, workspace.State);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.Redo());
        AssertPoint(new(3, 4), Assert.Single(workspace.Document.Paths).Points[0]);
    }

    [Fact]
    public void UpdatePathVertex_NoOpAndConstrainedRectangleDoNotCreateHistory()
    {
        var line = Line("line", 0, 0, 10, 0);
        var lineWorkspace = Workspace([line], SelectedPathIds: [line.Id]);
        Assert.False(lineWorkspace.UpdatePathVertex(line.Id, 0, line.Points[0]));
        Assert.False(lineWorkspace.CanUndo);

        var rectangle = new Editor2DPreviewPath(
            "rectangle", "LWPOLYLINE",
            [new(0, 0), new(10, 0), new(10, 5), new(0, 5)],
            true,
            IsAxisAlignedRectangle: true);
        var workspace = Workspace([rectangle], SelectedPathIds: [rectangle.Id]);

        Assert.False(workspace.UpdatePathVertex(rectangle.Id, 0, new(2, 2)));
        Assert.Equal(rectangle, Assert.Single(workspace.Document.Paths));
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void ApplySelectionTransform_CopyClonesRectangleAutoMeasurementsAndRectMetadata()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var originalId = workspace.CreateRectangle(
            new(0, 0),
            new(20, 10),
            initialFilletRadius: 2,
            pathId: "rectangle")!;
        workspace.ClearHistory();

        Assert.True(workspace.ApplySelectionTransform(
            Editor2DAffineTransform.CreateTranslation(5, 7),
            createCopy: true));

        var copyId = Assert.Single(workspace.SelectedPathIds);
        Assert.NotEqual(originalId, copyId);
        Assert.Equal(4, workspace.Measurements.Count);
        var width = workspace.Measurements.Single(item => item.Id == $"{copyId}:width");
        var height = workspace.Measurements.Single(item => item.Id == $"{copyId}:height");
        AssertPoint(new(5, -1), width.Start);
        AssertPoint(new(25, -1), width.End);
        AssertPoint(new(-3, 7), height.Start);
        AssertPoint(new(-3, 17), height.End);
        Assert.Equal(new Editor2DPoint(5, 7), width.RectP1);
        Assert.Equal(new Editor2DPoint(25, 17), width.RectP2);
        Assert.Equal(width.RectP1, height.RectP1);
        Assert.Equal(width.RectP2, height.RectP2);
        Assert.Equal(8, workspace.CornerParameters.Count);
        Assert.Equal([originalId, copyId], Assert.Single(workspace.Layers).PathIds);
        Assert.True(workspace.Undo());
        Assert.Single(workspace.Document.Paths);
        Assert.Equal(2, workspace.Measurements.Count);
    }

    [Fact]
    public void ApplySelectionTransform_CopyAlwaysClonesUnknownAndBlankAttachedAutos()
    {
        var path = Line("line", 0, 0, 4, 0);
        var blank = new Editor2DMeasurement(
            "blank", new(0, 2), new(4, 2), true, path.Id, DimensionType: null);
        var secondBlank = new Editor2DMeasurement(
            "blank-2", new(0, 4), new(4, 4), true, path.Id, DimensionType: "  ");
        var unknown = new Editor2DMeasurement(
            "unknown", new(0, 3), new(4, 3), true, path.Id, DimensionType: " Mystery / Type ");
        var workspace = Workspace(
            [path],
            SelectedPathIds: [path.Id],
            Measurements: [blank, secondBlank, unknown]);

        Assert.True(workspace.ApplySelectionTransform(
            Editor2DAffineTransform.CreateTranslation(10, 5),
            createCopy: true));

        var copyId = Assert.Single(workspace.SelectedPathIds);
        var copies = workspace.Measurements.Where(item => item.EntityPathId == copyId).ToArray();
        Assert.Equal(3, copies.Length);
        Assert.Contains(copies, item => item.Id == $"{copyId}:auto");
        Assert.Contains(copies, item => item.Id == $"{copyId}:auto:2");
        Assert.Contains(copies, item => item.Id == $"{copyId}:mystery---type");
        AssertPoint(new(10, 7), copies.Single(item => item.Id == $"{copyId}:auto").Start);
        AssertPoint(new(10, 9), copies.Single(item => item.Id == $"{copyId}:auto:2").Start);
        AssertPoint(new(10, 8), copies.Single(item => item.Id == $"{copyId}:mystery---type").Start);
        Assert.All(copies, item => Assert.Equal(4, item.Distance, 8));
        Assert.Contains(blank, workspace.Measurements);
        Assert.Contains(secondBlank, workspace.Measurements);
        Assert.Contains(unknown, workspace.Measurements);
    }

    [Fact]
    public void ApplySelectionTransform_CopyClearsAutoParameterIdentityAndRemainsDrivable()
    {
        var path = Line("line", 0, 0, 4, 0);
        var automatic = new Editor2DMeasurement(
            "line:length", new(0, 0), new(4, 0), true, path.Id, "length",
            VarName: "d1", Expression: "4", IsParametric: true, EvaluatedValue: 4);
        var dependent = new Editor2DMeasurement(
            "dependent", new(0, 10), new(8, 10), VarName: "d2", Expression: "d1 * 2",
            IsParametric: true, EvaluatedValue: 8);
        var workspace = Workspace(
            [path],
            SelectedPathIds: [path.Id],
            Measurements: [automatic, dependent]);

        Assert.True(workspace.ApplySelectionTransform(
            Editor2DAffineTransform.CreateTranslation(10, 0),
            createCopy: true));

        var copyId = Assert.Single(workspace.SelectedPathIds);
        var copy = workspace.Measurements.Single(item => item.EntityPathId == copyId);
        Assert.Equal($"{copyId}:length", copy.Id);
        Assert.Null(copy.VarName);
        Assert.Null(copy.Expression);
        Assert.False(copy.IsParametric);
        Assert.Null(copy.EvaluatedValue);
        Assert.True(workspace.TrySetMeasurementValue(copy.Id, 8, out var error), error);
        Assert.Equal(8, workspace.Measurements.Single(item => item.Id == copy.Id).Distance, 8);
        Assert.Equal("d1", workspace.Measurements.Single(item => item.Id == automatic.Id).VarName);
        Assert.Equal(8, workspace.Measurements.Single(item => item.Id == dependent.Id).EvaluatedValue!.Value, 8);
    }

    [Fact]
    public void ApplySelectionTransform_RecoversLegacyRectangleVisibleOffsetsForTransformAndCopy()
    {
        var rectangle = new Editor2DPreviewPath(
            "rectangle", "LWPOLYLINE", [new(0, 0), new(20, 0), new(20, 10), new(0, 10)],
            true, IsAxisAlignedRectangle: true);
        var width = new Editor2DMeasurement(
            "rectangle:width", new(0, -8), new(20, -8), true, rectangle.Id, "width",
            new(0, 0), new(20, 10), OffsetDistance: 0);
        var height = new Editor2DMeasurement(
            "rectangle:height", new(-8, 0), new(-8, 10), true, rectangle.Id, "height",
            new(0, 0), new(20, 10), OffsetDistance: 0);
        var serialized = System.Text.Json.JsonSerializer.Serialize(new Editor2DWorkspaceState(
            Document([rectangle]),
            SelectedPathIds: [rectangle.Id],
            Measurements: [width, height],
            Layers: [new Editor2DLayer("layer", "Layer", [rectangle.Id])],
            ActiveLayerId: "layer"));
        Editor2DWorkspaceViewModel Restored()
        {
            var workspace = new Editor2DWorkspaceViewModel();
            workspace.Apply(System.Text.Json.JsonSerializer.Deserialize<Editor2DWorkspaceState>(serialized)!, recordHistory: false);
            workspace.ClearHistory();
            return workspace;
        }

        var transformed = Restored();
        Assert.True(transformed.ApplySelectionTransform(Editor2DAffineTransform.CreateTranslation(5, 7)));
        var movedWidth = transformed.Measurements.Single(item => item.DimensionType == "width");
        var movedHeight = transformed.Measurements.Single(item => item.DimensionType == "height");
        Assert.Equal(-8, movedWidth.OffsetDistance, 8);
        Assert.Equal(-8, movedHeight.OffsetDistance, 8);
        AssertPoint(new(5, -1), movedWidth.Start);
        AssertPoint(new(-3, 7), movedHeight.Start);

        var copied = Restored();
        Assert.True(copied.ApplySelectionTransform(
            Editor2DAffineTransform.CreateTranslation(5, 7),
            createCopy: true));
        var copyId = Assert.Single(copied.SelectedPathIds);
        var copiedWidth = copied.Measurements.Single(item => item.Id == $"{copyId}:width");
        var copiedHeight = copied.Measurements.Single(item => item.Id == $"{copyId}:height");
        Assert.Equal(-8, copiedWidth.OffsetDistance, 8);
        Assert.Equal(-8, copiedHeight.OffsetDistance, 8);
        AssertPoint(new(5, -1), copiedWidth.Start);
        AssertPoint(new(-3, 7), copiedHeight.Start);
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
