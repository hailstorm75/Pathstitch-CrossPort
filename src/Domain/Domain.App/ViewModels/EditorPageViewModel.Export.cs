using Domain.App.Models;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;

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

            var dxfOptions = ParseDxfOptions();
            if (CanPreserveGeneratedDxfSource(document, dxfOptions.NormalizedDxfVersion))
            {
                await _editorOutputPreviewService
                    .CopyDxfPreservingStructureAsync(_preservableGeneratedDxfSourceBytes!, outputPath, cancellationToken)
                    .ConfigureAwait(true);
            }
            else
            {
                var exportDocument = BuildExportDocument(document);
                var mergeResult = EditorDxfMergeResult.NotSupported;
                if (CanAttemptGeneratedDxfMerge(document, dxfOptions.NormalizedDxfVersion))
                {
                    mergeResult = await _editorOutputPreviewService
                        .TrySaveMergedDxfDocumentAsync(
                            _preservableGeneratedDxfSourceBytes!,
                            exportDocument,
                            outputPath,
                            dxfOptions,
                            cancellationToken)
                        .ConfigureAwait(true);
                }                if (!mergeResult.Succeeded)
                {
                    await _editorOutputPreviewService
                        .SaveExportDocumentAsync(exportDocument, outputPath, dxfOptions, cancellationToken)
                        .ConfigureAwait(true);
                }
            }
            StatusText = $"Exported DXF to {Path.GetFileName(outputPath)}";
            RecordActivity("Export DXF", Path.GetFileName(outputPath), markDocumentDirty: false);
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
            RecordActivity("Export SVG", Path.GetFileName(outputPath), markDocumentDirty: false);
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
            RecordActivity("Export PNG", Path.GetFileName(outputPath), markDocumentDirty: false);
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
            RecordActivity("Export PDF", Path.GetFileName(outputPath), markDocumentDirty: false);
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
        => BuildExportDocument(document, TwoDExportSelectedOnly, TwoDExportMeasurementLines);

    private Editor2DExportDocument BuildFullExportDocument(Editor2DPreviewDocument document)
        => BuildExportDocument(document, selectedOnly: false, includeMeasurementLines: false);

    private Editor2DExportDocument BuildExportDocument(
        Editor2DPreviewDocument document,
        bool selectedOnly,
        bool includeMeasurementLines)
    {
        var exportDocument = selectedOnly
            ? CreateUpdatedTwoDDocument(document, GetSelectedTwoDPaths())
            : document;
        if (includeMeasurementLines && TwoDMeasurements.Count > 0)
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

    private void CaptureGeneratedDxfPreservationBaseline(
        Editor2DPreviewDocument? document,
        string? sourcePath,
        string? sourceDataBase64)
    {
        ClearGeneratedDxfPreservationBaseline();
        if (document is null
            || string.IsNullOrWhiteSpace(sourcePath)
            || !Path.GetExtension(sourcePath).Equals(".dxf", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(sourceDataBase64))
        {
            return;
        }

        try
        {
            var sourceBytes = Convert.FromBase64String(sourceDataBase64);
            var sourceVersion = ReadDxfVersion(sourceBytes);
            if (sourceVersion is null)
                return;

            _preservableGeneratedDxfFingerprint = ComputeExportFingerprint(BuildFullExportDocument(document));
            _preservableGeneratedDxfSourceBytes = sourceBytes;
            _preservableGeneratedDxfVersion = sourceVersion;
        }
        catch (FormatException)
        {
            ClearGeneratedDxfPreservationBaseline();
        }
    }

    private void ClearGeneratedDxfPreservationBaseline()
    {
        _preservableGeneratedDxfFingerprint = null;
        _preservableGeneratedDxfSourceBytes = null;
        _preservableGeneratedDxfVersion = null;
    }

    private bool CanAttemptGeneratedDxfMerge(
        Editor2DPreviewDocument document,
        string? requiredDxfVersion,
        bool requireFullExportOptions = true)
    {
        if (requireFullExportOptions
            && (TwoDExportSelectedOnly || TwoDExportMeasurementLines && TwoDMeasurements.Count > 0))
        {
            return false;
        }
        return document.Paths.Count > 0
               && _preservableGeneratedDxfSourceBytes is { Length: > 0 }
               && !string.IsNullOrWhiteSpace(_preservableGeneratedDxfVersion)
               && (requiredDxfVersion is null
                   || string.Equals(requiredDxfVersion, _preservableGeneratedDxfVersion, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanPreserveGeneratedDxfSource(
        Editor2DPreviewDocument document,
        string? requiredDxfVersion,
        bool requireFullExportOptions = true)
    {
        if (requireFullExportOptions
            && (TwoDExportSelectedOnly || TwoDExportMeasurementLines && TwoDMeasurements.Count > 0))
        {
            return false;
        }

        return _preservableGeneratedDxfSourceBytes is { Length: > 0 }
               && !string.IsNullOrWhiteSpace(_preservableGeneratedDxfFingerprint)
               && !string.IsNullOrWhiteSpace(_preservableGeneratedDxfVersion)
               && (requiredDxfVersion is null
                   || string.Equals(requiredDxfVersion, _preservableGeneratedDxfVersion, StringComparison.OrdinalIgnoreCase))
               && string.Equals(
                   ComputeExportFingerprint(BuildFullExportDocument(document)),
                   _preservableGeneratedDxfFingerprint,
                   StringComparison.Ordinal);
    }

    private static string ComputeExportFingerprint(Editor2DExportDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", 1);
            writer.WriteStartArray("paths");
            foreach (var path in document.Geometry.Paths)
            {
                writer.WriteStartObject();
                writer.WriteString("entityType", path.EntityType.ToUpperInvariant());
                writer.WriteBoolean("closed", path.IsClosed);
                writer.WriteBoolean("construction", path.IsConstruction);
                writer.WriteBoolean("filled", path.IsFilled);
                WritePoints(writer, "points", path.Points);
                WritePoint(writer, "start", path.Start);
                WritePoint(writer, "center", path.Center);
                WriteNullableDouble(writer, "radius", path.Radius);
                WriteNullableDouble(writer, "startAngle", path.StartAngleDegrees);
                WriteNullableDouble(writer, "endAngle", path.EndAngleDegrees);
                if (path.FillLoops is { } fillLoops)
                {
                    writer.WriteStartArray("fillLoops");
                    foreach (var loop in fillLoops)
                        WritePoints(writer, null, loop);
                    writer.WriteEndArray();
                }
                else
                {
                    writer.WriteNull("fillLoops");
                }

                if (path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteString("text", path.Text);
                    writer.WriteNumber("textHeight", path.TextHeight ?? 5.0);
                    writer.WriteNumber("rotation", path.RotationDegrees ?? 0.0);
                    writer.WriteNumber("widthFactor", path.WidthFactor ?? 1.0);
                    writer.WriteString("fontFamily", path.FontFamily);
                    writer.WriteNumber("characterSpacing", path.CharacterSpacing);
                    writer.WriteBoolean("bold", path.IsBold);
                    writer.WriteBoolean("italic", path.IsItalic);
                    writer.WriteBoolean("underline", path.IsUnderline);
                }

                var metadata = document.PathMetadata.TryGetValue(path.Id, out var value)
                    ? value
                    : new Editor2DExportPathMetadata("EDITED_OUTPUT", "#000000", int.MaxValue);
                writer.WriteStartObject("metadata");
                writer.WriteString("layer", path.IsConstruction ? "CONSTRUCTION" : metadata.LayerName.Trim());
                writer.WriteString("color", path.IsConstruction ? "#808080" : metadata.ColorHex.ToUpperInvariant());
                writer.WriteNumber("order", path.IsConstruction ? 0 : metadata.Order);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WritePoints(
        Utf8JsonWriter writer,
        string? propertyName,
        IReadOnlyList<Editor2DPoint> points)
    {
        if (propertyName is null)
            writer.WriteStartArray();
        else
            writer.WriteStartArray(propertyName);
        foreach (var point in points)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(point.X);
            writer.WriteNumberValue(point.Y);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }

    private static void WritePoint(Utf8JsonWriter writer, string propertyName, Editor2DPoint? point)
    {
        if (point is not { } value)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteStartArray(propertyName);
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }

    private static void WriteNullableDouble(Utf8JsonWriter writer, string propertyName, double? value)
    {
        if (value is { } number)
            writer.WriteNumber(propertyName, number);
        else
            writer.WriteNull(propertyName);
    }

    private static string? ReadDxfVersion(byte[] sourceBytes)
    {
        if (sourceBytes.AsSpan().StartsWith(System.Text.Encoding.ASCII.GetBytes("AutoCAD Binary DXF")))
            return null;

        var lines = System.Text.Encoding.Latin1.GetString(sourceBytes)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n');
        for (var index = 0; index + 3 < lines.Length; index += 2)
        {
            if (lines[index].Trim() != "9"
                || !lines[index + 1].Trim().Equals("$ACADVER", StringComparison.OrdinalIgnoreCase)
                || lines[index + 2].Trim() != "1")
            {
                continue;
            }

            return lines[index + 3].Trim().ToUpperInvariant() switch
            {
                "AC1032" => "R2018",
                "AC1027" => "R2013",
                "AC1024" => "R2010",
                "AC1021" => "R2007",
                "AC1015" => "R2000",
                _ => null,
            };
        }

        return null;
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
