using Domain.App.Models;
using Domain.App.Services;
using Pathstitch.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Tests;

public sealed class EditorQuickDxfExportTests
{
    [Fact]
    public async Task Export_IsDisabledAndDoesNotOpenPickerWithoutDocument()
    {
        var dialogs = new RecordingFileDialogService("ignored.dxf");
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(dialogs);

        Assert.False(viewModel.CanExportTwoDDxf);

        await viewModel.ExportTwoDDxfAsync();

        Assert.Equal(0, dialogs.ExportPickerCallCount);
    }

    [Fact]
    public async Task Export_CancelDoesNotWriteDocument()
    {
        var dialogs = new RecordingFileDialogService(null);
        var output = new RecordingOutputPreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(dialogs, output);
        viewModel.TwoDDocument = CreateDocument();

        Assert.True(viewModel.CanExportTwoDDxf);

        await viewModel.ExportTwoDDxfAsync();

        Assert.Null(output.SavedDocument);
    }

    [Fact]
    public async Task Export_WritesExactCurrentDocumentToPickedPath()
    {
        const string outputPath = "export.dxf";
        var document = CreateDocument();
        var output = new RecordingOutputPreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
            new RecordingFileDialogService(outputPath),
            output);
        viewModel.TwoDDocument = document;

        await viewModel.ExportTwoDDxfAsync();

        Assert.Same(document, output.SavedDocument);
        Assert.Equal(outputPath, output.SavedPath);
    }

    [Fact]
    public async Task Export_SelectedOnlyWritesOnlySelectedPaths()
    {
        const string outputPath = "selected.dxf";
        var document = new Editor2DPreviewDocument(
            [
                new Editor2DPreviewPath("selected", "LINE", [new(1, 2), new(3, 4)], false),
                new Editor2DPreviewPath("other", "LINE", [new(10, 20), new(30, 40)], false),
            ],
            new Editor2DBounds(1, 2, 30, 40),
            new Dictionary<string, int> { ["LINE"] = 2 },
            []);
        var output = new RecordingOutputPreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
            new RecordingFileDialogService(outputPath),
            output);
        viewModel.TwoDDocument = document;
        viewModel.TwoDSelectedPathIds = ["selected"];
        viewModel.TwoDExportSelectedOnly = true;

        await viewModel.ExportTwoDDxfAsync();

        Assert.NotNull(output.SavedDocument);
        var path = Assert.Single(output.SavedDocument!.Paths);
        Assert.Equal("selected", path.Id);
        Assert.Equal(1, output.SavedDocument.Bounds.MinX);
        Assert.Equal(4, output.SavedDocument.Bounds.MaxY);
    }

    [Fact]
    public async Task Export_WriteFailureSetsErrorMessage()
    {
        var output = new RecordingOutputPreviewService(new IOException("disk full"));
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
            new RecordingFileDialogService("export.dxf"),
            output);
        viewModel.TwoDDocument = CreateDocument();

        await viewModel.ExportTwoDDxfAsync();

        Assert.Equal("Could not export DXF: disk full", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Export_RealWriterRoundTripsGeometry()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-export-{Guid.NewGuid():N}.dxf");
        try
        {
            var document = CreateDocument();
            var service = new DxfOutputPreviewService();

            await service.SavePreviewDocumentAsync(document, outputPath);
            var loaded = await service.LoadPreviewDocumentAsync(outputPath);

            Assert.NotNull(loaded);
            Assert.Single(loaded.Paths);
            Assert.Equal("LWPOLYLINE", loaded.Paths[0].EntityType);
            Assert.Equal(document.Paths[0].Points, loaded.Paths[0].Points);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SvgWriter_RoundTripsGeometryThroughExistingPreviewPipeline()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-export-{Guid.NewGuid():N}.svg");
        try
        {
            var document = CreateDocument();
            var service = new DxfOutputPreviewService();

            await service.SavePreviewDocumentAsync(document, outputPath);
            var loaded = await service.LoadPreviewDocumentAsync(outputPath);

            Assert.NotNull(loaded);
            Assert.Single(loaded.Paths);
            Assert.Equal("POLYLINE", loaded.Paths[0].EntityType);
            Assert.Equal(document.Paths[0].Points, loaded.Paths[0].Points);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SvgWriter_AppliesPrecisionAndStrokeWidthOptions()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-export-options-{Guid.NewGuid():N}.svg");
        try
        {
            var document = new Editor2DPreviewDocument(
                [new Editor2DPreviewPath("line", "LINE", [new(1.2345, 2.3456), new(3.4567, 4.5678)], false)],
                new Editor2DBounds(1.2345, 2.3456, 3.4567, 4.5678),
                new Dictionary<string, int> { ["LINE"] = 1 },
                []);
            await new DxfOutputPreviewService().SavePreviewDocumentAsync(
                document,
                outputPath,
                new Editor2DExportOptions(2, 1.25));

            var svg = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("1.23,2.35", svg, StringComparison.Ordinal);
            Assert.Contains("stroke-width=\"1.25\"", svg, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Export_IncludesMeasurementLinesWhenRequested()
    {
        var output = new RecordingOutputPreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
            new RecordingFileDialogService("measurements.dxf"),
            output);
        viewModel.TwoDDocument = CreateDocument();
        viewModel.TwoDMeasurements = [new Editor2DMeasurement("measure-1", new(10, 20), new(30, 40))];
        viewModel.TwoDExportMeasurementLines = true;

        await viewModel.ExportTwoDDxfAsync();

        Assert.NotNull(output.SavedDocument);
        Assert.Contains(output.SavedDocument!.Paths, path => path.Id == "measurement-export-measure-1");
    }

    [Fact]
    public async Task DxfWriter_UsesRequestedReleaseVersion()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-dxf-version-{Guid.NewGuid():N}.dxf");
        try
        {
            await new DxfOutputPreviewService().SavePreviewDocumentAsync(
                CreateDocument(),
                outputPath,
                new Editor2DExportOptions(DxfVersion: "R2018"));

            var dxf = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("$ACADVER", dxf, StringComparison.Ordinal);
            Assert.Contains("AC1032", dxf, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task PngWriter_UsesRequestedLongestEdgeAndTransparency()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-png-export-{Guid.NewGuid():N}.png");
        try
        {
            var document = new Editor2DPreviewDocument(
                [new Editor2DPreviewPath("line", "LINE", [new(0, 0), new(10, 5)], false)],
                new Editor2DBounds(0, 0, 10, 5),
                new Dictionary<string, int> { ["LINE"] = 1 },
                []);
            await new DxfOutputPreviewService().SavePreviewDocumentAsync(
                document,
                outputPath,
                new Editor2DExportOptions(PngLongestEdge: 256, PngTransparent: true));

            using var bitmap = SKBitmap.Decode(outputPath);
            Assert.Equal(256, bitmap.Width);
            Assert.Equal(128, bitmap.Height);
            Assert.Equal(0, bitmap.GetPixel(0, 127).Alpha);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task PdfWriter_ProducesSinglePageVectorDocument()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-pdf-export-{Guid.NewGuid():N}.pdf");
        try
        {
            await new DxfOutputPreviewService().SavePreviewDocumentAsync(
                CreateDocument(), outputPath, Editor2DExportOptions.Defaults);

            var pdf = await File.ReadAllTextAsync(outputPath);
            Assert.StartsWith("%PDF-1.4", pdf, StringComparison.Ordinal);
            Assert.Contains("/Type /Page", pdf, StringComparison.Ordinal);
            Assert.Contains(" m\n", pdf, StringComparison.Ordinal);
            Assert.Contains(" l\n", pdf, StringComparison.Ordinal);
            Assert.Contains("xref", pdf, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static Editor2DPreviewDocument CreateDocument()
        => new(
            [new Editor2DPreviewPath("line-1", "LINE", [new(1, 2), new(3, 4)], false)],
            new Editor2DBounds(1, 2, 3, 4),
            new Dictionary<string, int> { ["LINE"] = 1 },
            []);

    private sealed class RecordingFileDialogService(string? exportPath) : IProjectFileDialogService
    {
        public int ExportPickerCallCount { get; private set; }

        public Task<string?> PickDxfExportFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
        {
            ExportPickerCallCount++;
            return Task.FromResult(exportPath);
        }

        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class RecordingOutputPreviewService(Exception? saveException = null) : IEditorOutputPreviewService
    {
        public Editor2DPreviewDocument? SavedDocument { get; private set; }
        public string? SavedPath { get; private set; }

        public Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default)
        {
            if (saveException is not null)
                throw saveException;
            SavedDocument = document;
            SavedPath = outputPath;
            return Task.CompletedTask;
        }

        public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<Editor2DPreviewDocument?>(null);

        public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<EditorGeneratedOutputSummary?>(null);
    }
}
