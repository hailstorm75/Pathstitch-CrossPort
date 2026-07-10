using Domain.App.Models;
using Domain.App.ViewModels;

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
    public void Queue_RejectsNonProjectsAndDuplicateStablePaths()
    {
        var workspace = new EditorBatchWorkspaceViewModel();

        Assert.False(workspace.AddProject("drawing.dxf"));
        Assert.True(workspace.AddProject("one.stch"));
        Assert.False(workspace.AddProject("one.stch"));
        Assert.Single(workspace.Items);
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
