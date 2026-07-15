using Domain.App.Models;
using Domain.App.ViewModels;
using System.Text.Json;

namespace Pathstitch.App.Tests;

public sealed class Editor2DWorkspaceViewModelTests
{
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
