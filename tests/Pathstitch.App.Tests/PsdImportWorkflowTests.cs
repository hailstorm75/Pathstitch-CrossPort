using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class PsdImportWorkflowTests
{
    [Fact]
    public void LoadAsIs_PreservesRasterAndVectorLayersInOneUndoStep()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var originalLayerCount = workspace.Layers.Count;

        var result = workspace.ImportPsd(CreateImport(), PsdImportMode.LoadAsIs);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(originalLayerCount + 2, workspace.Layers.Count);
        Assert.Single(workspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage);
        var vector = Assert.Single(workspace.Document.Paths);
        Assert.Equal([new Editor2DPoint(0, 0), new Editor2DPoint(120, 60)], vector.Points);
        Assert.True(workspace.CanUndo);

        workspace.Undo();

        Assert.Equal(originalLayerCount, workspace.Layers.Count);
        Assert.Empty(workspace.Document.Paths);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void LoadAsOneImage_UsesOnlyFlattenedComposite()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        var result = workspace.ImportPsd(CreateImport(), PsdImportMode.LoadAsOneImage);

        Assert.True(result.IsSuccess, result.Message);
        var imageLayer = Assert.Single(workspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage);
        Assert.Equal("composite", imageLayer.ReferenceImage!.DataBase64);
        Assert.Empty(workspace.Document.Paths);
    }

    [Fact]
    public void DroppedPsd_OffsetsRasterAndVectorContentByInsertionPoint()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        var result = workspace.ImportPsd(
            CreateImport(),
            PsdImportMode.LoadAsIs,
            new Editor2DPoint(50, -25));

        Assert.True(result.IsSuccess, result.Message);
        var image = Assert.Single(workspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage).ReferenceImage!;
        Assert.Equal(62, image.X);
        Assert.Equal(-31, image.Y);
        var vector = Assert.Single(workspace.Document.Paths);
        Assert.Equal([new Editor2DPoint(50, -25), new Editor2DPoint(170, 35)], vector.Points);
    }

    [Fact]
    public void DroppedFlattenedPsd_CentersCompositeAtInsertionPoint()
    {
        var workspace = new Editor2DWorkspaceViewModel();

        var result = workspace.ImportPsd(
            CreateImport(),
            PsdImportMode.LoadAsOneImage,
            new Editor2DPoint(-30, 15));

        Assert.True(result.IsSuccess, result.Message);
        var image = Assert.Single(workspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage).ReferenceImage!;
        Assert.Equal(-30, image.X);
        Assert.Equal(15, image.Y);
    }

    [Theory]
    [InlineData(PsdImportMode.AutoVectorize, 2, 1)]
    [InlineData(PsdImportMode.MergeAndVectorize, 1, 1)]
    public void VectorizeModes_TraceExpectedRasterContent(
        PsdImportMode mode,
        int expectedPathCount,
        int expectedReferenceCount)
    {
        var trace = new RecordingTraceService([
            [new(0, 0), new(20, 0), new(20, 10), new(0, 10)],
        ]);
        var workspace = new Editor2DWorkspaceViewModel(trace);

        var result = workspace.ImportPsd(CreateImport(), mode);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(expectedPathCount, workspace.Document.Paths.Count);
        Assert.Equal(expectedReferenceCount, workspace.Layers.Count(layer => layer.Kind == Editor2DLayerKind.ReferenceImage));
        Assert.Equal(1, trace.CallCount);
    }

    [Fact]
    public void VectorizeWithoutTracer_LeavesWorkspaceUntouched()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var original = workspace.State;

        var result = workspace.ImportPsd(CreateImport(), PsdImportMode.AutoVectorize);

        Assert.False(result.IsSuccess);
        Assert.Same(original, workspace.State);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public async Task ActivatedPsd_ParsesPromptsImportsAndActivatesTwoD()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-{Guid.NewGuid():N}.psd");
        await File.WriteAllTextAsync(path, "fake");
        var parser = new RecordingPsdService(CreateImport(path));
        var prompt = new FixedModePrompt(PsdImportMode.LoadAsOneImage);
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests(
            psdImportService: parser,
            psdImportModePromptService: prompt);
        try
        {
            await editor.OpenActivatedFilesAsync([path]);

            Assert.Equal(Path.GetFullPath(path), parser.SourcePath);
            Assert.Equal(PsdImportMode.LoadAsOneImage, prompt.SelectedMode);
            Assert.Equal(EditorMode.TwoD, editor.ActiveEditorMode);
            Assert.Single(editor.TwoDWorkspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage);
            Assert.Contains("Imported 1 image file", editor.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DroppedPsd_CarriesInsertionPointThroughDeferredPrompt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-drop-{Guid.NewGuid():N}.psd");
        await File.WriteAllTextAsync(path, "fake");
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests(
            psdImportService: new RecordingPsdService(CreateImport(path)),
            psdImportModePromptService: new FixedModePrompt(PsdImportMode.LoadAsOneImage));
        try
        {
            await editor.OpenDroppedFilesAsync([path], new Editor2DPoint(80, -40));

            var image = Assert.Single(editor.TwoDReferenceImages);
            Assert.Equal(80, image.X);
            Assert.Equal(-40, image.Y);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DesktopRoutingAndPackagedRuntime_IncludePsdImport()
    {
        var root = FindRepositoryRoot();
        var picker = File.ReadAllText(Path.Combine(root, "src", "Pathstitch.App", "Services", "ProjectFileDialogService.cs"));
        var build = File.ReadAllText(Path.Combine(root, "geometry-worker", "build-runtime.ps1"));
        var wheelLock = File.ReadAllText(Path.Combine(root, "geometry-worker", "python-wheel-lock.json"));
        var spec = File.ReadAllText(Path.Combine(root, "geometry-worker", "runtime-spec.json"));

        Assert.Contains("*.psd", picker, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("psd_tools", build, StringComparison.Ordinal);
        Assert.Contains("psd-tools", wheelLock, StringComparison.Ordinal);
        Assert.Contains("psd-layer-import", spec, StringComparison.Ordinal);
    }

    private static PsdImportData CreateImport(string sourcePath = "sample.psd") => new(
        sourcePath,
        400,
        200,
        "composite",
        400,
        200,
        [new("Ink", "raster", 20, 10, 20, -10, false)],
        [new("Cut", [new([new(0, 0), new(200, 100)], false)], true)]);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PathstitchCross.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class RecordingTraceService(IReadOnlyList<IReadOnlyList<Editor2DPoint>> contours)
        : IReferenceImageTraceService
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<IReadOnlyList<Editor2DPoint>> TraceContours(
            string imageDataBase64,
            Editor2DReferenceImageTraceOptions options)
        {
            CallCount++;
            return contours;
        }
    }

    private sealed class RecordingPsdService(PsdImportData import) : IPsdImportService
    {
        public string? SourcePath { get; private set; }

        public Task<PsdImportData> ParseAsync(string sourcePath, CancellationToken cancellationToken = default)
        {
            SourcePath = sourcePath;
            return Task.FromResult(import with { SourcePath = sourcePath });
        }
    }

    private sealed class FixedModePrompt(PsdImportMode mode) : IPsdImportModePromptService
    {
        public PsdImportMode? SelectedMode { get; private set; }

        public Task<PsdImportMode?> PromptAsync(PsdImportData import, CancellationToken cancellationToken = default)
        {
            SelectedMode = mode;
            return Task.FromResult<PsdImportMode?>(mode);
        }
    }
}
