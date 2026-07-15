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
        set => SetProperty(ref _twoDPngLongestEdgeText, value ?? string.Empty);
    }

    public bool TwoDPngTransparent
    {
        get => _twoDPngTransparent;
        set => SetProperty(ref _twoDPngTransparent, value);
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
        }
    }

    public bool TwoDExportMeasurementLines
    {
        get => _twoDExportMeasurementLines;
        set => SetProperty(ref _twoDExportMeasurementLines, value);
    }

    public string TwoDSvgPrecisionText
    {
        get => _twoDSvgPrecisionText;
        set { if (SetProperty(ref _twoDSvgPrecisionText, value ?? string.Empty)) OnPropertyChanged(nameof(CanApplyTwoDSvgOptions)); }
    }

    public string TwoDSvgStrokeWidthText
    {
        get => _twoDSvgStrokeWidthText;
        set { if (SetProperty(ref _twoDSvgStrokeWidthText, value ?? string.Empty)) OnPropertyChanged(nameof(CanApplyTwoDSvgOptions)); }
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
        set => SetProperty(ref _twoDDxfVersion, value is null ? "R2010" : new Editor2DExportOptions(DxfVersion: value).NormalizedDxfVersion);
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
                .SavePreviewDocumentAsync(BuildExportDocument(document), outputPath, ParseDxfOptions(), cancellationToken)
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
                .SavePreviewDocumentAsync(
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
                .SavePreviewDocumentAsync(BuildExportDocument(document), outputPath, ParsePngOptions(), cancellationToken)
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
                .SavePreviewDocumentAsync(BuildExportDocument(document), outputPath, ParseSvgOptions(), cancellationToken)
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

    private Editor2DPreviewDocument BuildExportDocument(Editor2DPreviewDocument document)
    {
        var exportDocument = TwoDExportSelectedOnly && TwoDSelectedPathIds.Count > 0
            ? CreateUpdatedTwoDDocument(document, GetSelectedTwoDPaths())
            : document;
        if (!TwoDExportMeasurementLines || TwoDMeasurements.Count == 0)
            return exportDocument;

        var measurementPaths = TwoDMeasurements
            .Select(measurement => new Editor2DPreviewPath(
                $"measurement-export-{measurement.Id}",
                "LINE",
                [measurement.Start, measurement.End],
                IsClosed: false))
            .ToArray();
        return CreateUpdatedTwoDDocument(exportDocument, exportDocument.Paths.Concat(measurementPaths).ToArray());
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
