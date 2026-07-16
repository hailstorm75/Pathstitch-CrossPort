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
        builder.Append(Number(bounds.MinX, precision)).Append(' ').Append(Number(bounds.MinY, precision)).Append(' ')
            .Append(Number(width, precision)).Append(' ').Append(Number(height, precision)).Append("\">");

        foreach (var path in document.Paths)
        {
            if (string.Equals(path.EntityType, "TEXT", StringComparison.OrdinalIgnoreCase)
                && path.Start is Editor2DPoint textStart
                && !string.IsNullOrWhiteSpace(path.Text))
            {
                builder.Append("<text x=\"").Append(Number(textStart.X, precision)).Append("\" y=\"")
                    .Append(Number(textStart.Y, precision)).Append("\" font-size=\"")
                    .Append(Number(path.TextHeight ?? 5.0, precision)).Append("\">")
                    .Append(XmlEncode(path.Text!)).Append("</text>");
                continue;
            }

            if (string.Equals(path.EntityType, "CIRCLE", StringComparison.OrdinalIgnoreCase)
                && path.Center is Editor2DPoint center
                && path.Radius is double radius
                && radius > 0)
            {
                builder.Append("<circle cx=\"").Append(Number(center.X, precision)).Append("\" cy=\"")
                    .Append(Number(center.Y, precision)).Append("\" r=\"").Append(Number(radius, precision))
                    .Append("\" fill=\"none\" stroke-width=\"").Append(strokeWidth).Append("\" />");
                continue;
            }

            var points = string.Join(" ", path.Points.Select(point => $"{Number(point.X, precision)},{Number(point.Y, precision)}"));
            var element = path.IsClosed ? "polygon" : "polyline";
            builder.Append('<').Append(element).Append(" points=\"").Append(points)
                .Append("\" fill=\"none\" stroke-width=\"").Append(strokeWidth).Append("\" />");
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
        builder.Append(Number(bounds.MinX, precision)).Append(' ').Append(Number(bounds.MinY, precision)).Append(' ')
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
        if (string.Equals(path.EntityType, "TEXT", StringComparison.OrdinalIgnoreCase)
            && path.Start is Editor2DPoint textStart
            && !string.IsNullOrWhiteSpace(path.Text))
        {
            builder.Append("<text x=\"").Append(Number(textStart.X, precision)).Append("\" y=\"")
                .Append(Number(textStart.Y, precision)).Append("\" font-size=\"")
                .Append(Number(path.TextHeight ?? 5.0, precision)).Append("\">")
                .Append(XmlEncode(path.Text!)).Append("</text>");
            return;
        }

        if (string.Equals(path.EntityType, "CIRCLE", StringComparison.OrdinalIgnoreCase)
            && path.Center is Editor2DPoint center
            && path.Radius is double radius
            && radius > 0)
        {
            builder.Append("<circle cx=\"").Append(Number(center.X, precision)).Append("\" cy=\"")
                .Append(Number(center.Y, precision)).Append("\" r=\"").Append(Number(radius, precision)).Append("\" />");
            return;
        }

        var points = string.Join(" ", path.Points.Select(point => $"{Number(point.X, precision)},{Number(point.Y, precision)}"));
        var element = path.IsClosed ? "polygon" : "polyline";
        builder.Append('<').Append(element).Append(" points=\"").Append(points).Append("\" />");
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
        => value.ToString($"0.{new string('#', precision)}", CultureInfo.InvariantCulture);

    private static string XmlEncode(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
