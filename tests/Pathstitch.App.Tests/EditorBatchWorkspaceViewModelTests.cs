using Domain.App.Models;
using Domain.App.ViewModels;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class EditorBatchWorkspaceViewModelTests
{
    [Fact]
    public async Task RunAsync_ValidatesQueuedProjectsAndContinuesAfterFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-BatchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var validPath = Path.Combine(directory, "valid.stch");
            var missingPath = Path.Combine(directory, "missing.stch");
            await File.WriteAllTextAsync(validPath, "{\"projectName\":\"Batch\"}");
            var workspace = new EditorBatchWorkspaceViewModel();
            Assert.True(workspace.AddProject(validPath));
            Assert.True(workspace.AddProject(missingPath));

            await workspace.RunAsync();

            Assert.Equal(EditorBatchItemStatus.Succeeded, workspace.Items[0].Status);
            Assert.Equal(EditorBatchItemStatus.Failed, workspace.Items[1].Status);
            Assert.Equal("Batch complete: 1 succeeded, 1 failed.", workspace.Summary);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Queue_AcceptsDxfAndProjectsAndRejectsDuplicates()
    {
        var workspace = new EditorBatchWorkspaceViewModel();

        Assert.True(workspace.AddFile("drawing.dxf"));
        Assert.True(workspace.AddFile("one.stch"));
        Assert.False(workspace.AddFile("one.stch"));
        Assert.Equal(2, workspace.Items.Count);
    }

    [Fact]
    public async Task RunAsync_ValidatesDxfInputs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-batch-{Guid.NewGuid():N}.dxf");
        await File.WriteAllTextAsync(path, "0\nSECTION\n");
        try
        {
            var workspace = new EditorBatchWorkspaceViewModel();
            Assert.True(workspace.AddFile(path));
            await workspace.RunAsync();
            Assert.Equal(EditorBatchItemStatus.Succeeded, workspace.Items[0].Status);
            Assert.Equal("Valid DXF input", workspace.Items[0].Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportDxfAsync_WritesOutput()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-BatchExport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "drawing.dxf");
        await File.WriteAllTextAsync(input, "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n10\n0\nENDSEC\n0\nEOF\n");
        try
        {
            var workspace = new EditorBatchWorkspaceViewModel { OutputDirectory = Path.Combine(directory, "out") };
            Assert.True(workspace.AddFile(input));
            await workspace.ExportDxfAsync(new DxfOutputPreviewService());
            Assert.NotNull(workspace.Items[0].OutputPath);
            Assert.True(File.Exists(workspace.Items[0].OutputPath));
            Assert.Equal(EditorBatchItemStatus.Succeeded, workspace.Items[0].Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EnteringAndRunningBatch_DoesNotMutateEditorWorkspaceState()
    {
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests();
        await editor.SetActiveEditorModeAsync(EditorMode.TwoD);
        editor.ActivateTwoDCircleTool();
        var document = editor.TwoDDocument;
        await editor.SetActiveEditorModeAsync(EditorMode.ThreeD);
        editor.ActivateMoveTool();

        await editor.SetActiveEditorModeAsync(EditorMode.Batch);
        await editor.BatchWorkspace.RunAsync();

        Assert.Same(document, editor.TwoDDocument);
        Assert.Equal(Editor2DTool.SketchCircle, editor.TwoDActiveTool);
        Assert.Equal(Editor3DTool.Move, editor.ActiveTool);
    }
}
