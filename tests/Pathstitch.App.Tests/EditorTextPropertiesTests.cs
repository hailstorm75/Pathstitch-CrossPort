using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Tests;

public sealed class EditorTextPropertiesTests
{
    [Fact]
    public async Task SelectedText_AppliesMultilineTypographyAndPersistsIt()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        await editor.SetActiveEditorModeAsync(EditorMode.TwoD);
        var text = new Editor2DPreviewPath(
            "text",
            "TEXT",
            Editor2DGeometry.BuildTextBoundsPoints(new Editor2DPoint(2, 3), "Old", 5),
            true,
            Start: new Editor2DPoint(2, 3),
            Text: "Old",
            TextHeight: 5);
        editor.TwoDDocument = editor.TwoDDocument! with { Paths = [text] };
        editor.TwoDSelectedPathIds = [text.Id];
        editor.TwoDSelectedTextDraft = "First line\nSecond line";
        editor.TwoDSelectedTextHeightText = "8";
        editor.TwoDSelectedTextFontFamily = "Segoe UI";
        editor.TwoDSelectedTextCharacterSpacingText = "1.25";
        editor.TwoDSelectedTextBold = true;
        editor.TwoDSelectedTextItalic = true;
        editor.TwoDSelectedTextUnderline = true;

        Assert.True(editor.ApplyTwoDSelectedText());
        var updated = Assert.Single(editor.TwoDDocument.Paths);
        Assert.Equal("First line\nSecond line", updated.Text);
        Assert.Equal(8, updated.TextHeight);
        Assert.Equal("Segoe UI", updated.FontFamily);
        Assert.Equal(1.25, updated.CharacterSpacing);
        Assert.True(updated.IsBold);
        Assert.True(updated.IsItalic);
        Assert.True(updated.IsUnderline);

        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-TextTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "text.stch");
            var state = Editor2DWorkspaceState.Empty with
            {
                IsInitialized = true,
                Document = editor.TwoDDocument,
            };
            var service = new Project3DStateService();
            await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: state));
            var restored = await service.LoadAsync(projectPath);
            var restoredText = Assert.Single(restored.TwoDWorkspaceState!.Document.Paths);
            Assert.Equal(updated.Text, restoredText.Text);
            Assert.Equal(updated.FontFamily, restoredText.FontFamily);
            Assert.Equal(updated.CharacterSpacing, restoredText.CharacterSpacing);
            Assert.True(restoredText.IsBold && restoredText.IsItalic && restoredText.IsUnderline);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TextUi_UsesInstalledFontsAndRendererConsumesStyleProperties()
    {
        var inspector = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DInspector.axaml");
        var codeBehind = ReadRepositoryFile("src", "Pathstitch.App", "Pages", "Editor2DPersistentPanels.axaml.cs");
        var canvas = ReadRepositoryFile("src", "Pathstitch.App", "Controls", "DxfPreviewCanvas.cs");

        Assert.Contains("InstalledFontSelector", inspector, StringComparison.Ordinal);
        Assert.Contains("FontManager.Current.SystemFonts", codeBehind, StringComparison.Ordinal);
        Assert.Contains("path.IsBold", canvas, StringComparison.Ordinal);
        Assert.Contains("path.IsItalic", canvas, StringComparison.Ordinal);
        Assert.Contains("path.IsUnderline", canvas, StringComparison.Ordinal);
        Assert.Contains("path.CharacterSpacing", canvas, StringComparison.Ordinal);
        Assert.Contains("TwoDSelectedTextFitModeOptions", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextFitMode_PersistsAcrossSelectionReEditAndNonePreservesWarp()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        await editor.SetActiveEditorModeAsync(EditorMode.TwoD);
        var text = new Editor2DPreviewPath(
            "text",
            "TEXT",
            Editor2DGeometry.BuildTextBoundsPoints(new Editor2DPoint(0, 0), "AB", 5, widthFactor: 3),
            true,
            Start: new Editor2DPoint(0, 0),
            Text: "AB",
            TextHeight: 5,
            WidthFactor: 3);
        editor.TwoDDocument = editor.TwoDDocument! with { Paths = [text] };
        editor.TwoDSelectedPathIds = [text.Id];
        editor.TwoDSelectedTextFitMode = "Width";
        editor.TwoDSelectedPathIds = [];
        editor.TwoDSelectedPathIds = [text.Id];

        Assert.Equal("Width", editor.TwoDSelectedTextFitMode);
        editor.TwoDSelectedTextFitMode = "None";
        editor.TwoDSelectedTextDraft = "AB!";
        Assert.True(editor.ApplyTwoDSelectedText());
        Assert.Equal(3, Assert.Single(editor.TwoDDocument.Paths).WidthFactor);
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(Path.Combine(pathParts));
    }
}
