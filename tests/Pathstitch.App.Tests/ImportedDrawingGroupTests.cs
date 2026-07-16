using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class ImportedDrawingGroupTests
{
    [Fact]
    public void AddImportedDrawings_ArrangesAndNamespacesGroupsInOneUndoableApply()
    {
        var existing = Line("existing", 0);
        var layer = new Editor2DLayer("geometry", "Geometry", [existing.Id]);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(new Editor2DWorkspaceState(
            Document(existing), Layers: [layer], ActiveLayerId: layer.Id), recordHistory: false);
        workspace.ClearHistory();

        var result = workspace.AddImportedDrawings(
        [
            new Editor2DImportedDrawing("first.dxf", 1, Document(Line("source-a", 10, 20))),
            new Editor2DImportedDrawing("second.svg", 2.5, Document(Line("source-b", -5, 5))),
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, workspace.ImportGroups.Count);
        Assert.All(workspace.ImportGroups, group =>
        {
            Assert.StartsWith("import-", Assert.Single(group.GeneratedPathIds), StringComparison.Ordinal);
            var importLayer = Assert.Single(workspace.Layers, candidate => candidate.Id == group.OwningLayerId);
            Assert.Equal(group.GeneratedPathIds, importLayer.PathIds);
        });
        Assert.Equal("first", workspace.Layers.Single(candidate => candidate.Id == workspace.ImportGroups[0].OwningLayerId).Name);
        Assert.Equal("second", workspace.Layers.Single(candidate => candidate.Id == workspace.ImportGroups[1].OwningLayerId).Name);
        Assert.Equal(30, GroupBounds(workspace, workspace.ImportGroups[0]).CenterX, 8);
        Assert.Equal(60, GroupBounds(workspace, workspace.ImportGroups[1]).CenterX, 8);
        Assert.Equal([existing.Id], workspace.Layers.Single(candidate => candidate.Id == layer.Id).PathIds);
        Assert.Equal(workspace.ImportGroups.SelectMany(group => group.GeneratedPathIds), workspace.SelectedPathIds);

        Assert.True(workspace.Undo());
        Assert.Empty(workspace.ImportGroups);
        Assert.Equal(existing, Assert.Single(workspace.Document.Paths));
        Assert.True(workspace.Redo());
        Assert.Equal(2, workspace.ImportGroups.Count);
    }

    [Fact]
    public void ReloadImportGroups_PreservesCentersAndInsertionOrderAndUndoRedo()
    {
        var layer = new Editor2DLayer("geometry", "Geometry", []);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(new Editor2DWorkspaceState(
            Document(), Layers: [layer], ActiveLayerId: layer.Id), recordHistory: false);
        Assert.True(workspace.AddImportedDrawings(
        [
            new Editor2DImportedDrawing("first.dxf", 1, Document(Line("a", 0, 10))),
            new Editor2DImportedDrawing("second.dxf", 1, Document(Line("b", 0, 10))),
        ]).IsSuccess);
        workspace.ClearHistory();
        var first = workspace.ImportGroups[0];
        var second = workspace.ImportGroups[1];
        var firstCenter = GroupBounds(workspace, first).CenterX;
        var secondCenter = GroupBounds(workspace, second).CenterX;
        var beforeReload = workspace.Document;

        var result = workspace.ReloadImportGroups(new Dictionary<string, Editor2DPreviewDocument>
        {
            [second.Id] = Document(Line("new-b", -2, 2)),
            [first.Id] = Document(Line("new-a", -10, 10)),
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(firstCenter, GroupBounds(workspace, workspace.ImportGroups[0]).CenterX, 8);
        Assert.Equal(secondCenter, GroupBounds(workspace, workspace.ImportGroups[1]).CenterX, 8);
        Assert.Equal(20, GroupBounds(workspace, workspace.ImportGroups[0]).Width, 8);
        Assert.Equal(4, GroupBounds(workspace, workspace.ImportGroups[1]).Width, 8);
        var documentIds = workspace.Document.Paths.Select(path => path.Id).ToArray();
        Assert.True(Array.IndexOf(documentIds, workspace.ImportGroups[0].GeneratedPathIds[0])
                    < Array.IndexOf(documentIds, workspace.ImportGroups[1].GeneratedPathIds[0]));
        Assert.Equal(
            workspace.Document.Paths.Select(path => path.Id),
            workspace.Layers.OrderBy(layer => layer.Order).SelectMany(layer => layer.PathIds));

        Assert.True(workspace.Undo());
        Assert.Equal(beforeReload, workspace.Document);
        Assert.True(workspace.Redo());
        Assert.Equal(20, GroupBounds(workspace, workspace.ImportGroups[0]).Width, 8);
    }

    [Fact]
    public void ReloadImportGroup_UsesFreshIdsSingleOwningLayerAndRefreshesDiagnostics()
    {
        var basePath = Line("base", -10);
        var baseLayer = new Editor2DLayer("base-layer", "Base", [basePath.Id]);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(new Editor2DWorkspaceState(
            DocumentWithUnsupported(["BASE", "SPLINE"], basePath), Layers: [baseLayer], ActiveLayerId: baseLayer.Id), recordHistory: false);
        Assert.True(workspace.AddImportedDrawings(
            [new Editor2DImportedDrawing("source.dxf", 1, DocumentWithUnsupported(["SPLINE"], Line("a", 0), Line("b", 10)))]).IsSuccess);
        var original = Assert.Single(workspace.ImportGroups);
        var oldIds = original.GeneratedPathIds.ToArray();
        var importLayer = workspace.Layers.Single(layer => layer.Id == original.OwningLayerId);
        var splitLayer = new Editor2DLayer("split", "Split", [oldIds[1]], Order: importLayer.Order + 1);
        workspace.Apply(workspace.State with
        {
            Layers = workspace.Layers
                .Select(layer => layer.Id == importLayer.Id ? layer with { PathIds = [oldIds[0]] } : layer)
                .Append(splitLayer)
                .ToArray(),
            Measurements = [new Editor2DMeasurement("stale", new(0, 0), new(1, 0), true, oldIds[0])],
            SewingHoleParameters = Editor2DSewingHoleParameters.Default with
            {
                AvoidanceEnabled = true,
                AvoidPathIds = [oldIds[0]],
            },
        }, recordHistory: false);

        Assert.True(workspace.ReloadImportGroups(new Dictionary<string, Editor2DPreviewDocument>
        {
            [original.Id] = DocumentWithUnsupported(["HATCH"], Line("fresh", 0)),
        }).IsSuccess);

        var reloaded = Assert.Single(workspace.ImportGroups);
        Assert.DoesNotContain(reloaded.GeneratedPathIds, id => oldIds.Contains(id, StringComparer.Ordinal));
        Assert.Equal(reloaded.GeneratedPathIds, workspace.Layers.Single(layer => layer.Id == reloaded.OwningLayerId).PathIds);
        Assert.Empty(workspace.Layers.Single(layer => layer.Id == splitLayer.Id).PathIds);
        Assert.Equal(1, workspace.Layers.Sum(layer => layer.PathIds.Count(id => reloaded.GeneratedPathIds.Contains(id, StringComparer.Ordinal))));
        Assert.Contains("BASE", workspace.Document.UnsupportedEntityTypes);
        Assert.Contains("HATCH", workspace.Document.UnsupportedEntityTypes);
        Assert.Contains("SPLINE", workspace.Document.UnsupportedEntityTypes);
        Assert.Empty(workspace.Measurements);
        Assert.Empty(workspace.SewingHoleParameters.AvoidPathIds!);
    }

    [Fact]
    public void Workspace_NormalizesOwnershipAndLooksUpSelectedImportGroup()
    {
        var first = Line("first", 0);
        var second = Line("second", 10);
        var third = Line("third", 20);
        var document = Document(first, second, third);
        var layer = new Editor2DLayer("geometry", "Geometry", [first.Id, second.Id, third.Id]);
        var groups = new Editor2DImportGroup[]
        {
            new(" group-1 ", " C:\\drawings\\first.dxf ", 25.4, [first.Id, "missing", second.Id], "stale", 99, 99),
            new("group-1", "duplicate.dxf", 1, [third.Id], layer.Id, 0, 0),
            new("invalid-scale", "invalid.dxf", double.NaN, [third.Id], layer.Id, 0, 0),
            new("group-2", "second.svg", 1, [second.Id, third.Id], layer.Id, 0, 0),
        };
        var workspace = new Editor2DWorkspaceViewModel();

        workspace.Apply(new Editor2DWorkspaceState(
            document,
            Layers: [layer],
            ActiveLayerId: layer.Id,
            ImportGroups: groups), recordHistory: false);

        Assert.Equal(2, workspace.ImportGroups.Count);
        var firstGroup = workspace.ImportGroups[0];
        Assert.Equal("group-1", firstGroup.Id);
        Assert.Equal("C:\\drawings\\first.dxf", firstGroup.SourceFilePath);
        Assert.Equal([first.Id, second.Id], firstGroup.GeneratedPathIds);
        Assert.Equal(layer.Id, firstGroup.OwningLayerId);
        Assert.Equal(0, firstGroup.OwningLayerPathIndex);
        Assert.Equal(0, firstGroup.DocumentPathIndex);
        var secondGroup = workspace.ImportGroups[1];
        Assert.Equal([third.Id], secondGroup.GeneratedPathIds);
        Assert.Same(secondGroup, workspace.FindImportGroupForPath(third.Id));

        workspace.SetSelection([third.Id]);
        Assert.Equal(secondGroup.Id, workspace.GetSelectedImportGroup()!.Id);
        workspace.SetSelection([first.Id, third.Id]);
        Assert.Null(workspace.GetSelectedImportGroup());
    }

    [Fact]
    public void Workspace_ImportGroupsParticipateInUndoAndRedoState()
    {
        var path = Line("imported", 0);
        var document = Document(path);
        var layer = new Editor2DLayer("geometry", "Geometry", [path.Id]);
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(new Editor2DWorkspaceState(document, Layers: [layer]), recordHistory: false);
        workspace.ClearHistory();

        workspace.Apply(workspace.State with
        {
            ImportGroups = [new Editor2DImportGroup("import", "drawing.dxf", 1, [path.Id], layer.Id, 0, 0)],
        });

        Assert.Single(workspace.ImportGroups);
        Assert.True(workspace.Undo());
        Assert.Empty(workspace.ImportGroups);
        Assert.True(workspace.Redo());
        Assert.Single(workspace.ImportGroups);
    }

    [Fact]
    public async Task ProjectRoundTrip_PreservesImportedDrawingGroupAndLegacyStateDefaultsEmpty()
    {
        var path = Line("imported", 0);
        var document = Document(path);
        var layer = new Editor2DLayer("geometry", "Geometry", [path.Id]);
        var group = new Editor2DImportGroup("import", "source.svg", 2.5, [path.Id], layer.Id, 0, 0, ["FILTER"]);
        var workspaceState = new Editor2DWorkspaceState(
            document,
            Layers: [layer],
            ActiveLayerId: layer.Id,
            ImportGroups: [group]);
        var projectPath = Path.Combine(Path.GetTempPath(), $"import-group-{Guid.NewGuid():N}.stch");

        try
        {
            var service = new Project3DStateService();
            await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: workspaceState));
            var loaded = await service.LoadAsync(projectPath);
            var restored = new Editor2DWorkspaceViewModel();
            restored.Apply(loaded.TwoDWorkspaceState!, recordHistory: false);

            var restoredGroup = Assert.Single(restored.ImportGroups);
            Assert.Equal(group.Id, restoredGroup.Id);
            Assert.Equal(group.SourceFilePath, restoredGroup.SourceFilePath);
            Assert.Equal(group.AppliedUnitScale, restoredGroup.AppliedUnitScale);
            Assert.Equal(group.GeneratedPathIds, restoredGroup.GeneratedPathIds);
            Assert.Equal(group.OwningLayerId, restoredGroup.OwningLayerId);
            Assert.Equal(group.UnsupportedEntityTypes, restoredGroup.UnsupportedEntityTypes);

            var legacy = new Editor2DWorkspaceViewModel();
            legacy.Apply(new Editor2DWorkspaceState(document, Layers: [layer]), recordHistory: false);
            Assert.Empty(legacy.ImportGroups);
        }
        finally
        {
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }

    private static Editor2DBounds GroupBounds(Editor2DWorkspaceViewModel workspace, Editor2DImportGroup group)
    {
        var points = workspace.Document.Paths
            .Where(path => group.GeneratedPathIds.Contains(path.Id, StringComparer.Ordinal))
            .SelectMany(path => path.Points)
            .ToArray();
        return new Editor2DBounds(
            points.Min(point => point.X), points.Min(point => point.Y),
            points.Max(point => point.X), points.Max(point => point.Y));
    }

    private static Editor2DPreviewPath Line(string id, double x)
        => Line(id, x, x + 5);

    private static Editor2DPreviewPath Line(string id, double startX, double endX)
        => new(id, "LINE", [new(startX, 0), new(endX, 0)], false);

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
        => DocumentWithUnsupported([], paths);

    private static Editor2DPreviewDocument DocumentWithUnsupported(
        IReadOnlyList<string> unsupported,
        params Editor2DPreviewPath[] paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        return new(
            paths,
            points.Length == 0
                ? new Editor2DBounds(0, 0, 0, 0)
                : new Editor2DBounds(points.Min(point => point.X), 0, points.Max(point => point.X), 0),
            new Dictionary<string, int> { ["LINE"] = paths.Length },
            unsupported);
    }
}
