using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Domain.App.Models;

namespace Pathstitch.App.Services;

internal readonly record struct DxfPoint(double X, double Y);

internal readonly record struct DxfVector3(double X, double Y, double Z);

internal sealed record DxfPolyline(IReadOnlyList<DxfPoint> Points, bool IsClosed);

internal readonly record struct DxfVertex(double X, double Y, double Bulge);

internal sealed record DxfPreviewPath(
    string Id,
    string EntityType,
    IReadOnlyList<DxfPoint> Points,
    bool IsClosed,
    bool IsAxisAlignedRectangle = false,
    DxfPoint? Start = null,
    string? Text = null,
    double? TextHeight = null,
    double? RotationDegrees = null,
    double? WidthFactor = null,
    DxfPoint? Center = null,
    double? Radius = null,
    double? StartAngleDegrees = null,
    double? EndAngleDegrees = null,
    bool IsFilled = false,
    string? LayerName = null,
    string? EntityHandle = null,
    IReadOnlyList<IReadOnlyList<DxfPoint>>? FillLoops = null,
    string? FontFamily = null,
    double CharacterSpacing = 0.0,
    bool IsBold = false,
    bool IsItalic = false,
    bool IsUnderline = false,
    Editor2DTextBasis? TextBasis = null);

internal sealed record DxfPreviewDocument(
    IReadOnlyList<DxfPreviewPath> Paths,
    IReadOnlyDictionary<string, int> EntityCounts,
    IReadOnlyList<string> UnsupportedEntityTypes);

internal readonly record struct DxfUnitMetadata(int? InsUnitsCode, double? MillimetersPerDrawingUnit, bool HasMalformedDeclaration = false)
{
    public bool HasUnitScale => MillimetersPerDrawingUnit is not null;
}

internal static class EditorDxfDocument
{
    public static void SavePreviewDocument(
        string outputPath,
        Editor2DPreviewDocument document,
        string layerName = "EDITED_OUTPUT",
        Editor2DExportOptions? options = null)
        => SaveDocument(outputPath, document, layerName, options, null);

    public static void SaveExportDocument(
        string outputPath,
        Editor2DExportDocument document,
        Editor2DExportOptions? options = null)
        => SaveDocument(outputPath, document.Geometry, "EDITED_OUTPUT", options, document.PathMetadata);

    private static void SaveDocument(
        string outputPath,
        Editor2DPreviewDocument document,
        string layerName,
        Editor2DExportOptions? options,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata,
        bool preserveSourceEntityHandles = false)
    {
        if (Path.GetExtension(outputPath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (pathMetadata is null)
                SvgOutputDocumentWriter.Save(outputPath, document, options);
            else
                SvgOutputDocumentWriter.Save(outputPath, new Editor2DExportDocument(document, pathMetadata), options);
            return;
        }

        if (Path.GetExtension(outputPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            PngOutputDocumentWriter.Save(outputPath, document, options ?? Editor2DExportOptions.Defaults);
            return;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var acadVersionCode = AcadVersionCode(options?.NormalizedDxfVersion ?? Editor2DExportOptions.Defaults.DxfVersion);
        var useLegacyUnicodeEscapes = !SupportsUtf8(acadVersionCode);
        var builder = new StringBuilder();
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "HEADER");
        AppendPair(builder, 9, "$ACADVER");
        AppendPair(builder, 1, acadVersionCode);
        AppendCanonicalMillimeterHeader(builder);
        AppendPair(builder, 0, "ENDSEC");
        var exportLayers = pathMetadata is null ? null : BuildExportLayers(document, pathMetadata);
        var hasRichText = document.Paths.Any(NeedsPathstitchTextXData);
        if (exportLayers is not null)
            AppendLayerTables(builder, exportLayers.Layers, hasRichText);
        else if (document.Paths.Any(static path => path.IsConstruction))
            AppendLayerTables(
                builder,
                [new ExportLayer("CONSTRUCTION", "#808080", 0, 0, true)],
                hasRichText);
        else if (hasRichText)
            AppendPathstitchAppIdTables(builder);
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "ENTITIES");

        foreach (var path in document.Paths)
        {
            var entityLayerName = path.IsConstruction
                ? "CONSTRUCTION"
                : exportLayers is not null && exportLayers.PathLayerNames.TryGetValue(path.Id, out var mappedLayerName)
                    ? mappedLayerName
                    : layerName;
            if (string.Equals(path.EntityType, "TEXT", StringComparison.OrdinalIgnoreCase)
                && path.Start is Editor2DPoint textStart
                && !string.IsNullOrWhiteSpace(path.Text))
            {
                AppendText(
                    builder,
                    entityLayerName,
                    textStart,
                    path.Text!,
                    path.TextHeight ?? 5.0,
                    path.RotationDegrees ?? 0.0,
                    path.WidthFactor ?? 1.0,
                    path.TextBasis,
                    path.IsConstruction,
                    path.FontFamily,
                    path.CharacterSpacing,
                    path.IsBold,
                    path.IsItalic,
                    path.IsUnderline,
                    useLegacyUnicodeEscapes,
                    preserveSourceEntityHandles ? path.SourceEntityHandle : null);
                continue;
            }

            if (preserveSourceEntityHandles
                && string.Equals(path.EntityType, "LINE", StringComparison.OrdinalIgnoreCase)
                && path.Points.Count >= 2)
            {
                AppendLine(
                    builder,
                    entityLayerName,
                    path.Points[0],
                    path.Points[^1],
                    path.IsConstruction,
                    preserveSourceEntityHandles ? path.SourceEntityHandle : null);
                continue;
            }

            if (string.Equals(path.EntityType, "CIRCLE", StringComparison.OrdinalIgnoreCase)
                && path.Center is Editor2DPoint circleCenter
                && path.Radius is double circleRadius
                && circleRadius > 1e-9)
            {
                AppendCircle(builder, entityLayerName, circleCenter, circleRadius, path.IsConstruction,
                    preserveSourceEntityHandles ? path.SourceEntityHandle : null);
                continue;
            }

            if (string.Equals(path.EntityType, "ARC", StringComparison.OrdinalIgnoreCase)
                && path.Center is Editor2DPoint arcCenter
                && path.Radius is double arcRadius
                && path.StartAngleDegrees is double startAngleDegrees
                && path.EndAngleDegrees is double endAngleDegrees
                && arcRadius > 1e-9)
            {
                AppendArc(
                    builder,
                    entityLayerName,
                    arcCenter,
                    arcRadius,
                    startAngleDegrees,
                    endAngleDegrees,
                    path.IsConstruction,
                    preserveSourceEntityHandles ? path.SourceEntityHandle : null);
                continue;
            }

            var polyline = new DxfPolyline(
                path.Points.Select(static point => new DxfPoint(point.X, point.Y)).ToArray(),
                path.IsClosed);
            if (path.IsFilled && path.IsClosed && path.Points.Count >= 3 && !path.IsConstruction)
            {
                var fillLoops = path.FillLoops?.Select(loop => (IReadOnlyList<DxfPoint>)loop
                    .Select(static point => new DxfPoint(point.X, point.Y)).ToArray()).ToArray();
                AppendHatch(builder, entityLayerName, polyline, fillLoops,
                    preserveSourceEntityHandles ? path.SourceEntityHandle : null);
            }
            else
            {
                AppendLwPolyline(builder, entityLayerName, polyline, path.IsConstruction,
                    preserveSourceEntityHandles ? path.SourceEntityHandle : null);
            }
        }

        AppendPair(builder, 0, "ENDSEC");
        AppendPair(builder, 0, "EOF");
        WriteDxf(outputPath, builder.ToString(), acadVersionCode);
    }

    private sealed record ExportLayer(string Name, string ColorHex, int Order, int FirstPathIndex, bool IsConstruction);
    private sealed record ExportLayerMap(
        IReadOnlyList<ExportLayer> Layers,
        IReadOnlyDictionary<string, string> PathLayerNames);

    private static ExportLayerMap BuildExportLayers(
        Editor2DPreviewDocument document,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata> pathMetadata)
    {
        var layers = new Dictionary<string, ExportLayer>(StringComparer.OrdinalIgnoreCase);
        var assignedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(["CONSTRUCTION"], StringComparer.OrdinalIgnoreCase);
        var pathLayerNames = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < document.Paths.Count; index++)
        {
            var path = document.Paths[index];
            var metadata = pathMetadata.TryGetValue(path.Id, out var value)
                ? value
                : new Editor2DExportPathMetadata("EDITED_OUTPUT", "#000000", int.MaxValue);
            var construction = path.IsConstruction;
            var sourceName = string.IsNullOrWhiteSpace(metadata.LayerName) ? "EDITED_OUTPUT" : metadata.LayerName.Trim();
            string name;
            if (construction)
            {
                name = "CONSTRUCTION";
            }
            else if (assignedNames.TryGetValue(sourceName, out var assignedName))
            {
                name = assignedName;
            }
            else
            {
                var baseName = NormalizeLayerName(sourceName);
                name = baseName;
                for (var suffix = 1; !usedNames.Add(name); suffix++)
                    name = $"{baseName}_{suffix}";
                assignedNames[sourceName] = name;
            }
            pathLayerNames[path.Id] = name;
            var color = construction ? "#808080" : NormalizeColorHex(metadata.ColorHex);
            var order = construction ? int.MaxValue : metadata.Order;
            if (!layers.ContainsKey(name))
                layers[name] = new ExportLayer(name, color, order, index, construction);
        }

        var ordered = layers.Values
            .OrderBy(static layer => layer.Order)
            .ThenBy(static layer => layer.FirstPathIndex)
            .ThenBy(static layer => layer.Name, StringComparer.Ordinal)
            .ToArray();
        return new ExportLayerMap(ordered, pathLayerNames);
    }

    private static void AppendLayerTables(
        StringBuilder builder,
        IReadOnlyList<ExportLayer> layers,
        bool includePathstitchAppId = false)
    {
        var hasConstruction = layers.Any(static layer => layer.IsConstruction);
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "TABLES");
        if (hasConstruction)
            AppendDashedLineTypeTable(builder, "10", "11");
        if (includePathstitchAppId)
            AppendPathstitchAppIdTable(builder);

        AppendPair(builder, 0, "TABLE");
        AppendPair(builder, 2, "LAYER");
        AppendPair(builder, 5, "20");
        AppendPair(builder, 330, "0");
        AppendPair(builder, 100, "AcDbSymbolTable");
        AppendPair(builder, 70, layers.Count.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            AppendPair(builder, 0, "LAYER");
            AppendPair(builder, 5, (0x21 + index).ToString("X", CultureInfo.InvariantCulture));
            AppendPair(builder, 330, "20");
            AppendPair(builder, 100, "AcDbSymbolTableRecord");
            AppendPair(builder, 100, "AcDbLayerTableRecord");
            AppendPair(builder, 2, layer.Name);
            AppendPair(builder, 70, "0");
            AppendPair(builder, 62, layer.IsConstruction ? "8" : "7");
            if (!layer.IsConstruction)
                AppendPair(builder, 420, RgbTrueColor(layer.ColorHex).ToString(CultureInfo.InvariantCulture));
            AppendPair(builder, 6, layer.IsConstruction ? "DASHED" : "CONTINUOUS");
        }
        AppendPair(builder, 0, "ENDTAB");
        AppendPair(builder, 0, "ENDSEC");
    }

    private static void AppendPathstitchAppIdTables(StringBuilder builder)
    {
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "TABLES");
        AppendPathstitchAppIdTable(builder);
        AppendPair(builder, 0, "ENDSEC");
    }

    private static void AppendPathstitchAppIdTable(StringBuilder builder)
    {
        AppendPair(builder, 0, "TABLE");
        AppendPair(builder, 2, "APPID");
        AppendPair(builder, 5, "12");
        AppendPair(builder, 330, "0");
        AppendPair(builder, 100, "AcDbSymbolTable");
        AppendPair(builder, 70, "1");
        AppendPair(builder, 0, "APPID");
        AppendPair(builder, 5, "13");
        AppendPair(builder, 330, "12");
        AppendPair(builder, 100, "AcDbSymbolTableRecord");
        AppendPair(builder, 100, "AcDbRegAppTableRecord");
        AppendPair(builder, 2, "PATHSTITCH");
        AppendPair(builder, 70, "0");
        AppendPair(builder, 0, "ENDTAB");
    }
    private static void AppendDashedLineTypeTable(StringBuilder builder, string tableHandle, string recordHandle)
    {
        AppendPair(builder, 0, "TABLE");
        AppendPair(builder, 2, "LTYPE");
        AppendPair(builder, 5, tableHandle);
        AppendPair(builder, 330, "0");
        AppendPair(builder, 100, "AcDbSymbolTable");
        AppendPair(builder, 70, "1");
        AppendPair(builder, 0, "LTYPE");
        AppendPair(builder, 5, recordHandle);
        AppendPair(builder, 330, tableHandle);
        AppendPair(builder, 100, "AcDbSymbolTableRecord");
        AppendPair(builder, 100, "AcDbLinetypeTableRecord");
        AppendPair(builder, 2, "DASHED");
        AppendPair(builder, 70, "0");
        AppendPair(builder, 3, "Dashed __ __ __");
        AppendPair(builder, 72, "65");
        AppendPair(builder, 73, "2");
        AppendPair(builder, 40, "0.75");
        AppendPair(builder, 49, "0.5");
        AppendPair(builder, 74, "0");
        AppendPair(builder, 49, "-0.25");
        AppendPair(builder, 74, "0");
        AppendPair(builder, 0, "ENDTAB");
    }

    private static string NormalizeLayerName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "EDITED_OUTPUT" : value.Trim();
        var invalid = new HashSet<char>(['<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', '=', ',']);
        return new string(name.Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray());
    }

    private static string NormalizeColorHex(string? value)
    {
        var color = value?.Trim() ?? string.Empty;
        if (color.Length == 7 && color[0] == '#' && int.TryParse(color[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return color.ToUpperInvariant();
        return "#000000";
    }

    private static int RgbTrueColor(string colorHex)
        => int.Parse(colorHex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string AcadVersionCode(string version)
        => version switch
        {
            "R2018" => "AC1032",
            "R2013" => "AC1027",
            "R2010" => "AC1024",
            "R2007" => "AC1021",
            "R2000" => "AC1015",
            _ => "AC1024",
        };

    private static string[] ReadDxfLines(string dxfPath)
    {
        var bytes = File.ReadAllBytes(dxfPath);
        var binarySignature = Encoding.ASCII.GetBytes("AutoCAD Binary DXF");
        if (bytes.AsSpan().StartsWith(binarySignature))
        {
            throw new InvalidDataException(
                "Binary DXF input is not supported. Save or convert the drawing as ASCII DXF before importing it.");
        }

        var transportBytes = bytes.AsSpan();
        if (transportBytes.StartsWith(Encoding.UTF8.Preamble))
            transportBytes = transportBytes[Encoding.UTF8.Preamble.Length..];
        var transportView = Encoding.Latin1.GetString(transportBytes);
        var transportLines = SplitRawDxfLines(transportView);
        var (acadVersionCode, drawingCodePage) = ReadTransportHeaderDeclarations(transportLines);

        Encoding encoding;
        if (acadVersionCode is not null && SupportsUtf8(acadVersionCode))
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }
        else
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codePage = ResolveDwgCodePage(drawingCodePage);
            try
            {
                encoding = Encoding.GetEncoding(
                    codePage,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"DXF declares unsupported $DWGCODEPAGE '{drawingCodePage}'.",
                    exception);
            }
        }

        try
        {
            return File.ReadAllLines(dxfPath, encoding);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                $"DXF byte stream is invalid for {(drawingCodePage ?? acadVersionCode ?? "Windows-1252")}.",
                exception);
        }
    }

    private static (string? AcadVersionCode, string? DrawingCodePage) ReadTransportHeaderDeclarations(
        IReadOnlyList<RawDxfLine> lines)
    {
        if (lines.Count == 0 || lines.Count % 2 != 0)
            throw new InvalidDataException("ASCII DXF contains an incomplete group-code pair.");

        var pairCount = lines.Count / 2;
        string Code(int pairIndex) => lines[pairIndex * 2].Content.Trim();
        string Value(int pairIndex) => lines[(pairIndex * 2) + 1].Content.Trim();

        var headerStart = -1;
        var headerEnd = -1;
        for (var pairIndex = 0; pairIndex + 1 < pairCount; pairIndex++)
        {
            if (Code(pairIndex) != "0"
                || !Value(pairIndex).Equals("SECTION", StringComparison.OrdinalIgnoreCase)
                || Code(pairIndex + 1) != "2"
                || !Value(pairIndex + 1).Equals("HEADER", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (headerStart >= 0)
                throw new InvalidDataException("ASCII DXF contains more than one HEADER section.");
            headerStart = pairIndex + 2;
            for (var cursor = headerStart; cursor < pairCount; cursor++)
            {
                if (Code(cursor) == "0" && Value(cursor).Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
                {
                    headerEnd = cursor;
                    break;
                }
            }
            if (headerEnd < 0)
                throw new InvalidDataException("ASCII DXF HEADER section is missing ENDSEC.");
            pairIndex = headerEnd;
        }
        if (headerStart < 0)
            return (null, null);

        string? acadVersionCode = null;
        string? drawingCodePage = null;
        for (var pairIndex = headerStart; pairIndex < headerEnd; pairIndex++)
        {
            if (Code(pairIndex) != "9")
                continue;
            var variableName = Value(pairIndex);
            if (!variableName.Equals("$ACADVER", StringComparison.OrdinalIgnoreCase)
                && !variableName.Equals("$DWGCODEPAGE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (++pairIndex >= headerEnd)
                throw new InvalidDataException($"DXF HEADER variable '{variableName}' has no value pair.");
            var expectedValueCode = variableName.Equals("$ACADVER", StringComparison.OrdinalIgnoreCase) ? "1" : "3";
            if (Code(pairIndex) != expectedValueCode)
            {
                throw new InvalidDataException(
                    $"DXF HEADER variable '{variableName}' must use group code {expectedValueCode}.");
            }
            var declaredValue = Value(pairIndex);
            if (variableName.Equals("$ACADVER", StringComparison.OrdinalIgnoreCase))
            {
                if (acadVersionCode is not null)
                    throw new InvalidDataException("DXF HEADER contains duplicate $ACADVER declarations.");
                acadVersionCode = declaredValue.ToUpperInvariant();
            }
            else
            {
                if (drawingCodePage is not null)
                    throw new InvalidDataException("DXF HEADER contains duplicate $DWGCODEPAGE declarations.");
                drawingCodePage = declaredValue;
            }
        }
        return (acadVersionCode, drawingCodePage);
    }
    private static int ResolveDwgCodePage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 1252;
        var normalized = value.Trim();
        if (normalized.Equals("UTF-8", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("UTF8", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.CodePage;
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var codePage)
            && codePage > 0
            ? codePage
            : throw new InvalidDataException($"DXF declares malformed $DWGCODEPAGE '{value}'.");
    }
    private static bool SupportsUtf8(string acadVersionCode)
        => int.TryParse(acadVersionCode.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
           && version >= 1021;

    private static string ReadAcadVersionCode(IReadOnlyList<string> lines)
    {
        for (var index = 0; index + 3 < lines.Count; index += 2)
        {
            if (lines[index].Trim() == "9"
                && lines[index + 1].Trim().Equals("$ACADVER", StringComparison.OrdinalIgnoreCase)
                && lines[index + 2].Trim() == "1")
            {
                var value = lines[index + 3].Trim();
                if (value.StartsWith("AC", StringComparison.OrdinalIgnoreCase))
                    return value.ToUpperInvariant();
            }
        }

        return AcadVersionCode(Editor2DExportOptions.Defaults.DxfVersion);
    }

    private static void WriteDxf(string outputPath, string content, string acadVersionCode)
    {
        if (SupportsUtf8(acadVersionCode))
        {
            File.WriteAllText(outputPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        File.WriteAllText(outputPath, EscapeDxfUnicode(content), Encoding.ASCII);
    }

    private static string EscapeDxfUnicode(string value)
    {
        if (value.All(static character => character <= 0x7F))
            return value;

        var escaped = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character <= 0x7F)
                escaped.Append(character);
            else
                escaped.Append(@"\U+").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
        }
        return escaped.ToString();
    }

    private static string DecodeDxfUnicodeEscapes(string value)
    {
        if (value.IndexOf(@"\U+", StringComparison.OrdinalIgnoreCase) < 0)
            return value;

        var decoded = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            if (index + 7 <= value.Length
                && value[index] == '\\'
                && (value[index + 1] is 'U' or 'u')
                && value[index + 2] == '+'
                && ushort.TryParse(
                    value.AsSpan(index + 3, 4),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var codeUnit))
            {
                decoded.Append((char)codeUnit);
                index += 7;
                continue;
            }

            decoded.Append(value[index]);
            index++;
        }
        return decoded.ToString();
    }

    private static IReadOnlyList<string> ChunkDxfStringValue(
        string value,
        int maximumEncodedLength,
        bool useLegacyUnicodeEscapes)
    {
        if (value.Length == 0)
            return [];

        var chunks = new List<string>();
        var chunk = new StringBuilder();
        var encodedLength = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeLength = useLegacyUnicodeEscapes
                ? runeText.Sum(static character => character <= 0x7F ? 1 : 7)
                : Encoding.UTF8.GetByteCount(runeText);
            if (chunk.Length > 0 && encodedLength + runeLength > maximumEncodedLength)
            {
                chunks.Add(chunk.ToString());
                chunk.Clear();
                encodedLength = 0;
            }

            chunk.Append(runeText);
            encodedLength += runeLength;
        }

        if (chunk.Length > 0)
            chunks.Add(chunk.ToString());
        return chunks;
    }
    private static void AppendCanonicalMillimeterHeader(StringBuilder builder)
    {
        AppendPair(builder, 9, "$INSUNITS");
        AppendPair(builder, 70, EditorLengthUnits.MillimeterInsUnitsCode.ToString(CultureInfo.InvariantCulture));
        AppendPair(builder, 9, "$MEASUREMENT");
        AppendPair(builder, 70, EditorLengthUnits.MetricMeasurementCode.ToString(CultureInfo.InvariantCulture));
    }

    private sealed record RawDxfLine(string Content, string Raw, string Terminator);

    private sealed record RawDxfPair(string Code, string Value, string Raw);

    public static void CopyPreservingStructureWithCanonicalHeader(string sourcePath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        CopyPreservingStructureWithCanonicalHeader(File.ReadAllBytes(sourcePath), outputPath);
    }

    public static void CopyPreservingStructureWithCanonicalHeader(
        ReadOnlyMemory<byte> sourceBytes,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var binarySignature = Encoding.ASCII.GetBytes("AutoCAD Binary DXF");
        if (sourceBytes.Span.StartsWith(binarySignature))
        {
            throw new InvalidDataException(
                "Binary DXF structure-preserving export is not supported. Convert the source to ASCII DXF or edit it so Pathstitch can serialize the preview geometry.");
        }

        var sourceText = Encoding.Latin1.GetString(sourceBytes.Span);
        var rawLines = SplitRawDxfLines(sourceText);
        if (rawLines.Count == 0 || rawLines.Count % 2 != 0)
            throw new InvalidDataException("ASCII DXF contains an incomplete group-code pair.");

        var pairs = new List<RawDxfPair>(rawLines.Count / 2);
        for (var index = 0; index < rawLines.Count; index += 2)
        {
            pairs.Add(new RawDxfPair(
                NormalizeRawGroupCode(rawLines[index].Content, index),
                rawLines[index + 1].Content.Trim(),
                rawLines[index].Raw + rawLines[index + 1].Raw));
        }

        var headerStart = -1;
        var headerEnd = -1;
        for (var index = 0; index + 1 < pairs.Count; index++)
        {
            if (pairs[index].Code == "0"
                && pairs[index].Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase)
                && pairs[index + 1].Code == "2"
                && pairs[index + 1].Value.Equals("HEADER", StringComparison.OrdinalIgnoreCase))
            {
                headerStart = index + 2;
                for (var cursor = headerStart; cursor < pairs.Count; cursor++)
                {
                    if (pairs[cursor].Code == "0"
                        && pairs[cursor].Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
                    {
                        headerEnd = cursor;
                        break;
                    }
                }
                break;
            }
        }

        var newline = rawLines.Select(static line => line.Terminator)
            .FirstOrDefault(static value => value.Length > 0)
            ?? Environment.NewLine;
        var canonicalUnits = string.Concat(
            "9", newline, "$INSUNITS", newline,
            "70", newline, EditorLengthUnits.MillimeterInsUnitsCode.ToString(CultureInfo.InvariantCulture), newline,
            "9", newline, "$MEASUREMENT", newline,
            "70", newline, EditorLengthUnits.MetricMeasurementCode.ToString(CultureInfo.InvariantCulture), newline);
        if (headerStart < 0)
        {
            var header = string.Concat(
                "0", newline, "SECTION", newline,
                "2", newline, "HEADER", newline,
                "9", newline, "$ACADVER", newline,
                "1", newline, AcadVersionCode(Editor2DExportOptions.Defaults.DxfVersion), newline,
                canonicalUnits,
                "0", newline, "ENDSEC", newline);
            WritePreservedDxf(outputPath, header + sourceText);
            return;
        }
        if (headerEnd < 0)
            throw new InvalidDataException("ASCII DXF contains an incomplete HEADER section.");

        var remove = new bool[pairs.Count];
        for (var index = headerStart; index < headerEnd; index++)
        {
            if (pairs[index].Code != "9"
                || (!pairs[index].Value.Equals("$INSUNITS", StringComparison.OrdinalIgnoreCase)
                    && !pairs[index].Value.Equals("$MEASUREMENT", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            remove[index] = true;
            for (var cursor = index + 1; cursor < headerEnd && pairs[cursor].Code is not "9" and not "0"; cursor++)
                remove[cursor] = true;
        }


        var output = new StringBuilder(sourceText.Length + canonicalUnits.Length);
        for (var index = 0; index < pairs.Count; index++)
        {
            if (index == headerEnd)
                output.Append(canonicalUnits);
            if (!remove[index])
                output.Append(pairs[index].Raw);
        }

        WritePreservedDxf(outputPath, output.ToString());
    }

    private sealed record RawDxfEntityRecord(
        int Start,
        int End,
        string EntityType,
        string? Handle);

    private sealed record DxfHandleAllocationPlan(
        IReadOnlyList<string> AllocatedHandles,
        string NextHandseed,
        int? HandseedValuePairIndex,
        int HeaderEndPairIndex);

    private sealed record DxfLayerTableInfo(
        string TableHandle,
        int DeclaredCapacity,
        int RecordCount,
        int CountPairIndex,
        int TableStartPairIndex,
        int EndTablePairIndex,
        IReadOnlyDictionary<string, string> LayerNames);

    private sealed record DxfSymbolTableInfo(
        string TableHandle,
        int DeclaredCapacity,
        int RecordCount,
        int CountPairIndex,
        int TableStartPairIndex,
        int EndTablePairIndex,
        IReadOnlyDictionary<string, string> RecordNames);

    private sealed record DxfSymbolDependencyPlan(
        string TableName,
        string RecordType,
        string RecordName,
        string TableHandle,
        string RecordHandle,
        DxfSymbolTableInfo? ExistingTable);

    private readonly record struct DxfPlanarSimilarity(
        double M11,
        double M12,
        double M21,
        double M22,
        double OffsetX,
        double OffsetY,
        double Scale,
        bool IsReflection)
    {
        public DxfPoint Transform(DxfPoint point)
            => new(
                (M11 * point.X) + (M12 * point.Y) + OffsetX,
                (M21 * point.X) + (M22 * point.Y) + OffsetY);
    }

    private sealed record DxfNewLayerDefinition(
        string Name,
        string ColorHex,
        string Handle,
        bool IsConstruction);
    public static EditorDxfMergeResult TryMergePreservingStructure(
        ReadOnlyMemory<byte> sourceBytes,
        Editor2DExportDocument document,
        string outputPath,
        Editor2DExportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (sourceBytes.IsEmpty
            || sourceBytes.Span.StartsWith(Encoding.ASCII.GetBytes("AutoCAD Binary DXF"))
            || document.Geometry.Paths.Count == 0)
        {
            return EditorDxfMergeResult.NotSupported;
        }

        var currentByHandle = new Dictionary<string, Editor2DPreviewPath>(StringComparer.OrdinalIgnoreCase);
        var newPaths = new List<Editor2DPreviewPath>();
        var pathIds = new HashSet<string>(StringComparer.Ordinal);
        var mergePathMetadata = new Dictionary<string, Editor2DExportPathMetadata>(document.PathMetadata, StringComparer.Ordinal);
        foreach (var path in document.Geometry.Paths)
        {
            if (!pathIds.Add(path.Id))
                return EditorDxfMergeResult.NotSupported;
            if (string.IsNullOrWhiteSpace(path.SourceEntityHandle))
            {
                newPaths.Add(path);
                continue;
            }
            if (!currentByHandle.TryAdd(NormalizeHandle(path.SourceEntityHandle), path))
                return EditorDxfMergeResult.NotSupported;
            var resolvedLayer = document.PathMetadata.TryGetValue(path.Id, out var metadata)
                ? metadata.LayerName
                : "EDITED_OUTPUT";
            if (string.IsNullOrWhiteSpace(path.SourceLayerName))
                return EditorDxfMergeResult.NotSupported;
            if (resolvedLayer.Trim().Equals("Layer 1", StringComparison.OrdinalIgnoreCase))
            {
                var existingMetadata = document.PathMetadata.TryGetValue(path.Id, out var sourceMetadata)
                    ? sourceMetadata
                    : new Editor2DExportPathMetadata(path.SourceLayerName, "#4D7FFF", int.MaxValue);
                if (!existingMetadata.ColorHex.Equals("#4D7FFF", StringComparison.OrdinalIgnoreCase))
                    return EditorDxfMergeResult.NotSupported;
                mergePathMetadata[path.Id] = existingMetadata with { LayerName = path.SourceLayerName.Trim() };
            }
            else if (!string.Equals(resolvedLayer.Trim(), path.SourceLayerName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return EditorDxfMergeResult.NotSupported;
            }
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return EditorDxfMergeResult.NotSupported;
        Directory.CreateDirectory(outputDirectory);
        var canonicalPath = Path.Combine(outputDirectory, $".pathstitch-source-{Guid.NewGuid():N}.dxf");
        var generatedPath = Path.Combine(outputDirectory, $".pathstitch-generated-{Guid.NewGuid():N}.dxf");
        var candidatePath = Path.Combine(outputDirectory, $".pathstitch-candidate-{Guid.NewGuid():N}.dxf");
        try
        {
            CopyPreservingStructureWithCanonicalHeader(sourceBytes, canonicalPath);
            var sourcePreview = LoadPreviewDocument(canonicalPath);
            var sourcePreviewByHandle = new Dictionary<string, DxfPreviewPath>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourcePath in sourcePreview.Paths)
            {
                if (string.IsNullOrWhiteSpace(sourcePath.EntityHandle)
                    || !sourcePreviewByHandle.TryAdd(NormalizeHandle(sourcePath.EntityHandle), sourcePath))
                {
                    return EditorDxfMergeResult.NotSupported;
                }
            }
            if (sourcePreviewByHandle.Count < currentByHandle.Count
                || currentByHandle.Keys.Any(handle => !sourcePreviewByHandle.ContainsKey(handle)))
            {
                return EditorDxfMergeResult.NotSupported;
            }

            var sourceText = Encoding.Latin1.GetString(File.ReadAllBytes(canonicalPath));
            if (!TryReadRawDxfPairs(sourceText, out var sourcePairs)
                || !TryReadAcadVersion(sourcePairs, out var sourceVersion)
                || !TryReadEntityRecords(sourcePairs, out var sourceRecords))
            {
                return EditorDxfMergeResult.NotSupported;
            }
            var sourceByHandle = BuildUniqueEntityHandleMap(sourceRecords);
            if (sourceByHandle is null)
                return EditorDxfMergeResult.NotSupported;

            DxfHandleAllocationPlan? handlePlan = null;
            DxfLayerTableInfo? layerTableInfo = null;
            string? modelSpaceOwner = null;
            var newLayerDefinitions = new List<DxfNewLayerDefinition>();
            var dependencyPlans = new List<DxfSymbolDependencyPlan>();
            var newPathsByHandle = new Dictionary<string, Editor2DPreviewPath>(StringComparer.OrdinalIgnoreCase);
            var assignedPathsById = new Dictionary<string, Editor2DPreviewPath>(StringComparer.Ordinal);
            var geometryToWrite = document.Geometry;
            var pendingLayers = new List<(string Name, string ColorHex, bool IsConstruction)>();
            var resolvedLayerByPathId = new Dictionary<string, string>(StringComparer.Ordinal);
            if (newPaths.Count > 0)
            {
                if (!TryReadLayerTableInfo(sourcePairs, out var parsedLayerTableInfo)
                    || (modelSpaceOwner = ResolveModelSpaceOwner(sourcePairs, sourceRecords)) is null)
                {
                    return EditorDxfMergeResult.NotSupported;
                }
                layerTableInfo = parsedLayerTableInfo;
                var sourceLayerNames = sourcePreview.Paths
                    .Select(static path => path.LayerName)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Select(static name => name!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var pendingLayerIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in newPaths)
                {
                    var isHatch = path.EntityType.Equals("HATCH", StringComparison.OrdinalIgnoreCase);
                    if (!IsMergeSupportedEntityType(path.EntityType)
                        || path.IsFilled && !isHatch
                        || isHatch && (!path.IsFilled || !path.IsClosed || path.Points.Count < 3)
                        || isHatch && path.IsConstruction
                        || !document.PathMetadata.TryGetValue(path.Id, out var metadata))
                    {
                        return EditorDxfMergeResult.NotSupported;
                    }
                    var normalizedColor = path.IsConstruction ? "#808080" : NormalizeColorHex(metadata.ColorHex);
                    if (path.IsConstruction
                        ? !metadata.LayerName.Trim().Equals("CONSTRUCTION", StringComparison.OrdinalIgnoreCase)
                          || !metadata.ColorHex.Trim().Equals("#808080", StringComparison.OrdinalIgnoreCase)
                        : !metadata.ColorHex.Trim().Equals(normalizedColor, StringComparison.OrdinalIgnoreCase))
                    {
                        return EditorDxfMergeResult.NotSupported;
                    }
                    var requestedLayer = path.IsConstruction
                        ? "CONSTRUCTION"
                        : NormalizeLayerName(metadata.LayerName);
                    string? actualLayer = null;
                    if (layerTableInfo.LayerNames.TryGetValue(requestedLayer, out var existingLayer))
                    {
                        actualLayer = existingLayer;
                    }
                    else if (requestedLayer.Equals("Layer 1", StringComparison.OrdinalIgnoreCase)
                             && sourceLayerNames.Length == 1)
                    {
                        var sourceLayer = NormalizeLayerName(sourceLayerNames[0]);
                        actualLayer = layerTableInfo.LayerNames.TryGetValue(sourceLayer, out existingLayer)
                            ? existingLayer
                            : sourceLayer;
                    }
                    actualLayer ??= requestedLayer;

                    if (!layerTableInfo.LayerNames.ContainsKey(actualLayer))
                    {
                        if (pendingLayerIndexByName.TryGetValue(actualLayer, out var pendingIndex))
                        {
                            if (!pendingLayers[pendingIndex].ColorHex.Equals(normalizedColor, StringComparison.OrdinalIgnoreCase)
                                || pendingLayers[pendingIndex].IsConstruction != path.IsConstruction)
                                return EditorDxfMergeResult.NotSupported;
                        }
                        else
                        {
                            pendingLayerIndexByName[actualLayer] = pendingLayers.Count;
                            pendingLayers.Add((actualLayer, normalizedColor, path.IsConstruction));
                        }
                    }
                    else if (!path.IsConstruction
                             && !normalizedColor.Equals("#4D7FFF", StringComparison.OrdinalIgnoreCase))
                    {
                        return EditorDxfMergeResult.NotSupported;
                    }
                    resolvedLayerByPathId[path.Id] = actualLayer;
                }
            }

            var dependencyDrafts = new List<(
                string TableName,
                string RecordType,
                string RecordName,
                DxfSymbolTableInfo? ExistingTable)>();
            var dependencyRequests = new List<(string TableName, string RecordType, string RecordName)>();
            if (document.Geometry.Paths.Any(NeedsPathstitchTextXData))
                dependencyRequests.Add(("APPID", "APPID", "PATHSTITCH"));
            if (pendingLayers.Any(static layer => !layer.IsConstruction))
                dependencyRequests.Add(("LTYPE", "LTYPE", "CONTINUOUS"));
            if (newPaths.Any(static path => path.IsConstruction))
                dependencyRequests.Add(("LTYPE", "LTYPE", "DASHED"));

            var requiredTextStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (newPaths.Any(path => path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)))
                requiredTextStyles.Add("STANDARD");
            foreach (var pair in currentByHandle)
            {
                if (!pair.Value.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
                    || GeometryMatches(sourcePreviewByHandle[pair.Key], pair.Value))
                {
                    continue;
                }
                if (!sourceByHandle.TryGetValue(pair.Key, out var sourceRecord)
                    || !TryReadTextStyleName(sourcePairs, sourceRecord, out var styleName))
                {
                    return EditorDxfMergeResult.NotSupported;
                }
                requiredTextStyles.Add(styleName);
            }
            foreach (var requiredStyle in requiredTextStyles)
            {
                if (requiredStyle.Equals("STANDARD", StringComparison.OrdinalIgnoreCase))
                {
                    dependencyRequests.Add(("STYLE", "STYLE", "STANDARD"));
                    continue;
                }
                if (!TryReadOptionalSymbolTableInfo(sourcePairs, "STYLE", "STYLE", out var styleTable)
                    || styleTable?.RecordNames.ContainsKey(requiredStyle) != true)
                {
                    return EditorDxfMergeResult.NotSupported;
                }
            }
            foreach (var request in dependencyRequests)
            {
                if (!TryReadOptionalSymbolTableInfo(
                        sourcePairs,
                        request.TableName,
                        request.RecordType,
                        out var tableInfo))
                {
                    return EditorDxfMergeResult.NotSupported;
                }
                if (tableInfo is not null
                    && request.TableName.Equals("LTYPE", StringComparison.OrdinalIgnoreCase)
                    && layerTableInfo is not null
                    && tableInfo.TableStartPairIndex >= layerTableInfo.TableStartPairIndex)
                {
                    return EditorDxfMergeResult.NotSupported;
                }
                if (tableInfo?.RecordNames.ContainsKey(request.RecordName) == true)
                {
                    if (request.RecordName.Equals("DASHED", StringComparison.OrdinalIgnoreCase)
                        && !IsValidSimpleDashedLinetype(sourcePairs, tableInfo.TableHandle))
                    {
                        return EditorDxfMergeResult.NotSupported;
                    }
                    continue;
                }
                dependencyDrafts.Add((request.TableName, request.RecordType, request.RecordName, tableInfo));
            }

            var missingDependencyTableCount = dependencyDrafts
                .Where(static dependency => dependency.ExistingTable is null)
                .Select(static dependency => dependency.TableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var allocationCount = pendingLayers.Count
                                  + newPaths.Count
                                  + dependencyDrafts.Count
                                  + missingDependencyTableCount;
            if (allocationCount > 0)
            {
                if (!TryCreateGlobalHandlePlan(sourcePairs, allocationCount, out handlePlan))
                    return EditorDxfMergeResult.NotSupported;
                var allocatedIndex = 0;
                foreach (var pendingLayer in pendingLayers)
                {
                    newLayerDefinitions.Add(new DxfNewLayerDefinition(
                        pendingLayer.Name,
                        pendingLayer.ColorHex,
                        handlePlan.AllocatedHandles[allocatedIndex++],
                        pendingLayer.IsConstruction));
                }
                foreach (var path in newPaths)
                {
                    var actualLayer = resolvedLayerByPathId[path.Id];
                    var handle = handlePlan.AllocatedHandles[allocatedIndex++];
                    var assignedPath = path with
                    {
                        SourceEntityHandle = handle,
                        SourceLayerName = actualLayer,
                    };
                    assignedPathsById[path.Id] = assignedPath;
                    newPathsByHandle[handle] = assignedPath;
                    mergePathMetadata[path.Id] = document.PathMetadata[path.Id] with
                    {
                        LayerName = actualLayer,
                        ColorHex = path.IsConstruction
                            ? "#808080"
                            : NormalizeColorHex(document.PathMetadata[path.Id].ColorHex),
                    };
                }
                var missingTableHandles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dependency in dependencyDrafts)
                {
                    var tableHandle = dependency.ExistingTable?.TableHandle;
                    if (tableHandle is null
                        && !missingTableHandles.TryGetValue(dependency.TableName, out tableHandle))
                    {
                        tableHandle = handlePlan.AllocatedHandles[allocatedIndex++];
                        missingTableHandles[dependency.TableName] = tableHandle;
                    }
                    var recordHandle = handlePlan.AllocatedHandles[allocatedIndex++];
                    dependencyPlans.Add(new DxfSymbolDependencyPlan(
                        dependency.TableName,
                        dependency.RecordType,
                        dependency.RecordName,
                        tableHandle,
                        recordHandle,
                        dependency.ExistingTable));
                }
                geometryToWrite = document.Geometry with
                {
                    Paths = document.Geometry.Paths.Select(path =>
                        assignedPathsById.TryGetValue(path.Id, out var assignedPath) ? assignedPath : path).ToArray(),
                };
            }
            SaveDocument(
                generatedPath,
                geometryToWrite,
                "EDITED_OUTPUT",
                options,
                mergePathMetadata,
                preserveSourceEntityHandles: true);

            var generatedText = Encoding.Latin1.GetString(File.ReadAllBytes(generatedPath));
            if (!TryReadRawDxfPairs(generatedText, out var generatedPairs)
                || !TryReadAcadVersion(generatedPairs, out var generatedVersion)
                || !string.Equals(sourceVersion, generatedVersion, StringComparison.OrdinalIgnoreCase)
                || !TryReadEntityRecords(generatedPairs, out var generatedRecords))
            {
                return EditorDxfMergeResult.NotSupported;
            }
            var generatedByHandle = BuildUniqueEntityHandleMap(generatedRecords);
            if (generatedByHandle is null)
                return EditorDxfMergeResult.NotSupported;
            foreach (var pair in newPathsByHandle)
            {
                if (!generatedByHandle.TryGetValue(pair.Key, out var generatedRecord)
                    || !generatedRecord.EntityType.Equals(pair.Value.EntityType, StringComparison.OrdinalIgnoreCase))
                {
                    return EditorDxfMergeResult.NotSupported;
                }
            }

            var deletedHandles = sourcePreviewByHandle.Keys
                .Where(handle => !currentByHandle.ContainsKey(handle))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var handle in deletedHandles)
            {
                if (!IsMergeSupportedEntityType(sourcePreviewByHandle[handle].EntityType)
                    || !sourceByHandle.TryGetValue(handle, out var sourceRecord)
                    || !IsMergeSupportedEntityType(sourceRecord.EntityType)
                    || HasExternalHandleReference(sourcePairs, sourceRecord, handle))
                {
                    return EditorDxfMergeResult.NotSupported;
                }
            }
            foreach (var pair in currentByHandle)
            {
                if (!sourceByHandle.TryGetValue(pair.Key, out var sourceRecord)
                    || !sourceRecord.EntityType.Equals(pair.Value.EntityType, StringComparison.OrdinalIgnoreCase))
                {
                    return EditorDxfMergeResult.NotSupported;
                }
                if (IsMergeSupportedEntityType(pair.Value.EntityType))
                {
                    var geometryMatches = GeometryMatches(sourcePreviewByHandle[pair.Key], pair.Value);

                    if (!geometryMatches
                        && pair.Value.EntityType.Equals("HATCH", StringComparison.OrdinalIgnoreCase)
                        && !IsSafeMergeHatchRecord(sourcePairs, sourceRecord))
                    {
                        return EditorDxfMergeResult.NotSupported;
                    }
                    if (!generatedByHandle.TryGetValue(pair.Key, out var generatedRecord)
                        || !generatedRecord.EntityType.Equals(pair.Value.EntityType, StringComparison.OrdinalIgnoreCase))
                    {
                        return EditorDxfMergeResult.NotSupported;
                    }
                }
                else if (!GeometryMatches(sourcePreviewByHandle[pair.Key], pair.Value))
                {
                    return EditorDxfMergeResult.NotSupported;
                }
            }

            foreach (var sourceRecord in sourceRecords.Where(record => IsMergeSupportedEntityType(record.EntityType)))
            {
                if (string.IsNullOrWhiteSpace(sourceRecord.Handle))
                    return EditorDxfMergeResult.NotSupported;
                var handle = NormalizeHandle(sourceRecord.Handle);
                if (!currentByHandle.ContainsKey(handle) && !deletedHandles.Contains(handle))
                    return EditorDxfMergeResult.NotSupported;
            }

            var replacements = new Dictionary<int, (int End, string Raw)>();
            var insertions = new Dictionary<int, string>();
            var hatchValidationToleranceByHandle = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var newline = sourceText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            foreach (var handle in deletedHandles)
            {
                var sourceRecord = sourceByHandle[handle];
                replacements[sourceRecord.Start] = (sourceRecord.End, string.Empty);
            }
            foreach (var pair in currentByHandle)
            {
                var sourceRecord = sourceByHandle[pair.Key];
                if (GeometryMatches(sourcePreviewByHandle[pair.Key], pair.Value))
                    continue;
                if (!IsMergeSupportedEntityType(pair.Value.EntityType))
                    return EditorDxfMergeResult.NotSupported;
                if (pair.Value.EntityType.Equals("HATCH", StringComparison.OrdinalIgnoreCase)
                    && (HasNonZeroHatchBulge(sourcePairs, sourceRecord)
                        || HasHatchEdgePath(sourcePairs, sourceRecord)))
                {
                    if (!TryBuildTransformedHatchRaw(
                            sourcePairs,
                            sourceRecord,
                            sourcePreviewByHandle[pair.Key],
                            pair.Value,
                            newline,
                            out var transformedHatchRaw,
                            out var hatchValidationTolerance))
                    {
                        return EditorDxfMergeResult.NotSupported;
                    }
                    replacements[sourceRecord.Start] = (sourceRecord.End, transformedHatchRaw);
                    hatchValidationToleranceByHandle[pair.Key] = hatchValidationTolerance;
                    continue;
                }
                var generatedRecord = generatedByHandle[pair.Key];
                replacements[sourceRecord.Start] = (
                    sourceRecord.End,
                    BuildMergedEntityRaw(sourcePairs, sourceRecord, generatedPairs, generatedRecord));
            }

            if (handlePlan is not null)
            {
                if (newPaths.Count > 0)
                {
                    var entitiesEndPairIndex = FindEntitiesEndPairIndex(sourcePairs);
                    if (entitiesEndPairIndex < 0 || modelSpaceOwner is null)
                        return EditorDxfMergeResult.NotSupported;
                    var newEntityRaw = string.Concat(newPaths.Select(path =>
                    {
                        var assignedPath = assignedPathsById[path.Id];
                        var generatedRecord = generatedByHandle[NormalizeHandle(assignedPath.SourceEntityHandle!)];
                        return BuildNewEntityRaw(generatedPairs, generatedRecord, modelSpaceOwner, newline);
                    }));
                    AppendInsertion(insertions, entitiesEndPairIndex, newEntityRaw);
                }

                if (newLayerDefinitions.Count > 0)
                {
                    if (layerTableInfo is null
                        || layerTableInfo.RecordCount > int.MaxValue - newLayerDefinitions.Count)
                    {
                        return EditorDxfMergeResult.NotSupported;
                    }
                    AppendInsertion(
                        insertions,
                        layerTableInfo.EndTablePairIndex,
                        string.Concat(newLayerDefinitions.Select(layer =>
                            BuildNewLayerRaw(layer, layerTableInfo.TableHandle, newline))));
                    var requiredLayerCapacity = layerTableInfo.RecordCount + newLayerDefinitions.Count;
                    if (requiredLayerCapacity > layerTableInfo.DeclaredCapacity)
                    {
                        replacements[layerTableInfo.CountPairIndex] = (
                            layerTableInfo.CountPairIndex + 1,
                            BuildRawPair(
                                newline,
                                70,
                                requiredLayerCapacity.ToString(CultureInfo.InvariantCulture)));
                    }
                }

                foreach (var dependencyGroup in dependencyPlans
                             .Where(static dependency => dependency.ExistingTable is not null)
                             .GroupBy(static dependency => dependency.TableHandle, StringComparer.OrdinalIgnoreCase))
                {
                    var dependencies = dependencyGroup.ToArray();
                    var existingTable = dependencies[0].ExistingTable!;
                    if (existingTable.RecordCount > int.MaxValue - dependencies.Length)
                        return EditorDxfMergeResult.NotSupported;
                    AppendInsertion(
                        insertions,
                        existingTable.EndTablePairIndex,
                        string.Concat(dependencies.Select(dependency =>
                            BuildNewSymbolRecordRaw(dependency, newline))));
                    var requiredCapacity = existingTable.RecordCount + dependencies.Length;
                    if (requiredCapacity > existingTable.DeclaredCapacity)
                    {
                        replacements[existingTable.CountPairIndex] = (
                            existingTable.CountPairIndex + 1,
                            BuildRawPair(
                                newline,
                                70,
                                requiredCapacity.ToString(CultureInfo.InvariantCulture)));
                    }
                }

                var tablesEndPairIndex = -1;
                foreach (var dependencyGroup in dependencyPlans
                             .Where(static dependency => dependency.ExistingTable is null)
                             .GroupBy(static dependency => dependency.TableHandle, StringComparer.OrdinalIgnoreCase))
                {
                    var dependencies = dependencyGroup.ToArray();
                    var dependency = dependencies[0];
                    var dependencyInsertionIndex = dependency.TableName.Equals(
                        "LTYPE",
                        StringComparison.OrdinalIgnoreCase)
                        ? layerTableInfo?.TableStartPairIndex ?? -1
                        : (tablesEndPairIndex = tablesEndPairIndex >= 0
                            ? tablesEndPairIndex
                            : FindTablesEndPairIndex(sourcePairs));
                    if (dependencyInsertionIndex < 0)
                        return EditorDxfMergeResult.NotSupported;
                    AppendInsertion(
                        insertions,
                        dependencyInsertionIndex,
                        BuildNewSymbolTableRaw(dependencies, newline));
                }

                var handseedRaw = BuildRawPair(newline, 5, handlePlan.NextHandseed);
                if (handlePlan.HandseedValuePairIndex is int handseedValuePairIndex)
                    replacements[handseedValuePairIndex] = (handseedValuePairIndex + 1, handseedRaw);
                else
                    AppendInsertion(
                        insertions,
                        handlePlan.HeaderEndPairIndex,
                        BuildRawPair(newline, 9, "$HANDSEED") + handseedRaw);
            }

            var output = new StringBuilder(sourceText.Length + 512);
            for (var index = 0; index < sourcePairs.Count;)
            {
                if (insertions.TryGetValue(index, out var insertion))
                    output.Append(insertion);
                if (replacements.TryGetValue(index, out var replacement))
                {
                    output.Append(replacement.Raw);
                    index = replacement.End;
                    continue;
                }
                output.Append(sourcePairs[index].Raw);
                index++;
            }
            WritePreservedDxf(candidatePath, output.ToString());
            if (!TryValidateMergedCandidate(
                    candidatePath,
                    geometryToWrite,
                    mergePathMetadata,
                    dependencyRequests,
                    newPathsByHandle,
                    hatchValidationToleranceByHandle,
                    out var canonicalPathsByPathId))
            {
                return EditorDxfMergeResult.NotSupported;
            }
            File.Move(candidatePath, fullOutputPath, overwrite: true);
            var provenanceByPathId = geometryToWrite.Paths.ToDictionary(
                path => path.Id,
                path => new EditorDxfEntityProvenance(
                    NormalizeHandle(path.SourceEntityHandle!),
                    mergePathMetadata.TryGetValue(path.Id, out var metadata)
                        ? metadata.LayerName
                        : path.SourceLayerName ?? "EDITED_OUTPUT"),
                StringComparer.Ordinal);
            return EditorDxfMergeResult.Success(
                provenanceByPathId,
                File.ReadAllBytes(outputPath),
                canonicalPathsByPathId);
        }
        catch (InvalidDataException)
        {
            return EditorDxfMergeResult.NotSupported;
        }
        catch (DecoderFallbackException)
        {
            return EditorDxfMergeResult.NotSupported;
        }
        finally
        {
            if (File.Exists(canonicalPath))
                File.Delete(canonicalPath);
            if (File.Exists(generatedPath))
                File.Delete(generatedPath);
            if (File.Exists(candidatePath))
                File.Delete(candidatePath);
        }
    }

    private static bool IsMergeSupportedEntityType(string entityType)
        => entityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
           || entityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
           || entityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
           || entityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
           || entityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
           || entityType.Equals("HATCH", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeHandle(string handle)
        => handle.Trim().ToUpperInvariant();
    private static bool TryCreateGlobalHandlePlan(
        IReadOnlyList<RawDxfPair> pairs,
        int allocationCount,
        out DxfHandleAllocationPlan plan)
    {
        plan = new DxfHandleAllocationPlan([], string.Empty, null, -1);
        var headerStart = -1;
        var headerEnd = -1;
        for (var index = 0; index + 1 < pairs.Count; index++)
        {
            if (pairs[index].Code == "0"
                && pairs[index].Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase)
                && pairs[index + 1].Code == "2"
                && pairs[index + 1].Value.Equals("HEADER", StringComparison.OrdinalIgnoreCase))
            {
                headerStart = index + 2;
                break;
            }
        }
        if (headerStart < 0)
            return false;
        for (var index = headerStart; index < pairs.Count; index++)
        {
            if (pairs[index].Code == "0" && pairs[index].Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
            {
                headerEnd = index;
                break;
            }
        }
        if (headerEnd < 0)
            return false;

        int? handseedValueIndex = null;
        ulong handseed = 1;
        for (var index = headerStart; index < headerEnd; index++)
        {
            if (pairs[index].Code != "9" || !pairs[index].Value.Equals("$HANDSEED", StringComparison.OrdinalIgnoreCase))
                continue;
            if (handseedValueIndex is not null
                || index + 1 >= headerEnd
                || pairs[index + 1].Code != "5"
                || !TryParseHandleValue(pairs[index + 1].Value, out handseed))
            {
                return false;
            }
            handseedValueIndex = index + 1;
        }

        var used = new HashSet<ulong>();
        ulong maxDefined = 0;
        for (var index = 0; index < pairs.Count; index++)
        {
            if (index >= headerStart && index < headerEnd)
                continue;
            if (pairs[index].Code is not ("5" or "105"))
                continue;
            if (!TryParseHandleValue(pairs[index].Value, out var handle) || handle == 0 || !used.Add(handle))
                return false;
            maxDefined = Math.Max(maxDefined, handle);
        }
        if (maxDefined == ulong.MaxValue)
            return false;
        var candidate = Math.Max(maxDefined + 1, handseed);
        if (candidate == 0)
            return false;
        var allocated = new List<string>(allocationCount);
        while (allocated.Count < allocationCount)
        {
            while (used.Contains(candidate))
            {
                if (candidate == ulong.MaxValue)
                    return false;
                candidate++;
            }
            allocated.Add(candidate.ToString("X", CultureInfo.InvariantCulture));
            used.Add(candidate);
            if (candidate == ulong.MaxValue)
                return false;
            candidate++;
        }
        plan = new DxfHandleAllocationPlan(
            allocated,
            candidate.ToString("X", CultureInfo.InvariantCulture),
            handseedValueIndex,
            headerEnd);
        return true;
    }

    private static bool TryReadLayerTableInfo(
        IReadOnlyList<RawDxfPair> pairs,
        out DxfLayerTableInfo info)
    {
        info = new DxfLayerTableInfo(string.Empty, 0, 0, -1, -1, -1,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var tableStarts = new List<int>();
        for (var index = 0; index + 1 < pairs.Count; index++)
        {
            if (pairs[index].Code == "0"
                && pairs[index].Value.Equals("TABLE", StringComparison.OrdinalIgnoreCase)
                && pairs[index + 1].Code == "2"
                && pairs[index + 1].Value.Equals("LAYER", StringComparison.OrdinalIgnoreCase))
            {
                tableStarts.Add(index);
            }
        }
        if (tableStarts.Count != 1)
            return false;

        var tableStart = tableStarts[0];
        var endTable = -1;
        for (var index = tableStart + 2; index < pairs.Count; index++)
        {
            if (pairs[index].Code == "0" && pairs[index].Value.Equals("ENDTAB", StringComparison.OrdinalIgnoreCase))
            {
                endTable = index;
                break;
            }
        }
        if (endTable < 0)
            return false;

        var firstRecord = endTable;
        for (var index = tableStart + 2; index < endTable; index++)
        {
            if (pairs[index].Code == "0")
            {
                firstRecord = index;
                break;
            }
        }
        string? tableHandle = null;
        int? declaredCount = null;
        var countPairIndex = -1;
        for (var index = tableStart + 2; index < firstRecord; index++)
        {
            if (pairs[index].Code == "5")
            {
                if (tableHandle is not null)
                    return false;
                tableHandle = NormalizeHandle(pairs[index].Value);
            }
            else if (pairs[index].Code == "70")
            {
                if (declaredCount is not null
                    || !int.TryParse(
                        pairs[index].Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedCount)
                    || parsedCount < 0)
                {
                    return false;
                }
                declaredCount = parsedCount;
                countPairIndex = index;
            }
        }
        if (string.IsNullOrWhiteSpace(tableHandle)
            || declaredCount is null
            || !TryParseHandleValue(tableHandle, out var parsedTableHandle)
            || parsedTableHandle == 0)
        {
            return false;
        }

        var layerNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var recordCount = 0;
        for (var start = firstRecord; start < endTable;)
        {
            if (pairs[start].Code != "0" || !pairs[start].Value.Equals("LAYER", StringComparison.OrdinalIgnoreCase))
                return false;
            var end = start + 1;
            while (end < endTable && pairs[end].Code != "0")
                end++;
            string? name = null;
            for (var index = start + 1; index < end; index++)
            {
                if (pairs[index].Code == "2" && name is null)
                    name = pairs[index].Value.Trim();
            }
            if (string.IsNullOrWhiteSpace(name) || !layerNames.TryAdd(name, name))
                return false;
            recordCount++;
            start = end;
        }
        if (recordCount > declaredCount.Value)
            return false;

        info = new DxfLayerTableInfo(
            tableHandle,
            declaredCount.Value,
            recordCount,
            countPairIndex,
            tableStart,
            endTable,
            layerNames);
        return true;
    }

    private static bool TryReadOptionalSymbolTableInfo(
        IReadOnlyList<RawDxfPair> pairs,
        string tableName,
        string recordType,
        out DxfSymbolTableInfo? info)
    {
        info = null;
        var tableStarts = new List<int>();
        for (var index = 0; index + 1 < pairs.Count; index++)
        {
            if (pairs[index].Code == "0"
                && pairs[index].Value.Equals("TABLE", StringComparison.OrdinalIgnoreCase)
                && pairs[index + 1].Code == "2"
                && pairs[index + 1].Value.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            {
                tableStarts.Add(index);
            }
        }
        if (tableStarts.Count == 0)
            return true;
        if (tableStarts.Count != 1)
            return false;

        var tableStart = tableStarts[0];
        var endTable = -1;
        for (var index = tableStart + 2; index < pairs.Count; index++)
        {
            if (pairs[index].Code == "0")
            {
                if (pairs[index].Value.Equals("ENDTAB", StringComparison.OrdinalIgnoreCase))
                    endTable = index;
                break;
            }
        }
        if (endTable < 0)
        {
            for (var index = tableStart + 2; index < pairs.Count; index++)
            {
                if (pairs[index].Code == "0"
                    && pairs[index].Value.Equals("ENDTAB", StringComparison.OrdinalIgnoreCase))
                {
                    endTable = index;
                    break;
                }
            }
        }
        if (endTable < 0)
            return false;

        var firstRecord = endTable;
        for (var index = tableStart + 2; index < endTable; index++)
        {
            if (pairs[index].Code == "0")
            {
                firstRecord = index;
                break;
            }
        }
        string? tableHandle = null;
        int? declaredCount = null;
        var countPairIndex = -1;
        for (var index = tableStart + 2; index < firstRecord; index++)
        {
            if (pairs[index].Code == "5")
            {
                if (tableHandle is not null)
                    return false;
                tableHandle = NormalizeHandle(pairs[index].Value);
            }
            else if (pairs[index].Code == "70")
            {
                if (declaredCount is not null
                    || !int.TryParse(
                        pairs[index].Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedCount)
                    || parsedCount < 0)
                {
                    return false;
                }
                declaredCount = parsedCount;
                countPairIndex = index;
            }
        }
        if (string.IsNullOrWhiteSpace(tableHandle)
            || declaredCount is null
            || !TryParseHandleValue(tableHandle, out var parsedTableHandle)
            || parsedTableHandle == 0)
        {
            return false;
        }

        var recordNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var recordCount = 0;
        for (var start = firstRecord; start < endTable;)
        {
            if (pairs[start].Code != "0"
                || !pairs[start].Value.Equals(recordType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var end = start + 1;
            while (end < endTable && pairs[end].Code != "0")
                end++;
            string? name = null;
            string? handle = null;
            string? owner = null;
            for (var index = start + 1; index < end; index++)
            {
                if (pairs[index].Code == "2")
                {
                    if (name is not null)
                        return false;
                    name = pairs[index].Value.Trim();
                }
                else if (pairs[index].Code == "5")
                {
                    if (handle is not null)
                        return false;
                    handle = NormalizeHandle(pairs[index].Value);
                }
                else if (pairs[index].Code == "330")
                {
                    if (owner is not null)
                        return false;
                    owner = NormalizeHandle(pairs[index].Value);
                }
            }
            if (string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(handle)
                || !TryParseHandleValue(handle, out var parsedRecordHandle)
                || parsedRecordHandle == 0
                || !string.Equals(owner, tableHandle, StringComparison.OrdinalIgnoreCase)
                || !recordNames.TryAdd(name, name))
            {
                return false;
            }
            recordCount++;
            start = end;
        }
        if (recordCount > declaredCount.Value)
            return false;

        info = new DxfSymbolTableInfo(
            tableHandle,
            declaredCount.Value,
            recordCount,
            countPairIndex,
            tableStart,
            endTable,
            recordNames);
        return true;
    }
    private static bool IsValidSimpleDashedLinetype(
        IReadOnlyList<RawDxfPair> pairs,
        string tableHandle)
    {
        for (var start = 0; start < pairs.Count; start++)
        {
            if (pairs[start].Code != "0"
                || !pairs[start].Value.Equals("LTYPE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var end = start + 1;
            while (end < pairs.Count && pairs[end].Code != "0")
                end++;
            var isDashed = false;
            var ownerMatches = false;
            int? elementCount = null;
            double? patternLength = null;
            int? alignment = null;
            var elementLengths = new List<double>();
            var elementTypes = new List<int>();
            var hasComplexDependency = false;
            for (var index = start + 1; index < end; index++)
            {
                var pair = pairs[index];
                switch (pair.Code)
                {
                    case "2":
                        isDashed = pair.Value.Trim().Equals("DASHED", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "330":
                        ownerMatches = NormalizeHandle(pair.Value).Equals(
                            NormalizeHandle(tableHandle),
                            StringComparison.OrdinalIgnoreCase);
                        break;
                    case "72":
                        if (alignment is not null
                            || !int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAlignment))
                            return false;
                        alignment = parsedAlignment;
                        break;
                    case "73":
                        if (elementCount is not null
                            || !int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount))
                            return false;
                        elementCount = parsedCount;
                        break;
                    case "40":
                        if (patternLength is not null || !TryParseDouble(pair.Value, out var parsedLength))
                            return false;
                        patternLength = parsedLength;
                        break;
                    case "49":
                        if (!TryParseDouble(pair.Value, out var elementLength))
                            return false;
                        elementLengths.Add(elementLength);
                        break;
                    case "74":
                        if (!int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var elementType))
                            return false;
                        elementTypes.Add(elementType);
                        break;
                    case "340":
                        hasComplexDependency = true;
                        break;
                }
            }
            if (!isDashed)
                continue;
            return ownerMatches
                   && alignment == 65
                   && elementCount is > 0
                   && elementCount == elementLengths.Count
                   && patternLength is > 0
                   && double.IsFinite(patternLength.Value)
                   && elementLengths.All(double.IsFinite)
                   && elementTypes.Count == elementCount
                   && elementTypes.All(static value => value == 0)
                   && !hasComplexDependency;
        }
        return false;
    }

    private static bool TryParseHandleValue(string value, out ulong handle)
        => ulong.TryParse(value.Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out handle);

    private static string? ResolveModelSpaceOwner(
        IReadOnlyList<RawDxfPair> pairs,
        IReadOnlyList<RawDxfEntityRecord> records)
    {
        var entityOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            for (var index = record.Start + 1; index < record.End; index++)
            {
                if (pairs[index].Code == "1001")
                    break;
                if (pairs[index].Code == "330" && !string.IsNullOrWhiteSpace(pairs[index].Value))
                    entityOwners.Add(NormalizeHandle(pairs[index].Value));
            }
        }
        if (entityOwners.Count > 1)
            return null;

        var modelSpaceBlockRecordHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var start = 0; start < pairs.Count; start++)
        {
            if (pairs[start].Code != "0"
                || !pairs[start].Value.Equals("BLOCK_RECORD", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var end = start + 1;
            while (end < pairs.Count && pairs[end].Code != "0")
                end++;
            string? handle = null;
            string? name = null;
            for (var index = start + 1; index < end; index++)
            {
                if (pairs[index].Code == "5" && handle is null)
                    handle = NormalizeHandle(pairs[index].Value);
                else if (pairs[index].Code == "2" && name is null)
                    name = pairs[index].Value.Trim();
            }
            if (name?.Equals("*Model_Space", StringComparison.OrdinalIgnoreCase) == true
                && !string.IsNullOrWhiteSpace(handle))
            {
                modelSpaceBlockRecordHandles.Add(handle);
            }
        }
        if (modelSpaceBlockRecordHandles.Count > 1)
            return null;

        var entityOwner = entityOwners.SingleOrDefault();
        var tableOwner = modelSpaceBlockRecordHandles.SingleOrDefault();
        if (entityOwner is not null && tableOwner is not null
            && !entityOwner.Equals(tableOwner, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var resolvedOwner = tableOwner ?? entityOwner;
        if (resolvedOwner is null)
            return null;
        return pairs.Any(pair =>
            pair.Code is "5" or "105"
            && NormalizeHandle(pair.Value).Equals(resolvedOwner, StringComparison.OrdinalIgnoreCase))
            ? resolvedOwner
            : null;
    }

    private static int FindTablesEndPairIndex(IReadOnlyList<RawDxfPair> pairs)
    {
        var tablesSectionStarts = new List<int>();
        for (var index = 0; index + 1 < pairs.Count; index++)
        {
            if (pairs[index].Code == "0"
                && pairs[index].Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase)
                && pairs[index + 1].Code == "2"
                && pairs[index + 1].Value.Equals("TABLES", StringComparison.OrdinalIgnoreCase))
            {
                tablesSectionStarts.Add(index);
            }
        }
        if (tablesSectionStarts.Count != 1)
            return -1;
        for (var index = tablesSectionStarts[0] + 2; index < pairs.Count; index++)
        {
            if (pairs[index].Code == "0"
                && pairs[index].Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
            if (pairs[index].Code == "0"
                && pairs[index].Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }
        }
        return -1;
    }
    private static int FindEntitiesEndPairIndex(IReadOnlyList<RawDxfPair> pairs)
    {
        var inEntities = false;
        for (var index = 0; index + 1 < pairs.Count; index++)
        {
            if (pairs[index].Code == "0"
                && pairs[index].Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase)
                && pairs[index + 1].Code == "2")
            {
                inEntities = pairs[index + 1].Value.Equals("ENTITIES", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (inEntities && pairs[index].Code == "0" && pairs[index].Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return -1;
    }

    private static string BuildRawPair(string newline, int code, string value)
        => string.Concat(code.ToString(CultureInfo.InvariantCulture), newline, value, newline);

    private static void AppendInsertion(
        IDictionary<int, string> insertions,
        int pairIndex,
        string raw)
    {
        insertions[pairIndex] = insertions.TryGetValue(pairIndex, out var existing)
            ? existing + raw
            : raw;
    }

    private static string BuildNewSymbolTableRaw(
        IReadOnlyList<DxfSymbolDependencyPlan> dependencies,
        string newline)
    {
        if (dependencies.Count == 0)
            throw new InvalidDataException("DXF symbol table requires at least one record.");
        var first = dependencies[0];
        if (dependencies.Any(dependency =>
                !dependency.TableName.Equals(first.TableName, StringComparison.OrdinalIgnoreCase)
                || !dependency.TableHandle.Equals(first.TableHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("DXF symbol table records do not share one table owner.");
        }
        return string.Concat(
            BuildRawPair(newline, 0, "TABLE"),
            BuildRawPair(newline, 2, first.TableName),
            BuildRawPair(newline, 5, first.TableHandle),
            BuildRawPair(newline, 330, "0"),
            BuildRawPair(newline, 100, "AcDbSymbolTable"),
            BuildRawPair(newline, 70, dependencies.Count.ToString(CultureInfo.InvariantCulture)),
            string.Concat(dependencies.Select(dependency => BuildNewSymbolRecordRaw(dependency, newline))),
            BuildRawPair(newline, 0, "ENDTAB"));
    }

    private static string BuildNewSymbolRecordRaw(
        DxfSymbolDependencyPlan dependency,
        string newline)
    {
        var common = string.Concat(
            BuildRawPair(newline, 0, dependency.RecordType),
            BuildRawPair(newline, 5, dependency.RecordHandle),
            BuildRawPair(newline, 330, dependency.TableHandle),
            BuildRawPair(newline, 100, "AcDbSymbolTableRecord"));
        return dependency.TableName.ToUpperInvariant() switch
        {
            "APPID" => string.Concat(
                common,
                BuildRawPair(newline, 100, "AcDbRegAppTableRecord"),
                BuildRawPair(newline, 2, dependency.RecordName),
                BuildRawPair(newline, 70, "0")),
            "LTYPE" when dependency.RecordName.Equals("DASHED", StringComparison.OrdinalIgnoreCase) => string.Concat(
                common,
                BuildRawPair(newline, 100, "AcDbLinetypeTableRecord"),
                BuildRawPair(newline, 2, dependency.RecordName),
                BuildRawPair(newline, 70, "0"),
                BuildRawPair(newline, 3, "Dashed __ __ __"),
                BuildRawPair(newline, 72, "65"),
                BuildRawPair(newline, 73, "2"),
                BuildRawPair(newline, 40, "0.75"),
                BuildRawPair(newline, 49, "0.5"),
                BuildRawPair(newline, 74, "0"),
                BuildRawPair(newline, 49, "-0.25"),
                BuildRawPair(newline, 74, "0")),
            "LTYPE" => string.Concat(
                common,
                BuildRawPair(newline, 100, "AcDbLinetypeTableRecord"),
                BuildRawPair(newline, 2, dependency.RecordName),
                BuildRawPair(newline, 70, "0"),
                BuildRawPair(newline, 3, "Solid line"),
                BuildRawPair(newline, 72, "65"),
                BuildRawPair(newline, 73, "0"),
                BuildRawPair(newline, 40, "0")),
            "STYLE" => string.Concat(
                common,
                BuildRawPair(newline, 100, "AcDbTextStyleTableRecord"),
                BuildRawPair(newline, 2, dependency.RecordName),
                BuildRawPair(newline, 70, "0"),
                BuildRawPair(newline, 40, "0"),
                BuildRawPair(newline, 41, "1"),
                BuildRawPair(newline, 50, "0"),
                BuildRawPair(newline, 71, "0"),
                BuildRawPair(newline, 42, "2.5"),
                BuildRawPair(newline, 3, "txt"),
                BuildRawPair(newline, 4, string.Empty)),
            _ => throw new InvalidDataException(
                $"Unsupported DXF symbol dependency table '{dependency.TableName}'."),
        };
    }
    private static string BuildNewLayerRaw(
        DxfNewLayerDefinition layer,
        string layerTableHandle,
        string newline)
        => string.Concat(
            BuildRawPair(newline, 0, "LAYER"),
            BuildRawPair(newline, 5, layer.Handle),
            BuildRawPair(newline, 330, layerTableHandle),
            BuildRawPair(newline, 100, "AcDbSymbolTableRecord"),
            BuildRawPair(newline, 100, "AcDbLayerTableRecord"),
            BuildRawPair(newline, 2, layer.Name),
            BuildRawPair(newline, 70, "0"),
            BuildRawPair(newline, 62, layer.IsConstruction ? "8" : "7"),
            layer.IsConstruction
                ? string.Empty
                : BuildRawPair(
                    newline,
                    420,
                    RgbTrueColor(layer.ColorHex).ToString(CultureInfo.InvariantCulture)),
            BuildRawPair(newline, 6, layer.IsConstruction ? "DASHED" : "CONTINUOUS"));

    private static string BuildNewEntityRaw(
        IReadOnlyList<RawDxfPair> generatedPairs,
        RawDxfEntityRecord generated,
        string ownerHandle,
        string newline)
    {
        var result = new StringBuilder();
        for (var index = generated.Start; index < generated.End; index++)
        {
            result.Append(generatedPairs[index].Raw);
            if (index == generated.Start + 1 && generatedPairs[index].Code == "5")
                result.Append(BuildRawPair(newline, 330, ownerHandle));
        }
        return result.ToString();
    }
    private static bool HasHatchEdgePath(
        IReadOnlyList<RawDxfPair> pairs,
        RawDxfEntityRecord record)
    {
        for (var index = record.Start + 1; index < record.End; index++)
        {
            if (pairs[index].Code == "1001")
                break;
            if (pairs[index].Code == "92"
                && int.TryParse(pairs[index].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags)
                && (flags & 2) == 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasNonZeroHatchBulge(
        IReadOnlyList<RawDxfPair> pairs,
        RawDxfEntityRecord record)
    {
        for (var index = record.Start + 1; index < record.End; index++)
        {
            if (pairs[index].Code == "1001")
                break;
            if (pairs[index].Code == "42"
                && TryParseDouble(pairs[index].Value, out var bulge)
                && Math.Abs(bulge) > 1e-12)
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryBuildTransformedHatchRaw(
        IReadOnlyList<RawDxfPair> sourcePairs,
        RawDxfEntityRecord sourceRecord,
        DxfPreviewPath sourcePreview,
        Editor2DPreviewPath current,
        string newline,
        out string raw,
        out double candidateValidationTolerance)
    {
        raw = string.Empty;
        candidateValidationTolerance = 0.0;
        var declaredLoopCount = -1;
        var actualLoopCount = 0;
        for (var index = sourceRecord.Start + 1; index < sourceRecord.End; index++)
        {
            if (sourcePairs[index].Code == "1001")
                break;
            if (sourcePairs[index].Code == "91")
            {
                if (declaredLoopCount >= 0
                    || !int.TryParse(
                        sourcePairs[index].Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out declaredLoopCount)
                    || declaredLoopCount <= 0)
                {
                    return false;
                }
            }
            else if (sourcePairs[index].Code == "92")
            {
                actualLoopCount++;
            }
        }
        if (declaredLoopCount != actualLoopCount)
            return false;
        return HasHatchEdgePath(sourcePairs, sourceRecord)
            ? TryBuildTransformedEdgeHatchRaw(
                sourcePairs, sourceRecord, sourcePreview, current, newline, out raw, out candidateValidationTolerance)
            : TryBuildTransformedPolylineHatchRaw(
                sourcePairs, sourceRecord, sourcePreview, current, newline, out raw, out candidateValidationTolerance);
    }

    private static bool TryBuildTransformedPolylineHatchRaw(
        IReadOnlyList<RawDxfPair> sourcePairs,
        RawDxfEntityRecord sourceRecord,
        DxfPreviewPath sourcePreview,
        Editor2DPreviewPath current,
        string newline,
        out string raw,
        out double candidateValidationTolerance)
    {
        raw = string.Empty;
        candidateValidationTolerance = 0.0;
        if (!TryInferHatchSimilarity(sourcePreview, current, out var transform))
            return false;
        candidateValidationTolerance = 0.101 * Math.Max(1.0, transform.Scale);
        var replacements = new Dictionary<int, string>();
        var inPolylineBoundary = false;
        var transformedVertexCount = 0;
        for (var index = sourceRecord.Start + 1; index < sourceRecord.End; index++)
        {
            var pair = sourcePairs[index];
            if (pair.Code == "1001")
                break;
            if (pair.Code == "92")
            {
                if (!int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags)
                    || (flags & 2) == 0)
                {
                    return false;
                }
                inPolylineBoundary = true;
                continue;
            }
            if (pair.Code == "75")
            {
                inPolylineBoundary = false;
                continue;
            }
            if (!inPolylineBoundary)
                continue;
            if (pair.Code == "10")
            {
                var yIndex = index + 1;
                while (yIndex < sourceRecord.End
                       && sourcePairs[yIndex].Code is not ("10" or "92" or "75")
                       && sourcePairs[yIndex].Code != "20")
                {
                    yIndex++;
                }
                if (yIndex >= sourceRecord.End
                    || sourcePairs[yIndex].Code != "20"
                    || !TryParseDouble(pair.Value, out var x)
                    || !TryParseDouble(sourcePairs[yIndex].Value, out var y))
                {
                    return false;
                }
                var transformed = transform.Transform(new DxfPoint(x, y));
                if (!double.IsFinite(transformed.X) || !double.IsFinite(transformed.Y))
                    return false;
                replacements[index] = BuildRawPair(newline, 10, Format(transformed.X));
                replacements[yIndex] = BuildRawPair(newline, 20, Format(transformed.Y));
                transformedVertexCount++;
            }
            else if (pair.Code == "42" && transform.IsReflection)
            {
                if (!TryParseDouble(pair.Value, out var bulge))
                    return false;
                replacements[index] = BuildRawPair(newline, 42, Format(-bulge));
            }
        }
        if (transformedVertexCount < 2)
            return false;
        var builder = new StringBuilder();
        for (var index = sourceRecord.Start; index < sourceRecord.End; index++)
            builder.Append(replacements.TryGetValue(index, out var replacement) ? replacement : sourcePairs[index].Raw);
        raw = builder.ToString();
        return true;
    }

    private static bool TryBuildTransformedEdgeHatchRaw(
        IReadOnlyList<RawDxfPair> sourcePairs,
        RawDxfEntityRecord sourceRecord,
        DxfPreviewPath sourcePreview,
        Editor2DPreviewPath current,
        string newline,
        out string raw,
        out double candidateValidationTolerance)
    {
        raw = string.Empty;
        candidateValidationTolerance = 0.0;
        if (!TryInferHatchSimilarity(sourcePreview, current, out var transform))
            return false;
        candidateValidationTolerance = 0.101 * Math.Max(1.0, transform.Scale);
        var replacements = new Dictionary<int, string>();
        var parsedLoopCount = 0;
        for (var boundaryStart = sourceRecord.Start + 1; boundaryStart < sourceRecord.End; boundaryStart++)
        {
            if (sourcePairs[boundaryStart].Code != "92")
                continue;
            if (!int.TryParse(
                    sourcePairs[boundaryStart].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var boundaryFlags))
            {
                return false;
            }
            var boundaryEnd = boundaryStart + 1;
            while (boundaryEnd < sourceRecord.End
                   && sourcePairs[boundaryEnd].Code is not ("92" or "75"))
            {
                boundaryEnd++;
            }
            var boundaryReferenceIndex = -1;
            for (var index = boundaryEnd - 1; index > boundaryStart; index--)
            {
                if (sourcePairs[index].Code == "97")
                {
                    boundaryReferenceIndex = index;
                    break;
                }
            }
            if (boundaryReferenceIndex < 0 || sourcePairs[boundaryReferenceIndex].Value.Trim() != "0")
                return false;

            if ((boundaryFlags & 2) != 0)
            {
                if (!TransformCoordinatePairs(boundaryStart + 1, boundaryReferenceIndex, "10", "20", asVector: false)
                    || !TransformBulges(boundaryStart + 1, boundaryReferenceIndex))
                {
                    return false;
                }
                parsedLoopCount++;
                boundaryStart = boundaryEnd - 1;
                continue;
            }

            var edgeCount = -1;
            var edgeHeaderEnd = -1;
            for (var index = boundaryStart + 1; index < boundaryReferenceIndex; index++)
            {
                if (sourcePairs[index].Code == "93")
                {
                    if (edgeCount >= 0
                        || !int.TryParse(
                            sourcePairs[index].Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out edgeCount)
                        || edgeCount <= 0)
                    {
                        return false;
                    }
                    edgeHeaderEnd = index + 1;
                }
            }
            if (edgeHeaderEnd < 0)
                return false;
            var edgeStarts = new List<int>();
            for (var index = edgeHeaderEnd; index < boundaryReferenceIndex; index++)
            {
                if (sourcePairs[index].Code == "72")
                    edgeStarts.Add(index);
            }
            if (edgeStarts.Count != edgeCount)
                return false;
            for (var edgeIndex = 0; edgeIndex < edgeStarts.Count; edgeIndex++)
            {
                var edgeStart = edgeStarts[edgeIndex];
                var edgeEnd = edgeIndex + 1 < edgeStarts.Count
                    ? edgeStarts[edgeIndex + 1]
                    : boundaryReferenceIndex;
                if (!int.TryParse(
                        sourcePairs[edgeStart].Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var edgeType))
                {
                    return false;
                }
                switch (edgeType)
                {
                    case 1:
                        if (!TransformSinglePoint(edgeStart + 1, edgeEnd, "10", "20")
                            || !TransformSinglePoint(edgeStart + 1, edgeEnd, "11", "21"))
                            return false;
                        break;
                    case 2:
                        if (!TransformSinglePoint(edgeStart + 1, edgeEnd, "10", "20")
                            || !TransformPositiveScalar(edgeStart + 1, edgeEnd, "40")
                            || !TransformDirectionAngle(edgeStart + 1, edgeEnd, "50")
                            || !TransformDirectionAngle(edgeStart + 1, edgeEnd, "51")
                            || !ToggleDirectionFlag(edgeStart + 1, edgeEnd))
                            return false;
                        break;
                    case 3:
                        if (!TransformSinglePoint(edgeStart + 1, edgeEnd, "10", "20")
                            || !TransformSingleVector(edgeStart + 1, edgeEnd, "11", "21")
                            || !ValidatePositiveScalar(edgeStart + 1, edgeEnd, "40")
                            || !TransformEllipseParameter(edgeStart + 1, edgeEnd, "50")
                            || !TransformEllipseParameter(edgeStart + 1, edgeEnd, "51")
                            || !ToggleDirectionFlag(edgeStart + 1, edgeEnd))
                            return false;
                        break;
                    case 4:
                        if (!TransformCoordinatePairs(edgeStart + 1, edgeEnd, "10", "20", asVector: false)
                            || !TransformOptionalCoordinatePairs(edgeStart + 1, edgeEnd, "11", "21", asVector: false)
                            || !TransformOptionalCoordinatePairs(edgeStart + 1, edgeEnd, "12", "22", asVector: true)
                            || !TransformOptionalCoordinatePairs(edgeStart + 1, edgeEnd, "13", "23", asVector: true))
                            return false;
                        break;
                    default:
                        return false;
                }
            }
            parsedLoopCount++;
            boundaryStart = boundaryEnd - 1;
        }
        if (parsedLoopCount == 0)
            return false;
        var builder = new StringBuilder();
        for (var index = sourceRecord.Start; index < sourceRecord.End; index++)
            builder.Append(replacements.TryGetValue(index, out var replacement) ? replacement : sourcePairs[index].Raw);
        raw = builder.ToString();
        return true;

        bool TransformSinglePoint(int start, int end, string xCode, string yCode)
            => CountCode(start, end, xCode) == 1
               && CountCode(start, end, yCode) == 1
               && TransformCoordinatePairs(start, end, xCode, yCode, asVector: false);

        bool TransformSingleVector(int start, int end, string xCode, string yCode)
            => CountCode(start, end, xCode) == 1
               && CountCode(start, end, yCode) == 1
               && TransformCoordinatePairs(start, end, xCode, yCode, asVector: true);

        bool TransformOptionalCoordinatePairs(int start, int end, string xCode, string yCode, bool asVector)
        {
            var xCount = CountCode(start, end, xCode);
            var yCount = CountCode(start, end, yCode);
            return xCount == yCount
                   && (xCount == 0 || TransformCoordinatePairs(start, end, xCode, yCode, asVector));
        }

        bool TransformCoordinatePairs(int start, int end, string xCode, string yCode, bool asVector)
        {
            var transformedCount = 0;
            for (var index = start; index < end; index++)
            {
                if (sourcePairs[index].Code != xCode)
                    continue;
                var yIndex = index + 1;
                while (yIndex < end
                       && sourcePairs[yIndex].Code != xCode
                       && sourcePairs[yIndex].Code != yCode)
                    yIndex++;
                if (yIndex >= end
                    || sourcePairs[yIndex].Code != yCode
                    || !TryParseDouble(sourcePairs[index].Value, out var x)
                    || !TryParseDouble(sourcePairs[yIndex].Value, out var y))
                    return false;
                var transformed = asVector
                    ? new DxfPoint(
                        (transform.M11 * x) + (transform.M12 * y),
                        (transform.M21 * x) + (transform.M22 * y))
                    : transform.Transform(new DxfPoint(x, y));
                if (!double.IsFinite(transformed.X) || !double.IsFinite(transformed.Y))
                    return false;
                replacements[index] = BuildRawPair(newline, int.Parse(xCode, CultureInfo.InvariantCulture), Format(transformed.X));
                replacements[yIndex] = BuildRawPair(newline, int.Parse(yCode, CultureInfo.InvariantCulture), Format(transformed.Y));
                transformedCount++;
            }
            return transformedCount > 0;
        }

        bool TransformBulges(int start, int end)
        {
            for (var index = start; index < end; index++)
            {
                if (sourcePairs[index].Code != "42")
                    continue;
                if (!TryParseDouble(sourcePairs[index].Value, out var bulge))
                    return false;
                if (transform.IsReflection)
                    replacements[index] = BuildRawPair(newline, 42, Format(-bulge));
            }
            return true;
        }

        bool TransformPositiveScalar(int start, int end, string code)
        {
            var indices = Enumerable.Range(start, end - start)
                .Where(index => sourcePairs[index].Code == code)
                .ToArray();
            if (indices.Length != 1
                || !TryParseDouble(sourcePairs[indices[0]].Value, out var value)
                || !double.IsFinite(value)
                || value <= 0
                || !double.IsFinite(value * transform.Scale))
            {
                return false;
            }
            replacements[indices[0]] = BuildRawPair(
                newline,
                int.Parse(code, CultureInfo.InvariantCulture),
                Format(value * transform.Scale));
            return true;
        }

        bool ValidatePositiveScalar(int start, int end, string code)
        {
            var indices = Enumerable.Range(start, end - start)
                .Where(index => sourcePairs[index].Code == code)
                .ToArray();
            return indices.Length == 1
                   && TryParseDouble(sourcePairs[indices[0]].Value, out var value)
                   && double.IsFinite(value)
                   && value > 0;
        }

        bool TransformDirectionAngle(int start, int end, string code)
        {
            var indices = Enumerable.Range(start, end - start)
                .Where(index => sourcePairs[index].Code == code)
                .ToArray();
            if (indices.Length != 1 || !TryParseDouble(sourcePairs[indices[0]].Value, out var degrees))
                return false;
            var radians = degrees * (Math.PI / 180.0);
            var x = (transform.M11 * Math.Cos(radians)) + (transform.M12 * Math.Sin(radians));
            var y = (transform.M21 * Math.Cos(radians)) + (transform.M22 * Math.Sin(radians));
            var transformedDegrees = Math.Atan2(y, x) * (180.0 / Math.PI);
            if (Math.Abs(transformedDegrees) < 5e-4)
                transformedDegrees = 0.0;
            replacements[indices[0]] = BuildRawPair(
                newline,
                int.Parse(code, CultureInfo.InvariantCulture),
                Format(transformedDegrees));
            return true;
        }

        bool TransformEllipseParameter(int start, int end, string code)
        {
            var indices = Enumerable.Range(start, end - start)
                .Where(index => sourcePairs[index].Code == code)
                .ToArray();
            if (indices.Length != 1 || !TryParseDouble(sourcePairs[indices[0]].Value, out var degrees))
                return false;
            if (transform.IsReflection)
            {
                replacements[indices[0]] = BuildRawPair(
                    newline,
                    int.Parse(code, CultureInfo.InvariantCulture),
                    Format(-degrees));
            }
            return true;
        }

        bool ToggleDirectionFlag(int start, int end)
        {
            var indices = Enumerable.Range(start, end - start)
                .Where(index => sourcePairs[index].Code == "73")
                .ToArray();
            if (indices.Length != 1 || sourcePairs[indices[0]].Value.Trim() is not ("0" or "1"))
                return false;
            if (transform.IsReflection)
            {
                replacements[indices[0]] = BuildRawPair(
                    newline,
                    73,
                    sourcePairs[indices[0]].Value.Trim() == "0" ? "1" : "0");
            }
            return true;
        }

        int CountCode(int start, int end, string code)
            => Enumerable.Range(start, end - start).Count(index => sourcePairs[index].Code == code);
    }

    private static bool TryInferHatchSimilarity(
        DxfPreviewPath source,
        Editor2DPreviewPath current,
        out DxfPlanarSimilarity transform)
    {
        transform = default;
        if (source.FillLoops is null
            || current.FillLoops is null
            || source.FillLoops.Count != current.FillLoops.Count)
        {
            return false;
        }
        var sourcePoints = new List<DxfPoint>();
        var currentPoints = new List<Editor2DPoint>();
        for (var loopIndex = 0; loopIndex < source.FillLoops.Count; loopIndex++)
        {
            if (source.FillLoops[loopIndex].Count != current.FillLoops[loopIndex].Count)
                return false;
            sourcePoints.AddRange(source.FillLoops[loopIndex]);
            currentPoints.AddRange(current.FillLoops[loopIndex]);
        }
        if (sourcePoints.Count < 3
            || !source.FillLoops.Any(loop => Math.Abs(SignedArea(loop)) > 1e-10))
        {
            return false;
        }
        var secondIndex = -1;
        for (var index = 1; index < sourcePoints.Count; index++)
        {
            var dx = sourcePoints[index].X - sourcePoints[0].X;
            var dy = sourcePoints[index].Y - sourcePoints[0].Y;
            if ((dx * dx) + (dy * dy) > 1e-16)
            {
                secondIndex = index;
                break;
            }
        }
        if (secondIndex < 0)
            return false;
        var sourceDx = sourcePoints[secondIndex].X - sourcePoints[0].X;
        var sourceDy = sourcePoints[secondIndex].Y - sourcePoints[0].Y;
        var currentDx = currentPoints[secondIndex].X - currentPoints[0].X;
        var currentDy = currentPoints[secondIndex].Y - currentPoints[0].Y;
        var sourceLengthSquared = (sourceDx * sourceDx) + (sourceDy * sourceDy);
        var candidates = new[]
        {
            CreateSimilarity(
                (currentDx * sourceDx + currentDy * sourceDy) / sourceLengthSquared,
                (currentDy * sourceDx - currentDx * sourceDy) / sourceLengthSquared,
                isReflection: false),
            CreateSimilarity(
                (currentDx * sourceDx - currentDy * sourceDy) / sourceLengthSquared,
                (currentDx * sourceDy + currentDy * sourceDx) / sourceLengthSquared,
                isReflection: true),
        };
        foreach (var linear in candidates)
        {
            var offsetX = currentPoints[0].X - ((linear.M11 * sourcePoints[0].X) + (linear.M12 * sourcePoints[0].Y));
            var offsetY = currentPoints[0].Y - ((linear.M21 * sourcePoints[0].X) + (linear.M22 * sourcePoints[0].Y));
            var candidate = linear with { OffsetX = offsetX, OffsetY = offsetY };
            if (!double.IsFinite(candidate.Scale) || candidate.Scale <= 1e-12)
                continue;
            if (sourcePoints.Select(candidate.Transform).Zip(currentPoints).All(pair =>
                    ValuesMatch(pair.First.X, pair.Second.X)
                    && ValuesMatch(pair.First.Y, pair.Second.Y)))
            {
                transform = candidate;
                return true;
            }
        }
        return false;

        static DxfPlanarSimilarity CreateSimilarity(double a, double b, bool isReflection)
        {
            var scale = Math.Sqrt((a * a) + (b * b));
            return isReflection
                ? new DxfPlanarSimilarity(a, b, b, -a, 0, 0, scale, true)
                : new DxfPlanarSimilarity(a, -b, b, a, 0, 0, scale, false);
        }
    }

    private static bool IsSafeMergeHatchRecord(
        IReadOnlyList<RawDxfPair> pairs,
        RawDxfEntityRecord record)
    {
        var solidName = false;
        var solidFill = false;
        var nonAssociative = false;
        var elevationIsZero = true;
        var extrusionXIsZero = true;
        var extrusionYIsZero = true;
        var extrusionZIsOne = true;
        var loopCount = 0;
        for (var index = record.Start + 1; index < record.End; index++)
        {
            var pair = pairs[index];
            if (pair.Code == "1001")
                break;
            switch (pair.Code)
            {
                case "2":
                    solidName = pair.Value.Equals("SOLID", StringComparison.OrdinalIgnoreCase);
                    break;
                case "70":
                    solidFill = pair.Value.Trim() == "1";
                    break;
                case "71":
                    nonAssociative = pair.Value.Trim() == "0";
                    break;
                case "30":
                    elevationIsZero = TryParseDouble(pair.Value.Trim(), out var elevation)
                                      && Math.Abs(elevation) <= 1e-9;
                    break;
                case "210":
                    extrusionXIsZero = TryParseDouble(pair.Value.Trim(), out var extrusionX)
                                       && Math.Abs(extrusionX) <= 1e-9;
                    break;
                case "220":
                    extrusionYIsZero = TryParseDouble(pair.Value.Trim(), out var extrusionY)
                                       && Math.Abs(extrusionY) <= 1e-9;
                    break;
                case "230":
                    extrusionZIsOne = TryParseDouble(pair.Value.Trim(), out var extrusionZ)
                                      && Math.Abs(extrusionZ - 1.0) <= 1e-9;
                    break;
                case "92":
                    if (!int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        return false;
                    loopCount++;
                    break;
                case "72":
                    if (!int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var boundaryValue)
                        || boundaryValue is < 0 or > 4)
                    {
                        return false;
                    }
                    break;
                case "42":
                    if (!TryParseDouble(pair.Value.Trim(), out var bulge)
                        || !double.IsFinite(bulge))
                    {
                        return false;
                    }
                    break;
                case "98":
                    if (pair.Value.Trim() != "0")
                        return false;
                    break;
            }
        }
        return solidName
               && solidFill
               && nonAssociative
               && elevationIsZero
               && extrusionXIsZero
               && extrusionYIsZero
               && extrusionZIsOne
               && loopCount > 0;
    }

    private static bool TryReadTextStyleName(
        IReadOnlyList<RawDxfPair> pairs,
        RawDxfEntityRecord record,
        out string styleName)
    {
        styleName = "STANDARD";
        var found = false;
        for (var index = record.Start + 1; index < record.End; index++)
        {
            if (pairs[index].Code == "1001")
                break;
            if (pairs[index].Code != "7")
                continue;
            if (found || string.IsNullOrWhiteSpace(pairs[index].Value))
                return false;
            styleName = pairs[index].Value.Trim();
            found = true;
        }
        return true;
    }
    private static bool HasAppIdRecord(IReadOnlyList<RawDxfPair> pairs, string applicationName)
    {
        for (var start = 0; start < pairs.Count; start++)
        {
            if (pairs[start].Code != "0"
                || !pairs[start].Value.Equals("APPID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var end = start + 1;
            while (end < pairs.Count && pairs[end].Code != "0")
                end++;
            for (var index = start + 1; index < end; index++)
            {
                if (pairs[index].Code == "2"
                    && pairs[index].Value.Equals(applicationName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasPathstitchTextXData(
        IReadOnlyList<RawDxfPair> pairs,
        RawDxfEntityRecord record)
    {
        for (var index = record.Start + 1; index < record.End; index++)
        {
            if (pairs[index].Code == "1001"
                && pairs[index].Value.Equals("PATHSTITCH", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasExternalHandleReference(
        IReadOnlyList<RawDxfPair> pairs,
        RawDxfEntityRecord owningRecord,
        string handle)
    {
        for (var index = 0; index < pairs.Count; index++)
        {
            if (index >= owningRecord.Start && index < owningRecord.End)
                continue;
            if (!IsHandleReferenceCode(pairs[index].Code)
                || string.IsNullOrWhiteSpace(pairs[index].Value))
            {
                continue;
            }
            if (NormalizeHandle(pairs[index].Value).Equals(handle, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsHandleReferenceCode(string code)
    {
        if (code == "1005")
            return true;
        if (!int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericCode))
            return false;
        return numericCode is >= 330 and <= 369
            or >= 390 and <= 399
            or 480 or 481;
    }

    private static string NormalizeRawGroupCode(string content, int rawLineIndex)
    {
        var code = content.Trim();
        if (rawLineIndex != 0)
            return code;
        code = code.TrimStart('\uFEFF');
        return code.StartsWith("ï»¿", StringComparison.Ordinal) ? code[3..] : code;
    }
    private static bool TryReadRawDxfPairs(string text, out IReadOnlyList<RawDxfPair> pairs)
    {
        var rawLines = SplitRawDxfLines(text);
        if (rawLines.Count == 0 || rawLines.Count % 2 != 0)
        {
            pairs = [];
            return false;
        }
        var parsed = new List<RawDxfPair>(rawLines.Count / 2);
        for (var index = 0; index < rawLines.Count; index += 2)
        {
            var code = NormalizeRawGroupCode(rawLines[index].Content, index);
            if (!int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                pairs = [];
                return false;
            }
            parsed.Add(new RawDxfPair(
                NormalizeRawGroupCode(rawLines[index].Content, index),
                rawLines[index + 1].Content.Trim(),
                rawLines[index].Raw + rawLines[index + 1].Raw));
        }
        pairs = parsed;
        return true;
    }

    private static bool TryReadAcadVersion(IReadOnlyList<RawDxfPair> pairs, out string version)
    {
        for (var index = 0; index + 1 < pairs.Count; index++)
        {
            if (pairs[index].Code == "9"
                && pairs[index].Value.Equals("$ACADVER", StringComparison.OrdinalIgnoreCase)
                && pairs[index + 1].Code == "1")
            {
                version = pairs[index + 1].Value.ToUpperInvariant();
                return true;
            }
        }
        version = string.Empty;
        return false;
    }

    private static bool TryReadEntityRecords(
        IReadOnlyList<RawDxfPair> pairs,
        out IReadOnlyList<RawDxfEntityRecord> records)
    {
        var entitiesStart = -1;
        var entitiesEnd = -1;
        for (var index = 0; index + 1 < pairs.Count; index++)
        {
            if (pairs[index].Code == "0"
                && pairs[index].Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase)
                && pairs[index + 1].Code == "2"
                && pairs[index + 1].Value.Equals("ENTITIES", StringComparison.OrdinalIgnoreCase))
            {
                entitiesStart = index + 2;
                break;
            }
        }
        if (entitiesStart < 0)
        {
            records = [];
            return false;
        }
        for (var index = entitiesStart; index < pairs.Count; index++)
        {
            if (pairs[index].Code == "0" && pairs[index].Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
            {
                entitiesEnd = index;
                break;
            }
        }
        if (entitiesEnd < 0)
        {
            records = [];
            return false;
        }

        var parsed = new List<RawDxfEntityRecord>();
        for (var start = entitiesStart; start < entitiesEnd;)
        {
            if (pairs[start].Code != "0")
            {
                records = [];
                return false;
            }
            var end = start + 1;
            while (end < entitiesEnd && pairs[end].Code != "0")
                end++;
            string? handle = null;
            for (var index = start + 1; index < end; index++)
            {
                if (pairs[index].Code == "5")
                {
                    handle = pairs[index].Value;
                    break;
                }
            }
            parsed.Add(new RawDxfEntityRecord(start, end, pairs[start].Value, handle));
            start = end;
        }
        records = parsed;
        return true;
    }

    private static Dictionary<string, RawDxfEntityRecord>? BuildUniqueEntityHandleMap(
        IReadOnlyList<RawDxfEntityRecord> records)
    {
        var result = new Dictionary<string, RawDxfEntityRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Handle))
                continue;
            if (!result.TryAdd(NormalizeHandle(record.Handle), record))
                return null;
        }
        return result;
    }

    private static bool GeometryMatches(DxfPreviewPath source, Editor2DPreviewPath current)
    {
        if (!source.EntityType.Equals(current.EntityType, StringComparison.OrdinalIgnoreCase)
            || source.IsClosed != current.IsClosed)
            return false;
        if (source.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
        {
            return PointsMatch(source.Start, current.Start)
                   && string.Equals(source.Text, current.Text, StringComparison.Ordinal)
                   && ValuesMatch(source.TextHeight, current.TextHeight)
                   && ValuesMatch(source.RotationDegrees, current.RotationDegrees)
                   && ValuesMatch(source.WidthFactor, current.WidthFactor)
                   && TextBasesMatch(source, current)
                   && string.Equals(source.FontFamily, current.FontFamily, StringComparison.Ordinal)
                   && ValuesMatch(source.CharacterSpacing, current.CharacterSpacing)
                   && source.IsBold == current.IsBold
                   && source.IsItalic == current.IsItalic
                   && source.IsUnderline == current.IsUnderline;
        }
        if (source.IsFilled != current.IsFilled)
            return false;
        if (source.IsFilled && !LoopsMatch(source.FillLoops, current.FillLoops))
            return false;
        if (source.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase))
            return PointsMatch(source.Center, current.Center)
                   && ValuesMatch(source.Radius, current.Radius);
        if (source.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase))
            return PointsMatch(source.Center, current.Center)
                   && ValuesMatch(source.Radius, current.Radius)
                   && ValuesMatch(source.StartAngleDegrees, current.StartAngleDegrees)
                   && ValuesMatch(source.EndAngleDegrees, current.EndAngleDegrees);
        return source.Points.Count == current.Points.Count
               && source.Points.Zip(current.Points).All(pair => PointsMatch(pair.First, pair.Second));
    }

    private static bool HasCanonicalConstructionStyle(
        IReadOnlyList<RawDxfPair> pairs,
        RawDxfEntityRecord record)
    {
        string? layer = null;
        string? lineType = null;
        string? color = null;
        string? owner = null;
        for (var index = record.Start + 1; index < record.End; index++)
        {
            var pair = pairs[index];
            if (pair.Code == "1001")
                break;
            switch (pair.Code)
            {
                case "8":
                    if (layer is not null)
                        return false;
                    layer = pair.Value.Trim();
                    break;
                case "6":
                    if (lineType is not null)
                        return false;
                    lineType = pair.Value.Trim();
                    break;
                case "62":
                    if (color is not null)
                        return false;
                    color = pair.Value.Trim();
                    break;
                case "330":
                    if (owner is not null)
                        return false;
                    owner = pair.Value.Trim();
                    break;
            }
        }
        return layer?.Equals("CONSTRUCTION", StringComparison.OrdinalIgnoreCase) == true
               && lineType?.Equals("DASHED", StringComparison.OrdinalIgnoreCase) == true
               && color == "8"
               && !string.IsNullOrWhiteSpace(owner)
               && TryParseHandleValue(owner, out var parsedOwner)
               && parsedOwner != 0;
    }

    private static Editor2DPreviewPath BuildCanonicalEditorPath(
        DxfPreviewPath candidate,
        Editor2DPreviewPath expected)
        => expected with
        {
            EntityType = candidate.EntityType,
            Points = candidate.Points.Select(static point => new Editor2DPoint(point.X, point.Y)).ToArray(),
            IsClosed = candidate.IsClosed,
            IsAxisAlignedRectangle = candidate.IsAxisAlignedRectangle,
            Start = candidate.Start is { } start ? new Editor2DPoint(start.X, start.Y) : null,
            Text = candidate.Text,
            TextHeight = candidate.TextHeight,
            RotationDegrees = candidate.RotationDegrees,
            WidthFactor = candidate.WidthFactor,
            TextBasis = candidate.TextBasis,
            Center = candidate.Center is { } center ? new Editor2DPoint(center.X, center.Y) : null,
            Radius = candidate.Radius,
            StartAngleDegrees = candidate.StartAngleDegrees,
            EndAngleDegrees = candidate.EndAngleDegrees,
            IsFilled = candidate.IsFilled,
            SourceLayerName = candidate.LayerName,
            SourceEntityHandle = candidate.EntityHandle,
            FillLoops = candidate.FillLoops?.Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                .Select(static point => new Editor2DPoint(point.X, point.Y)).ToArray()).ToArray(),
            FontFamily = candidate.FontFamily,
            CharacterSpacing = candidate.CharacterSpacing,
            IsBold = candidate.IsBold,
            IsItalic = candidate.IsItalic,
            IsUnderline = candidate.IsUnderline,
        };

    private static bool CandidateHatchGeometryMatches(
        DxfPreviewPath candidate,
        Editor2DPreviewPath expected,
        double flatteningTolerance)
    {
        if (!candidate.IsFilled
            || !expected.IsFilled
            || candidate.FillLoops is null
            || expected.FillLoops is null
            || candidate.FillLoops.Count != expected.FillLoops.Count)
        {
            return false;
        }
        for (var loopIndex = 0; loopIndex < candidate.FillLoops.Count; loopIndex++)
        {
            var candidateLoop = candidate.FillLoops[loopIndex];
            var expectedLoop = expected.FillLoops[loopIndex];
            if (candidateLoop.Count < 3 || expectedLoop.Count < 3)
                return false;
            if (candidateLoop.Any(point => DistanceToClosedLoop(point, expectedLoop) > flatteningTolerance)
                || expectedLoop.Any(point => DistanceToClosedLoop(point, candidateLoop) > flatteningTolerance))
            {
                return false;
            }
        }
        return true;
    }

    private static double DistanceToClosedLoop(
        DxfPoint point,
        IReadOnlyList<Editor2DPoint> loop)
    {
        var minimum = double.PositiveInfinity;
        for (var index = 0; index < loop.Count; index++)
        {
            var start = loop[index];
            var end = loop[(index + 1) % loop.Count];
            minimum = Math.Min(minimum, DistanceToSegment(point.X, point.Y, start.X, start.Y, end.X, end.Y));
        }
        return minimum;
    }

    private static double DistanceToClosedLoop(
        Editor2DPoint point,
        IReadOnlyList<DxfPoint> loop)
    {
        var minimum = double.PositiveInfinity;
        for (var index = 0; index < loop.Count; index++)
        {
            var start = loop[index];
            var end = loop[(index + 1) % loop.Count];
            minimum = Math.Min(minimum, DistanceToSegment(point.X, point.Y, start.X, start.Y, end.X, end.Y));
        }
        return minimum;
    }

    private static double DistanceToSegment(
        double pointX,
        double pointY,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= 1e-20)
            return Math.Sqrt(((pointX - startX) * (pointX - startX)) + ((pointY - startY) * (pointY - startY)));
        var parameter = Math.Clamp(
            (((pointX - startX) * deltaX) + ((pointY - startY) * deltaY)) / lengthSquared,
            0.0,
            1.0);
        var closestX = startX + (parameter * deltaX);
        var closestY = startY + (parameter * deltaY);
        var distanceX = pointX - closestX;
        var distanceY = pointY - closestY;
        return Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY));
    }

    private static bool CandidateGeometryMatches(
        DxfPreviewPath candidate,
        Editor2DPreviewPath expected,
        double hatchValidationTolerance)
    {
        if (candidate.EntityType.Equals("HATCH", StringComparison.OrdinalIgnoreCase)
            && expected.EntityType.Equals("HATCH", StringComparison.OrdinalIgnoreCase))
        {
            return CandidateHatchGeometryMatches(candidate, expected, hatchValidationTolerance);
        }
        if (!candidate.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
            || !expected.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
        {
            return GeometryMatches(candidate, expected);
        }
        return GeometryMatches(
            candidate with
            {
                RotationDegrees = candidate.RotationDegrees ?? 0.0,
                WidthFactor = candidate.WidthFactor ?? 1.0,
            },
            expected with
            {
                RotationDegrees = expected.RotationDegrees ?? 0.0,
                WidthFactor = expected.WidthFactor ?? 1.0,
            });
    }

    private static bool TryValidateMergedCandidate(
        string candidatePath,
        Editor2DPreviewDocument expectedGeometry,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata> pathMetadata,
        IReadOnlyList<(string TableName, string RecordType, string RecordName)> dependencyRequests,
        IReadOnlyDictionary<string, Editor2DPreviewPath> newPathsByHandle,
        IReadOnlyDictionary<string, double> hatchValidationToleranceByHandle,
        out IReadOnlyDictionary<string, Editor2DPreviewPath> canonicalPathsByPathId)
    {
        var canonicalPaths = new Dictionary<string, Editor2DPreviewPath>(StringComparer.Ordinal);
        canonicalPathsByPathId = canonicalPaths;
        var candidate = LoadPreviewDocument(candidatePath);
        var candidateByHandle = new Dictionary<string, DxfPreviewPath>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidate.Paths)
        {
            if (string.IsNullOrWhiteSpace(path.EntityHandle)
                || !candidateByHandle.TryAdd(NormalizeHandle(path.EntityHandle), path))
            {
                return false;
            }
        }

        var expectedHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var expectedPath in expectedGeometry.Paths)
        {
            if (string.IsNullOrWhiteSpace(expectedPath.SourceEntityHandle)
                || !expectedHandles.Add(NormalizeHandle(expectedPath.SourceEntityHandle))
                || !candidateByHandle.TryGetValue(
                    NormalizeHandle(expectedPath.SourceEntityHandle),
                    out var candidatePathByHandle)
                || !CandidateGeometryMatches(
                    candidatePathByHandle,
                    expectedPath,
                    hatchValidationToleranceByHandle.TryGetValue(
                        NormalizeHandle(expectedPath.SourceEntityHandle),
                        out var hatchTolerance)
                        ? hatchTolerance
                        : 0.101))
            {
                return false;
            }
            if (hatchValidationToleranceByHandle.ContainsKey(
                    NormalizeHandle(expectedPath.SourceEntityHandle)))
            {
                canonicalPaths[expectedPath.Id] = BuildCanonicalEditorPath(candidatePathByHandle, expectedPath);
            }
            var expectedLayer = pathMetadata.TryGetValue(expectedPath.Id, out var metadata)
                ? metadata.LayerName
                : expectedPath.SourceLayerName;
            if (string.IsNullOrWhiteSpace(expectedLayer)
                || !string.Equals(
                    candidatePathByHandle.LayerName?.Trim(),
                    expectedLayer.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var candidateText = Encoding.Latin1.GetString(File.ReadAllBytes(candidatePath));
        if (!TryReadRawDxfPairs(candidateText, out var candidatePairs)
            || !TryReadEntityRecords(candidatePairs, out var candidateRecords))
        {
            return false;
        }
        var candidateRecordsByHandle = BuildUniqueEntityHandleMap(candidateRecords);
        if (candidateRecordsByHandle is null)
            return false;
        foreach (var newPath in newPathsByHandle)
        {
            if (!newPath.Value.IsConstruction)
                continue;
            if (!candidateRecordsByHandle.TryGetValue(newPath.Key, out var candidateRecord)
                || !HasCanonicalConstructionStyle(candidatePairs, candidateRecord))
            {
                return false;
            }
        }
        foreach (var dependency in dependencyRequests)
        {
            if (!TryReadOptionalSymbolTableInfo(
                    candidatePairs,
                    dependency.TableName,
                    dependency.RecordType,
                    out var candidateTable)
                || candidateTable?.RecordNames.ContainsKey(dependency.RecordName) != true
                || dependency.RecordName.Equals("DASHED", StringComparison.OrdinalIgnoreCase)
                   && !IsValidSimpleDashedLinetype(candidatePairs, candidateTable.TableHandle))
            {
                return false;
            }
        }
        return true;
    }

    private static bool LoopsMatch(
        IReadOnlyList<IReadOnlyList<DxfPoint>>? left,
        IReadOnlyList<IReadOnlyList<Editor2DPoint>>? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return left.Count == right.Count
               && left.Zip(right).All(loop => loop.First.Count == loop.Second.Count
                   && loop.First.Zip(loop.Second).All(point => PointsMatch(point.First, point.Second)));
    }
    private static bool PointsMatch(DxfPoint? left, Editor2DPoint? right)
        => left is { } a && right is { } b
           && ValuesMatch(a.X, b.X)
           && ValuesMatch(a.Y, b.Y);

    private static bool TextBasesMatch(DxfPreviewPath source, Editor2DPreviewPath current)
    {
        var sourceBasis = source.TextBasis
            ?? Editor2DGeometry.CreateLegacyTextBasis(
                source.TextHeight ?? 5.0,
                source.RotationDegrees ?? 0.0,
                source.WidthFactor ?? 1.0);
        var currentBasis = Editor2DGeometry.ResolveTextBasis(current);
        return ValuesMatch(sourceBasis.Ux, currentBasis.Ux)
               && ValuesMatch(sourceBasis.Uy, currentBasis.Uy)
               && ValuesMatch(sourceBasis.Vx, currentBasis.Vx)
               && ValuesMatch(sourceBasis.Vy, currentBasis.Vy);
    }
    private static bool ValuesMatch(double? left, double? right)
        => left is { } a && right is { } b && ValuesMatch(a, b);

    private static bool ValuesMatch(double left, double right)
        => Math.Abs(left - right) <= 1e-8 * Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));

    private static string BuildMergedEntityRaw(
        IReadOnlyList<RawDxfPair> sourcePairs,
        RawDxfEntityRecord source,
        IReadOnlyList<RawDxfPair> generatedPairs,
        RawDxfEntityRecord generated)
    {
        var result = new StringBuilder();
        var generatedInsert = generated.Start + 1;
        result.Append(generatedPairs[generated.Start].Raw);
        if (generatedInsert < generated.End && generatedPairs[generatedInsert].Code == "5")
        {
            result.Append(generatedPairs[generatedInsert].Raw);
            generatedInsert++;
        }

        RawDxfPair? sourceNativeTextStyle = null;
        if (source.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
        {
            for (var index = source.Start + 1; index < source.End; index++)
            {
                if (sourcePairs[index].Code == "1001")
                    break;
                if (sourcePairs[index].Code == "7")
                {
                    sourceNativeTextStyle = sourcePairs[index];
                    break;
                }
            }
        }

        var inExtensionGroup = false;
        var xdataStart = source.End;
        for (var index = source.Start + 1; index < source.End; index++)
        {
            var pair = sourcePairs[index];
            if (pair.Code == "1001")
            {
                xdataStart = index;
                break;
            }
            if (pair.Code == "102")
            {
                inExtensionGroup = !pair.Value.TrimStart().StartsWith("}", StringComparison.Ordinal);
                result.Append(pair.Raw);
                continue;
            }
            if (sourceNativeTextStyle is not null && pair.Code == "7")
                continue;
            if (inExtensionGroup || IsPreservedEntityEnvelopeCode(pair.Code))
                result.Append(pair.Raw);
        }

        var generatedXdataStart = generated.End;
        for (var index = generatedInsert; index < generated.End; index++)
        {
            if (generatedPairs[index].Code == "1001")
            {
                generatedXdataStart = index;
                break;
            }
            if (sourceNativeTextStyle is not null && generatedPairs[index].Code == "7")
            {
                result.Append(sourceNativeTextStyle.Raw);
                continue;
            }
            result.Append(generatedPairs[index].Raw);
        }
        if (source.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
        {
            AppendMergedTextXData(
                result,
                sourcePairs,
                xdataStart,
                source.End,
                generatedPairs,
                generatedXdataStart,
                generated.End);
        }
        else
        {
            for (var index = xdataStart; index < source.End; index++)
                result.Append(sourcePairs[index].Raw);
        }
        return result.ToString();
    }

    private static void AppendMergedTextXData(
        StringBuilder result,
        IReadOnlyList<RawDxfPair> sourcePairs,
        int sourceStart,
        int sourceEnd,
        IReadOnlyList<RawDxfPair> generatedPairs,
        int generatedStart,
        int generatedEnd)
    {
        var generatedPathstitchStart = -1;
        var generatedPathstitchEnd = -1;
        for (var start = generatedStart; start < generatedEnd;)
        {
            var end = start + 1;
            while (end < generatedEnd && generatedPairs[end].Code != "1001")
                end++;
            if (generatedPairs[start].Code == "1001"
                && generatedPairs[start].Value.Equals("PATHSTITCH", StringComparison.OrdinalIgnoreCase))
            {
                generatedPathstitchStart = start;
                generatedPathstitchEnd = end;
                break;
            }
            start = end;
        }

        var replacedPathstitch = false;
        for (var start = sourceStart; start < sourceEnd;)
        {
            var end = start + 1;
            while (end < sourceEnd && sourcePairs[end].Code != "1001")
                end++;
            var isPathstitch = sourcePairs[start].Code == "1001"
                               && sourcePairs[start].Value.Equals("PATHSTITCH", StringComparison.OrdinalIgnoreCase);
            if (isPathstitch)
            {
                if (!replacedPathstitch && generatedPathstitchStart >= 0)
                {
                    for (var index = generatedPathstitchStart; index < generatedPathstitchEnd; index++)
                        result.Append(generatedPairs[index].Raw);
                }
                replacedPathstitch = true;
            }
            else
            {
                for (var index = start; index < end; index++)
                    result.Append(sourcePairs[index].Raw);
            }
            start = end;
        }
        if (!replacedPathstitch && generatedPathstitchStart >= 0)
        {
            for (var index = generatedPathstitchStart; index < generatedPathstitchEnd; index++)
                result.Append(generatedPairs[index].Raw);
        }
    }

    private static bool IsPreservedEntityEnvelopeCode(string code)
        => code is "330" or "360" or "67" or "410" or "6" or "7" or "48" or "60" or "62"
            or "347" or "370" or "390" or "420" or "430" or "440";
    private static void WritePreservedDxf(string outputPath, string content)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidDataException("DXF output path has no directory.");
        Directory.CreateDirectory(outputDirectory);
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, Encoding.Latin1.GetBytes(content));
            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static IReadOnlyList<RawDxfLine> SplitRawDxfLines(string text)
    {
        var lines = new List<RawDxfLine>();
        var start = 0;
        while (start < text.Length)
        {
            var end = start;
            while (end < text.Length && text[end] is not '\r' and not '\n')
                end++;

            var terminatorEnd = end;
            if (terminatorEnd < text.Length && text[terminatorEnd] == '\r')
                terminatorEnd++;
            if (terminatorEnd < text.Length && text[terminatorEnd] == '\n')
                terminatorEnd++;

            var terminator = text[end..terminatorEnd];
            lines.Add(new RawDxfLine(
                text[start..end],
                text[start..terminatorEnd],
                terminator));
            start = terminatorEnd;
        }

        return lines;
    }
    public static void SaveLwPolylines(
        string outputPath,
        string layerName,
        IReadOnlyList<DxfPolyline> polylines)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var acadVersionCode = AcadVersionCode(Editor2DExportOptions.Defaults.DxfVersion);
        var builder = new StringBuilder();
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "HEADER");
        AppendPair(builder, 9, "$ACADVER");
        AppendPair(builder, 1, acadVersionCode);
        AppendCanonicalMillimeterHeader(builder);
        AppendPair(builder, 0, "ENDSEC");
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "ENTITIES");

        foreach (var polyline in polylines)
        {
            AppendLwPolyline(builder, layerName, polyline);
        }

        AppendPair(builder, 0, "ENDSEC");
        AppendPair(builder, 0, "EOF");
        WriteDxf(outputPath, builder.ToString(), acadVersionCode);
    }

    public static void SaveOrAppendLwPolylines(
        string outputPath,
        string layerName,
        IReadOnlyList<DxfPolyline> polylines,
        string? existingDxfPath,
        double gap = 10.0)
    {
        if (string.IsNullOrWhiteSpace(existingDxfPath)
            || !File.Exists(existingDxfPath)
            || Path.GetExtension(existingDxfPath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            SaveLwPolylines(outputPath, layerName, polylines);
            return;
        }

        var lines = ReadDxfLines(existingDxfPath);
        var entitiesEndIndex = FindEntitiesEndIndex(lines);
        if (entitiesEndIndex < 0)
        {
            SaveLwPolylines(outputPath, layerName, polylines);
            return;
        }

        var metadata = ReadUnitMetadata(lines);
        if (metadata.HasMalformedDeclaration
            || metadata.MillimetersPerDrawingUnit is not { } millimetersPerDrawingUnit
            || !double.IsFinite(millimetersPerDrawingUnit)
            || millimetersPerDrawingUnit <= 0)
        {
            throw new InvalidDataException(
                "Cannot append millimetre geometry to a DXF with missing, unitless, or unsupported $INSUNITS metadata, including malformed or conflicting declarations.");
        }

        var drawingUnitsPerMillimeter = 1.0 / millimetersPerDrawingUnit;
        var convertedPolylines = Math.Abs(drawingUnitsPerMillimeter - 1.0) <= 1e-12
            ? polylines
            : polylines
                .Select(polyline => new DxfPolyline(
                    polyline.Points.Select(point => new DxfPoint(
                        point.X * drawingUnitsPerMillimeter,
                        point.Y * drawingUnitsPerMillimeter)).ToArray(),
                    polyline.IsClosed))
                .ToArray();
        var existingPoints = LoadPreviewDocument(existingDxfPath).Paths
            .SelectMany(static path => path.Points)
            .ToArray();
        var generatedPoints = convertedPolylines.SelectMany(static polyline => polyline.Points).ToArray();
        var translated = convertedPolylines;
        if (existingPoints.Length > 0 && generatedPoints.Length > 0)
        {
            var gapInDrawingUnits = Math.Max(0.0, gap) * drawingUnitsPerMillimeter;
            var deltaX = existingPoints.Max(static point => point.X)
                         + gapInDrawingUnits
                         - generatedPoints.Min(static point => point.X);
            translated = convertedPolylines
                .Select(polyline => new DxfPolyline(
                    polyline.Points.Select(point => new DxfPoint(point.X + deltaX, point.Y)).ToArray(),
                    polyline.IsClosed))
                .ToArray();
        }

        var builder = new StringBuilder();
        for (var index = 0; index < entitiesEndIndex; index++)
            builder.AppendLine(lines[index]);
        foreach (var polyline in translated)
            AppendLwPolyline(builder, layerName, polyline);
        for (var index = entitiesEndIndex; index < lines.Length; index++)
            builder.AppendLine(lines[index]);

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
        WriteDxf(outputPath, builder.ToString(), ReadAcadVersionCode(lines));
    }

    private static int FindEntitiesEndIndex(IReadOnlyList<string> lines)
    {
        var inEntities = false;
        for (var index = 0; index + 1 < lines.Count; index += 2)
        {
            var code = lines[index].Trim();
            var value = lines[index + 1].Trim();
            if (!inEntities
                && code == "0"
                && value.Equals("SECTION", StringComparison.OrdinalIgnoreCase)
                && index + 3 < lines.Count
                && lines[index + 2].Trim() == "2"
                && lines[index + 3].Trim().Equals("ENTITIES", StringComparison.OrdinalIgnoreCase))
            {
                inEntities = true;
                index += 2;
                continue;
            }

            if (inEntities && code == "0" && value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    public static IReadOnlyList<DxfPolyline> LoadPolylines(string dxfPath)
        => LoadPreviewDocument(dxfPath).Paths
            .Select(static path => new DxfPolyline(path.Points, path.IsClosed))
            .ToArray();

    internal static DxfUnitMetadata ReadUnitMetadata(string dxfPath)
    {
        if (string.IsNullOrWhiteSpace(dxfPath) || !File.Exists(dxfPath))
            return default;

        if (Path.GetExtension(dxfPath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            return default;

        var lines = ReadDxfLines(dxfPath);
        return ReadUnitMetadata(lines);
    }

    internal static DxfUnitMetadata ReadUnitMetadata(IEnumerable<string> lines)
        => ReadUnitMetadata(lines as string[] ?? lines.ToArray());

    public static DxfPreviewDocument LoadPreviewDocument(string dxfPath)
    {
        if (string.IsNullOrWhiteSpace(dxfPath) || !File.Exists(dxfPath))
            return new DxfPreviewDocument([], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), []);

        if (Path.GetExtension(dxfPath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            return SvgPreviewDocumentParser.Load(dxfPath);

        var lines = ReadDxfLines(dxfPath);
        var previewPaths = new List<DxfPreviewPath>();
        var entityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unsupportedEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inEntitiesSection = false;
        var entityIndex = 0;

        for (var i = 0; i + 1 < lines.Length; i += 2)
        {
            var code = lines[i].Trim();
            var value = lines[i + 1].Trim();

            if (code == "0"
                && string.Equals(value, "SECTION", StringComparison.OrdinalIgnoreCase)
                && i + 3 < lines.Length
                && string.Equals(lines[i + 2].Trim(), "2", StringComparison.Ordinal))
            {
                inEntitiesSection = string.Equals(lines[i + 3].Trim(), "ENTITIES", StringComparison.OrdinalIgnoreCase);
                i += 2;
                continue;
            }

            if (code == "0" && string.Equals(value, "ENDSEC", StringComparison.OrdinalIgnoreCase))
            {
                inEntitiesSection = false;
                continue;
            }

            if (!inEntitiesSection || code != "0")
                continue;

            IncrementEntityCount(entityCounts, value);

            if (string.Equals(value, "LWPOLYLINE", StringComparison.OrdinalIgnoreCase))
            {
                var entityStart = i;
                var entity = ParseLwPolyline(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(AttachSourceMetadata(lines, entityStart, entity));
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "POLYLINE", StringComparison.OrdinalIgnoreCase))
            {
                var entityStart = i;
                var entity = ParsePolyline(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(AttachSourceMetadata(lines, entityStart, entity));
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "LINE", StringComparison.OrdinalIgnoreCase))
            {
                var entityStart = i;
                var entity = ParseLine(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(AttachSourceMetadata(lines, entityStart, entity));
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "ARC", StringComparison.OrdinalIgnoreCase))
            {
                var entityStart = i;
                var entity = ParseArc(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(AttachSourceMetadata(lines, entityStart, entity));
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "CIRCLE", StringComparison.OrdinalIgnoreCase))
            {
                var entityStart = i;
                var entity = ParseCircle(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(AttachSourceMetadata(lines, entityStart, entity));
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "ELLIPSE", StringComparison.OrdinalIgnoreCase))
            {
                var entityStart = i;
                var entity = ParseEllipse(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(AttachSourceMetadata(lines, entityStart, entity));
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "SPLINE", StringComparison.OrdinalIgnoreCase))
            {
                var entityStart = i;
                var entity = ParseSpline(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(AttachSourceMetadata(lines, entityStart, entity));
                else
                    unsupportedEntityTypes.Add("SPLINE");
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "HATCH", StringComparison.OrdinalIgnoreCase))
            {
                var entityStart = i;
                var entities = ParseHatch(lines, ref i, entityIndex);
                if (entities.Count == 0)
                    unsupportedEntityTypes.Add("HATCH");
                else
                    previewPaths.AddRange(entities.Select(entity => AttachSourceMetadata(lines, entityStart, entity)));
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "TEXT", StringComparison.OrdinalIgnoreCase))
            {
                var entityStart = i;
                var entity = ParseText(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(AttachSourceMetadata(lines, entityStart, entity));
                else
                    unsupportedEntityTypes.Add("TEXT");
                entityIndex++;
                continue;
            }

            unsupportedEntityTypes.Add(value.ToUpperInvariant());
            entityIndex++;
        }

        return new DxfPreviewDocument(
            previewPaths,
            entityCounts.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            unsupportedEntityTypes.OrderBy(static type => type, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static DxfPreviewPath AttachSourceMetadata(string[] lines, int entityStart, DxfPreviewPath entity)
    {
        string? layerName = null;
        string? entityHandle = null;
        for (var cursor = entityStart + 2; cursor + 1 < lines.Length; cursor += 2)
        {
            var code = lines[cursor].Trim();
            if (code == "0")
                break;
            var value = lines[cursor + 1].Trim();
            if (code == "8")
                layerName = DecodeDxfUnicodeEscapes(value);
            else if (code == "5")
                entityHandle = value;
        }

        return entity with { LayerName = layerName, EntityHandle = entityHandle };
    }

    private static DxfUnitMetadata ReadUnitMetadata(string[] lines)
    {
        if (lines.Length < 4)
            return default;

        var inHeaderSection = false;
        var headerSectionCount = 0;
        var insUnitsValues = new List<int>();
        var hasMalformedDeclaration = lines.Length % 2 != 0;

        for (var i = 0; i + 1 < lines.Length; i += 2)
        {
            var code = lines[i].Trim();
            var value = lines[i + 1].Trim();

            if (code == "0"
                && string.Equals(value, "SECTION", StringComparison.OrdinalIgnoreCase)
                && i + 3 < lines.Length
                && string.Equals(lines[i + 2].Trim(), "2", StringComparison.Ordinal))
            {
                inHeaderSection = string.Equals(lines[i + 3].Trim(), "HEADER", StringComparison.OrdinalIgnoreCase);
                if (inHeaderSection)
                    headerSectionCount++;
                i += 2;
                continue;
            }

            if (code == "0" && string.Equals(value, "ENDSEC", StringComparison.OrdinalIgnoreCase))
            {
                inHeaderSection = false;
                continue;
            }

            if (!inHeaderSection
                || code != "9"
                || !string.Equals(value, "$INSUNITS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 3 >= lines.Length
                || lines[i + 2].Trim() != "70"
                || !int.TryParse(
                    lines[i + 3].Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var insUnitsCode))
            {
                hasMalformedDeclaration = true;
                continue;
            }

            insUnitsValues.Add(insUnitsCode);
        }

        hasMalformedDeclaration |= headerSectionCount > 1 || insUnitsValues.Count > 1;
        if (hasMalformedDeclaration)
            return new DxfUnitMetadata(null, null, true);
        if (insUnitsValues.Count != 1)
            return default;

        var codeValue = insUnitsValues[0];
        return new DxfUnitMetadata(
            codeValue,
            MapInsUnitsToMillimetersPerDrawingUnit(codeValue));
    }

    private static DxfPreviewPath? ParseLwPolyline(string[] lines, ref int index, int entityIndex)
    {
        var vertices = new List<DxfVertex>();
        var isClosed = false;
        double? currentX = null;
        var elevation = 0.0;
        var extrusionX = 0.0;
        var extrusionY = 0.0;
        var extrusionZ = 1.0;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "70" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
                isClosed = (flags & 1) != 0;

            if (code == "38" && TryParseDouble(value, out var parsedElevation))
                elevation = parsedElevation;
            else if (code == "210" && TryParseDouble(value, out var parsedExtrusionX))
                extrusionX = parsedExtrusionX;
            else if (code == "220" && TryParseDouble(value, out var parsedExtrusionY))
                extrusionY = parsedExtrusionY;
            else if (code == "230" && TryParseDouble(value, out var parsedExtrusionZ))
                extrusionZ = parsedExtrusionZ;
            else if (code == "10" && TryParseDouble(value, out var x))
                currentX = x;
            else if (code == "20" && currentX is double resolvedX && TryParseDouble(value, out var y))
            {
                vertices.Add(new DxfVertex(resolvedX, y, 0.0));
                currentX = null;
            }
            else if (code == "42"
                     && vertices.Count > 0
                     && TryParseDouble(value, out var bulge))
            {
                vertices[^1] = vertices[^1] with { Bulge = bulge };
            }

            cursor += 2;
        }

        index = cursor - 2;
        return CreatePreviewPath(
            $"lwpolyline-{entityIndex}",
            "LWPOLYLINE",
            vertices,
            isClosed,
            elevation,
            new DxfVector3(extrusionX, extrusionY, extrusionZ));
    }

    private static DxfPreviewPath? ParsePolyline(string[] lines, ref int index, int entityIndex)
    {
        var isClosed = false;
        var vertices = new List<DxfVertex>();
        var elevation = 0.0;
        var extrusionX = 0.0;
        var extrusionY = 0.0;
        var extrusionZ = 1.0;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();
            if (code == "0")
                break;

            if (code == "70" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
                isClosed = (flags & 1) != 0;
            else if (code == "30" && TryParseDouble(value, out var parsedElevation))
                elevation = parsedElevation;
            else if (code == "210" && TryParseDouble(value, out var parsedExtrusionX))
                extrusionX = parsedExtrusionX;
            else if (code == "220" && TryParseDouble(value, out var parsedExtrusionY))
                extrusionY = parsedExtrusionY;
            else if (code == "230" && TryParseDouble(value, out var parsedExtrusionZ))
                extrusionZ = parsedExtrusionZ;

            cursor += 2;
        }

        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code != "0")
            {
                cursor += 2;
                continue;
            }

            if (string.Equals(value, "VERTEX", StringComparison.OrdinalIgnoreCase))
            {
                var vertex = ParseVertex(lines, ref cursor);
                if (vertex is not null)
                    vertices.Add(vertex.Value);
                continue;
            }

            if (string.Equals(value, "SEQEND", StringComparison.OrdinalIgnoreCase))
            {
                cursor += 2;
                break;
            }

            break;
        }

        index = cursor - 2;
        return CreatePreviewPath(
            $"polyline-{entityIndex}",
            "POLYLINE",
            vertices,
            isClosed,
            elevation,
            new DxfVector3(extrusionX, extrusionY, extrusionZ));
    }

    private static DxfVertex? ParseVertex(string[] lines, ref int index)
    {
        double? x = null;
        double? y = null;
        var bulge = 0.0;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedX))
                x = resolvedX;
            else if (code == "20" && TryParseDouble(value, out var resolvedY))
                y = resolvedY;
            else if (code == "42" && TryParseDouble(value, out var resolvedBulge))
                bulge = resolvedBulge;

            cursor += 2;
        }

        index = cursor - 2;
        return x is double vertexX && y is double vertexY
            ? new DxfVertex(vertexX, vertexY, bulge)
            : null;
    }

    private static DxfPreviewPath? ParseLine(string[] lines, ref int index, int entityIndex)
    {
        double? x1 = null;
        double? y1 = null;
        double? x2 = null;
        double? y2 = null;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedX1))
                x1 = resolvedX1;
            else if (code == "20" && TryParseDouble(value, out var resolvedY1))
                y1 = resolvedY1;
            else if (code == "11" && TryParseDouble(value, out var resolvedX2))
                x2 = resolvedX2;
            else if (code == "21" && TryParseDouble(value, out var resolvedY2))
                y2 = resolvedY2;

            cursor += 2;
        }

        index = cursor - 2;
        return x1 is double lineX1 && y1 is double lineY1 && x2 is double lineX2 && y2 is double lineY2
            ? new DxfPreviewPath(
                Id: $"line-{entityIndex}",
                EntityType: "LINE",
                Points: [new DxfPoint(lineX1, lineY1), new DxfPoint(lineX2, lineY2)],
                IsClosed: false,
                IsAxisAlignedRectangle: false)
            : null;
    }

    private static DxfPreviewPath? ParseArc(string[] lines, ref int index, int entityIndex)
    {
        double? centerX = null;
        double? centerY = null;
        var centerZ = 0.0;
        double? radius = null;
        double? startAngleDegrees = null;
        double? endAngleDegrees = null;
        var extrusionX = 0.0;
        var extrusionY = 0.0;
        var extrusionZ = 1.0;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedCenterX))
                centerX = resolvedCenterX;
            else if (code == "20" && TryParseDouble(value, out var resolvedCenterY))
                centerY = resolvedCenterY;
            else if (code == "30" && TryParseDouble(value, out var resolvedCenterZ))
                centerZ = resolvedCenterZ;
            else if (code == "40" && TryParseDouble(value, out var resolvedRadius))
                radius = resolvedRadius;
            else if (code == "50" && TryParseDouble(value, out var resolvedStartAngle))
                startAngleDegrees = resolvedStartAngle;
            else if (code == "51" && TryParseDouble(value, out var resolvedEndAngle))
                endAngleDegrees = resolvedEndAngle;
            else if (code == "210" && TryParseDouble(value, out var parsedExtrusionX))
                extrusionX = parsedExtrusionX;
            else if (code == "220" && TryParseDouble(value, out var parsedExtrusionY))
                extrusionY = parsedExtrusionY;
            else if (code == "230" && TryParseDouble(value, out var parsedExtrusionZ))
                extrusionZ = parsedExtrusionZ;

            cursor += 2;
        }

        index = cursor - 2;
        if (centerX is not double arcCenterX
            || centerY is not double arcCenterY
            || radius is not double arcRadius
            || startAngleDegrees is not double arcStartAngleDegrees
            || endAngleDegrees is not double arcEndAngleDegrees)
        {
            return null;
        }

        var extrusion = new DxfVector3(extrusionX, extrusionY, extrusionZ);
        var points = TransformOcsPoints(
            ApproximateArcDegrees(
                arcCenterX,
                arcCenterY,
                arcRadius,
                arcStartAngleDegrees,
                arcEndAngleDegrees,
                isClosed: false),
            centerZ,
            extrusion);
        var retainsCircularMetadata = Math.Abs(extrusionX) <= 1e-12
                                      && Math.Abs(extrusionY) <= 1e-12
                                      && extrusionZ > 0.0;

        return points.Count >= 2
            ? new DxfPreviewPath(
                Id: $"arc-{entityIndex}",
                EntityType: retainsCircularMetadata ? "ARC" : "LWPOLYLINE",
                Points: points,
                IsClosed: false,
                IsAxisAlignedRectangle: false,
                Center: retainsCircularMetadata ? new DxfPoint(arcCenterX, arcCenterY) : null,
                Radius: retainsCircularMetadata ? arcRadius : null,
                StartAngleDegrees: retainsCircularMetadata ? arcStartAngleDegrees : null,
                EndAngleDegrees: retainsCircularMetadata ? arcEndAngleDegrees : null)
            : null;
    }
    private static DxfPreviewPath? ParseCircle(string[] lines, ref int index, int entityIndex)
    {
        double? centerX = null;
        double? centerY = null;
        var centerZ = 0.0;
        double? radius = null;
        var extrusionX = 0.0;
        var extrusionY = 0.0;
        var extrusionZ = 1.0;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedCenterX))
                centerX = resolvedCenterX;
            else if (code == "20" && TryParseDouble(value, out var resolvedCenterY))
                centerY = resolvedCenterY;
            else if (code == "30" && TryParseDouble(value, out var resolvedCenterZ))
                centerZ = resolvedCenterZ;
            else if (code == "40" && TryParseDouble(value, out var resolvedRadius))
                radius = resolvedRadius;
            else if (code == "210" && TryParseDouble(value, out var parsedExtrusionX))
                extrusionX = parsedExtrusionX;
            else if (code == "220" && TryParseDouble(value, out var parsedExtrusionY))
                extrusionY = parsedExtrusionY;
            else if (code == "230" && TryParseDouble(value, out var parsedExtrusionZ))
                extrusionZ = parsedExtrusionZ;

            cursor += 2;
        }

        index = cursor - 2;
        if (centerX is not double circleCenterX
            || centerY is not double circleCenterY
            || radius is not double circleRadius)
        {
            return null;
        }

        var extrusion = new DxfVector3(extrusionX, extrusionY, extrusionZ);
        var points = TransformOcsPoints(
            ApproximateArcDegrees(
                circleCenterX,
                circleCenterY,
                circleRadius,
                startAngleDegrees: 0.0,
                endAngleDegrees: 360.0,
                isClosed: true),
            centerZ,
            extrusion);
        var retainsCircularMetadata = Math.Abs(extrusionX) <= 1e-12
                                      && Math.Abs(extrusionY) <= 1e-12
                                      && extrusionZ > 0.0;

        return points.Count >= 3
            ? new DxfPreviewPath(
                Id: $"circle-{entityIndex}",
                EntityType: retainsCircularMetadata ? "CIRCLE" : "LWPOLYLINE",
                Points: points,
                IsClosed: true,
                IsAxisAlignedRectangle: false,
                Center: retainsCircularMetadata ? new DxfPoint(circleCenterX, circleCenterY) : null,
                Radius: retainsCircularMetadata ? circleRadius : null,
                StartAngleDegrees: retainsCircularMetadata ? 0.0 : null,
                EndAngleDegrees: retainsCircularMetadata ? 360.0 : null)
            : null;
    }
    private static DxfPreviewPath? ParseEllipse(string[] lines, ref int index, int entityIndex)
    {
        double? centerX = null;
        double? centerY = null;
        double? majorAxisX = null;
        double? majorAxisY = null;
        var majorAxisZ = 0.0;
        var extrusionX = 0.0;
        var extrusionY = 0.0;
        var extrusionZ = 1.0;
        double? ratio = null;
        double? startParameter = null;
        double? endParameter = null;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedCenterX))
                centerX = resolvedCenterX;
            else if (code == "20" && TryParseDouble(value, out var resolvedCenterY))
                centerY = resolvedCenterY;
            else if (code == "11" && TryParseDouble(value, out var resolvedMajorAxisX))
                majorAxisX = resolvedMajorAxisX;
            else if (code == "21" && TryParseDouble(value, out var resolvedMajorAxisY))
                majorAxisY = resolvedMajorAxisY;
            else if (code == "31" && TryParseDouble(value, out var resolvedMajorAxisZ))
                majorAxisZ = resolvedMajorAxisZ;
            else if (code == "210" && TryParseDouble(value, out var parsedExtrusionX))
                extrusionX = parsedExtrusionX;
            else if (code == "220" && TryParseDouble(value, out var parsedExtrusionY))
                extrusionY = parsedExtrusionY;
            else if (code == "230" && TryParseDouble(value, out var parsedExtrusionZ))
                extrusionZ = parsedExtrusionZ;
            else if (code == "40" && TryParseDouble(value, out var resolvedRatio))
                ratio = resolvedRatio;
            else if (code == "41" && TryParseDouble(value, out var resolvedStartParameter))
                startParameter = resolvedStartParameter;
            else if (code == "42" && TryParseDouble(value, out var resolvedEndParameter))
                endParameter = resolvedEndParameter;

            cursor += 2;
        }

        index = cursor - 2;
        if (centerX is not double ellipseCenterX
            || centerY is not double ellipseCenterY
            || majorAxisX is not double ellipseMajorAxisX
            || majorAxisY is not double ellipseMajorAxisY
            || ratio is not double ellipseRatio)
        {
            return null;
        }

        var normalizedRatio = Math.Abs(ellipseRatio);
        if (normalizedRatio <= 1e-9)
            return null;

        var majorAxis = new DxfVector3(ellipseMajorAxisX, ellipseMajorAxisY, majorAxisZ);
        var majorLength = Math.Sqrt(
            (majorAxis.X * majorAxis.X) + (majorAxis.Y * majorAxis.Y) + (majorAxis.Z * majorAxis.Z));
        if (majorLength <= 1e-9)
            return null;

        var normal = NormalizeVector(
            new DxfVector3(extrusionX, extrusionY, extrusionZ),
            new DxfVector3(0.0, 0.0, 1.0));
        var minorDirection = Cross(normal, majorAxis);
        var minorDirectionLength = Math.Sqrt(
            (minorDirection.X * minorDirection.X)
            + (minorDirection.Y * minorDirection.Y)
            + (minorDirection.Z * minorDirection.Z));
        if (minorDirectionLength <= 1e-9)
            return null;
        var minorScale = (majorLength * normalizedRatio) / minorDirectionLength;
        var minorAxis = new DxfVector3(
            minorDirection.X * minorScale,
            minorDirection.Y * minorScale,
            minorDirection.Z * minorScale);

        var start = startParameter ?? 0.0;
        var end = endParameter ?? Math.PI * 2.0;
        var sweep = NormalizeParameterSweep(start, end);
        var isClosed = Math.Abs(sweep - (Math.PI * 2.0)) <= 1e-6;
        var points = FlattenEllipse(
            ellipseCenterX,
            ellipseCenterY,
            majorAxis.X,
            majorAxis.Y,
            minorAxis.X,
            minorAxis.Y,
            start,
            sweep,
            isClosed);

        return points.Count >= 2
            ? new DxfPreviewPath(
                Id: $"ellipse-{entityIndex}",
                EntityType: "ELLIPSE",
                Points: points,
                IsClosed: isClosed,
                IsAxisAlignedRectangle: false)
            : null;
    }

    private static DxfPreviewPath? ParseSpline(string[] lines, ref int index, int entityIndex)
    {
        var flags = 0;
        int? degree = null;
        var knots = new List<double>();
        var weights = new List<double>();
        var controlPoints = new List<DxfPoint>();
        var fitPoints = new List<DxfPoint>();
        double? controlX = null;
        double? fitX = null;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();
            if (code == "0")
                break;

            if (code == "70" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFlags))
                flags = parsedFlags;
            else if (code == "71" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDegree))
                degree = parsedDegree;
            else if (code == "40" && TryParseDouble(value, out var knot))
                knots.Add(knot);
            else if (code == "41" && TryParseDouble(value, out var weight))
                weights.Add(weight);
            else if (code == "10" && TryParseDouble(value, out var parsedControlX))
                controlX = parsedControlX;
            else if (code == "20" && controlX is { } resolvedControlX && TryParseDouble(value, out var controlY))
            {
                controlPoints.Add(new DxfPoint(resolvedControlX, controlY));
                controlX = null;
            }
            else if (code == "11" && TryParseDouble(value, out var parsedFitX))
                fitX = parsedFitX;
            else if (code == "21" && fitX is { } resolvedFitX && TryParseDouble(value, out var fitY))
            {
                fitPoints.Add(new DxfPoint(resolvedFitX, fitY));
                fitX = null;
            }

            cursor += 2;
        }

        index = cursor - 2;
        var isClosed = (flags & 1) != 0 || (flags & 2) != 0;
        IReadOnlyList<DxfPoint> flattened;
        if (degree is { } splineDegree
            && splineDegree >= 1
            && controlPoints.Count >= splineDegree + 1)
        {
            flattened = FlattenRationalBSpline(controlPoints, knots, weights, splineDegree);
        }
        else
        {
            flattened = FlattenFitPointSpline(fitPoints, isClosed);
        }

        return flattened.Count >= 2
            ? new DxfPreviewPath(
                Id: $"spline-{entityIndex}",
                EntityType: "SPLINE",
                Points: flattened,
                IsClosed: isClosed,
                IsAxisAlignedRectangle: false)
            : null;
    }

    private static IReadOnlyList<DxfPoint> FlattenRationalBSpline(
        IReadOnlyList<DxfPoint> controlPoints,
        IReadOnlyList<double> sourceKnots,
        IReadOnlyList<double> sourceWeights,
        int degree)
    {
        var pointCount = controlPoints.Count;
        var expectedKnotCount = pointCount + degree + 1;
        var knots = sourceKnots.Count == expectedKnotCount
            ? sourceKnots.ToArray()
            : CreateClampedUniformKnots(pointCount, degree);
        if (knots.Length != expectedKnotCount
            || knots.Where((value, position) => position > 0 && value < knots[position - 1]).Any())
        {
            return [];
        }

        var weights = sourceWeights.Count == 0
            ? Enumerable.Repeat(1.0, pointCount).ToArray()
            : sourceWeights.ToArray();
        if (weights.Length != pointCount
            || weights.Any(weight => !double.IsFinite(weight) || Math.Abs(weight) <= 1e-12))
        {
            return [];
        }

        var n = pointCount - 1;
        var start = knots[degree];
        var end = knots[n + 1];
        if (!double.IsFinite(start) || !double.IsFinite(end) || end <= start)
            return [];

        var result = new List<DxfPoint>();
        for (var span = degree; span <= n; span++)
        {
            var spanStart = Math.Max(knots[span], start);
            var spanEnd = Math.Min(knots[span + 1], end);
            if (spanEnd - spanStart <= 1e-12)
                continue;

            var startPoint = EvaluateRationalBSpline(controlPoints, knots, weights, degree, spanStart);
            var endPoint = EvaluateRationalBSpline(controlPoints, knots, weights, degree, spanEnd);
            if (startPoint is null || endPoint is null)
                return [];
            if (result.Count == 0 || !AreSamePoint(result[^1], startPoint.Value))
                result.Add(startPoint.Value);
            AppendAdaptiveSplineSegment(
                result,
                parameter => EvaluateRationalBSpline(controlPoints, knots, weights, degree, parameter),
                spanStart,
                startPoint.Value,
                spanEnd,
                endPoint.Value,
                depth: 0);
        }

        return result;
    }

    private static double[] CreateClampedUniformKnots(int pointCount, int degree)
    {
        var knots = new double[pointCount + degree + 1];
        var last = pointCount - degree;
        for (var index = 0; index < knots.Length; index++)
        {
            knots[index] = index <= degree
                ? 0.0
                : index >= pointCount
                    ? last
                    : index - degree;
        }
        return knots;
    }

    private static DxfPoint? EvaluateRationalBSpline(
        IReadOnlyList<DxfPoint> controlPoints,
        IReadOnlyList<double> knots,
        IReadOnlyList<double> weights,
        int degree,
        double parameter)
    {
        var n = controlPoints.Count - 1;
        var span = FindSplineSpan(knots, degree, n, parameter);
        if (span < degree || span > n)
            return null;

        var values = new (double X, double Y, double W)[degree + 1];
        for (var j = 0; j <= degree; j++)
        {
            var controlIndex = span - degree + j;
            var weight = weights[controlIndex];
            values[j] = (
                controlPoints[controlIndex].X * weight,
                controlPoints[controlIndex].Y * weight,
                weight);
        }

        for (var level = 1; level <= degree; level++)
        {
            for (var j = degree; j >= level; j--)
            {
                var knotIndex = span - degree + j;
                var denominator = knots[knotIndex + degree - level + 1] - knots[knotIndex];
                var alpha = Math.Abs(denominator) <= 1e-15
                    ? 0.0
                    : (parameter - knots[knotIndex]) / denominator;
                values[j] = (
                    ((1.0 - alpha) * values[j - 1].X) + (alpha * values[j].X),
                    ((1.0 - alpha) * values[j - 1].Y) + (alpha * values[j].Y),
                    ((1.0 - alpha) * values[j - 1].W) + (alpha * values[j].W));
            }
        }

        var resolved = values[degree];
        return Math.Abs(resolved.W) <= 1e-15
            ? null
            : new DxfPoint(resolved.X / resolved.W, resolved.Y / resolved.W);
    }

    private static int FindSplineSpan(
        IReadOnlyList<double> knots,
        int degree,
        int n,
        double parameter)
    {
        if (parameter >= knots[n + 1] - 1e-12)
            return n;
        if (parameter <= knots[degree] + 1e-12)
            return degree;

        var low = degree;
        var high = n + 1;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (parameter < knots[middle])
                high = middle;
            else
                low = middle;
        }
        return low;
    }

    private static void AppendAdaptiveSplineSegment(
        List<DxfPoint> output,
        Func<double, DxfPoint?> evaluate,
        double startParameter,
        DxfPoint start,
        double endParameter,
        DxfPoint end,
        int depth)
    {
        const double tolerance = 0.1;
        const int maximumDepth = 14;
        var quarterParameter = startParameter + ((endParameter - startParameter) * 0.25);
        var middleParameter = (startParameter + endParameter) * 0.5;
        var threeQuarterParameter = startParameter + ((endParameter - startParameter) * 0.75);
        var quarter = evaluate(quarterParameter);
        var middle = evaluate(middleParameter);
        var threeQuarter = evaluate(threeQuarterParameter);
        if (quarter is null || middle is null || threeQuarter is null)
            return;

        var deviation = Math.Max(
            DistanceToSegment(quarter.Value, start, end),
            Math.Max(
                DistanceToSegment(middle.Value, start, end),
                DistanceToSegment(threeQuarter.Value, start, end)));
        if (depth >= maximumDepth || deviation <= tolerance)
        {
            if (output.Count == 0 || !AreSamePoint(output[^1], end))
                output.Add(end);
            return;
        }

        AppendAdaptiveSplineSegment(
            output,
            evaluate,
            startParameter,
            start,
            middleParameter,
            middle.Value,
            depth + 1);
        AppendAdaptiveSplineSegment(
            output,
            evaluate,
            middleParameter,
            middle.Value,
            endParameter,
            end,
            depth + 1);
    }

    private static double DistanceToSegment(DxfPoint point, DxfPoint start, DxfPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= 1e-18)
            return Math.Sqrt(
                ((point.X - start.X) * (point.X - start.X))
                + ((point.Y - start.Y) * (point.Y - start.Y)));

        var position = Math.Clamp(
            (((point.X - start.X) * deltaX) + ((point.Y - start.Y) * deltaY)) / lengthSquared,
            0.0,
            1.0);
        var nearestX = start.X + (position * deltaX);
        var nearestY = start.Y + (position * deltaY);
        var distanceX = point.X - nearestX;
        var distanceY = point.Y - nearestY;
        return Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY));
    }

    private static IReadOnlyList<DxfPoint> FlattenFitPointSpline(
        IReadOnlyList<DxfPoint> fitPoints,
        bool isClosed)
    {
        if (fitPoints.Count < 2)
            return [];
        if (fitPoints.Count == 2)
            return fitPoints.ToArray();

        const int segmentsPerSpan = 16;
        var result = new List<DxfPoint> { fitPoints[0] };
        var spanCount = isClosed ? fitPoints.Count : fitPoints.Count - 1;
        for (var span = 0; span < spanCount; span++)
        {
            var p0 = fitPoints[isClosed
                ? (span - 1 + fitPoints.Count) % fitPoints.Count
                : Math.Max(span - 1, 0)];
            var p1 = fitPoints[span];
            var p2 = fitPoints[(span + 1) % fitPoints.Count];
            var p3 = fitPoints[isClosed
                ? (span + 2) % fitPoints.Count
                : Math.Min(span + 2, fitPoints.Count - 1)];
            for (var segment = 1; segment <= segmentsPerSpan; segment++)
            {
                var t = (double)segment / segmentsPerSpan;
                var t2 = t * t;
                var t3 = t2 * t;
                result.Add(new DxfPoint(
                    0.5 * ((2.0 * p1.X)
                        + ((-p0.X + p2.X) * t)
                        + (((2.0 * p0.X) - (5.0 * p1.X) + (4.0 * p2.X) - p3.X) * t2)
                        + ((-p0.X + (3.0 * p1.X) - (3.0 * p2.X) + p3.X) * t3)),
                    0.5 * ((2.0 * p1.Y)
                        + ((-p0.Y + p2.Y) * t)
                        + (((2.0 * p0.Y) - (5.0 * p1.Y) + (4.0 * p2.Y) - p3.Y) * t2)
                        + ((-p0.Y + (3.0 * p1.Y) - (3.0 * p2.Y) + p3.Y) * t3))));
            }
        }
        return result;
    }

    private static IReadOnlyList<DxfPreviewPath> ParseHatch(string[] lines, ref int index, int entityIndex)
    {
        var pairs = new List<(string Code, string Value)>();
        var cursor = index + 2;
        while (cursor + 1 < lines.Length && lines[cursor].Trim() != "0")
        {
            pairs.Add((lines[cursor].Trim(), lines[cursor + 1].Trim()));
            cursor += 2;
        }
        index = cursor - 2;

        var elevation = 0.0;
        var extrusionX = 0.0;
        var extrusionY = 0.0;
        var extrusionZ = 1.0;
        for (var pairIndex = 0; pairIndex < pairs.Count && pairs[pairIndex].Code != "92"; pairIndex++)
        {
            var pair = pairs[pairIndex];
            if (pair.Code == "30" && TryParseDouble(pair.Value, out var parsedElevation))
                elevation = parsedElevation;
            else if (pair.Code == "210" && TryParseDouble(pair.Value, out var parsedExtrusionX))
                extrusionX = parsedExtrusionX;
            else if (pair.Code == "220" && TryParseDouble(pair.Value, out var parsedExtrusionY))
                extrusionY = parsedExtrusionY;
            else if (pair.Code == "230" && TryParseDouble(pair.Value, out var parsedExtrusionZ))
                extrusionZ = parsedExtrusionZ;
        }

        var loops = new List<IReadOnlyList<DxfPoint>>();
        for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            if (pairs[pairIndex].Code != "92"
                || !int.TryParse(pairs[pairIndex].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var boundaryFlags))
            {
                continue;
            }

            var nextBoundary = pairIndex + 1;
            while (nextBoundary < pairs.Count
                   && pairs[nextBoundary].Code != "92"
                   && pairs[nextBoundary].Code != "75")
            {
                nextBoundary++;
            }

            IReadOnlyList<DxfPoint> loop = (boundaryFlags & 2) != 0
                ? ParseHatchPolylineBoundary(pairs, pairIndex + 1, nextBoundary)
                : ParseHatchEdgeBoundary(pairs, pairIndex + 1, nextBoundary);
            if (loop.Count >= 3)
            {
                loops.Add(TransformOcsPoints(
                    RemoveRepeatedClosingPoint(loop),
                    elevation,
                    new DxfVector3(extrusionX, extrusionY, extrusionZ)));
            }
            pairIndex = nextBoundary - 1;
        }

        if (loops.Count == 0)
            return [];

        var outerIndex = Enumerable.Range(0, loops.Count)
            .MaxBy(loopIndex => Math.Abs(SignedArea(loops[loopIndex])));
        var outer = loops[outerIndex];
        var orderedLoops = loops.Where((_, loopIndex) => loopIndex == outerIndex)
            .Concat(loops.Where((_, loopIndex) => loopIndex != outerIndex))
            .ToArray();
        return
        [
            new DxfPreviewPath(
                Id: $"hatch-{entityIndex}",
                EntityType: "HATCH",
                Points: outer,
                IsClosed: true,
                IsAxisAlignedRectangle: Editor2DGeometry.IsAxisAlignedRectangle(
                    outer.Select(static point => new Editor2DPoint(point.X, point.Y)).ToArray(),
                    isClosed: true),
                IsFilled: true,
                FillLoops: orderedLoops),
        ];
    }
    private static IReadOnlyList<DxfPoint> ParseHatchPolylineBoundary(
        IReadOnlyList<(string Code, string Value)> pairs,
        int start,
        int end)
    {
        var vertexCount = 0;
        var verticesStart = start;
        for (var index = start; index < end; index++)
        {
            if (pairs[index].Code == "93"
                && int.TryParse(pairs[index].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount))
            {
                vertexCount = parsedCount;
                verticesStart = index + 1;
                break;
            }
        }
        if (vertexCount < 2)
            return [];

        var vertices = new List<DxfVertex>(vertexCount);
        for (var index = verticesStart; index < end && vertices.Count < vertexCount; index++)
        {
            if (pairs[index].Code != "10" || !TryParseDouble(pairs[index].Value, out var x))
                continue;

            double? y = null;
            var bulge = 0.0;
            for (var valueIndex = index + 1; valueIndex < end && pairs[valueIndex].Code != "10"; valueIndex++)
            {
                if (pairs[valueIndex].Code == "20" && TryParseDouble(pairs[valueIndex].Value, out var parsedY))
                    y = parsedY;
                else if (pairs[valueIndex].Code == "42" && TryParseDouble(pairs[valueIndex].Value, out var parsedBulge))
                    bulge = parsedBulge;
            }
            if (y is not null)
                vertices.Add(new DxfVertex(x, y.Value, bulge));
        }
        return vertices.Count >= 2 ? BuildPolylinePoints(vertices, isClosed: true) : [];
    }

    private static IReadOnlyList<DxfPoint> ParseHatchEdgeBoundary(
        IReadOnlyList<(string Code, string Value)> pairs,
        int start,
        int end)
    {
        var points = new List<DxfPoint>();
        for (var index = start; index < end; index++)
        {
            if (pairs[index].Code != "72"
                || !int.TryParse(pairs[index].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var edgeType))
            {
                continue;
            }

            var edgeEnd = index + 1;
            while (edgeEnd < end && pairs[edgeEnd].Code != "72" && pairs[edgeEnd].Code != "97")
                edgeEnd++;
            var edgePoints = ParseHatchEdge(pairs, index + 1, edgeEnd, edgeType);
            AppendDistinctPoints(points, edgePoints);
            index = edgeEnd - 1;
        }
        return points;
    }

    private static IReadOnlyList<DxfPoint> ParseHatchEdge(
        IReadOnlyList<(string Code, string Value)> pairs,
        int start,
        int end,
        int edgeType)
    {
        double? Value(string code)
        {
            for (var index = start; index < end; index++)
            {
                if (pairs[index].Code == code && TryParseDouble(pairs[index].Value, out var parsed))
                    return parsed;
            }
            return null;
        }

        if (edgeType == 1
            && Value("10") is double startX
            && Value("20") is double startY
            && Value("11") is double endX
            && Value("21") is double endY)
        {
            return [new(startX, startY), new(endX, endY)];
        }

        if (edgeType == 2
            && Value("10") is double centerX
            && Value("20") is double centerY
            && Value("40") is double radius
            && Value("50") is double startAngle
            && Value("51") is double endAngle)
        {
            var counterClockwise = Value("73") is not 0.0;
            return ApproximateDirectedArcDegrees(centerX, centerY, radius, startAngle, endAngle, counterClockwise);
        }

        if (edgeType == 3
            && Value("10") is double ellipseCenterX
            && Value("20") is double ellipseCenterY
            && Value("11") is double majorAxisX
            && Value("21") is double majorAxisY
            && Value("40") is double ratio
            && Value("50") is double ellipseStartDegrees
            && Value("51") is double ellipseEndDegrees)
        {
            var counterClockwise = Value("73") is not 0.0;
            return ApproximateDirectedEllipseDegrees(
                ellipseCenterX, ellipseCenterY, majorAxisX, majorAxisY, ratio,
                ellipseStartDegrees, ellipseEndDegrees, counterClockwise);
        }

        if (edgeType == 4)
        {
            var degree = Value("94");
            var knots = pairs.Skip(start).Take(end - start)
                .Where(static pair => pair.Code == "40")
                .Select(pair => TryParseDouble(pair.Value, out var value) ? value : double.NaN)
                .Where(double.IsFinite)
                .ToArray();
            var weights = pairs.Skip(start).Take(end - start)
                .Where(static pair => pair.Code == "42")
                .Select(pair => TryParseDouble(pair.Value, out var value) ? value : double.NaN)
                .Where(double.IsFinite)
                .ToArray();
            var controlPoints = ReadPairedPoints(pairs, start, end, "10", "20");
            if (degree is >= 1 and <= int.MaxValue && controlPoints.Count >= (int)degree.Value + 1)
                return FlattenRationalBSpline(controlPoints, knots, weights, (int)degree.Value);
        }

        return [];
    }

    private static IReadOnlyList<DxfPoint> ReadPairedPoints(
        IReadOnlyList<(string Code, string Value)> pairs,
        int start,
        int end,
        string xCode,
        string yCode)
    {
        var points = new List<DxfPoint>();
        for (var index = start; index < end; index++)
        {
            if (pairs[index].Code != xCode || !TryParseDouble(pairs[index].Value, out var x))
                continue;
            for (var valueIndex = index + 1; valueIndex < end && pairs[valueIndex].Code != xCode; valueIndex++)
            {
                if (pairs[valueIndex].Code == yCode && TryParseDouble(pairs[valueIndex].Value, out var y))
                {
                    points.Add(new DxfPoint(x, y));
                    break;
                }
            }
        }
        return points;
    }

    private static IReadOnlyList<DxfPoint> ApproximateDirectedArcDegrees(
        double centerX,
        double centerY,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees,
        bool counterClockwise)
    {
        var start = DegreesToRadians(startAngleDegrees);
        var end = DegreesToRadians(endAngleDegrees);
        if (counterClockwise)
        {
            while (end <= start)
                end += Math.PI * 2.0;
        }
        else
        {
            while (end >= start)
                end -= Math.PI * 2.0;
        }
        var sweep = end - start;
        var segmentCount = CircularArcSegmentCount(radius, sweep);
        return Enumerable.Range(0, segmentCount + 1)
            .Select(segment => start + (sweep * segment / segmentCount))
            .Select(angle => new DxfPoint(centerX + (radius * Math.Cos(angle)), centerY + (radius * Math.Sin(angle))))
            .ToArray();
    }

    private static IReadOnlyList<DxfPoint> ApproximateDirectedEllipseDegrees(
        double centerX,
        double centerY,
        double majorAxisX,
        double majorAxisY,
        double ratio,
        double startAngleDegrees,
        double endAngleDegrees,
        bool counterClockwise)
    {
        var start = DegreesToRadians(startAngleDegrees);
        var end = DegreesToRadians(endAngleDegrees);
        if (counterClockwise)
        {
            while (end <= start)
                end += Math.PI * 2.0;
        }
        else
        {
            while (end >= start)
                end -= Math.PI * 2.0;
        }
        var sweep = end - start;
        var minorAxisX = -majorAxisY * Math.Abs(ratio);
        var minorAxisY = majorAxisX * Math.Abs(ratio);
        return FlattenEllipse(
            centerX,
            centerY,
            majorAxisX,
            majorAxisY,
            minorAxisX,
            minorAxisY,
            start,
            sweep,
            isClosed: false);
    }

    private static void AppendDistinctPoints(List<DxfPoint> destination, IReadOnlyList<DxfPoint> source)
    {
        foreach (var point in source)
        {
            if (destination.Count == 0 || !AreSamePoint(destination[^1], point))
                destination.Add(point);
        }
    }

    private static IReadOnlyList<DxfPoint> RemoveRepeatedClosingPoint(IReadOnlyList<DxfPoint> points)
        => points.Count > 3 && AreSamePoint(points[0], points[^1]) ? points.Take(points.Count - 1).ToArray() : points;

    private static double SignedArea(IReadOnlyList<DxfPoint> points)
    {
        var area = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            area += (current.X * next.Y) - (next.X * current.Y);
        }
        return area * 0.5;
    }
    private static DxfPreviewPath? ParseText(string[] lines, ref int index, int entityIndex)
    {
        double? startX = null;
        double? startY = null;
        var startZ = 0.0;
        var extrusionX = 0.0;
        var extrusionY = 0.0;
        var extrusionZ = 1.0;
        double? height = null;
        double? rotation = null;
        double? obliqueAngle = null;
        double? widthFactor = null;
        var textGenerationFlags = 0;
        string? text = null;
        var inPathstitchXData = false;
        var xdataStrings = new List<string>();
        var xdataIntegers = new List<int>();
        var xdataReals = new List<double>();

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1];

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value.Trim(), out var resolvedStartX))
                startX = resolvedStartX;
            else if (code == "20" && TryParseDouble(value.Trim(), out var resolvedStartY))
                startY = resolvedStartY;
            else if (code == "30" && TryParseDouble(value.Trim(), out var resolvedStartZ))
                startZ = resolvedStartZ;
            else if (code == "210" && TryParseDouble(value.Trim(), out var parsedExtrusionX))
                extrusionX = parsedExtrusionX;
            else if (code == "220" && TryParseDouble(value.Trim(), out var parsedExtrusionY))
                extrusionY = parsedExtrusionY;
            else if (code == "230" && TryParseDouble(value.Trim(), out var parsedExtrusionZ))
                extrusionZ = parsedExtrusionZ;
            else if (code == "40" && TryParseDouble(value.Trim(), out var resolvedHeight))
                height = resolvedHeight;
            else if (code == "41" && TryParseDouble(value.Trim(), out var parsedWidthFactor))
                widthFactor = parsedWidthFactor;
            else if (code == "71"
                     && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTextGenerationFlags))
                textGenerationFlags = parsedTextGenerationFlags & 6;
            else if (code == "1")
                text = DecodeDxfUnicodeEscapes(value);
            else if (code == "50" && TryParseDouble(value.Trim(), out var resolvedRotation))
                rotation = resolvedRotation;
            else if (code == "51" && TryParseDouble(value.Trim(), out var resolvedObliqueAngle))
                obliqueAngle = resolvedObliqueAngle;
            else if (code == "1001")
                inPathstitchXData = value.Trim().Equals("PATHSTITCH", StringComparison.OrdinalIgnoreCase);
            else if (inPathstitchXData && code == "1000")
                xdataStrings.Add(DecodeDxfUnicodeEscapes(value));
            else if (inPathstitchXData && code == "1070"
                     && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var xdataInteger))
                xdataIntegers.Add(xdataInteger);
            else if (inPathstitchXData && code == "1040" && TryParseDouble(value.Trim(), out var xdataReal))
                xdataReals.Add(xdataReal);

            cursor += 2;
        }

        index = cursor - 2;
        if (startX is not double resolvedX || startY is not double resolvedY || string.IsNullOrWhiteSpace(text))
            return null;

        var fontFamily = xdataStrings.Count > 0 && !string.IsNullOrWhiteSpace(xdataStrings[0])
            ? xdataStrings[0]
            : null;
        if (xdataStrings.Count > 1)
            text = string.Concat(xdataStrings.Skip(1)).Replace("\u0001NL\u0001", "\n", StringComparison.Ordinal);
        var characterSpacing = xdataReals.Count > 0 && double.IsFinite(xdataReals[0]) ? xdataReals[0] : 0.0;
        var isBold = xdataIntegers.Count > 0 && xdataIntegers[0] != 0;
        var isItalic = xdataIntegers.Count > 1 && xdataIntegers[1] != 0;
        var isUnderline = xdataIntegers.Count > 2 && xdataIntegers[2] != 0;
        var resolvedHeightValue = Math.Max(height ?? 5.0, 0.1);
        var resolvedRotationValue = rotation ?? 0.0;
        var resolvedWidthFactor = NormalizeWidthFactor(widthFactor ?? 1.0);
        var sourceXSign = resolvedWidthFactor < 0.0 ? -1.0 : 1.0;
        if ((textGenerationFlags & 2) != 0)
            sourceXSign = -sourceXSign;
        var sourceYSign = (textGenerationFlags & 4) != 0 ? -1.0 : 1.0;
        var resolvedWidthMagnitude = Math.Abs(resolvedWidthFactor);
        var extrusion = new DxfVector3(extrusionX, extrusionY, extrusionZ);
        var start = TransformOcsPoints([new DxfPoint(resolvedX, resolvedY)], startZ, extrusion)[0];
        var rotationRadians = DegreesToRadians(resolvedRotationValue);
        var obliqueRadians = DegreesToRadians(obliqueAngle ?? 0.0);
        var cosine = Math.Cos(rotationRadians);
        var sine = Math.Sin(rotationRadians);
        var tangent = Math.Tan(obliqueRadians);
        var projectedAxes = TransformOcsPoints(
            [
                new DxfPoint(
                    sourceXSign * resolvedHeightValue * resolvedWidthMagnitude * cosine,
                    sourceXSign * resolvedHeightValue * resolvedWidthMagnitude * sine),
                new DxfPoint(
                    sourceYSign * resolvedHeightValue * ((tangent * cosine) - sine),
                    sourceYSign * resolvedHeightValue * ((tangent * sine) + cosine)),
            ],
            0.0,
            extrusion);
        var textBasis = new Editor2DTextBasis(
            projectedAxes[0].X,
            projectedAxes[0].Y,
            projectedAxes[1].X,
            projectedAxes[1].Y);
        if (!textBasis.IsFinite || Math.Abs(textBasis.Determinant) <= 1e-9)
            return null;

        Editor2DGeometry.ProjectTextBasis(
            textBasis,
            out var projectedHeight,
            out var projectedRotation,
            out var projectedWidthFactor);
        resolvedHeightValue = projectedHeight ?? resolvedHeightValue;
        resolvedRotationValue = projectedRotation ?? resolvedRotationValue;
        resolvedWidthFactor = projectedWidthFactor ?? resolvedWidthFactor;
        var points = BuildTextBoundsPoints(
            start,
            text,
            resolvedHeightValue,
            resolvedRotationValue,
            resolvedWidthFactor,
            textBasis);
        return new DxfPreviewPath(
            Id: $"text-{entityIndex}",
            EntityType: "TEXT",
            Points: points,
            IsClosed: false,
            IsAxisAlignedRectangle: false,
            Start: start,
            Text: text,
            TextHeight: resolvedHeightValue,
            RotationDegrees: resolvedRotationValue,
            WidthFactor: resolvedWidthFactor,
            FontFamily: fontFamily,
            CharacterSpacing: characterSpacing,
            IsBold: isBold,
            IsItalic: isItalic,
            IsUnderline: isUnderline,
            TextBasis: textBasis);
    }
    private static DxfPreviewPath? CreatePreviewPath(
        string id,
        string entityType,
        IReadOnlyList<DxfVertex> vertices,
        bool isClosed,
        double elevation = 0.0,
        DxfVector3? extrusion = null)
    {
        if (vertices.Count < 2)
            return null;

        var points = BuildPolylinePoints(vertices, isClosed);
        if (extrusion is { } extrusionVector
            && (Math.Abs(elevation) > 1e-12
                || Math.Abs(extrusionVector.X) > 1e-12
                || Math.Abs(extrusionVector.Y) > 1e-12
                || Math.Abs(extrusionVector.Z - 1.0) > 1e-12))
        {
            points = TransformOcsPoints(points, elevation, extrusionVector);
        }
        var editorPoints = points
            .Select(static point => new Editor2DPoint(point.X, point.Y))
            .ToArray();
        return points.Count >= 2
            ? new DxfPreviewPath(
                Id: id,
                EntityType: entityType,
                Points: points,
                IsClosed: isClosed,
                IsAxisAlignedRectangle: Editor2DGeometry.IsAxisAlignedRectangle(editorPoints, isClosed))
            : null;
    }

    private static void AppendConstructionTables(StringBuilder builder)
        => AppendLayerTables(builder, [new ExportLayer("CONSTRUCTION", "#808080", 0, 0, true)]);

    private static void AppendLine(
        StringBuilder builder,
        string layerName,
        Editor2DPoint start,
        Editor2DPoint end,
        bool isConstruction = false,
        string? entityHandle = null)
    {
        AppendPair(builder, 0, "LINE");
        AppendOptionalEntityHandle(builder, entityHandle);
        AppendPair(builder, 100, "AcDbEntity");
        AppendPair(builder, 8, layerName);
        AppendConstructionEntityStyle(builder, isConstruction);
        AppendPair(builder, 100, "AcDbLine");
        AppendPair(builder, 10, Format(start.X));
        AppendPair(builder, 20, Format(start.Y));
        AppendPair(builder, 11, Format(end.X));
        AppendPair(builder, 21, Format(end.Y));
    }

    private static void AppendLwPolyline(
        StringBuilder builder,
        string layerName,
        DxfPolyline polyline,
        bool isConstruction = false,
        string? entityHandle = null)
    {
        if (polyline.Points.Count < 2)
            return;

        AppendPair(builder, 0, "LWPOLYLINE");
        AppendOptionalEntityHandle(builder, entityHandle);
        AppendPair(builder, 100, "AcDbEntity");
        AppendPair(builder, 8, layerName);
        AppendConstructionEntityStyle(builder, isConstruction);
        AppendPair(builder, 100, "AcDbPolyline");
        AppendPair(builder, 90, polyline.Points.Count.ToString(CultureInfo.InvariantCulture));
        AppendPair(builder, 70, (polyline.IsClosed ? 1 : 0).ToString(CultureInfo.InvariantCulture));

        foreach (var point in polyline.Points)
        {
            AppendPair(builder, 10, Format(point.X));
            AppendPair(builder, 20, Format(point.Y));
        }
    }

    private static void AppendCircle(
        StringBuilder builder,
        string layerName,
        Editor2DPoint center,
        double radius,
        bool isConstruction = false,
        string? entityHandle = null)
    {
        AppendPair(builder, 0, "CIRCLE");
        AppendOptionalEntityHandle(builder, entityHandle);
        AppendPair(builder, 100, "AcDbEntity");
        AppendPair(builder, 8, layerName);
        AppendConstructionEntityStyle(builder, isConstruction);
        AppendPair(builder, 100, "AcDbCircle");
        AppendPair(builder, 10, Format(center.X));
        AppendPair(builder, 20, Format(center.Y));
        AppendPair(builder, 40, Format(radius));
    }

    private static void AppendArc(
        StringBuilder builder,
        string layerName,
        Editor2DPoint center,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees,
        bool isConstruction = false,
        string? entityHandle = null)
    {
        AppendPair(builder, 0, "ARC");
        AppendOptionalEntityHandle(builder, entityHandle);
        AppendPair(builder, 100, "AcDbEntity");
        AppendPair(builder, 8, layerName);
        AppendConstructionEntityStyle(builder, isConstruction);
        AppendPair(builder, 100, "AcDbCircle");
        AppendPair(builder, 10, Format(center.X));
        AppendPair(builder, 20, Format(center.Y));
        AppendPair(builder, 40, Format(radius));
        AppendPair(builder, 100, "AcDbArc");
        AppendPair(builder, 50, Format(startAngleDegrees));
        AppendPair(builder, 51, Format(endAngleDegrees));
    }

    private static void AppendHatch(
        StringBuilder builder,
        string layerName,
        DxfPolyline polyline,
        IReadOnlyList<IReadOnlyList<DxfPoint>>? sourceLoops = null,
        string? entityHandle = null)
    {
        var loops = sourceLoops is { Count: > 0 }
            ? sourceLoops.Where(static loop => loop.Count >= 3).ToArray()
            : [polyline.Points];
        if (loops.Length == 0)
            loops = [polyline.Points];

        AppendPair(builder, 0, "HATCH");
        AppendOptionalEntityHandle(builder, entityHandle);
        AppendPair(builder, 100, "AcDbEntity");
        AppendPair(builder, 8, layerName);
        AppendPair(builder, 100, "AcDbHatch");
        AppendPair(builder, 10, "0");
        AppendPair(builder, 20, "0");
        AppendPair(builder, 30, "0");
        AppendPair(builder, 210, "0");
        AppendPair(builder, 220, "0");
        AppendPair(builder, 230, "1");
        AppendPair(builder, 2, "SOLID");
        AppendPair(builder, 70, "1");
        AppendPair(builder, 71, "0");
        AppendPair(builder, 91, loops.Length.ToString(CultureInfo.InvariantCulture));
        for (var loopIndex = 0; loopIndex < loops.Length; loopIndex++)
        {
            var loop = loops[loopIndex];
            AppendPair(builder, 92, loopIndex == 0 ? "3" : "2");
            AppendPair(builder, 72, "0");
            AppendPair(builder, 73, "1");
            AppendPair(builder, 93, loop.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var point in loop)
            {
                AppendPair(builder, 10, Format(point.X));
                AppendPair(builder, 20, Format(point.Y));
            }
            AppendPair(builder, 97, "0");
        }
        AppendPair(builder, 75, "0");
        AppendPair(builder, 76, "1");
        AppendPair(builder, 98, "0");
    }
    private static void DecomposeTextBasisForDxf(
        Editor2DTextBasis basis,
        out double height,
        out double rotationDegrees,
        out double widthFactor,
        out double obliqueAngleDegrees,
        out int generationFlags)
    {
        if (!basis.IsFinite || Math.Abs(basis.Determinant) <= 1e-9)
            throw new InvalidDataException("DXF TEXT basis must be finite and non-singular.");

        var uLength = Math.Sqrt((basis.Ux * basis.Ux) + (basis.Uy * basis.Uy));
        if (!double.IsFinite(uLength) || uLength <= 1e-9)
            throw new InvalidDataException("DXF TEXT baseline must have a positive finite length.");

        var sourceXSign = basis.Determinant < 0.0 ? -1.0 : 1.0;
        var rotationAxisX = sourceXSign * basis.Ux / uLength;
        var rotationAxisY = sourceXSign * basis.Uy / uLength;
        var perpendicularX = -rotationAxisY;
        var perpendicularY = rotationAxisX;
        var shearProjection = (basis.Vx * rotationAxisX) + (basis.Vy * rotationAxisY);
        var heightProjection = (basis.Vx * perpendicularX) + (basis.Vy * perpendicularY);
        if (!double.IsFinite(heightProjection) || Math.Abs(heightProjection) <= 1e-9)
            throw new InvalidDataException("DXF TEXT height projection is singular.");

        var sourceYSign = heightProjection < 0.0 ? -1.0 : 1.0;
        height = Math.Abs(heightProjection);
        widthFactor = uLength / height;
        rotationDegrees = Math.Atan2(rotationAxisY, rotationAxisX) * 180.0 / Math.PI;
        obliqueAngleDegrees = Math.Atan(shearProjection / heightProjection) * 180.0 / Math.PI;
        if (!double.IsFinite(widthFactor)
            || !double.IsFinite(obliqueAngleDegrees)
            || Math.Abs(obliqueAngleDegrees) > 85.0 + 1e-9)
        {
            throw new InvalidDataException("DXF TEXT oblique angle exceeds the supported +/-85 degree range.");
        }

        generationFlags = (sourceXSign < 0.0 ? 2 : 0) | (sourceYSign < 0.0 ? 4 : 0);
    }
    private static void AppendText(
        StringBuilder builder,
        string layerName,
        Editor2DPoint start,
        string text,
        double height,
        double rotationDegrees,
        double widthFactor,
        Editor2DTextBasis? textBasis,
        bool isConstruction = false,
        string? fontFamily = null,
        double characterSpacing = 0.0,
        bool isBold = false,
        bool isItalic = false,
        bool isUnderline = false,
        bool useLegacyUnicodeEscapes = false,
        string? entityHandle = null)
    {
        AppendPair(builder, 0, "TEXT");
        AppendOptionalEntityHandle(builder, entityHandle);
        AppendPair(builder, 100, "AcDbEntity");
        AppendPair(builder, 8, layerName);
        AppendConstructionEntityStyle(builder, isConstruction);
        AppendPair(builder, 100, "AcDbText");
        AppendPair(builder, 7, "STANDARD");
        AppendPair(builder, 10, Format(start.X));
        AppendPair(builder, 20, Format(start.Y));
        var outputHeight = Math.Max(height, 0.1);
        var outputRotation = rotationDegrees;
        var normalizedWidthFactor = NormalizeWidthFactor(widthFactor);
        var widthMagnitude = Math.Abs(normalizedWidthFactor);
        var obliqueAngle = 0.0;
        var generationFlags = normalizedWidthFactor < 0.0 ? 2 : 0;
        if (textBasis is not null)
        {
            DecomposeTextBasisForDxf(
                textBasis,
                out outputHeight,
                out outputRotation,
                out widthMagnitude,
                out obliqueAngle,
                out generationFlags);
        }

        AppendPair(builder, 40, Format(outputHeight));
        if (Math.Abs(widthMagnitude - 1.0) > 1e-9)
            AppendPair(builder, 41, Format(widthMagnitude));
        if (generationFlags != 0)
            AppendPair(builder, 71, generationFlags.ToString(CultureInfo.InvariantCulture));
        var normalizedText = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        AppendPair(builder, 1, normalizedText.Replace('\n', ' '));
        if (Math.Abs(outputRotation) > 1e-9)
            AppendPair(builder, 50, Format(outputRotation));
        if (Math.Abs(obliqueAngle) > 1e-9)
            AppendPair(builder, 51, Format(obliqueAngle));
        if (NeedsPathstitchTextXData(
                normalizedText,
                fontFamily,
                characterSpacing,
                isBold,
                isItalic,
                isUnderline))
        {
            AppendTextXData(
                builder,
                normalizedText,
                fontFamily,
                characterSpacing,
                isBold,
                isItalic,
                isUnderline,
                useLegacyUnicodeEscapes);
        }
    }

    private static bool NeedsPathstitchTextXData(Editor2DPreviewPath path)
        => string.Equals(path.EntityType, "TEXT", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(path.Text)
           && NeedsPathstitchTextXData(
               path.Text,
               path.FontFamily,
               path.CharacterSpacing,
               path.IsBold,
               path.IsItalic,
               path.IsUnderline);

    private static bool NeedsPathstitchTextXData(
        string text,
        string? fontFamily,
        double characterSpacing,
        bool isBold,
        bool isItalic,
        bool isUnderline)
        => !string.IsNullOrWhiteSpace(fontFamily)
           || isBold
           || isItalic
           || isUnderline
           || Math.Abs(characterSpacing) > 1e-12
           || text.IndexOfAny(['\r', '\n']) >= 0;

    private static void AppendTextXData(
        StringBuilder builder,
        string text,
        string? fontFamily,
        double characterSpacing,
        bool isBold,
        bool isItalic,
        bool isUnderline,
        bool useLegacyUnicodeEscapes)
    {
        AppendPair(builder, 1001, "PATHSTITCH");
        AppendPair(builder, 1000, fontFamily ?? string.Empty);
        AppendPair(builder, 1070, isBold ? "1" : "0");
        AppendPair(builder, 1070, isItalic ? "1" : "0");
        AppendPair(builder, 1070, isUnderline ? "1" : "0");
        AppendPair(builder, 1040, Format(double.IsFinite(characterSpacing) ? characterSpacing : 0.0));
        var encoded = text.Replace("\n", "\u0001NL\u0001", StringComparison.Ordinal);
        var chunks = ChunkDxfStringValue(encoded, 250, useLegacyUnicodeEscapes);
        foreach (var chunk in chunks)
            AppendPair(builder, 1000, chunk);
        if (chunks.Count == 0)
            AppendPair(builder, 1000, string.Empty);
    }
    private static void AppendOptionalEntityHandle(StringBuilder builder, string? entityHandle)
    {
        if (!string.IsNullOrWhiteSpace(entityHandle))
            AppendPair(builder, 5, entityHandle.Trim());
    }
    private static void AppendConstructionEntityStyle(StringBuilder builder, bool isConstruction)
    {
        if (!isConstruction)
            return;

        AppendPair(builder, 6, "DASHED");
        AppendPair(builder, 62, "8");
    }

    private static DxfPoint[] BuildTextBoundsPoints(
        DxfPoint start,
        string text,
        double height,
        double rotationDegrees,
        double widthFactor,
        Editor2DTextBasis? textBasis = null)
        => Editor2DGeometry.BuildTextBoundsPoints(
                new Editor2DPoint(start.X, start.Y),
                text,
                height,
                rotationDegrees,
                widthFactor,
                textBasis: textBasis)
            .Select(static point => new DxfPoint(point.X, point.Y))
            .ToArray();

    private static IReadOnlyList<DxfPoint> TransformOcsPoints(
        IReadOnlyList<DxfPoint> points,
        double elevation,
        DxfVector3 extrusion)
    {
        var normal = NormalizeVector(extrusion, new DxfVector3(0.0, 0.0, 1.0));
        var axisX = Math.Abs(normal.X) < (1.0 / 64.0) && Math.Abs(normal.Y) < (1.0 / 64.0)
            ? NormalizeVector(Cross(new DxfVector3(0.0, 1.0, 0.0), normal), new DxfVector3(1.0, 0.0, 0.0))
            : NormalizeVector(Cross(new DxfVector3(0.0, 0.0, 1.0), normal), new DxfVector3(1.0, 0.0, 0.0));
        var axisY = Cross(normal, axisX);
        return points.Select(point => new DxfPoint(
            (point.X * axisX.X) + (point.Y * axisY.X) + (elevation * normal.X),
            (point.X * axisX.Y) + (point.Y * axisY.Y) + (elevation * normal.Y))).ToArray();
    }

    private static DxfVector3 NormalizeVector(DxfVector3 vector, DxfVector3 fallback)
    {
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z));
        return length > 1e-12 && double.IsFinite(length)
            ? new DxfVector3(vector.X / length, vector.Y / length, vector.Z / length)
            : fallback;
    }

    private static DxfVector3 Cross(DxfVector3 left, DxfVector3 right)
        => new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    private static IReadOnlyList<DxfPoint> BuildPolylinePoints(IReadOnlyList<DxfVertex> vertices, bool isClosed)
    {
        var points = new List<DxfPoint>(vertices.Count + 12)
        {
            new(vertices[0].X, vertices[0].Y),
        };

        for (var i = 0; i < vertices.Count - 1; i++)
            AppendSegment(points, vertices[i], vertices[i + 1]);

        if (isClosed)
        {
            AppendSegment(points, vertices[^1], vertices[0]);
            if (points.Count > 1 && AreSamePoint(points[0], points[^1]))
                points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    private static void AppendSegment(List<DxfPoint> points, DxfVertex start, DxfVertex end)
    {
        if (Math.Abs(start.Bulge) <= 1e-9)
        {
            var endPoint = new DxfPoint(end.X, end.Y);
            if (points.Count == 0 || !AreSamePoint(points[^1], endPoint))
                points.Add(endPoint);
            return;
        }

        foreach (var point in ApproximateBulgeArc(start, end))
        {
            if (points.Count == 0 || !AreSamePoint(points[^1], point))
                points.Add(point);
        }
    }

    private static IReadOnlyList<DxfPoint> ApproximateBulgeArc(DxfVertex start, DxfVertex end)
    {
        var startPoint = new DxfPoint(start.X, start.Y);
        var endPoint = new DxfPoint(end.X, end.Y);
        var dx = endPoint.X - startPoint.X;
        var dy = endPoint.Y - startPoint.Y;
        var chordLength = Math.Sqrt((dx * dx) + (dy * dy));
        if (chordLength <= 1e-9 || Math.Abs(start.Bulge) <= 1e-9)
            return [endPoint];

        var sweep = 4.0 * Math.Atan(start.Bulge);
        var perpendicularX = -dy / chordLength;
        var perpendicularY = dx / chordLength;
        var midpointX = (startPoint.X + endPoint.X) / 2.0;
        var midpointY = (startPoint.Y + endPoint.Y) / 2.0;
        var centerOffset = chordLength * (1.0 - (start.Bulge * start.Bulge)) / (4.0 * start.Bulge);
        var centerX = midpointX + (perpendicularX * centerOffset);
        var centerY = midpointY + (perpendicularY * centerOffset);
        var radius = Math.Sqrt(((startPoint.X - centerX) * (startPoint.X - centerX)) + ((startPoint.Y - centerY) * (startPoint.Y - centerY)));
        if (radius <= 1e-9)
            return [endPoint];

        var startAngle = Math.Atan2(startPoint.Y - centerY, startPoint.X - centerX);
        var segmentCount = CircularArcSegmentCount(radius, sweep);
        var points = new List<DxfPoint>(segmentCount);

        for (var segmentIndex = 1; segmentIndex <= segmentCount; segmentIndex++)
        {
            var angle = startAngle + (sweep * segmentIndex / segmentCount);
            points.Add(new DxfPoint(
                centerX + (radius * Math.Cos(angle)),
                centerY + (radius * Math.Sin(angle))));
        }

        return points;
    }

    private static IReadOnlyList<DxfPoint> ApproximateArcDegrees(
        double centerX,
        double centerY,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees,
        bool isClosed)
    {
        if (radius <= 1e-9)
            return [];

        var startRadians = DegreesToRadians(startAngleDegrees);
        var endRadians = DegreesToRadians(endAngleDegrees);
        while (endRadians <= startRadians)
            endRadians += Math.PI * 2.0;

        var sweep = isClosed ? Math.PI * 2.0 : endRadians - startRadians;
        var segmentCount = CircularArcSegmentCount(radius, sweep, isClosed ? 3 : 1);
        var points = new List<DxfPoint>(segmentCount);

        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var angle = startRadians + (sweep * segmentIndex / segmentCount);
            points.Add(new DxfPoint(
                centerX + (radius * Math.Cos(angle)),
                centerY + (radius * Math.Sin(angle))));
        }

        if (!isClosed)
        {
            points.Add(new DxfPoint(
                centerX + (radius * Math.Cos(endRadians)),
                centerY + (radius * Math.Sin(endRadians))));
        }

        return points;
    }

    private static IReadOnlyList<DxfPoint> ApproximateEllipse(
        double centerX,
        double centerY,
        double majorAxisX,
        double majorAxisY,
        double ratio,
        double startParameter,
        double endParameter,
        bool isClosed)
    {
        var sweep = NormalizeParameterSweep(startParameter, endParameter);
        if (sweep <= 1e-9)
            return [];

        var minorAxisX = -majorAxisY * ratio;
        var minorAxisY = majorAxisX * ratio;
        return FlattenEllipse(
            centerX,
            centerY,
            majorAxisX,
            majorAxisY,
            minorAxisX,
            minorAxisY,
            startParameter,
            sweep,
            isClosed);
    }

    private const double CurveFlatteningTolerance = 0.1;
    private const int MaximumCurveSegments = 1_000_000;

    private static int CircularArcSegmentCount(double radius, double sweep, int minimum = 1)
    {
        var absoluteRadius = Math.Abs(radius);
        if (absoluteRadius <= 1e-9 || !double.IsFinite(absoluteRadius) || !double.IsFinite(sweep))
            return Math.Max(1, minimum);

        // Chord sagitta = r * (1 - cos(theta / 2)). Limit theta to PI so
        // one segment never represents a major arc, even when radius < tolerance.
        var effectiveTolerance = Math.Min(CurveFlatteningTolerance, absoluteRadius);
        var maximumAngle = 2.0 * Math.Acos(1.0 - (effectiveTolerance / absoluteRadius));
        if (!double.IsFinite(maximumAngle) || maximumAngle <= 1e-12)
            return MaximumCurveSegments;

        var required = Math.Ceiling(Math.Abs(sweep) / Math.Min(maximumAngle, Math.PI));
        return Math.Clamp(
            (int)Math.Min(required, MaximumCurveSegments),
            Math.Max(1, minimum),
            MaximumCurveSegments);
    }

    private static IReadOnlyList<DxfPoint> FlattenEllipse(
        double centerX,
        double centerY,
        double majorAxisX,
        double majorAxisY,
        double minorAxisX,
        double minorAxisY,
        double startParameter,
        double sweep,
        bool isClosed)
    {
        DxfPoint Evaluate(double parameter)
            => EvaluateEllipsePoint(
                centerX,
                centerY,
                majorAxisX,
                majorAxisY,
                minorAxisX,
                minorAxisY,
                parameter);

        var endParameter = startParameter + sweep;
        var points = new List<DxfPoint> { Evaluate(startParameter) };
        AppendAdaptiveEllipseSegment(
            points,
            Evaluate,
            startParameter,
            points[0],
            endParameter,
            Evaluate(endParameter),
            depth: 0);
        if (isClosed && points.Count > 1 && AreSamePoint(points[0], points[^1]))
            points.RemoveAt(points.Count - 1);
        return points;
    }

    private static void AppendAdaptiveEllipseSegment(
        List<DxfPoint> output,
        Func<double, DxfPoint> evaluate,
        double startParameter,
        DxfPoint start,
        double endParameter,
        DxfPoint end,
        int depth)
    {
        const int maximumDepth = 20;
        var quarterParameter = startParameter + ((endParameter - startParameter) * 0.25);
        var middleParameter = (startParameter + endParameter) * 0.5;
        var threeQuarterParameter = startParameter + ((endParameter - startParameter) * 0.75);
        var quarter = evaluate(quarterParameter);
        var middle = evaluate(middleParameter);
        var threeQuarter = evaluate(threeQuarterParameter);
        var deviation = Math.Max(
            DistanceToSegment(quarter, start, end),
            Math.Max(
                DistanceToSegment(middle, start, end),
                DistanceToSegment(threeQuarter, start, end)));
        if (depth >= maximumDepth || deviation <= CurveFlatteningTolerance)
        {
            if (output.Count == 0 || !AreSamePoint(output[^1], end))
                output.Add(end);
            return;
        }

        AppendAdaptiveEllipseSegment(
            output,
            evaluate,
            startParameter,
            start,
            middleParameter,
            middle,
            depth + 1);
        AppendAdaptiveEllipseSegment(
            output,
            evaluate,
            middleParameter,
            middle,
            endParameter,
            end,
            depth + 1);
    }
    private static DxfPoint EvaluateEllipsePoint(
        double centerX,
        double centerY,
        double majorAxisX,
        double majorAxisY,
        double minorAxisX,
        double minorAxisY,
        double parameter)
        => new(
            centerX + (majorAxisX * Math.Cos(parameter)) + (minorAxisX * Math.Sin(parameter)),
            centerY + (majorAxisY * Math.Cos(parameter)) + (minorAxisY * Math.Sin(parameter)));

    private static double NormalizeParameterSweep(double startParameter, double endParameter)
    {
        var sweep = endParameter - startParameter;
        while (sweep <= 0.0)
            sweep += Math.PI * 2.0;

        return sweep;
    }

    private static bool AreSamePoint(DxfPoint left, DxfPoint right)
        => Math.Abs(left.X - right.X) <= 1e-6
           && Math.Abs(left.Y - right.Y) <= 1e-6;

    private static void IncrementEntityCount(IDictionary<string, int> entityCounts, string entityType)
    {
        var key = entityType.ToUpperInvariant();
        entityCounts[key] = entityCounts.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private static double DegreesToRadians(double degrees)
        => degrees * Math.PI / 180.0;

    private static double NormalizeWidthFactor(double widthFactor)
    {
        var magnitude = Math.Max(Math.Abs(widthFactor), 0.1);
        return widthFactor < 0.0 ? -magnitude : magnitude;
    }

    private static double? MapInsUnitsToMillimetersPerDrawingUnit(int insUnitsCode)
        => EditorLengthUnits.MillimetersPerDrawingUnit(insUnitsCode);

    private static bool TryParseDouble(string raw, out double value)
        => double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private static void AppendPair(StringBuilder builder, int code, string value)
    {
        builder.Append(code.ToString(CultureInfo.InvariantCulture));
        builder.Append('\n');
        builder.Append(value);
        builder.Append('\n');
    }

    private static string Format(double value)
        => value.ToString("0.###############", CultureInfo.InvariantCulture);
}
