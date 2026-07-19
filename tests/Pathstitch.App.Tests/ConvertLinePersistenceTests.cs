using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class ConvertLinePersistenceTests
{
    [Fact]
    public void Conversion_PersistsOneGroupWithSourceSnapshotsOwnershipAndLayerOrder()
    {
        var before = Line("before", -10, 0, -5, 0);
        var source = Line("source", 0, 0, 20, 0);
        var after = Line("after", 25, 0, 30, 0);
        var workspace = Workspace([before, source, after], [new("cuts", "Cuts", [before.Id, source.Id, after.Id])]);
        workspace.SetSelection([source.Id]);

        var result = workspace.ApplyConvertedLines("dashed", new Dictionary<string, double>
        {
            ["dash_length"] = 4,
            ["gap"] = 2,
        });

        Assert.True(result.IsSuccess);
        var group = Assert.Single(workspace.ConvertLineGroups);
        var persistedSource = Assert.Single(group.Sources);
        Assert.Equal(source, persistedSource.SourcePath);
        Assert.Equal("cuts", persistedSource.SourceLayerId);
        Assert.Equal(1, persistedSource.SourceLayerPathIndex);
        Assert.Equal(1, persistedSource.SourceDocumentPathIndex);
        Assert.DoesNotContain(workspace.Document.Paths, path => path.Id == source.Id);
        Assert.NotEmpty(persistedSource.GeneratedPathIds);
        Assert.All(persistedSource.GeneratedPathIds, id => Assert.Same(group, workspace.FindConvertLineGroupForPath(id)));
        Assert.Equal(
            [before.Id, .. persistedSource.GeneratedPathIds, after.Id],
            Assert.Single(workspace.Layers).PathIds);
        Assert.Equal(group.GeneratedPathIds, workspace.SelectedPathIds);
    }

    [Fact]
    public void Restyle_RetainsGroupAndOriginalsAndUndoRedoAreAtomic()
    {
        var source = Line("source", 0, 0, 24, 0);
        var workspace = Workspace([source], [new("cuts", "Cuts", [source.Id])]);
        workspace.SetSelection([source.Id]);
        Assert.True(workspace.ApplyConvertedLines("dashed", new Dictionary<string, double>
        {
            ["dash_length"] = 4,
            ["gap"] = 2,
        }).IsSuccess);
        var originalGroup = Assert.Single(workspace.ConvertLineGroups);
        var dashedDocument = workspace.Document;

        Assert.True(workspace.RestyleConvertedLines(originalGroup.Id, "wave", new Dictionary<string, double>
        {
            ["wavelength"] = 6,
            ["amplitude"] = 3,
            ["samples_per_wave"] = 8,
        }).IsSuccess);

        var restyled = Assert.Single(workspace.ConvertLineGroups);
        Assert.Equal(originalGroup.Id, restyled.Id);
        Assert.Equal("wave", restyled.Style);
        Assert.Equal(source, Assert.Single(restyled.Sources).SourcePath);
        Assert.NotEqual(dashedDocument, workspace.Document);
        Assert.True(workspace.Undo());
        Assert.Equal(originalGroup, Assert.Single(workspace.ConvertLineGroups));
        Assert.Equal(dashedDocument, workspace.Document);
        Assert.True(workspace.Redo());
        Assert.Equal("wave", Assert.Single(workspace.ConvertLineGroups).Style);

        Assert.True(workspace.Undo());
        Assert.True(workspace.Undo());
        Assert.Empty(workspace.ConvertLineGroups);
        Assert.Equal(source, Assert.Single(workspace.Document.Paths));
        Assert.Equal([source.Id], Assert.Single(workspace.Layers).PathIds);
    }

    [Fact]
    public void Conversion_GroupsMultipleSourcesAndPreservesTheirSeparateLayers()
    {
        var first = Line("first", 0, 0, 12, 0);
        var second = Line("second", 0, 10, 12, 10);
        var workspace = Workspace(
            [first, second],
            [new("first-layer", "First", [first.Id]), new("second-layer", "Second", [second.Id])]);
        workspace.SetSelection([first.Id, second.Id]);

        Assert.True(workspace.ApplyConvertedLines("dashed", new Dictionary<string, double>
        {
            ["dash_length"] = 3,
            ["gap"] = 1,
        }).IsSuccess);

        var group = Assert.Single(workspace.ConvertLineGroups);
        Assert.Equal(2, group.Sources.Count);
        Assert.Equal("first-layer", group.Sources.Single(source => source.SourcePath.Id == first.Id).SourceLayerId);
        Assert.Equal("second-layer", group.Sources.Single(source => source.SourcePath.Id == second.Id).SourceLayerId);
        Assert.Equal(
            group.Sources.Single(source => source.SourcePath.Id == first.Id).GeneratedPathIds,
            workspace.Layers.Single(layer => layer.Id == "first-layer").PathIds);
        Assert.Equal(
            group.Sources.Single(source => source.SourcePath.Id == second.Id).GeneratedPathIds,
            workspace.Layers.Single(layer => layer.Id == "second-layer").PathIds);
    }

    [Fact]
    public void Conversion_RejectsUnsupportedStyleWithoutChangingWorkspace()
    {
        var source = Line("source", 0, 0, 12, 0);
        var workspace = Workspace([source], [new("cuts", "Cuts", [source.Id])]);
        workspace.SetSelection([source.Id]);
        var before = workspace.State;

        var result = workspace.ApplyConvertedLines("unknown", new Dictionary<string, double>());

        Assert.False(result.IsSuccess);
        Assert.Equal(before, workspace.State);
        Assert.Empty(workspace.ConvertLineGroups);
    }

    [Fact]
    public void EditingGeneratedMember_BakesGroupAndUndoRestoresEditability()
    {
        var source = Line("source", 0, 0, 24, 0);
        var workspace = Workspace([source], [new("cuts", "Cuts", [source.Id])]);
        workspace.SetSelection([source.Id]);
        Assert.True(workspace.ApplyConvertedLines("dashed", new Dictionary<string, double>
        {
            ["dash_length"] = 4,
            ["gap"] = 2,
        }).IsSuccess);
        var group = Assert.Single(workspace.ConvertLineGroups);
        workspace.SetSelection([group.GeneratedPathIds[0]]);

        Assert.Equal(1, workspace.DeleteSelection());
        Assert.Empty(workspace.ConvertLineGroups);
        Assert.True(workspace.Undo());
        var restored = Assert.Single(workspace.ConvertLineGroups);
        Assert.Equal(group.Id, restored.Id);
        Assert.Equal(group.GeneratedPathIds, restored.GeneratedPathIds);
        Assert.True(workspace.Redo());
        Assert.Empty(workspace.ConvertLineGroups);
    }

    [Fact]
    public void TransformingGeneratedMember_BakesGroupInSameUndoStep()
    {
        var source = Line("source", 0, 0, 24, 0);
        var workspace = Workspace([source], [new("cuts", "Cuts", [source.Id])]);
        workspace.SetSelection([source.Id]);
        Assert.True(workspace.ApplyConvertedLines("dashed", new Dictionary<string, double>()).IsSuccess);
        var group = Assert.Single(workspace.ConvertLineGroups);
        workspace.SetSelection([group.GeneratedPathIds[0]]);

        Assert.True(workspace.ApplyPreciseTransform(2, 0, 0).IsSuccess);
        Assert.Empty(workspace.ConvertLineGroups);
        Assert.True(workspace.Undo());
        Assert.Equal(group.Id, Assert.Single(workspace.ConvertLineGroups).Id);
    }

    [Fact]
    public void Apply_MalformedGroupsAreSafelyRepairedOrPruned()
    {
        var generated = Line("generated", 0, 0, 4, 0);
        var source = Line("source", 0, 0, 12, 0);
        var validSource = new Editor2DConvertLineSource(source, "cuts", 0, 0, [generated.Id]);
        var groups = new Editor2DConvertLineGroup[]
        {
            new(" group ", "unknown", new Dictionary<string, double> { ["gap"] = -10 }, [validSource]),
            new("group", "dotted", new Dictionary<string, double>(), [validSource]),
            new("broken", "dashed", new Dictionary<string, double>(),
                [new Editor2DConvertLineSource(source, null, 0, 0, null!)]),
        };
        var workspace = new Editor2DWorkspaceViewModel();

        workspace.Apply(new Editor2DWorkspaceState(
            Document(generated),
            Layers: [new("cuts", "Cuts", [generated.Id])],
            ActiveLayerId: "cuts",
            ConvertLineGroups: groups), recordHistory: false);

        var repaired = Assert.Single(workspace.ConvertLineGroups);
        Assert.Equal("group", repaired.Id);
        Assert.Equal("dashed", repaired.Style);
        Assert.Equal(4, repaired.Settings["dash_length"]);
        Assert.Equal(.1, repaired.Settings["gap"]);
    }

    [Fact]
    public async Task SaveStateRoundTrip_PreservesEditableGroupAndLegacyStateDefaultsEmpty()
    {
        var source = Line("source", 0, 0, 18, 0);
        var workspace = Workspace([source], [new("score", "Score", [source.Id])]);
        workspace.SetSelection([source.Id]);
        Assert.True(workspace.ApplyConvertedLines("dotted", new Dictionary<string, double>
        {
            ["spacing"] = 3,
            ["dot_radius"] = .5,
        }).IsSuccess);
        var expected = Assert.Single(workspace.ConvertLineGroups);
        var projectPath = Path.Combine(Path.GetTempPath(), $"convert-lines-{Guid.NewGuid():N}.stch");

        try
        {
            var service = new Project3DStateService();
            await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: workspace.State));
            var loaded = await service.LoadAsync(projectPath);
            var restored = new Editor2DWorkspaceViewModel();
            restored.Apply(loaded.TwoDWorkspaceState!, recordHistory: false);

            var restoredGroup = Assert.Single(restored.ConvertLineGroups);
            Assert.Equal(expected.Id, restoredGroup.Id);
            Assert.Equal(expected.Style, restoredGroup.Style);
            Assert.Equal(expected.Settings.OrderBy(item => item.Key), restoredGroup.Settings.OrderBy(item => item.Key));
            Assert.Equal(expected.Sources[0].SourcePath.Id, restoredGroup.Sources[0].SourcePath.Id);
            Assert.Equal(expected.Sources[0].SourcePath.Points, restoredGroup.Sources[0].SourcePath.Points);
            Assert.Equal(expected.Sources[0].GeneratedPathIds, restoredGroup.Sources[0].GeneratedPathIds);
            Assert.Equal("score", restoredGroup.Sources[0].SourceLayerId);
            Assert.True(restored.RestyleConvertedLines(expected.Id, "zigzag", new Dictionary<string, double>
            {
                ["wavelength"] = 5,
                ["amplitude"] = 2,
            }).IsSuccess);
            Assert.Equal("zigzag", Assert.Single(restored.ConvertLineGroups).Style);

            var legacy = new Editor2DWorkspaceViewModel();
            legacy.Apply(new Editor2DWorkspaceState(Document(source)), recordHistory: false);
            Assert.Empty(legacy.ConvertLineGroups);
        }
        finally
        {
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }

    private static Editor2DWorkspaceViewModel Workspace(
        IReadOnlyList<Editor2DPreviewPath> paths,
        IReadOnlyList<Editor2DLayer> layers)
    {
        var workspace = new Editor2DWorkspaceViewModel();
        workspace.Apply(new Editor2DWorkspaceState(
            Document(paths.ToArray()),
            Layers: layers,
            ActiveLayerId: layers[0].Id), recordHistory: false);
        workspace.ClearHistory();
        return workspace;
    }

    private static Editor2DPreviewPath Line(string id, double x1, double y1, double x2, double y2)
        => new(id, "LINE", [new(x1, y1), new(x2, y2)], false);

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        var bounds = points.Length == 0
            ? new Editor2DBounds(0, 0, 0, 0)
            : new Editor2DBounds(points.Min(point => point.X), points.Min(point => point.Y), points.Max(point => point.X), points.Max(point => point.Y));
        return new Editor2DPreviewDocument(
            paths,
            bounds,
            paths.GroupBy(path => path.EntityType).ToDictionary(group => group.Key, group => group.Count()),
            []);
    }
}
