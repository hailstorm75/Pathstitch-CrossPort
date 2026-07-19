using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Domain.App.Models;

namespace Pathstitch.App.Services;

internal static class SvgOutputDocumentWriter
{
    public static void Save(string outputPath, Editor2DPreviewDocument document, Editor2DExportOptions? options = null)
    {
        options ??= Editor2DExportOptions.Defaults;
        var bounds = document.Bounds;
        var width = Math.Max(bounds.MaxX - bounds.MinX, 1.0);
        var height = Math.Max(bounds.MaxY - bounds.MinY, 1.0);
        var precision = options.NormalizedSvgPrecision;
        var strokeWidth = Number(options.NormalizedSvgStrokeWidth, precision);
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"");
        builder.Append(Number(bounds.MinX, precision)).Append(' ').Append(Number(SvgCoordinateSystem.WorldBoundsToSvgMinY(bounds), precision)).Append(' ')
            .Append(Number(width, precision)).Append(' ').Append(Number(height, precision)).Append("\">");

        foreach (var path in document.Paths)
        {
            if (AppendText(builder, path, precision))
                continue;

            if (string.Equals(path.EntityType, "CIRCLE", StringComparison.OrdinalIgnoreCase)
                && path.Center is Editor2DPoint center
                && path.Radius is double radius
                && radius > 0)
            {
                builder.Append("<circle cx=\"").Append(Number(center.X, precision)).Append("\" cy=\"")
                    .Append(Number(SvgCoordinateSystem.WorldToSvgY(center.Y), precision)).Append("\" r=\"").Append(Number(radius, precision))
                    .Append("\" fill=\"").Append(path.IsFilled ? "black" : "none")
                    .Append("\" stroke=\"black\" stroke-width=\"").Append(strokeWidth).Append("\" />");
                continue;
            }

            if (path.IsFilled && path.FillLoops is { Count: > 0 })
            {
                AppendCompoundPath(builder, path.FillLoops, precision, "black", strokeWidth);
                continue;
            }

            var points = string.Join(" ", path.Points.Select(point => $"{Number(point.X, precision)},{Number(SvgCoordinateSystem.WorldToSvgY(point.Y), precision)}"));
            var element = path.IsClosed ? "polygon" : "polyline";
            builder.Append('<').Append(element).Append(" points=\"").Append(points)
                .Append("\" fill=\"").Append(path.IsFilled ? "black" : "none")
                .Append("\" stroke=\"black\" stroke-width=\"").Append(strokeWidth).Append("\" />");
        }

        builder.Append("</svg>");
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);
    }

    public static void Save(string outputPath, Editor2DExportDocument document, Editor2DExportOptions? options = null)
    {
        options ??= Editor2DExportOptions.Defaults;
        var geometry = document.Geometry;
        var bounds = geometry.Bounds;
        var width = Math.Max(bounds.MaxX - bounds.MinX, 1.0);
        var height = Math.Max(bounds.MaxY - bounds.MinY, 1.0);
        var precision = options.NormalizedSvgPrecision;
        var strokeWidth = Number(options.NormalizedSvgStrokeWidth, precision);
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"");
        builder.Append(Number(bounds.MinX, precision)).Append(' ').Append(Number(SvgCoordinateSystem.WorldBoundsToSvgMinY(bounds), precision)).Append(' ')
            .Append(Number(width, precision)).Append(' ').Append(Number(height, precision)).Append("\">");

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in BuildLayerGroups(geometry, document.PathMetadata))
        {
            var safeBase = "layer_" + Regex.Replace(group.LayerName, "[^a-zA-Z0-9\\-_.]", "_");
            var safeId = safeBase;
            for (var suffix = 1; !usedIds.Add(safeId); suffix++)
                safeId = $"{safeBase}_{suffix}";

            builder.Append("<g id=\"").Append(XmlEncode(safeId)).Append("\" data-layer-name=\"")
                .Append(XmlEncode(group.LayerName)).Append("\" stroke=\"").Append(group.ColorHex)
                .Append("\" color=\"").Append(group.ColorHex)
                .Append("\" fill=\"none\" stroke-width=\"").Append(strokeWidth).Append('"');
            if (group.IsConstruction)
                builder.Append(" stroke-dasharray=\"6 4\"");
            builder.Append('>');
            foreach (var path in group.Paths)
                AppendPath(builder, path, precision);
            builder.Append("</g>");
        }

        builder.Append("</svg>");
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);
    }

    private sealed record SvgLayerGroup(
        string LayerName,
        string ColorHex,
        int Order,
        int FirstPathIndex,
        bool IsConstruction,
        IReadOnlyList<Editor2DPreviewPath> Paths);

    private static IReadOnlyList<SvgLayerGroup> BuildLayerGroups(
        Editor2DPreviewDocument geometry,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata> metadata)
    {
        var groups = new Dictionary<string, (string Name, string Color, int Order, int First, bool Construction, List<Editor2DPreviewPath> Paths)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < geometry.Paths.Count; index++)
        {
            var path = geometry.Paths[index];
            var value = metadata.TryGetValue(path.Id, out var found)
                ? found
                : new Editor2DExportPathMetadata("EDITED_OUTPUT", "#000000", int.MaxValue);
            var construction = path.IsConstruction;
            var name = construction ? "CONSTRUCTION" : NormalizeLayerName(value.LayerName);
            var groupKey = construction ? "\0construction" : $"normal:{name}";
            var color = construction ? "#808080" : NormalizeColorHex(value.ColorHex);
            var order = construction ? int.MaxValue : value.Order;
            if (!groups.TryGetValue(groupKey, out var group))
                group = (name, color, order, index, construction, []);
            group.Paths.Add(path);
            groups[groupKey] = group;
        }

        return groups.Select(pair => new SvgLayerGroup(
                pair.Value.Name, pair.Value.Color, pair.Value.Order, pair.Value.First, pair.Value.Construction, pair.Value.Paths))
            .OrderBy(static group => group.Order)
            .ThenBy(static group => group.FirstPathIndex)
            .ThenBy(static group => group.LayerName, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AppendPath(StringBuilder builder, Editor2DPreviewPath path, int precision)
    {
        if (AppendText(builder, path, precision))
            return;

        if (string.Equals(path.EntityType, "CIRCLE", StringComparison.OrdinalIgnoreCase)
            && path.Center is Editor2DPoint center
            && path.Radius is double radius
            && radius > 0)
        {
            builder.Append("<circle cx=\"").Append(Number(center.X, precision)).Append("\" cy=\"")
                .Append(Number(SvgCoordinateSystem.WorldToSvgY(center.Y), precision)).Append("\" r=\"").Append(Number(radius, precision))
                .Append("\" fill=\"").Append(path.IsFilled ? "currentColor" : "none").Append("\" />");
            return;
        }

        if (path.IsFilled && path.FillLoops is { Count: > 0 })
        {
            AppendCompoundPath(builder, path.FillLoops, precision, "currentColor", strokeWidth: null);
            return;
        }

        var points = string.Join(" ", path.Points.Select(point => $"{Number(point.X, precision)},{Number(SvgCoordinateSystem.WorldToSvgY(point.Y), precision)}"));
        var element = path.IsClosed ? "polygon" : "polyline";
        builder.Append('<').Append(element).Append(" points=\"").Append(points)
            .Append("\" fill=\"").Append(path.IsFilled ? "currentColor" : "none").Append("\" />");
    }

    private static bool AppendText(StringBuilder builder, Editor2DPreviewPath path, int precision)
    {
        if (!string.Equals(path.EntityType, "TEXT", StringComparison.OrdinalIgnoreCase)
            || path.Start is not Editor2DPoint textStart
            || string.IsNullOrWhiteSpace(path.Text))
        {
            return false;
        }

        if (path.TextBasis is null)
            return AppendLegacyText(builder, path, textStart, precision);

        var basis = Editor2DGeometry.ResolveTextBasis(path);
        var height = Math.Sqrt((basis.Vx * basis.Vx) + (basis.Vy * basis.Vy));
        var uLength = Math.Sqrt((basis.Ux * basis.Ux) + (basis.Uy * basis.Uy));
        var widthMagnitude = uLength / height;
        var normalizedText = path.Text.Replace(((char)13).ToString(), string.Empty, StringComparison.Ordinal);
        var lines = normalizedText.Split((char)10);
        builder.Append("<text font-size=\"").Append(Number(height, precision)).Append('"')
            .Append(" fill=\"currentColor\" stroke=\"none\"");
        if (!string.IsNullOrWhiteSpace(path.FontFamily))
            builder.Append(" font-family=\"").Append(XmlEncode(path.FontFamily.Trim())).Append('"');
        if (path.IsBold)
            builder.Append(" font-weight=\"bold\"");
        if (path.IsItalic)
            builder.Append(" font-style=\"italic\"");
        if (path.IsUnderline)
            builder.Append(" text-decoration=\"underline\"");
        if (Math.Abs(path.CharacterSpacing) > 1e-12)
            builder.Append(" letter-spacing=\"").Append(Number(path.CharacterSpacing / widthMagnitude, precision)).Append('"');

        builder.Append(" transform=\"matrix(")
            .Append(Number(basis.Ux / height, precision)).Append(' ')
            .Append(Number(-basis.Uy / height, precision)).Append(' ')
            .Append(Number(-basis.Vx / height, precision)).Append(' ')
            .Append(Number(basis.Vy / height, precision)).Append(' ')
            .Append(Number(textStart.X, precision)).Append(' ')
            .Append(Number(SvgCoordinateSystem.WorldToSvgY(textStart.Y), precision)).Append(")\">");
        for (var index = 0; index < lines.Length; index++)
        {
            var localY = -(lines.Length - 1 - index) * height * 1.2;
            builder.Append("<tspan x=\"0\" y=\"")
                .Append(Number(localY, precision)).Append("\">")
                .Append(XmlEncode(lines[index])).Append("</tspan>");
        }
        builder.Append("</text>");
        return true;
    }
    private static bool AppendLegacyText(
        StringBuilder builder,
        Editor2DPreviewPath path,
        Editor2DPoint textStart,
        int precision)
    {
        var height = Math.Max(path.TextHeight ?? 5.0, 0.1);
        var sourceWidthFactor = path.WidthFactor ?? 1.0;
        var widthMagnitude = Math.Max(Math.Abs(sourceWidthFactor), 0.1);
        var widthFactor = sourceWidthFactor < 0.0 ? -widthMagnitude : widthMagnitude;
        var rotation = path.RotationDegrees ?? 0.0;
        var svgStartY = SvgCoordinateSystem.WorldToSvgY(textStart.Y);
        var normalizedText = path.Text!.Replace(((char)13).ToString(), string.Empty, StringComparison.Ordinal);
        var lines = normalizedText.Split((char)10);
        builder.Append("<text font-size=\"").Append(Number(height, precision)).Append('"')
            .Append(" fill=\"currentColor\" stroke=\"none\"");
        if (!string.IsNullOrWhiteSpace(path.FontFamily))
            builder.Append(" font-family=\"").Append(XmlEncode(path.FontFamily.Trim())).Append('"');
        if (path.IsBold)
            builder.Append(" font-weight=\"bold\"");
        if (path.IsItalic)
            builder.Append(" font-style=\"italic\"");
        if (path.IsUnderline)
            builder.Append(" text-decoration=\"underline\"");
        if (Math.Abs(path.CharacterSpacing) > 1e-12)
            builder.Append(" letter-spacing=\"").Append(Number(path.CharacterSpacing / widthMagnitude, precision)).Append('"');
        if (Math.Abs(rotation) > 1e-12 || Math.Abs(widthFactor - 1.0) > 1e-12)
        {
            builder.Append(" transform=\"translate(").Append(Number(textStart.X, precision)).Append(' ')
                .Append(Number(svgStartY, precision)).Append(") rotate(").Append(Number(-rotation, precision))
                .Append(") scale(").Append(Number(widthFactor, precision)).Append(" 1) translate(")
                .Append(Number(-textStart.X, precision)).Append(' ').Append(Number(-svgStartY, precision)).Append(")\"");
        }
        builder.Append('>');
        for (var index = 0; index < lines.Length; index++)
        {
            var worldY = textStart.Y + ((lines.Length - 1 - index) * height * 1.2);
            builder.Append("<tspan x=\"").Append(Number(textStart.X, precision)).Append("\" y=\"")
                .Append(Number(SvgCoordinateSystem.WorldToSvgY(worldY), precision)).Append("\">")
                .Append(XmlEncode(lines[index])).Append("</tspan>");
        }
        builder.Append("</text>");
        return true;
    }
    private static void AppendCompoundPath(
        StringBuilder builder,
        IReadOnlyList<IReadOnlyList<Editor2DPoint>> loops,
        int precision,
        string fill,
        string? strokeWidth)
    {
        builder.Append("<path d=\"");
        foreach (var loop in loops.Where(static loop => loop.Count >= 3))
        {
            builder.Append('M').Append(Number(loop[0].X, precision)).Append(' ')
                .Append(Number(SvgCoordinateSystem.WorldToSvgY(loop[0].Y), precision));
            foreach (var point in loop.Skip(1))
                builder.Append('L').Append(Number(point.X, precision)).Append(' ')
                    .Append(Number(SvgCoordinateSystem.WorldToSvgY(point.Y), precision));
            builder.Append('Z');
        }
        builder.Append("\" fill=\"").Append(fill).Append("\" fill-rule=\"evenodd\"");
        if (strokeWidth is not null)
            builder.Append(" stroke=\"black\" stroke-width=\"").Append(strokeWidth).Append('"');
        builder.Append(" />");
    }

    private static string NormalizeLayerName(string? value)
        => string.IsNullOrWhiteSpace(value) ? "EDITED_OUTPUT" : value.Trim();

    private static string NormalizeColorHex(string? value)
    {
        var color = value?.Trim() ?? string.Empty;
        return color.Length == 7 && color[0] == '#' && int.TryParse(color[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? color.ToUpperInvariant()
            : "#000000";
    }

    private static string Number(double value, int precision)
    {
        var normalized = value == 0.0 ? 0.0 : value;
        return normalized.ToString($"0.{new string('#', precision)}", CultureInfo.InvariantCulture);
    }

    private static string XmlEncode(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
