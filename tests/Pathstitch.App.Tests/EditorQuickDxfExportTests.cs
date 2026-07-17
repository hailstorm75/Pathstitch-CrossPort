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
        Assert.Empty(viewModel.ActivityLog);
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
        var activity = Assert.Single(viewModel.ActivityLog);
        Assert.Equal("Export DXF", activity.Action);
        Assert.Equal(outputPath, activity.Details);
        Assert.True(viewModel.IsDirty);
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
    public async Task Export_SelectedOnlyWithEmptySelectionWritesEmptyGeometry()
    {
        var output = new RecordingExportOutputPreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
            new RecordingFileDialogService("empty-selection.dxf"),
            output);
        viewModel.TwoDDocument = CreateDocument();
        viewModel.TwoDExportSelectedOnly = true;

        await viewModel.ExportTwoDDxfAsync();

        var exported = Assert.IsType<Editor2DExportDocument>(output.SavedExportDocument);
        Assert.Empty(exported.Geometry.Paths);
        Assert.Empty(exported.PathMetadata);
    }

    [Fact]
    public async Task ExportDocument_CarriesFlatLayerMetadataAndKeepsHiddenGeometry()
    {
        var document = new Editor2DPreviewDocument(
            [
                new Editor2DPreviewPath("cut", "LINE", [new(0, 0), new(10, 0)], false),
                new Editor2DPreviewPath("score", "LINE", [new(0, 5), new(10, 5)], false),
                new Editor2DPreviewPath("guide", "LINE", [new(0, 10), new(10, 10)], false, IsConstruction: true),
            ],
            new Editor2DBounds(0, 0, 10, 10),
            new Dictionary<string, int> { ["LINE"] = 3 },
            []);
        var output = new RecordingExportOutputPreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
            new RecordingFileDialogService("layers.dxf"),
            output);
        viewModel.TwoDDocument = document;
        var cutLayer = Assert.Single(viewModel.TwoDLayers);
        viewModel.RenameTwoDLayer(cutLayer.Id, "Cut");
        viewModel.SetTwoDLayerColor(cutLayer.Id, "#FF0000");
        viewModel.CreateTwoDLayer();
        var scoreLayer = viewModel.TwoDLayers.Single(layer => layer.Id != cutLayer.Id);
        viewModel.RenameTwoDLayer(scoreLayer.Id, "Score");
        viewModel.SetTwoDLayerColor(scoreLayer.Id, "#0000FF");
        viewModel.TwoDSelectedPathIds = ["score"];
        viewModel.AssignTwoDSelectionToLayer(scoreLayer.Id);
        viewModel.ToggleTwoDLayerVisibility(scoreLayer.Id);

        await viewModel.ExportTwoDDxfAsync();

        var exported = Assert.IsType<Editor2DExportDocument>(output.SavedExportDocument);
        Assert.Equal(["cut", "score", "guide"], exported.Geometry.Paths.Select(path => path.Id).ToArray());
        Assert.Equal(new Editor2DExportPathMetadata("Cut", "#FF0000", cutLayer.Order), exported.PathMetadata["cut"]);
        Assert.Equal(new Editor2DExportPathMetadata("Score", "#0000FF", scoreLayer.Order), exported.PathMetadata["score"]);
        Assert.Equal(new Editor2DExportPathMetadata("CONSTRUCTION", "#808080"), exported.PathMetadata["guide"]);
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
        Assert.Empty(viewModel.ActivityLog);
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
    public async Task ExportDocument_AssignsMeasurementConstructionMetadata()
    {
        var output = new RecordingExportOutputPreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
            new RecordingFileDialogService("measurements.dxf"),
            output);
        viewModel.TwoDDocument = CreateDocument();
        viewModel.TwoDMeasurements = [new Editor2DMeasurement("measure-1", new(10, 20), new(30, 40))];
        viewModel.TwoDExportMeasurementLines = true;

        await viewModel.ExportTwoDDxfAsync();

        var exported = Assert.IsType<Editor2DExportDocument>(output.SavedExportDocument);
        var measurement = Assert.Single(exported.Geometry.Paths, path => path.Id == "measurement-export-measure-1");
        Assert.True(measurement.IsConstruction);
        Assert.Equal(
            new Editor2DExportPathMetadata("CONSTRUCTION", "#808080"),
            exported.PathMetadata[measurement.Id]);
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
    public async Task DxfWriter_ExportsConstructionPathsOnDashedGrayConstructionLayer()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-construction-{Guid.NewGuid():N}.dxf");
        try
        {
            var document = new Editor2DPreviewDocument(
                [
                    new Editor2DPreviewPath("normal", "LINE", [new(0, 0), new(10, 0)], false),
                    new Editor2DPreviewPath(
                        "construction",
                        "LINE",
                        [new(0, 2), new(10, 2)],
                        false,
                        IsConstruction: true),
                    new Editor2DPreviewPath(
                        "construction-circle",
                        "CIRCLE",
                        [],
                        true,
                        Center: new(2, 4),
                        Radius: 1,
                        IsConstruction: true),
                    new Editor2DPreviewPath(
                        "construction-arc",
                        "ARC",
                        [],
                        false,
                        Center: new(5, 4),
                        Radius: 1,
                        StartAngleDegrees: 0,
                        EndAngleDegrees: 90,
                        IsConstruction: true),
                    new Editor2DPreviewPath(
                        "construction-text",
                        "TEXT",
                        [],
                        false,
                        Start: new(7, 4),
                        Text: "reference",
                        TextHeight: 1,
                        IsConstruction: true),
                ],
                new Editor2DBounds(0, 0, 10, 5),
                new Dictionary<string, int> { ["LINE"] = 2, ["CIRCLE"] = 1, ["ARC"] = 1, ["TEXT"] = 1 },
                []);

            await new DxfOutputPreviewService().SavePreviewDocumentAsync(document, outputPath);

            var dxf = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("0\nLTYPE\n2\nDASHED\n", dxf, StringComparison.Ordinal);
            Assert.Contains("3\nDashed __ __ __\n72\n65\n73\n2\n40\n0.75\n49\n0.5\n74\n0\n49\n-0.25\n", dxf, StringComparison.Ordinal);
            Assert.Contains("0\nLAYER\n2\nCONSTRUCTION\n70\n0\n62\n8\n6\nDASHED\n", dxf, StringComparison.Ordinal);
            Assert.Contains("0\nLWPOLYLINE\n8\nCONSTRUCTION\n6\nDASHED\n62\n8\n", dxf, StringComparison.Ordinal);
            Assert.Contains("0\nLWPOLYLINE\n8\nEDITED_OUTPUT\n90\n2\n", dxf, StringComparison.Ordinal);
            Assert.Equal(4, dxf.Split("8\nCONSTRUCTION\n6\nDASHED\n62\n8\n", StringSplitOptions.None).Length - 1);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task DxfWriter_NormalDocumentDoesNotAddConstructionTablesOrEntityStyle()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-normal-{Guid.NewGuid():N}.dxf");
        try
        {
            await new DxfOutputPreviewService().SavePreviewDocumentAsync(CreateDocument(), outputPath);

            var dxf = await File.ReadAllTextAsync(outputPath);
            Assert.DoesNotContain("\nLTYPE\n", dxf, StringComparison.Ordinal);
            Assert.DoesNotContain("\nCONSTRUCTION\n", dxf, StringComparison.Ordinal);
            Assert.DoesNotContain("\nDASHED\n", dxf, StringComparison.Ordinal);
            Assert.DoesNotContain("\n62\n8\n", dxf, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task DxfWriter_ExportDocumentWritesFlatLayerTableColorsAndPathLayerCodes()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-layered-{Guid.NewGuid():N}.dxf");
        try
        {
            var geometry = new Editor2DPreviewDocument(
                [
                    new Editor2DPreviewPath("cut", "LINE", [new(0, 0), new(10, 0)], false),
                    new Editor2DPreviewPath("score", "LINE", [new(0, 5), new(10, 5)], false),
                    new Editor2DPreviewPath("guide", "LINE", [new(0, 10), new(10, 10)], false, IsConstruction: true),
                ],
                new Editor2DBounds(0, 0, 10, 10),
                new Dictionary<string, int> { ["LINE"] = 3 },
                []);
            var export = new Editor2DExportDocument(
                geometry,
                new Dictionary<string, Editor2DExportPathMetadata>
                {
                    ["cut"] = new("Cut", "#FF0000", 0),
                    ["score"] = new("Score", "#0000FF", 1),
                    ["guide"] = new("ignored", "#00FF00", 2),
                });

            await new DxfOutputPreviewService().SaveExportDocumentAsync(
                export, outputPath, Editor2DExportOptions.Defaults);

            var dxf = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("0\nLAYER\n2\nCut\n70\n0\n62\n7\n420\n16711680\n6\nCONTINUOUS\n", dxf, StringComparison.Ordinal);
            Assert.Contains("0\nLAYER\n2\nScore\n70\n0\n62\n7\n420\n255\n6\nCONTINUOUS\n", dxf, StringComparison.Ordinal);
            Assert.Contains("0\nLAYER\n2\nCONSTRUCTION\n70\n0\n62\n8\n6\nDASHED\n", dxf, StringComparison.Ordinal);
            Assert.Equal(1, dxf.Split("8\nCut\n", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, dxf.Split("8\nScore\n", StringSplitOptions.None).Length - 1);
            Assert.Contains("8\nCONSTRUCTION\n6\nDASHED\n62\n8\n", dxf, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task DxfWriter_SuffixesSanitizedLayerNameCollisions()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-layer-collision-{Guid.NewGuid():N}.dxf");
        try
        {
            var geometry = new Editor2DPreviewDocument(
                [
                    new Editor2DPreviewPath("first", "LINE", [new(0, 0), new(1, 0)], false),
                    new Editor2DPreviewPath("second", "LINE", [new(0, 1), new(1, 1)], false),
                ],
                new Editor2DBounds(0, 0, 1, 1),
                new Dictionary<string, int> { ["LINE"] = 2 },
                []);
            var export = new Editor2DExportDocument(
                geometry,
                new Dictionary<string, Editor2DExportPathMetadata>
                {
                    ["first"] = new("Cut:Layer", "#FF0000", 0),
                    ["second"] = new("Cut?Layer", "#0000FF", 1),
                });

            await new DxfOutputPreviewService().SaveExportDocumentAsync(
                export, outputPath, Editor2DExportOptions.Defaults);

            var dxf = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("0\nLAYER\n2\nCut_Layer\n", dxf, StringComparison.Ordinal);
            Assert.Contains("0\nLAYER\n2\nCut_Layer_1\n", dxf, StringComparison.Ordinal);
            Assert.Equal(1, dxf.Split("8\nCut_Layer\n", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, dxf.Split("8\nCut_Layer_1\n", StringSplitOptions.None).Length - 1);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SvgWriter_ExportDocumentUsesLayerGroupsColorsAndStableSanitizedIds()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pathstitch-layered-{Guid.NewGuid():N}.svg");
        try
        {
            var geometry = new Editor2DPreviewDocument(
                [
                    new Editor2DPreviewPath("first", "LINE", [new(0, 0), new(1, 0)], false),
                    new Editor2DPreviewPath("second", "LINE", [new(0, 1), new(1, 1)], false),
                    new Editor2DPreviewPath("guide", "LINE", [new(0, 2), new(1, 2)], false, IsConstruction: true),
                ],
                new Editor2DBounds(0, 0, 1, 2),
                new Dictionary<string, int> { ["LINE"] = 3 },
                []);
            var export = new Editor2DExportDocument(
                geometry,
                new Dictionary<string, Editor2DExportPathMetadata>
                {
                    ["first"] = new("Cut Layer", "#AA0000", 0),
                    ["second"] = new("Cut:Layer", "#0000BB", 1),
                    ["guide"] = new("ignored", "#00FF00", 2),
                });

            await new DxfOutputPreviewService().SaveExportDocumentAsync(
                export, outputPath, new Editor2DExportOptions(2, 1.25));

            var svg = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("id=\"layer_Cut_Layer\" data-layer-name=\"Cut Layer\" stroke=\"#AA0000\"", svg, StringComparison.Ordinal);
            Assert.Contains("id=\"layer_Cut_Layer_1\" data-layer-name=\"Cut:Layer\" stroke=\"#0000BB\"", svg, StringComparison.Ordinal);
            Assert.Contains("id=\"layer_CONSTRUCTION\" data-layer-name=\"CONSTRUCTION\" stroke=\"#808080\"", svg, StringComparison.Ordinal);
            Assert.Contains("stroke-width=\"1.25\"", svg, StringComparison.Ordinal);
            Assert.Contains("stroke-dasharray=\"6 4\"", svg, StringComparison.Ordinal);
            Assert.True(svg.IndexOf("points=\"0,0 1,0\"", StringComparison.Ordinal)
                < svg.IndexOf("points=\"0,1 1,1\"", StringComparison.Ordinal));
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

            using (var bitmap = SKBitmap.Decode(outputPath))
            {
                Assert.Equal(256, bitmap.Width);
                Assert.Equal(128, bitmap.Height);
                Assert.Equal(0, bitmap.GetPixel(0, 127).Alpha);
            }
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
                CreatePdfDocument(), outputPath, Editor2DExportOptions.Defaults);

            var pdf = await File.ReadAllTextAsync(outputPath);
            Assert.StartsWith("%PDF-1.4", pdf, StringComparison.Ordinal);
            Assert.Contains("/Type /Page", pdf, StringComparison.Ordinal);
            Assert.Contains(" m\n", pdf, StringComparison.Ordinal);
            Assert.Contains(" l\n", pdf, StringComparison.Ordinal);
            Assert.Contains(" c\n", pdf, StringComparison.Ordinal);
            Assert.Contains("/F1 5 0 R", pdf, StringComparison.Ordinal);
            Assert.Contains("BT /F1", pdf, StringComparison.Ordinal);
            Assert.Contains("hello \\(pdf\\)", pdf, StringComparison.Ordinal);
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

    private static Editor2DPreviewDocument CreatePdfDocument()
        => new(
            [
                new Editor2DPreviewPath("line-1", "LINE", [new(1, 2), new(3, 4)], false),
                new Editor2DPreviewPath("circle-1", "CIRCLE", [], true, Center: new(2, 3), Radius: 0.5),
                new Editor2DPreviewPath("text-1", "TEXT", [], false, Start: new(1.2, 2.2), Text: "hello (pdf)", TextHeight: 2),
            ],
            new Editor2DBounds(1, 2, 3, 4),
            new Dictionary<string, int> { ["LINE"] = 1, ["CIRCLE"] = 1 },
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

    private sealed class RecordingExportOutputPreviewService : IEditorOutputPreviewService
    {
        public Editor2DExportDocument? SavedExportDocument { get; private set; }

        public Task SaveExportDocumentAsync(
            Editor2DExportDocument document,
            string outputPath,
            Editor2DExportOptions options,
            CancellationToken cancellationToken = default)
        {
            SavedExportDocument = document;
            return Task.CompletedTask;
        }

        public Task SavePreviewDocumentAsync(
            Editor2DPreviewDocument document,
            string outputPath,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<Editor2DPreviewDocument?>(null);

        public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<EditorGeneratedOutputSummary?>(null);
    }
}
