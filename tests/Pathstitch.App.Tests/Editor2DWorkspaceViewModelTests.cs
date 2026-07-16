using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using System.Text.Json;

namespace Pathstitch.App.Tests;

public sealed class Editor2DWorkspaceViewModelTests
{
    [Fact]
    public void SewingHoleGeometry_CountModePlacesExactClosedLoopCount()
    {
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath("outline", "LWPOLYLINE", [
                new Editor2DPoint(0, 0), new Editor2DPoint(20, 0),
                new Editor2DPoint(20, 10), new Editor2DPoint(0, 10)], true)],
            new Editor2DBounds(0, 0, 20, 10),
            new Dictionary<string, int>(),
            []);

        var preview = Editor2DSewingHoleGeometry.BuildPreview(
            document,
            ["outline"],
            new Editor2DSewingHoleParameters(
                DistributionMode: Editor2DSewingDistributionMode.Count,
                Count: 7,
                CornerMode: Editor2DSewingCornerMode.Continuous),
            "count-test");

        Assert.Equal(7, preview.Count);
    }

    [Fact]
    public void SewingHoleGeometry_VariableSpacingClampsClosedLoopPitch()
    {
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath("outline", "LWPOLYLINE", [
                new Editor2DPoint(0, 0), new Editor2DPoint(25, 0),
                new Editor2DPoint(25, 25), new Editor2DPoint(0, 25)], true)],
            new Editor2DBounds(0, 0, 25, 25),
            new Dictionary<string, int>(),
            []);

        var preview = Editor2DSewingHoleGeometry.BuildPreview(
            document,
            ["outline"],
            new Editor2DSewingHoleParameters(
                Pitch: 4,
                VariableSpacingEnabled: true,
                VariableSpacingMin: 8,
                VariableSpacingMax: 12,
                CornerMode: Editor2DSewingCornerMode.Continuous),
            "variable-test");

        Assert.Equal(12, preview.Count);
    }

    [Fact]
    public void SewingHoleGeometry_BoundsHugePitchPreview()
    {
        var document = new Editor2DPreviewDocument(
            [new Editor2DPreviewPath("outline", "LWPOLYLINE", [
                new Editor2DPoint(0, 0), new Editor2DPoint(100_000, 0),
                new Editor2DPoint(100_000, 100_000), new Editor2DPoint(0, 100_000)], true)],
            new Editor2DBounds(0, 0, 100_000, 100_000),
            new Dictionary<string, int>(),
            []);

        var preview = Editor2DSewingHoleGeometry.BuildPreview(
            document,
            ["outline"],
            new Editor2DSewingHoleParameters(Pitch: 0.1, CornerMode: Editor2DSewingCornerMode.Continuous),
            "bounded-test");

        Assert.Equal(12_000, preview.Count);
    }

    [Fact]
    public void Snapping_DefaultsOnAndPersistsWithoutPollutingUndoHistory()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        Assert.True(workspace.SnapEnabled);
        workspace.SetSnapEnabled(false);

        Assert.False(workspace.SnapEnabled);
        Assert.False(workspace.State.SnapEnabled);
        Assert.False(workspace.CanUndo);

        var reopened = new Editor2DWorkspaceViewModel();
        var serialized = JsonSerializer.Serialize(workspace.State);
        var restored = JsonSerializer.Deserialize<Editor2DWorkspaceState>(serialized);
        reopened.Apply(Assert.IsType<Editor2DWorkspaceState>(restored), recordHistory: false);
        Assert.False(reopened.SnapEnabled);

        var legacyJson = JsonSerializer.Serialize(Editor2DWorkspaceState.Empty)
            .Replace(",\"snapEnabled\":true", string.Empty, StringComparison.Ordinal);
        Assert.True(JsonSerializer.Deserialize<Editor2DWorkspaceState>(legacyJson)!.SnapEnabled);
    }

    [Fact]
    public void GridVisibility_DefaultsOnAndPersistsWithoutPollutingUndoHistory()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        Assert.True(workspace.GridVisible);
        workspace.SetGridVisible(false);

        Assert.False(workspace.GridVisible);
        Assert.False(workspace.State.GridVisible);
        Assert.False(workspace.CanUndo);

        var restored = JsonSerializer.Deserialize<Editor2DWorkspaceState>(JsonSerializer.Serialize(workspace.State));
        var reopened = new Editor2DWorkspaceViewModel();
        reopened.Apply(Assert.IsType<Editor2DWorkspaceState>(restored), recordHistory: false);
        Assert.False(reopened.GridVisible);
    }

    [Fact]
    public void ChainSelection_DefaultsOffAndPersistsWithoutPollutingUndoHistory()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        Assert.False(workspace.ChainSelectionEnabled);
        workspace.SetChainSelectionEnabled(true);

        Assert.True(workspace.ChainSelectionEnabled);
        Assert.True(workspace.State.ChainSelectionEnabled);
        Assert.False(workspace.CanUndo);

        var restored = JsonSerializer.Deserialize<Editor2DWorkspaceState>(JsonSerializer.Serialize(workspace.State));
        var reopened = new Editor2DWorkspaceViewModel();
        reopened.Apply(Assert.IsType<Editor2DWorkspaceState>(restored), recordHistory: false);
        Assert.True(reopened.ChainSelectionEnabled);
    }

    [Fact]
    public void PathPattern_ClonesSelectedGeometryAlongGuidePath()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(2, 0)], false);
        var guide = new Editor2DPreviewPath("guide", "LWPOLYLINE", [new(0, 10), new(10, 10)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source, guide] });
        workspace.SetSelection([source.Id]);

        var result = workspace.ApplyPathPattern(guide.Id, copyCount: 3, spacing: 4);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, workspace.Document.Paths.Count);
        Assert.Equal(2, workspace.Document.Paths.Count(path => path.Id.StartsWith("source:pattern:path:", StringComparison.Ordinal)));
        Assert.Equal(new Editor2DPoint(3, 10), workspace.Document.Paths[2].Points[0]);
    }

    [Fact]
    public void ExplodeCompoundPaths_SplitsSelfCrossingClosedPolylineIntoLoops()
    {
        var compound = new Editor2DPreviewPath(
            "bowtie",
            "LWPOLYLINE",
            [new(0, 0), new(10, 10), new(0, 10), new(10, 0)],
            IsClosed: true);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [compound] });
        workspace.SetSelection([compound.Id]);

        var result = workspace.ApplyExplodeCompoundPaths();

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, workspace.Document.Paths.Count);
        Assert.All(workspace.Document.Paths, path => Assert.True(path.IsClosed));
        Assert.Equal(2, workspace.SelectedPathIds.Count);
    }

    [Fact]
    public void ExplodeCompoundPaths_LeavesSimpleLoopUnchanged()
    {
        var rectangle = new Editor2DPreviewPath(
            "square",
            "LWPOLYLINE",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            IsClosed: true);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [rectangle] });
        workspace.SetSelection([rectangle.Id]);

        var result = workspace.ApplyExplodeCompoundPaths();

        Assert.False(result.IsSuccess);
        Assert.Single(workspace.Document.Paths);
        Assert.Equal(rectangle.Id, workspace.Document.Paths[0].Id);
    }

    [Fact]
    public void StrokeFillConversion_RoundTripsClosedPathAndPreservesSelection()
    {
        var path = new Editor2DPreviewPath(
            "square",
            "LWPOLYLINE",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            IsClosed: true);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [path] });
        workspace.SetSelection([path.Id]);

        Assert.True(workspace.ApplyStrokeToFill().IsSuccess);
        Assert.True(workspace.Document.Paths.Single().IsFilled);
        Assert.Equal([path.Id], workspace.SelectedPathIds);

        Assert.True(workspace.ApplyFillToStroke().IsSuccess);
        Assert.False(workspace.Document.Paths.Single().IsFilled);
        Assert.Equal(path.Points, workspace.Document.Paths.Single().Points);
    }

    [Fact]
    public void SelectedTextFitMode_UsesExistingTextBoxForWidthWarp()
    {
        var text = new Editor2DPreviewPath(
            "text",
            "TEXT",
            [new(0, 0), new(60, 0), new(60, 12), new(0, 12)],
            IsClosed: true,
            Start: new(0, 0),
            Text: "AB",
            TextHeight: 5,
            WidthFactor: 1);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [text] });
        workspace.SetSelection([text.Id]);

        var result = workspace.ApplySelectedText("AB", 5, "Inter", 0, false, false, false, "Width");

        Assert.True(result.IsSuccess, result.Message);
        var updated = Assert.Single(workspace.Document.Paths);
        Assert.True(updated.WidthFactor > 9.0);
        Assert.Equal(text.Start, updated.Start);
    }

    [Fact]
    public void CircularPattern_UsesExplicitPivotWhenProvided()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(10, 0), new(11, 0)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source] });
        workspace.SetSelection([source.Id]);

        var result = workspace.ApplyCircularPattern(2, 90, new Editor2DPoint(0, 0));

        Assert.True(result.IsSuccess);
        var copy = Assert.Single(workspace.Document.Paths, path => path.Id.StartsWith("source:pattern:circular:", StringComparison.Ordinal));
        Assert.Equal(0, copy.Points[0].X, 6);
        Assert.Equal(10, copy.Points[0].Y, 6);
    }

    [Fact]
    public void PreciseTransform_AppliesRotationAroundSelectionCenterAndTranslation()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(2, 0)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source] });
        workspace.SetSelection([source.Id]);

        var result = workspace.ApplyPreciseTransform(5, 3, 90);

        Assert.True(result.IsSuccess);
        var path = Assert.Single(workspace.Document.Paths);
        Assert.Equal(6, path.Points[0].X, 6);
        Assert.Equal(2, path.Points[0].Y, 6);
        Assert.Equal(6, path.Points[1].X, 6);
        Assert.Equal(4, path.Points[1].Y, 6);
    }

    [Fact]
    public void ApplyScale_UsesCenterCornerAndCustomPivots()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(2, 4), new(6, 8)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source] });
        workspace.SetSelection([source.Id]);

        Assert.True(workspace.ApplyScale(2, fromCenter: true).IsSuccess);
        Assert.Equal(new Editor2DPoint(0, 2), workspace.Document.Paths[0].Points[0]);
        Assert.Equal(new Editor2DPoint(8, 10), workspace.Document.Paths[0].Points[1]);

        workspace.Undo();
        Assert.True(workspace.ApplyScale(2, fromCenter: false).IsSuccess);
        Assert.Equal(new Editor2DPoint(2, 4), workspace.Document.Paths[0].Points[0]);
        Assert.Equal(new Editor2DPoint(10, 12), workspace.Document.Paths[0].Points[1]);

        workspace.Undo();
        Assert.True(workspace.ApplyScale(0.5, fromCenter: true, new Editor2DPoint(0, 0)).IsSuccess);
        Assert.Equal(new Editor2DPoint(1, 2), workspace.Document.Paths[0].Points[0]);
        Assert.Equal(new Editor2DPoint(3, 4), workspace.Document.Paths[0].Points[1]);
    }

    [Fact]
    public void ApplyScale_UpdatesCircleAndTextSizeMetadata()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var circle = new Editor2DPreviewPath(
            "circle", "CIRCLE", [new(1, 0), new(-1, 0)], true,
            Center: new Editor2DPoint(0, 0), Radius: 1);
        var text = new Editor2DPreviewPath(
            "text", "TEXT", [new(2, 0), new(4, 0)], false,
            Start: new Editor2DPoint(2, 0), Text: "A", TextHeight: 2);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [circle, text] });
        workspace.SetSelection([circle.Id, text.Id]);

        Assert.True(workspace.ApplyScale(3, fromCenter: true, new Editor2DPoint(0, 0)).IsSuccess);

        Assert.Equal(3, workspace.Document.Paths[0].Radius);
        Assert.Equal(6, workspace.Document.Paths[1].TextHeight);
        Assert.Equal(new Editor2DPoint(6, 0), workspace.Document.Paths[1].Start);
    }

    [Fact]
    public void ApplyMirror_PreservesSourcesAndSelectsReflectedCopies()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var line = new Editor2DPreviewPath(
            "line", "LINE", [new(2, 1), new(4, 3)], false,
            BezierAnchors: [new(new(2, 1), new(1, 1), new(3, 1))]);
        var circle = new Editor2DPreviewPath(
            "circle", "CIRCLE", [new(3, 0), new(1, 0)], true,
            Center: new Editor2DPoint(2, 0), Radius: 1);
        var arc = new Editor2DPreviewPath(
            "arc", "ARC", [new(1, 0), new(0, 1)], false,
            Center: new Editor2DPoint(0, 0), Radius: 1,
            StartAngleDegrees: 0, EndAngleDegrees: 90);
        var text = new Editor2DPreviewPath(
            "text", "TEXT", [new(2, 0), new(4, 0), new(4, 2), new(2, 2)], true,
            Start: new Editor2DPoint(2, 0), Text: "A", TextHeight: 2,
            RotationDegrees: 0, WidthFactor: 1);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [line, circle, arc, text] });
        workspace.SetSelection([line.Id, circle.Id, arc.Id, text.Id]);

        var result = workspace.ApplyMirror(new Editor2DPoint(0, -10), new Editor2DPoint(0, 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(8, workspace.Document.Paths.Count);
        Assert.Equal([line, circle, arc, text], workspace.Document.Paths.Take(4));
        Assert.Equal(4, workspace.SelectedPathIds.Count);
        Assert.All(workspace.SelectedPathIds, id => Assert.Contains(":mirror:", id, StringComparison.Ordinal));

        var mirroredLine = workspace.Document.Paths.Single(path => path.Id.StartsWith("line:mirror:", StringComparison.Ordinal));
        Assert.Equal(new Editor2DPoint(-2, 1), mirroredLine.Points[0]);
        Assert.Equal(new Editor2DPoint(-1, 1), mirroredLine.BezierAnchors![0].HandleIn);
        Assert.Equal(new Editor2DPoint(-3, 1), mirroredLine.BezierAnchors[0].HandleOut);

        var mirroredCircle = workspace.Document.Paths.Single(path => path.Id.StartsWith("circle:mirror:", StringComparison.Ordinal));
        Assert.Equal(new Editor2DPoint(-2, 0), mirroredCircle.Center);
        Assert.Equal(1, mirroredCircle.Radius);

        var mirroredArc = workspace.Document.Paths.Single(path => path.Id.StartsWith("arc:mirror:", StringComparison.Ordinal));
        Assert.Equal(90, mirroredArc.StartAngleDegrees!.Value, 6);
        Assert.Equal(180, mirroredArc.EndAngleDegrees!.Value, 6);

        var mirroredText = workspace.Document.Paths.Single(path => path.Id.StartsWith("text:mirror:", StringComparison.Ordinal));
        Assert.Equal(new Editor2DPoint(-2, 0), mirroredText.Start);
        Assert.Equal(180, mirroredText.RotationDegrees!.Value, 6);
        Assert.Equal(-1, mirroredText.WidthFactor);

        Assert.True(workspace.Undo());
        Assert.Equal([line, circle, arc, text], workspace.Document.Paths);
    }

    [Fact]
    public void ApplyMirror_CopyModePreservesOrientationAndMovesEachEntityCentroid()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var line = new Editor2DPreviewPath("line", "LINE", [new(2, 1), new(4, 3)], false);
        var circle = new Editor2DPreviewPath(
            "circle", "CIRCLE", [new(2, -1), new(4, 1)], true,
            Center: new Editor2DPoint(3, 0), Radius: 1);
        var text = new Editor2DPreviewPath(
            "text", "TEXT", [new(5, 1), new(9, 1), new(9, 3), new(5, 3)], true,
            Start: new Editor2DPoint(5, 1), Text: "Copy", TextHeight: 2,
            RotationDegrees: 30, WidthFactor: 0.8);
        var anchors = new[]
        {
            new Editor2DBezierAnchor(new(10, 0), HandleOut: new(11, 4)),
            new Editor2DBezierAnchor(new(14, 2), HandleIn: new(13, -2)),
        };
        var bezier = new Editor2DPreviewPath(
            "bezier", "LWPOLYLINE", Editor2DBezierGeometry.Flatten(anchors, closed: false), false,
            BezierAnchors: anchors);
        var sources = new[] { line, circle, text, bezier };
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = sources });
        workspace.SetSelection(sources.Select(static path => path.Id).ToArray());
        var axisStart = new Editor2DPoint(0, -10);
        var axisEnd = new Editor2DPoint(0, 10);

        Assert.True(workspace.ApplyMirror(axisStart, axisEnd, flip: false).IsSuccess);

        foreach (var source in sources)
        {
            var copy = workspace.Document.Paths.Single(path => path.Id.StartsWith($"{source.Id}:mirror:", StringComparison.Ordinal));
            var sourceCenter = BoundingBoxCenter(source.Points);
            var copyCenter = BoundingBoxCenter(copy.Points);
            Assert.Equal(-sourceCenter.X, copyCenter.X, 6);
            Assert.Equal(sourceCenter.Y, copyCenter.Y, 6);
        }

        var lineCopy = workspace.Document.Paths.Single(path => path.Id.StartsWith("line:mirror:", StringComparison.Ordinal));
        Assert.Equal(line.Points[1].X - line.Points[0].X, lineCopy.Points[1].X - lineCopy.Points[0].X, 6);
        Assert.Equal(line.Points[1].Y - line.Points[0].Y, lineCopy.Points[1].Y - lineCopy.Points[0].Y, 6);

        var circleCopy = workspace.Document.Paths.Single(path => path.Id.StartsWith("circle:mirror:", StringComparison.Ordinal));
        Assert.Equal(new Editor2DPoint(-3, 0), circleCopy.Center);
        Assert.Equal(circle.Radius, circleCopy.Radius);

        var textCopy = workspace.Document.Paths.Single(path => path.Id.StartsWith("text:mirror:", StringComparison.Ordinal));
        Assert.Equal(text.RotationDegrees, textCopy.RotationDegrees);
        Assert.Equal(text.WidthFactor, textCopy.WidthFactor);
        Assert.Equal(text.Text, textCopy.Text);

        var bezierCopy = workspace.Document.Paths.Single(path => path.Id.StartsWith("bezier:mirror:", StringComparison.Ordinal));
        var sourceHandle = anchors[0].HandleOut!;
        var sourceHandleOffset = new Editor2DPoint(
            sourceHandle.X - anchors[0].Point.X,
            sourceHandle.Y - anchors[0].Point.Y);
        var copiedAnchor = bezierCopy.BezierAnchors![0];
        var copiedHandle = copiedAnchor.HandleOut!;
        var copiedHandleOffset = new Editor2DPoint(
            copiedHandle.X - copiedAnchor.Point.X,
            copiedHandle.Y - copiedAnchor.Point.Y);
        Assert.Equal(sourceHandleOffset, copiedHandleOffset);

        Assert.All(workspace.MirrorLinks.Values, link => Assert.False(link.Mirror));

        static Editor2DPoint BoundingBoxCenter(IReadOnlyList<Editor2DPoint> points)
            => new(
                (points.Min(static point => point.X) + points.Max(static point => point.X)) / 2.0,
                (points.Min(static point => point.Y) + points.Max(static point => point.Y)) / 2.0);
    }

    [Fact]
    public void OffsetConstruction_UsesCaseInsensitiveGrayLayerAndUndoRedoIsAtomic()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(10, 0)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source] });
        var constructionLayer = workspace.CreateLayer("construction");
        var activeLayer = workspace.CreateLayer("Details");
        var constructionPath = new Editor2DPreviewPath(
            "offset-construction", "LINE", [new(0, 5), new(10, 5)], false, IsConstruction: true);

        var result = workspace.CommitCurveOffsetPreview([constructionPath], outward: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(activeLayer.Id, workspace.ActiveLayerId);
        var reused = Assert.Single(workspace.Layers, layer =>
            string.Equals(layer.Name, "CONSTRUCTION", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(constructionLayer.Id, reused.Id);
        Assert.Equal("CONSTRUCTION", reused.Name);
        Assert.Equal("#808080", reused.ColorHex);
        Assert.Contains(constructionPath.Id, reused.PathIds);
        Assert.DoesNotContain(constructionPath.Id, workspace.Layers.Single(layer => layer.Id == activeLayer.Id).PathIds);
        Assert.True(Assert.Single(workspace.Document.Paths, path => path.Id == constructionPath.Id).IsConstruction);

        Assert.True(workspace.Undo());
        Assert.Equal(activeLayer.Id, workspace.ActiveLayerId);
        Assert.DoesNotContain(workspace.Document.Paths, path => path.Id == constructionPath.Id);
        var undoneLayer = workspace.Layers.Single(layer => layer.Id == constructionLayer.Id);
        Assert.Equal("construction", undoneLayer.Name);
        Assert.Empty(undoneLayer.PathIds);

        Assert.True(workspace.Redo());
        Assert.Equal(activeLayer.Id, workspace.ActiveLayerId);
        Assert.True(workspace.Document.Paths.Single(path => path.Id == constructionPath.Id).IsConstruction);
        Assert.Contains(constructionPath.Id, workspace.Layers.Single(layer => layer.Id == constructionLayer.Id).PathIds);
    }

    [Fact]
    public void ApplyMirror_RejectsMissingSelectionAndDegenerateAxisWithoutHistory()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var line = new Editor2DPreviewPath("line", "LINE", [new(0, 0), new(1, 0)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [line] });
        workspace.ClearHistory();

        Assert.False(workspace.ApplyMirror(new(0, 0), new(0, 1)).IsSuccess);
        workspace.SetSelection([line.Id]);
        Assert.False(workspace.ApplyMirror(new(0, 0), new(0, 0)).IsSuccess);
        Assert.Single(workspace.Document.Paths);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void ApplyMirror_CreatesBidirectionalMetadataAndBreakKeepsGeometry()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(2, 0), new(4, 0)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source] });
        workspace.SetSelection([source.Id]);
        workspace.ClearHistory();
        var axisStart = new Editor2DPoint(0, 0);
        var axisEnd = new Editor2DPoint(0, 10);

        Assert.True(workspace.ApplyMirror(axisStart, axisEnd).IsSuccess);

        var copyId = Assert.Single(workspace.SelectedPathIds);
        Assert.Equal(2, workspace.MirrorLinks.Count);
        Assert.Equal(new Editor2DMirrorLink(copyId, axisStart, axisEnd), workspace.MirrorLinks[source.Id]);
        Assert.Equal(new Editor2DMirrorLink(source.Id, axisStart, axisEnd), workspace.MirrorLinks[copyId]);
        Assert.True(workspace.HasMirrorLinkSelection);
        var pathsBeforeBreak = workspace.Document.Paths.ToArray();

        Assert.True(workspace.BreakMirrorLinksForSelection());

        Assert.Empty(workspace.MirrorLinks);
        Assert.False(workspace.HasMirrorLinkSelection);
        Assert.Equal(pathsBeforeBreak, workspace.Document.Paths);
        Assert.False(workspace.BreakMirrorLinksForSelection());
        Assert.True(workspace.Undo());
        Assert.Equal([source], workspace.Document.Paths);
        Assert.False(workspace.Undo());
    }

    [Fact]
    public void MirrorLinks_AreOptionalTransientMetadataAndNeverBecomeStale()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(2, 0), new(4, 0)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source] });
        workspace.SetSelection([source.Id]);
        workspace.ClearHistory();

        Assert.True(workspace.ApplyMirror(new(0, 0), new(0, 10), keepLink: false).IsSuccess);
        Assert.Empty(workspace.MirrorLinks);
        Assert.True(workspace.Undo());
        Assert.True(workspace.Redo());
        workspace.SetSelection([source.Id]);

        Assert.True(workspace.ApplyMirror(new(0, 0), new(0, 10)).IsSuccess);
        Assert.Equal(2, workspace.MirrorLinks.Count);

        Assert.True(workspace.Undo());
        Assert.Empty(workspace.MirrorLinks);
        Assert.True(workspace.Redo());
        Assert.Empty(workspace.MirrorLinks);

        workspace.SetSelection([workspace.Document.Paths.Last().Id]);
        Assert.True(workspace.ApplyMirror(new(0, 0), new(0, 10)).IsSuccess);
        Assert.Equal(2, workspace.MirrorLinks.Count);
        Assert.Equal(1, workspace.DeleteSelection());
        Assert.Empty(workspace.MirrorLinks);
        Assert.True(workspace.Undo());
        Assert.Empty(workspace.MirrorLinks);
    }

    [Fact]
    public void ParametricMeasurement_StoresExpressionAndDrivesEndpoint()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetMeasurements([new Editor2DMeasurement("m", new(0, 0), new(10, 0))]);

        Assert.True(workspace.TrySetMeasurementExpression("m", "5 * 2", out var error), error);

        var measurement = Assert.Single(workspace.Measurements);
        Assert.Equal("d1", measurement.VarName);
        Assert.Equal("5 * 2", measurement.Expression);
        Assert.True(measurement.IsParametric);
        Assert.Equal(new Editor2DPoint(10, 0), measurement.End);

        Assert.True(workspace.SetMeasurementDriven("m", true));
        Assert.True(Assert.Single(workspace.Measurements).Driven);
    }

    [Fact]
    public void ParametricMeasurement_ResolvesDependenciesAndRejectsCycles()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.SetMeasurements([
            new Editor2DMeasurement("first", new(0, 0), new(10, 0), VarName: "d1", Expression: "10", IsParametric: true),
            new Editor2DMeasurement("second", new(0, 10), new(10, 10), VarName: "d2", Expression: "d1 * 2", IsParametric: true),
        ]);

        Assert.True(workspace.TrySetMeasurementExpression("first", "5", out var error), error);
        Assert.Equal(5, Assert.Single(workspace.Measurements, item => item.Id == "first").Distance, 6);
        Assert.Equal(10, Assert.Single(workspace.Measurements, item => item.Id == "second").Distance, 6);

        Assert.False(workspace.TrySetMeasurementExpression("first", "d2", out _));
    }

    [Fact]
    public void BlankWorkspace_IsImmediatelyEditableWithoutTwoD()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        Assert.Empty(workspace.Document.Paths);
        Assert.Equal(Editor2DTool.Select, workspace.ActiveTool);

        var line = new Editor2DPreviewPath(
            "line-1",
            "LINE",
            [new Editor2DPoint(0, 0), new Editor2DPoint(10, 0)],
            IsClosed: false);
        workspace.SetDocument(workspace.Document with { Paths = [line] });
        workspace.SetSelection([line.Id]);

        Assert.Single(workspace.Document.Paths);
        Assert.Equal([line.Id], workspace.SelectedPathIds);
        Assert.True(workspace.CanUndo);
    }

    [Fact]
    public void UndoAndRedo_RestoreEditableDocumentState()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var original = workspace.Document;
        var circle = new Editor2DPreviewPath(
            "circle-1",
            "CIRCLE",
            [],
            IsClosed: true,
            Center: new Editor2DPoint(5, 5),
            Radius: 2);

        workspace.SetDocument(original with { Paths = [circle] });
        Assert.True(workspace.Undo());
        Assert.Empty(workspace.Document.Paths);
        Assert.True(workspace.Redo());
        Assert.Equal(circle, Assert.Single(workspace.Document.Paths));
    }

    [Fact]
    public void Edit_RecordsDocumentSelectionToolAndMeasurementsAsOneTransaction()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var line = new Editor2DPreviewPath(
            "line-transaction",
            "LINE",
            [new Editor2DPoint(0, 0), new Editor2DPoint(12, 0)],
            IsClosed: false);
        var measurement = new Editor2DMeasurement(
            "measurement-transaction",
            new Editor2DPoint(0, 0),
            new Editor2DPoint(12, 0));

        workspace.Edit(state => state with
        {
            Document = state.Document with { Paths = [line] },
            IsInitialized = true,
            ActiveTool = Editor2DTool.Move,
            SelectedPathIds = [line.Id],
            Measurements = [measurement],
            SelectedMeasurementId = measurement.Id,
        });

        Assert.Equal(Editor2DTool.Move, workspace.ActiveTool);
        Assert.Equal([line.Id], workspace.SelectedPathIds);
        Assert.Equal(measurement, Assert.Single(workspace.Measurements));
        Assert.True(workspace.Undo());
        Assert.False(workspace.IsInitialized);
        Assert.Empty(workspace.Document.Paths);
        Assert.Empty(workspace.SelectedPathIds);
        Assert.Empty(workspace.Measurements);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.Redo());
        Assert.Equal(line, Assert.Single(workspace.Document.Paths));
        Assert.Equal(measurement.Id, workspace.SelectedMeasurementId);
    }

    [Fact]
    public void EditorPageCompatibilityProperties_DoNotOwnDuplicateTwoDBackingFields()
    {
        var fieldNames = typeof(EditorPageViewModel)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToArray();

        Assert.DoesNotContain("_twoDDocument", fieldNames);
        Assert.DoesNotContain("_twoDActiveTool", fieldNames);
        Assert.DoesNotContain("_twoDSelectedPathIds", fieldNames);
        Assert.DoesNotContain("_twoDMeasurements", fieldNames);
        Assert.DoesNotContain("_twoDSelectedMeasurementId", fieldNames);
        Assert.DoesNotContain("_twoDPolygonSides", fieldNames);
        Assert.DoesNotContain("_twoDExpandedRectanglePathIds", fieldNames);
        Assert.DoesNotContain("_twoDSelectedTextDraft", fieldNames);
        Assert.DoesNotContain("_twoDViewportZoom", fieldNames);
        Assert.DoesNotContain("_twoDConvertLineStyle", fieldNames);
        Assert.DoesNotContain("_twoDOffsetMode", fieldNames);
        Assert.DoesNotContain("_twoDPatternMode", fieldNames);
        Assert.DoesNotContain("_twoDGlueTabType", fieldNames);
        Assert.Contains("_twoDWorkspace", fieldNames);
    }

    [Fact]
    public void WorkspaceOwnsCoreEditingOperationsParametersAndHistory()
    {
        var workspaceType = typeof(Editor2DWorkspaceViewModel);
        var workspaceFields = workspaceType
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToArray();

        Assert.Contains("_state", workspaceFields);
        Assert.Contains("_undo", workspaceFields);
        Assert.Contains("_redo", workspaceFields);
        Assert.Contains("_polygonSides", workspaceFields);
        Assert.Contains("_convertLineStyle", workspaceFields);
        Assert.Contains("_offsetMode", workspaceFields);
        Assert.Contains("_patternMode", workspaceFields);
        Assert.Contains("_glueTabType", workspaceFields);
        Assert.NotNull(workspaceType.GetMethod(nameof(Editor2DWorkspaceViewModel.DeleteSelection)));
        Assert.NotNull(workspaceType.GetMethod(nameof(Editor2DWorkspaceViewModel.DeleteSelectedMeasurement)));
        Assert.NotNull(workspaceType.GetMethod(nameof(Editor2DWorkspaceViewModel.ExpandSelectedRectangles)));
        Assert.NotNull(workspaceType.GetMethod(nameof(Editor2DWorkspaceViewModel.ApplyExplodeCompoundPaths)));
        Assert.NotNull(workspaceType.GetMethod(nameof(Editor2DWorkspaceViewModel.ClearManualMeasurements)));
    }

    [Fact]
    public void OutputPartial_HasNoDuplicateTwoDStorageOrDirectEditCommits()
    {
        var output = File.ReadAllText(FindRepositoryFile(
            "src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.Output.cs"));

        Assert.DoesNotContain("private string _twoDOffset", output, StringComparison.Ordinal);
        Assert.DoesNotContain("private string _twoDPattern", output, StringComparison.Ordinal);
        Assert.DoesNotContain("private string _twoDGlue", output, StringComparison.Ordinal);
        Assert.DoesNotContain("private string _twoDConvert", output, StringComparison.Ordinal);
        Assert.DoesNotContain("TwoDDocument = CreateUpdatedTwoDDocument", output, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitTwoDWorkspaceEdit", output, StringComparison.Ordinal);
        Assert.Contains("_twoDWorkspace.Apply", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EditableTwoDProperties_DoNotUseGeneratedOutputTerminology()
    {
        var editableTwoDProperties = typeof(EditorPageViewModel)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(Editor2DTool)
                || property.PropertyType == typeof(Editor2DPreviewDocument)
                || property.PropertyType == typeof(IReadOnlyList<Editor2DMeasurement>)
                || property.Name.Contains("Selection", StringComparison.Ordinal)
                    && property.Name.Contains("TwoD", StringComparison.Ordinal)
                || property.Name.Contains("Workspace", StringComparison.Ordinal)
                    && property.Name.Contains("TwoD", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(editableTwoDProperties);
        Assert.DoesNotContain(
            editableTwoDProperties,
            property => property.Name.Contains("GeneratedOutput", StringComparison.Ordinal));
        Assert.Contains(editableTwoDProperties, property => property.Name == nameof(EditorPageViewModel.TwoDDocument));
        Assert.Contains(editableTwoDProperties, property => property.Name == nameof(EditorPageViewModel.TwoDActiveTool));
    }

    [Fact]
    public void Apply_DropsSelectionsThatAreNotInTheDocument()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        workspace.Apply(Editor2DWorkspaceState.Empty with { SelectedPathIds = ["missing"] });

        Assert.Empty(workspace.SelectedPathIds);
    }

    [Fact]
    public void Layers_CreateAssignSelectToggleLockAndReorderGeometry()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var first = new Editor2DPreviewPath("first", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(1, 0)], false);
        var second = new Editor2DPreviewPath("second", "LINE", [new Editor2DPoint(0, 1), new Editor2DPoint(1, 1)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [first, second] });
        var baseLayer = Assert.Single(workspace.Layers);
        var detailLayer = workspace.CreateLayer("Details");

        Assert.True(workspace.AssignPathsToLayer(detailLayer.Id, [second.Id]));
        Assert.True(workspace.SelectLayer(detailLayer.Id));
        Assert.Equal([second.Id], workspace.SelectedPathIds);
        Assert.True(workspace.ToggleLayerVisibility(detailLayer.Id));
        Assert.False(workspace.Layers.Single(layer => layer.Id == detailLayer.Id).IsVisible);
        Assert.True(workspace.ToggleLayerLock(detailLayer.Id));
        Assert.True(workspace.Layers.Single(layer => layer.Id == detailLayer.Id).IsLocked);
        Assert.True(workspace.MoveLayer(detailLayer.Id, -1));
        Assert.Equal(detailLayer.Id, workspace.Layers[0].Id);
        Assert.Equal([first.Id], workspace.Layers.Single(layer => layer.Id == baseLayer.Id).PathIds);
    }

    [Fact]
    public void Layers_RenameTrimsWhitespaceAndRecordsUndoHistory()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var layer = workspace.CreateLayer("Draft");

        Assert.False(workspace.RenameLayer(layer.Id, "   "));
        Assert.Equal("Draft", workspace.Layers.Single(item => item.Id == layer.Id).Name);

        Assert.True(workspace.RenameLayer(layer.Id, "  Final  "));
        Assert.Equal("Final", workspace.Layers.Single(item => item.Id == layer.Id).Name);
        Assert.True(workspace.CanUndo);

        Assert.True(workspace.Undo());
        Assert.Equal("Draft", workspace.Layers.Single(item => item.Id == layer.Id).Name);
    }

    [Fact]
    public void Layers_SetColorValidatesHexAndRecordsUndoHistory()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var layer = workspace.CreateLayer("Sketch");

        Assert.False(workspace.SetLayerColor(layer.Id, "blue"));
        Assert.Equal("#4D7FFF", workspace.Layers.Single(item => item.Id == layer.Id).ColorHex);

        Assert.True(workspace.SetLayerColor(layer.Id, " #ff8800 "));
        Assert.Equal("#FF8800", workspace.Layers.Single(item => item.Id == layer.Id).ColorHex);
        Assert.True(workspace.Undo());
        Assert.Equal("#4D7FFF", workspace.Layers.Single(item => item.Id == layer.Id).ColorHex);
    }

    [Fact]
    public void Folders_CreateMoveDeleteAndDetachChildren()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var layer = workspace.CreateLayer("Sketch");
        var parent = workspace.CreateFolder("Production");
        var child = workspace.CreateFolder("Cut", parent.Id);

        Assert.True(workspace.MoveLayerToFolder(layer.Id, child.Id));
        Assert.Equal(child.Id, workspace.Layers.Single(item => item.Id == layer.Id).ParentFolderId);
        Assert.True(workspace.DeleteFolder(parent.Id));
        Assert.Single(workspace.Folders);
        Assert.Equal(child.Id, workspace.Layers.Single(item => item.Id == layer.Id).ParentFolderId);
        Assert.True(workspace.DeleteFolder(child.Id));
        Assert.Empty(workspace.Folders);
        Assert.Null(workspace.Layers.Single(item => item.Id == layer.Id).ParentFolderId);
        Assert.True(workspace.CanUndo);
    }

    [Fact]
    public void Folders_NormalizeInvalidParentAndCycleToRoot()
    {
        var first = new Editor2DLayerFolder("first", "First", "second");
        var second = new Editor2DLayerFolder("second", "Second", "first");
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(Editor2DWorkspaceState.Empty with { Folders = [first, second] });

        Assert.All(workspace.Folders, folder => Assert.NotEqual(folder.Id, folder.ParentFolderId));
    }

    [Fact]
    public void Layers_DeleteGeometryLayer_ReassignsPathsAndKeepsOneGeometryLayer()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var first = new Editor2DPreviewPath("first", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(1, 0)], false);
        var second = new Editor2DPreviewPath("second", "LINE", [new Editor2DPoint(0, 1), new Editor2DPoint(1, 1)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [first, second] });
        var baseLayer = Assert.Single(workspace.Layers);
        var detailLayer = workspace.CreateLayer("Details");

        Assert.True(workspace.AssignPathsToLayer(detailLayer.Id, [second.Id]));
        workspace.SelectLayer(detailLayer.Id);

        Assert.True(workspace.DeleteLayer(detailLayer.Id));

        var remaining = Assert.Single(workspace.Layers);
        Assert.Equal(baseLayer.Id, remaining.Id);
        Assert.Equal([first.Id, second.Id], remaining.PathIds);
        Assert.Equal(baseLayer.Id, workspace.ActiveLayerId);
        Assert.Equal([second.Id], workspace.SelectedPathIds);
        Assert.True(workspace.CanUndo);

        Assert.False(workspace.DeleteLayer(baseLayer.Id));

        Assert.True(workspace.Undo());
        Assert.Contains(workspace.Layers, layer => layer.Id == detailLayer.Id);
        Assert.Equal(2, workspace.Layers.Count);
    }

    [Fact]
    public void Layers_DeleteReferenceImageRemovesImageLayerAndKeepsActiveGeometry()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var path = new Editor2DPreviewPath("shape", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(2, 0)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [path] });
        var geometryLayer = Assert.Single(workspace.Layers);
        var imageLayer = workspace.ImportReferenceImage("reference.png", "ZmFrZQ==", 10, 10);

        Assert.True(imageLayer.IsReferenceImage);
        Assert.Equal(imageLayer.Id, workspace.ActiveLayerId);

        Assert.True(workspace.DeleteLayer(imageLayer.Id));

        Assert.Single(workspace.Layers);
        Assert.Equal(geometryLayer.Id, workspace.ActiveLayerId);
        Assert.Equal([path.Id], workspace.Layers.Single().PathIds);
    }

    [Fact]
    public void Layers_MergeWithBelowMovesPathsAndRemovesSource()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var first = new Editor2DPreviewPath("first", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(1, 0)], false);
        var second = new Editor2DPreviewPath("second", "LINE", [new Editor2DPoint(0, 1), new Editor2DPoint(1, 1)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [first, second] });
        var baseLayer = Assert.Single(workspace.Layers);
        var detailLayer = workspace.CreateLayer("Details");

        Assert.True(workspace.AssignPathsToLayer(detailLayer.Id, [second.Id]));
        Assert.True(workspace.MergeLayerWithBelow(detailLayer.Id));

        var merged = Assert.Single(workspace.Layers);
        Assert.Equal(baseLayer.Id, merged.Id);
        Assert.Equal([first.Id, second.Id], merged.PathIds);
        Assert.Equal(baseLayer.Id, workspace.ActiveLayerId);
        Assert.False(workspace.MergeLayerWithBelow(merged.Id));
    }

    [Fact]
    public void Layers_MergeSelectedLayersCombinesWholeLayers()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var first = new Editor2DPreviewPath("first", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(1, 0)], false);
        var second = new Editor2DPreviewPath("second", "LINE", [new Editor2DPoint(0, 1), new Editor2DPoint(1, 1)], false);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [first, second] });
        var baseLayer = Assert.Single(workspace.Layers);
        var detailLayer = workspace.CreateLayer("Details");
        Assert.True(workspace.AssignPathsToLayer(detailLayer.Id, [second.Id]));
        workspace.SetSelection([first.Id, second.Id]);

        Assert.True(workspace.MergeSelectedLayers());

        var merged = Assert.Single(workspace.Layers);
        Assert.Equal(baseLayer.Id, merged.Id);
        Assert.Equal([first.Id, second.Id], merged.PathIds);
        Assert.False(workspace.MergeSelectedLayers());
    }

    [Fact]
    public void CornerParameters_RecomputeFromPersistedSourceWithoutLinkingValues()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath(
            "rectangle",
            "LWPOLYLINE",
            [new Editor2DPoint(0, 0), new Editor2DPoint(10, 0), new Editor2DPoint(10, 10), new Editor2DPoint(0, 10)],
            IsClosed: true,
            IsAxisAlignedRectangle: true);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source] });
        var first = new Editor2DCornerParameter("rectangle:0", source.Id, 0, Editor2DCornerKind.Fillet, 1, source.Points);
        var second = new Editor2DCornerParameter("rectangle:1", source.Id, 1, Editor2DCornerKind.Fillet, 1, source.Points);
        workspace.SetCornerParameters([first, second]);

        Assert.True(workspace.UpdateCornerParameter(first.Id, 2));

        Assert.Equal(2, workspace.CornerParameters.Single(item => item.Id == first.Id).Value);
        Assert.Equal(1, workspace.CornerParameters.Single(item => item.Id == second.Id).Value);
        Assert.True(workspace.Document.Paths.Single().Points.Count > source.Points.Count);
        Assert.Equal(source.Points, workspace.CornerParameters[0].SourcePoints);
    }

    [Fact]
    public void CreateRectangle_WithInitialFilletSeedsEditableCornerParametersAtomically()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        var pathId = workspace.CreateRectangle(
            new(0, 0), new(20, 10), initialFilletRadius: 2,
            continuity: Editor2DFilletContinuity.G2,
            pathId: "rounded-rectangle");

        Assert.Equal("rounded-rectangle", pathId);
        var path = Assert.Single(workspace.Document.Paths);
        Assert.True(path.IsClosed);
        Assert.False(path.IsAxisAlignedRectangle);
        Assert.True(path.Points.Count > 4);
        Assert.Equal([path.Id], workspace.SelectedPathIds);
        Assert.Equal(4, workspace.CornerParameters.Count);
        Assert.All(workspace.CornerParameters, parameter =>
        {
            Assert.Equal(path.Id, parameter.PathId);
            Assert.Equal(Editor2DCornerKind.Fillet, parameter.Kind);
            Assert.Equal(Editor2DFilletContinuity.G2, parameter.Continuity);
            Assert.Equal(2, parameter.Value);
            Assert.Equal([new(0, 0), new(20, 0), new(20, 10), new(0, 10)], parameter.SourcePoints);
        });
        Assert.Equal(2, workspace.Measurements.Count);
        Assert.All(workspace.Measurements, measurement =>
        {
            Assert.True(measurement.IsAutoDimension);
            Assert.Equal(path.Id, measurement.EntityPathId);
            Assert.Equal(2, measurement.FilletRadius);
        });
        Assert.Contains(path.Id, workspace.ActiveLayer!.PathIds);

        var restoredState = JsonSerializer.Deserialize<Editor2DWorkspaceState>(
            JsonSerializer.Serialize(workspace.State));
        var restored = new Editor2DWorkspaceViewModel();
        restored.Apply(Assert.IsType<Editor2DWorkspaceState>(restoredState), recordHistory: false);
        Assert.Equal(4, restored.CornerParameters.Count);
        Assert.All(restored.Measurements, measurement => Assert.Equal(2, measurement.FilletRadius));

        Assert.True(workspace.Undo());
        Assert.Empty(workspace.Document.Paths);
        Assert.Empty(workspace.CornerParameters);
        Assert.Empty(workspace.Measurements);
        Assert.True(workspace.Redo());
        Assert.Equal(4, workspace.CornerParameters.Count);
        Assert.Equal(2, workspace.Measurements.Count);
        Assert.Equal(1, workspace.ExpandSelectedRectangles());
        Assert.Empty(workspace.CornerParameters);
        Assert.Empty(workspace.Measurements);
        Assert.True(workspace.Document.Paths[0].Points.Count > 4);
    }

    [Fact]
    public void CreateRectangle_ClampsFilletAndLeavesSharpRectangleUnparameterized()
    {
        var rounded = Editor2DRectangleCreationService.Create("rounded", new(0, 0), new(20, 10), 100);
        var sharp = Editor2DRectangleCreationService.Create("sharp", new(0, 0), new(20, 10), 0);

        Assert.NotNull(rounded);
        Assert.All(rounded!.CornerParameters, parameter => Assert.Equal(5, parameter.Value));
        Assert.False(rounded.Path.IsAxisAlignedRectangle);
        Assert.NotNull(sharp);
        Assert.Empty(sharp!.CornerParameters);
        Assert.True(sharp.Path.IsAxisAlignedRectangle);
        Assert.Equal(4, sharp.Path.Points.Count);
        Assert.Null(Editor2DRectangleCreationService.Create("flat", new(0, 0), new(20, 0), 2));
    }

    [Theory]
    [InlineData(20, 10)]
    [InlineData(12, 5)]
    [InlineData(6, 2)]
    [InlineData(2, 0)]
    public void CornerSession_SeedsAllCornersWithLargestFittingMacPreset(double side, double expected)
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath(
            "square", "LWPOLYLINE", [new(0, 0), new(side, 0), new(side, side), new(0, side)], true);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source] });
        workspace.SetSelection([source.Id]);
        workspace.ClearHistory();

        var activeId = workspace.BeginCornerToolSession(Editor2DCornerKind.Fillet);

        Assert.NotNull(activeId);
        Assert.Equal(4, workspace.CornerParameters.Count);
        Assert.All(workspace.CornerParameters, parameter => Assert.Equal(expected, parameter.Value));
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void CornerSession_ConfirmIsOneUndoStepAndCancelRestoresExactSnapshot()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var source = new Editor2DPreviewPath(
            "square", "LWPOLYLINE", [new(0, 0), new(20, 0), new(20, 20), new(0, 20)], true);
        workspace.SetDocument(Editor2DWorkspaceState.Empty.Document with { Paths = [source] });
        workspace.SetSelection([source.Id]);
        workspace.ClearHistory();

        var activeId = Assert.IsType<string>(workspace.BeginCornerToolSession(Editor2DCornerKind.Chamfer));
        Assert.True(workspace.UpdateCornerParameter(activeId, 3));
        Assert.True(workspace.UpdateCornerParameter(activeId, 4));
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.ConfirmCornerToolSession());
        Assert.True(workspace.CanUndo);

        Assert.True(workspace.Undo());
        Assert.Equal([source], workspace.Document.Paths);
        Assert.Empty(workspace.CornerParameters);
        Assert.False(workspace.CanUndo);
        Assert.True(workspace.Redo());
        Assert.Equal(4, workspace.CornerParameters.Single(parameter => parameter.Id == activeId).Value);

        workspace.ClearHistory();
        var committedDocument = workspace.Document;
        var committedParameters = workspace.CornerParameters.ToArray();
        Assert.NotNull(workspace.BeginCornerToolSession(Editor2DCornerKind.Fillet));
        Assert.True(workspace.UpdateCornerParameter(workspace.CornerParameters[0].Id, 2));
        Assert.True(workspace.CancelCornerToolSession());
        Assert.Equal(committedDocument, workspace.Document);
        Assert.Equal(committedParameters, workspace.CornerParameters);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void CornerValueFromPoint_MapsBisectorDragToFilletAndChamferValues()
    {
        var previous = new Editor2DPoint(-10, 0);
        var corner = new Editor2DPoint(0, 0);
        var next = new Editor2DPoint(0, 10);

        var fillet = Editor2DCornerGeometry.ValueFromPoint(previous, corner, next, new(-3, 3), Editor2DCornerKind.Fillet);
        var chamfer = Editor2DCornerGeometry.ValueFromPoint(previous, corner, next, new(-3, 3), Editor2DCornerKind.Chamfer);

        Assert.Equal(3 * Math.Sqrt(2), fillet, 6);
        Assert.Equal(3 * Math.Sqrt(2), chamfer, 6);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
