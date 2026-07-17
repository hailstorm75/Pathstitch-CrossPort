using Domain.App.Models;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private bool _twoDExportSelectedOnly;
    private bool _twoDExportMeasurementLines;
    private string _twoDSvgPrecisionText = "3";
    private string _twoDSvgStrokeWidthText = "0.5";
    private string _twoDDxfVersion = "R2010";
    private string _twoDPngLongestEdgeText = "2048";
    private bool _twoDPngTransparent = true;

    public bool CanExportTwoDDxf => TwoDDocument is not null;

    public bool CanExportTwoDSvg => TwoDDocument is not null;

    public bool CanExportTwoDPng => TwoDDocument is not null;

    public bool CanExportTwoDPdf => TwoDDocument is not null;

    public string TwoDPngLongestEdgeText
    {
        get => _twoDPngLongestEdgeText;
        set { if (SetProperty(ref _twoDPngLongestEdgeText, value ?? string.Empty)) PersistTwoDExportPreferences(); }
    }

    public bool TwoDPngTransparent
    {
        get => _twoDPngTransparent;
        set { if (SetProperty(ref _twoDPngTransparent, value)) PersistTwoDExportPreferences(); }
    }

    public bool TwoDExportSelectedOnly
    {
        get => _twoDExportSelectedOnly;
        set
        {
            if (_twoDExportSelectedOnly == value)
                return;

            _twoDExportSelectedOnly = value;
            OnPropertyChanged();
            PersistTwoDExportPreferences();
        }
    }

    public bool TwoDExportMeasurementLines
    {
        get => _twoDExportMeasurementLines;
        set { if (SetProperty(ref _twoDExportMeasurementLines, value)) PersistTwoDExportPreferences(); }
    }

    public string TwoDSvgPrecisionText
    {
        get => _twoDSvgPrecisionText;
        set { if (SetProperty(ref _twoDSvgPrecisionText, value ?? string.Empty)) { OnPropertyChanged(nameof(CanApplyTwoDSvgOptions)); PersistTwoDExportPreferences(); } }
    }

    public string TwoDSvgStrokeWidthText
    {
        get => _twoDSvgStrokeWidthText;
        set { if (SetProperty(ref _twoDSvgStrokeWidthText, value ?? string.Empty)) { OnPropertyChanged(nameof(CanApplyTwoDSvgOptions)); PersistTwoDExportPreferences(); } }
    }

    public bool CanApplyTwoDSvgOptions
        => int.TryParse(TwoDSvgPrecisionText, out var precision)
           && precision is >= 0 and <= 15
           && double.TryParse(TwoDSvgStrokeWidthText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var width)
           && double.IsFinite(width)
           && width >= 0;

    public IReadOnlyList<string> TwoDDxfVersionOptions => Editor2DExportOptions.DxfVersionOptions;

    public string TwoDDxfVersion
    {
        get => _twoDDxfVersion;
        set
        {
            var normalized = value is null ? "R2010" : new Editor2DExportOptions(DxfVersion: value).NormalizedDxfVersion;
            if (SetProperty(ref _twoDDxfVersion, normalized))
                PersistTwoDExportPreferences();
        }
    }

    private void PersistTwoDExportPreferences()
    {
        if (_isApplyingTwoDWorkspaceState)
            return;

        _twoDWorkspace.Apply(_twoDWorkspace.State with
        {
            ExportPreferences = new Editor2DExportPreferences(
                TwoDExportSelectedOnly,
                TwoDExportMeasurementLines,
                TwoDSvgPrecisionText,
                TwoDSvgStrokeWidthText,
                TwoDDxfVersion,
                TwoDPngLongestEdgeText,
                TwoDPngTransparent),
        }, recordHistory: false);
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
    }

    public async Task ExportTwoDDxfAsync(CancellationToken cancellationToken = default)
    {
        var document = TwoDDocument;
        if (document is null)
            return;

        try
        {
            ErrorMessage = null;
            var suggestedFileName = string.IsNullOrWhiteSpace(ProjectName)
                ? "Pathstitch Export.dxf"
                : $"{ProjectName}.dxf";
            var outputPath = await _projectFileDialogService
                .PickDxfExportFileAsync(suggestedFileName, cancellationToken)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            await _editorOutputPreviewService
                .SaveExportDocumentAsync(BuildExportDocument(document), outputPath, ParseDxfOptions(), cancellationToken)
                .ConfigureAwait(true);
            StatusText = $"Exported DXF to {Path.GetFileName(outputPath)}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to export 2D workspace DXF.");
            ErrorMessage = $"Could not export DXF: {exception.Message}";
        }
    }

    public async Task ExportTwoDSvgAsync(CancellationToken cancellationToken = default)
    {
        var document = TwoDDocument;
        if (document is null)
            return;

        try
        {
            ErrorMessage = null;
            var suggestedFileName = string.IsNullOrWhiteSpace(ProjectName)
                ? "Pathstitch Export.svg"
                : $"{ProjectName}.svg";
            var outputPath = await _projectFileDialogService
                .PickSvgExportFileAsync(suggestedFileName, cancellationToken)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            await _editorOutputPreviewService
                .SaveExportDocumentAsync(
                    BuildExportDocument(document),
                    outputPath,
                    ParseSvgOptions(),
                    cancellationToken)
                .ConfigureAwait(true);
            StatusText = $"Exported SVG to {Path.GetFileName(outputPath)}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to export 2D workspace SVG.");
            ErrorMessage = $"Could not export SVG: {exception.Message}";
        }
    }

    public async Task ExportTwoDPngAsync(CancellationToken cancellationToken = default)
    {
        var document = TwoDDocument;
        if (document is null)
            return;

        try
        {
            ErrorMessage = null;
            var suggestedFileName = string.IsNullOrWhiteSpace(ProjectName)
                ? "Pathstitch Export.png"
                : $"{ProjectName}.png";
            var outputPath = await _projectFileDialogService
                .PickPngExportFileAsync(suggestedFileName, cancellationToken)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            await _editorOutputPreviewService
                .SaveExportDocumentAsync(BuildExportDocument(document), outputPath, ParsePngOptions(), cancellationToken)
                .ConfigureAwait(true);
            StatusText = $"Exported PNG to {Path.GetFileName(outputPath)}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to export 2D workspace PNG.");
            ErrorMessage = $"Could not export PNG: {exception.Message}";
        }
    }

    public async Task ExportTwoDPdfAsync(CancellationToken cancellationToken = default)
    {
        var document = TwoDDocument;
        if (document is null)
            return;

        try
        {
            ErrorMessage = null;
            var suggestedFileName = string.IsNullOrWhiteSpace(ProjectName)
                ? "Pathstitch Export.pdf"
                : $"{ProjectName}.pdf";
            var outputPath = await _projectFileDialogService
                .PickPdfExportFileAsync(suggestedFileName, cancellationToken)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            await _editorOutputPreviewService
                .SaveExportDocumentAsync(BuildExportDocument(document), outputPath, ParseSvgOptions(), cancellationToken)
                .ConfigureAwait(true);
            StatusText = $"Exported PDF to {Path.GetFileName(outputPath)}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to export 2D workspace PDF.");
            ErrorMessage = $"Could not export PDF: {exception.Message}";
        }
    }

    private Editor2DExportDocument BuildExportDocument(Editor2DPreviewDocument document)
    {
        var exportDocument = TwoDExportSelectedOnly
            ? CreateUpdatedTwoDDocument(document, GetSelectedTwoDPaths())
            : document;
        if (TwoDExportMeasurementLines && TwoDMeasurements.Count > 0)
        {
            var measurementPaths = TwoDMeasurements
                .Select(measurement => new Editor2DPreviewPath(
                    $"measurement-export-{measurement.Id}",
                    "LINE",
                    [measurement.Start, measurement.End],
                    IsClosed: false,
                    IsConstruction: true))
                .ToArray();
            exportDocument = CreateUpdatedTwoDDocument(
                exportDocument,
                exportDocument.Paths.Concat(measurementPaths).ToArray());
        }

        var metadata = new Dictionary<string, Editor2DExportPathMetadata>(StringComparer.Ordinal);
        var geometryLayers = TwoDLayers
            .Where(layer => layer.Kind == Editor2DLayerKind.Geometry)
            .OrderBy(layer => layer.Order)
            .ThenBy(layer => layer.Id, StringComparer.Ordinal)
            .ToArray();
        foreach (var path in exportDocument.Paths)
        {
            if (path.IsConstruction)
            {
                metadata[path.Id] = new Editor2DExportPathMetadata("CONSTRUCTION", "#808080");
                continue;
            }

            var layer = geometryLayers.FirstOrDefault(candidate => candidate.PathIds.Contains(path.Id, StringComparer.Ordinal));
            metadata[path.Id] = layer is null
                ? new Editor2DExportPathMetadata("EDITED_OUTPUT", "#000000", int.MaxValue)
                : new Editor2DExportPathMetadata(layer.Name, layer.ColorHex, layer.Order);
        }

        return new Editor2DExportDocument(exportDocument, metadata);
    }

    private Editor2DExportOptions ParseSvgOptions()
    {
        var precision = int.TryParse(TwoDSvgPrecisionText, out var parsedPrecision) ? parsedPrecision : 3;
        var strokeWidth = double.TryParse(
            TwoDSvgStrokeWidthText,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsedStrokeWidth)
            ? parsedStrokeWidth
            : 0.5;
        return new Editor2DExportOptions(precision, strokeWidth, TwoDExportMeasurementLines, TwoDDxfVersion);
    }

    private Editor2DExportOptions ParseDxfOptions()
        => new(IncludeMeasurementLines: TwoDExportMeasurementLines, DxfVersion: TwoDDxfVersion);

    private Editor2DExportOptions ParsePngOptions()
    {
        var edge = int.TryParse(TwoDPngLongestEdgeText, out var parsedEdge) ? parsedEdge : 2048;
        return new Editor2DExportOptions(
            IncludeMeasurementLines: TwoDExportMeasurementLines,
            PngLongestEdge: edge,
            PngTransparent: TwoDPngTransparent);
    }
}
