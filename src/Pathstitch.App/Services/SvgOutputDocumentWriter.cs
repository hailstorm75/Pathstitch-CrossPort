using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

    private static string Number(double value, int precision)
        => value.ToString($"0.{new string('#', precision)}", CultureInfo.InvariantCulture);

    private static string XmlEncode(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
