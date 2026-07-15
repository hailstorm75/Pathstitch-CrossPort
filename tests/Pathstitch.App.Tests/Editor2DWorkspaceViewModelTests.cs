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
