using Domain.App.ViewModels;
using Domain.App.Models;
using Domain.App.Services;
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

    [Fact]
    public async Task ApplyOffsetAsync_StoresTransformedDocumentForExport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-BatchOffset", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "drawing.dxf");
        await File.WriteAllTextAsync(input, "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n10\n0\nENDSEC\n0\nEOF\n");
        try
        {
            var workspace = new EditorBatchWorkspaceViewModel();
            Assert.True(workspace.AddFile(input));
            await workspace.ApplyOffsetAsync(
                new DxfOutputPreviewService(),
                new StubOffsetGeometryKernel(),
                2.0);
            Assert.Equal(EditorBatchItemStatus.Succeeded, workspace.Items[0].Status);
            Assert.Equal("Offset applied", workspace.Items[0].Message);
            Assert.NotNull(workspace.Items[0].Document);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplySewingHolesAsync_AppendsGeneratedCircles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-BatchSewing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "drawing.dxf");
        await File.WriteAllTextAsync(input, "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n8\n0\n90\n4\n70\n1\n10\n0\n20\n0\n10\n20\n20\n0\n10\n20\n20\n10\n10\n0\n20\n10\n0\nENDSEC\n0\nEOF\n");
        try
        {
            var workspace = new EditorBatchWorkspaceViewModel();
            Assert.True(workspace.AddFile(input));
            await workspace.ApplySewingHolesAsync(
                new DxfOutputPreviewService(),
                new Editor2DSewingHoleParameters(Diameter: 1, Pitch: 5, Margin: 1));
            Assert.Equal(EditorBatchItemStatus.Succeeded, workspace.Items[0].Status);
            Assert.Contains(workspace.Items[0].Document!.Paths, path => path.EntityType == "CIRCLE");
            Assert.Contains("sewing holes applied", workspace.Items[0].Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SelectedOnlyOperations_LeaveUnselectedDxfUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pathstitch-BatchSelection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var first = Path.Combine(directory, "first.dxf");
        var second = Path.Combine(directory, "second.dxf");
        const string dxf = "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n10\n0\nENDSEC\n0\nEOF\n";
        await File.WriteAllTextAsync(first, dxf);
        await File.WriteAllTextAsync(second, dxf);
        try
        {
            var workspace = new EditorBatchWorkspaceViewModel();
            Assert.True(workspace.AddFile(first));
            Assert.True(workspace.AddFile(second));
            workspace.Items[1].IsSelected = false;
            await workspace.ApplySewingHolesAsync(new DxfOutputPreviewService(), new Editor2DSewingHoleParameters());
            Assert.NotNull(workspace.Items[0].Document);
            Assert.Null(workspace.Items[1].Document);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubOffsetGeometryKernel : IEditor2DGeometryKernelService
    {
        public Task<Editor2DGeometryKernelResult> BuildCurveOffsetPathsAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double offsetDistance,
            bool offsetOutward,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Editor2DGeometryKernelResult.Success(sourcePaths));

        public Task<Editor2DGeometryKernelResult> BuildThicknessOutlinesAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double thickness,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Editor2DGeometryKernelResult.Success(sourcePaths));
    }
}
