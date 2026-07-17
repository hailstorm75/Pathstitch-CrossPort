using Domain.App.Models;

namespace Pathstitch.App.Tests;

public sealed class EditorDirectToolTests
{
    [Fact]
    public void Catalog_ContainsEveryRestoredDirectToolByStableIdentifier()
    {
        var identifiers = EditorToolCatalog.ForMode(EditorMode.TwoD)
            .Select(descriptor => descriptor.Identifier)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("2d.add-sewing-holes", identifiers);
        Assert.Contains("2d.flip-horizontal", identifiers);
        Assert.Contains("2d.flip-vertical", identifiers);
        Assert.Contains("2d.duplicate", identifiers);
    }

    [Fact]
    public async Task DuplicateAndFlipActions_OperateOnSelectionWithoutExtraXamlHandlers()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        await editor.SetActiveEditorModeAsync(EditorMode.TwoD);
        var source = new Editor2DPreviewPath(
            "line",
            "LINE",
            [new Editor2DPoint(0, 0), new Editor2DPoint(10, 5)],
            IsClosed: false);
        editor.TwoDDocument = editor.TwoDDocument! with { Paths = [source] };
        editor.TwoDSelectedPathIds = [source.Id];

        editor.ActivateSidebarItem("duplicate");
        var copy = Assert.Single(editor.TwoDDocument.Paths, path => path.Id != source.Id);
        Assert.Equal(new Editor2DPoint(5, 5), copy.Points[0]);
        Assert.Equal([copy.Id], editor.TwoDSelectedPathIds);

        editor.ActivateSidebarItem("flip-horizontal");
        var flippedCopy = Assert.Single(editor.TwoDDocument.Paths, path => path.Id == copy.Id);
        Assert.Equal(15, flippedCopy.Points[0].X);
        Assert.Equal(5, flippedCopy.Points[1].X);
    }

    [Fact]
    public void FlipAction_TransformsAttachedMetadataInOneUndoStep()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        var source = new Editor2DPreviewPath(
            "line", "LINE", [new(0, 0), new(10, 5)], IsClosed: false);
        var measurement = new Editor2DMeasurement(
            "measurement", new(0, 2), new(10, 2), EntityPathId: source.Id, DimensionType: "length");
        var corner = new Editor2DCornerParameter(
            "corner", source.Id, 1, Editor2DCornerKind.Chamfer, 1,
            [new(0, 0), new(10, 0), new(10, 5)]);
        editor.TwoDWorkspace.Apply(new Editor2DWorkspaceState(
            Document(source),
            SelectedPathIds: [source.Id],
            Measurements: [measurement],
            CornerParameters: [corner]), recordHistory: false);
        editor.TwoDWorkspace.ClearHistory();

        Assert.True(editor.FlipTwoDSelection(horizontal: true));

        Assert.Equal([new Editor2DPoint(10, 0), new Editor2DPoint(0, 5)], editor.TwoDDocument!.Paths[0].Points);
        Assert.Equal(new Editor2DPoint(10, 2), Assert.Single(editor.TwoDMeasurements).Start);
        Assert.Equal(
            [new Editor2DPoint(10, 0), new Editor2DPoint(0, 0), new Editor2DPoint(0, 5)],
            Assert.Single(editor.TwoDCornerParameters).SourcePoints);
        Assert.True(editor.CanUndoTwoDWorkspace);

        Assert.True(editor.UndoTwoDWorkspace());
        Assert.False(editor.CanUndoTwoDWorkspace);
        Assert.Equal(source.Points, editor.TwoDDocument!.Paths[0].Points);
        Assert.Equal(measurement, Assert.Single(editor.TwoDMeasurements));
        Assert.Equal(corner, Assert.Single(editor.TwoDCornerParameters));
    }

    [Fact]
    public void DuplicateAction_ClonesEditableMetadataWithoutCloningMeasurementsAndUndoesAtomically()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        var generated = new Editor2DPreviewPath(
            "generated", "LINE", [new(0, 0), new(4, 0)], IsClosed: false);
        var measurement = new Editor2DMeasurement(
            "measurement", new(0, 2), new(4, 2), EntityPathId: generated.Id, DimensionType: "length");
        var corner = new Editor2DCornerParameter(
            "corner", generated.Id, 1, Editor2DCornerKind.Chamfer, 1,
            [new(0, 0), new(4, 0), new(4, 4)]);
        var group = new Editor2DConvertLineGroup(
            "group", "dashed", new Dictionary<string, double>(),
            [new Editor2DConvertLineSource(
                new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(12, 0)], false),
                null, 0, 0, [generated.Id])]);
        editor.TwoDWorkspace.Apply(new Editor2DWorkspaceState(
            Document(generated),
            SelectedPathIds: [generated.Id],
            Measurements: [measurement],
            CornerParameters: [corner],
            ConvertLineGroups: [group]), recordHistory: false);
        editor.TwoDWorkspace.ClearHistory();

        Assert.True(editor.DuplicateTwoDSelection(10, 0));

        var copyId = Assert.Single(editor.TwoDSelectedPathIds);
        Assert.Equal(2, editor.TwoDDocument!.Paths.Count);
        Assert.Equal(new Editor2DPoint(10, 0), editor.TwoDDocument.Paths.Single(path => path.Id == copyId).Points[0]);
        Assert.Equal(2, editor.TwoDCornerParameters.Count);
        Assert.Contains(editor.TwoDCornerParameters, parameter => parameter.PathId == copyId);
        Assert.Equal(2, editor.TwoDWorkspace.ConvertLineGroups.Count);
        Assert.Contains(editor.TwoDWorkspace.ConvertLineGroups, candidate =>
            candidate.Id != group.Id && candidate.GeneratedPathIds.SequenceEqual([copyId]));
        Assert.Equal([measurement], editor.TwoDMeasurements);
        Assert.True(editor.CanUndoTwoDWorkspace);

        Assert.True(editor.UndoTwoDWorkspace());
        Assert.False(editor.CanUndoTwoDWorkspace);
        Assert.Single(editor.TwoDDocument!.Paths);
        Assert.Equal([corner], editor.TwoDCornerParameters);
        var restoredGroup = Assert.Single(editor.TwoDWorkspace.ConvertLineGroups);
        Assert.Equal(group.Id, restoredGroup.Id);
        Assert.Equal(group.Style, restoredGroup.Style);
        Assert.Equal(group.GeneratedPathIds, restoredGroup.GeneratedPathIds);
        Assert.Equal([measurement], editor.TwoDMeasurements);
    }

    [Fact]
    public void ToolRail_IsResizableAndUsesAReflowingPanel()
    {
        var shell = ReadPage("EditorShellView.axaml");
        var rail = ReadPage("EditorToolRail.axaml");

        Assert.Contains("editor.tool-rail.resize", shell, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"180\"", shell, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel", rail, StringComparison.Ordinal);
        Assert.Contains("Orientation=\"Horizontal\"", rail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolRail_CustomizationUiReordersByStableIdentifierAndPersistsTheOrder()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        await editor.SetActiveEditorModeAsync(EditorMode.TwoD);
        var initial = editor.SidebarTools.Select(tool => tool.Identifier).ToArray();
        var movedIdentifier = initial[1];

        Assert.True(editor.MoveToolCustomization(movedIdentifier, -1));

        Assert.Equal(movedIdentifier, editor.SidebarTools[0].Identifier);
        Assert.Equal(
            editor.SidebarTools[0].Order,
            editor.ToolCustomizations.Single(item => item.Identifier == movedIdentifier).Order);
        var rail = ReadPage("EditorToolRail.axaml");
        Assert.Contains("editor.tool-rail.customize", rail, StringComparison.Ordinal);
        Assert.Contains("OnMoveToolEarlierClicked", rail, StringComparison.Ordinal);
        Assert.Contains("OnMoveToolLaterClicked", rail, StringComparison.Ordinal);
        Assert.Contains("Identifier, StringFormat={}{0}.move-up", rail, StringComparison.Ordinal);
    }

    private static string ReadPage(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Pathstitch.App", "Pages", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(fileName);
    }

    private static Editor2DPreviewDocument Document(Editor2DPreviewPath path)
        => new(
            [path],
            new Editor2DBounds(
                path.Points.Min(point => point.X),
                path.Points.Min(point => point.Y),
                path.Points.Max(point => point.X),
                path.Points.Max(point => point.Y)),
            new Dictionary<string, int> { [path.EntityType] = 1 },
            []);
}
