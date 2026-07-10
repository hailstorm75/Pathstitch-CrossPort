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
        Assert.Equal(new Editor2DPoint(10, 10), copy.Points[0]);
        Assert.Equal([copy.Id], editor.TwoDSelectedPathIds);

        editor.ActivateSidebarItem("flip-horizontal");
        var flippedCopy = Assert.Single(editor.TwoDDocument.Paths, path => path.Id == copy.Id);
        Assert.Equal(20, flippedCopy.Points[0].X);
        Assert.Equal(10, flippedCopy.Points[1].X);
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
}
