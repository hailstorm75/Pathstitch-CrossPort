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
        var raster = Assert.Single(workspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage);
        Assert.Equal(1.0, raster.ReferenceImage!.Opacity);
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
        Assert.Equal(1.0, imageLayer.ReferenceImage.Opacity);
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

    [Fact]
    public void NonDroppedPsd_FitsAndCentersInCurrentViewport()
    {
        var workspace = new Editor2DWorkspaceViewModel();
        var viewport = new Editor2DViewportPlacement(
            PixelWidth: 2000,
            PixelHeight: 1000,
            Zoom: 1,
            OffsetX: 100,
            OffsetY: -50);

        var result = workspace.ImportPsd(
            CreateImport(),
            PsdImportMode.LoadAsIs,
            viewport: viewport);

        Assert.True(result.IsSuccess, result.Message);
        var image = Assert.Single(workspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage).ReferenceImage!;
        Assert.Equal(-20, image.X);
        Assert.Equal(-90, image.Y);
        Assert.Equal(80, image.Width);
        Assert.Equal(40, image.Height);
        Assert.Equal(4, image.CalibrationUnitsPerPixel);
        var vector = Assert.Single(workspace.Document.Paths);
        Assert.Equal([new Editor2DPoint(-100, -50), new Editor2DPoint(700, 350)], vector.Points);
    }

    [Theory]
    [InlineData(PsdImportMode.AutoVectorize, 1, 2)]
    [InlineData(PsdImportMode.MergeAndVectorize, 0, 1)]
    public async Task VectorizeModes_StagePreviewAndUseSeparateCommitUndo(
        PsdImportMode mode,
        int importedPathCount,
        int committedPathCount)
    {
        var trace = new RecordingTraceService([
            [new(0, 0), new(20, 0), new(20, 10), new(0, 10)],
        ]);
        var workspace = new Editor2DWorkspaceViewModel(trace);
        var originalLayerCount = workspace.Layers.Count;

        var result = workspace.ImportPsd(CreateImport(), mode);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(importedPathCount, workspace.Document.Paths.Count);
        Assert.True(workspace.HasReferenceImageTraceSession);
        Assert.True(workspace.IsReferenceImageTraceBatch);
        Assert.Equal(1, workspace.ReferenceImageTraceBatchCount);
        Assert.Single(workspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage);
        Assert.True(await workspace.WaitForReferenceImageTracePreviewAsync());
        Assert.Equal(1, trace.CallCount);

        Assert.NotNull(workspace.CommitReferenceImageTrace());

        Assert.Equal(committedPathCount, workspace.Document.Paths.Count);
        Assert.False(workspace.HasReferenceImageTraceSession);
        Assert.False(Assert.Single(workspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage).IsVisible);

        Assert.True(workspace.Undo());
        Assert.Equal(importedPathCount, workspace.Document.Paths.Count);
        Assert.Equal(originalLayerCount + 2, workspace.Layers.Count);
        Assert.True(Assert.Single(workspace.Layers, layer => layer.Kind == Editor2DLayerKind.ReferenceImage).IsVisible);

        Assert.True(workspace.Undo());
        Assert.Empty(workspace.Document.Paths);
        Assert.Equal(originalLayerCount, workspace.Layers.Count);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public async Task AutoVectorize_SharedOptionsCommitEveryRasterAndCancelKeepsImport()
    {
        var trace = new RecordingTraceService([
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
        ]);
        var workspace = new Editor2DWorkspaceViewModel(trace);
        var import = CreateImport() with
        {
            RasterLayers = [
                new("Ink", "ink", 10, 10, -20, 0, true),
                new("Shade", "shade", 10, 10, 20, 0, true),
            ],
            VectorLayers = [],
        };

        var result = workspace.ImportPsd(import, PsdImportMode.AutoVectorize);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, workspace.ReferenceImageTraceBatchCount);
        Assert.True(await workspace.WaitForReferenceImageTracePreviewAsync());
        var activeId = Assert.IsType<string>(workspace.ReferenceImageTraceLayerId);
        Assert.True(workspace.SetReferenceImageTraceThreshold(activeId, 0.72));
        Assert.True(await workspace.WaitForReferenceImageTracePreviewAsync());
        Assert.All(
            workspace.Layers.Where(layer => layer.IsReferenceImage),
            layer => Assert.Equal(0.72, layer.ReferenceImage!.TraceThreshold));

        workspace.CancelReferenceImageTrace();

        Assert.False(workspace.HasReferenceImageTraceSession);
        Assert.Empty(workspace.Document.Paths);
        Assert.Equal(2, workspace.Layers.Count(layer => layer.IsReferenceImage));
        Assert.All(workspace.Layers.Where(layer => layer.IsReferenceImage), layer => Assert.True(layer.IsVisible));
        Assert.True(workspace.CanUndo);

        Assert.True(workspace.BeginReferenceImageTraceBatch(
            workspace.Layers.Where(layer => layer.IsReferenceImage).Select(layer => layer.Id).ToArray()));
        Assert.True(await workspace.WaitForReferenceImageTracePreviewAsync());
        Assert.Equal(2, workspace.ReferenceImageTracePreviewPaths.Count);
        var callsBeforeCommit = trace.CallCount;
        Assert.NotNull(workspace.CommitReferenceImageTrace());
        Assert.Equal(callsBeforeCommit, trace.CallCount);

        Assert.Equal(2, workspace.Document.Paths.Count);
        Assert.Equal(3, workspace.Layers.Count(layer => layer.Kind == Editor2DLayerKind.Geometry));
        Assert.All(workspace.Layers.Where(layer => layer.IsReferenceImage), layer => Assert.False(layer.IsVisible));
        Assert.Contains(trace.Options, options => options.Threshold == 0.72);
        Assert.Contains("ink", trace.ImageData);
        Assert.Contains("shade", trace.ImageData);
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
        editor.UpdateTwoDViewportSize(2000, 1000);
        editor.TwoDViewportZoom = 1;
        editor.TwoDViewportOffsetX = 100;
        editor.TwoDViewportOffsetY = -50;
        try
        {
            await editor.OpenActivatedFilesAsync([path]);

            Assert.Equal(Path.GetFullPath(path), parser.SourcePath);
            Assert.Equal(PsdImportMode.LoadAsOneImage, prompt.SelectedMode);
            Assert.Equal(EditorMode.TwoD, editor.ActiveEditorMode);
            var image = Assert.Single(
                editor.TwoDWorkspace.Layers,
                layer => layer.Kind == Editor2DLayerKind.ReferenceImage).ReferenceImage!;
            Assert.Equal(-100, image.X);
            Assert.Equal(-50, image.Y);
            Assert.Equal(1600, image.Width);
            Assert.Equal(800, image.Height);
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
    public void DesktopRoutingAndPackagedRuntime_IncludePsdImportAndFixtureGate()
    {
        var root = FindRepositoryRoot();
        var picker = File.ReadAllText(Path.Combine(root, "src", "Pathstitch.App", "Services", "ProjectFileDialogService.cs"));
        var build = File.ReadAllText(Path.Combine(root, "geometry-worker", "build-runtime.ps1"));
        var wheelLock = File.ReadAllText(Path.Combine(root, "geometry-worker", "python-wheel-lock.json"));
        var spec = File.ReadAllText(Path.Combine(root, "geometry-worker", "runtime-spec.json"));
        var fixtureRoot = Path.Combine(
            root,
            "tests",
            "Pathstitch.App.Tests",
            "Fixtures");
        var fixture = File.ReadAllText(Path.Combine(fixtureRoot, "psd-import-parity-cases.json"));
        var layeredPsd = File.ReadAllBytes(Path.Combine(fixtureRoot, "psd", "layered-hidden-group.psd"));
        var flattenedPsd = File.ReadAllBytes(Path.Combine(fixtureRoot, "psd", "flattened-2x1.psd"));
        var malformedPsd = File.ReadAllBytes(Path.Combine(fixtureRoot, "psd", "malformed-truncated.psd"));

        Assert.Contains("*.psd", picker, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("psd_tools", build, StringComparison.Ordinal);
        Assert.Contains("pathstitch_core.test_psd_fixture_matrix", build, StringComparison.Ordinal);
        Assert.Contains("PATHSTITCH_PSD_FIXTURE_MATRIX", build, StringComparison.Ordinal);
        Assert.Contains("psd-tools", wheelLock, StringComparison.Ordinal);
        Assert.Contains("psd-layer-import", spec, StringComparison.Ordinal);
        Assert.Contains("nested-raster-vector-visibility", fixture, StringComparison.Ordinal);
        Assert.Contains("flattened-fallback", fixture, StringComparison.Ordinal);
        Assert.Equal("8BPS", System.Text.Encoding.ASCII.GetString(layeredPsd, 0, 4));
        Assert.Equal("8BPS", System.Text.Encoding.ASCII.GetString(flattenedPsd, 0, 4));
        Assert.Equal("8BPS", System.Text.Encoding.ASCII.GetString(malformedPsd));
    }

    private static PsdImportData CreateImport(string sourcePath = "sample.psd") => new(
        sourcePath,
        400,
        200,
        "composite",
        400,
        200,
        [new("Ink", "raster", 20, 10, 20, -10, true)],
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
        public List<string> ImageData { get; } = [];
        public List<Editor2DReferenceImageTraceOptions> Options { get; } = [];

        public IReadOnlyList<IReadOnlyList<Editor2DPoint>> TraceContours(
            string imageDataBase64,
            Editor2DReferenceImageTraceOptions options)
        {
            CallCount++;
            ImageData.Add(imageDataBase64);
            Options.Add(options);
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
