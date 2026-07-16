using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Domain.App.Models;

namespace Pathstitch.App.Services;

internal static class PdfOutputDocumentWriter
{
    private const double PageWidth = 612.0;
    private const double PageHeight = 792.0;
    private const double Margin = 36.0;

    public static void Save(string outputPath, Editor2DPreviewDocument document)
        => Save(outputPath, document, null);

    public static void Save(string outputPath, Editor2DExportDocument document)
        => Save(outputPath, document.Geometry, document.PathMetadata);

    private static void Save(
        string outputPath,
        Editor2DPreviewDocument document,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata)
    {
        var bounds = document.Bounds;
        var width = Math.Max(bounds.MaxX - bounds.MinX, 1.0);
        var height = Math.Max(bounds.MaxY - bounds.MinY, 1.0);
        var scale = Math.Min((PageWidth - (2 * Margin)) / width, (PageHeight - (2 * Margin)) / height);
        var content = new StringBuilder()
            .Append("q\n0 0 0 RG 0 0 0 rg 1 w\n");

        foreach (var path in document.Paths)
        {
            var (red, green, blue) = ExportPathColor.ResolvePdf(path, pathMetadata);
            content.Append(Number(red)).Append(' ').Append(Number(green)).Append(' ').Append(Number(blue))
                .Append(" RG ").Append(Number(red)).Append(' ').Append(Number(green)).Append(' ').Append(Number(blue))
                .Append(" rg\n");
            if (path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
                && path.Center is not null
                && path.Radius is > 0)
            {
                AppendCircle(content, path.Center, path.Radius.Value, bounds, scale);
                content.Append(path.IsFilled ? "B\n" : "S\n");
                continue;
            }

            if (path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(path.Text)
                && (path.Start ?? path.Points.FirstOrDefault()) is { } textStart)
            {
                var mapped = ToPagePoint(textStart, bounds, scale);
                var textHeight = Math.Max(path.TextHeight ?? 5.0, 0.1) * scale;
                content.Append("BT /F1 ").Append(Number(textHeight)).Append(" Tf ")
                    .Append(Number(mapped.X)).Append(' ').Append(Number(mapped.Y)).Append(" Td (")
                    .Append(EscapeText(path.Text)).Append(") Tj ET\n");
                continue;
            }

            if (path.Points.Count == 0)
                continue;

            var first = ToPagePoint(path.Points[0], bounds, scale);
            content.Append(Number(first.X)).Append(' ').Append(Number(first.Y)).Append(" m\n");
            foreach (var point in path.Points.Skip(1))
            {
                var mapped = ToPagePoint(point, bounds, scale);
                content.Append(Number(mapped.X)).Append(' ').Append(Number(mapped.Y)).Append(" l\n");
            }

            if (path.IsClosed)
                content.Append("h\n");
            content.Append(path.IsFilled ? "B\n" : "S\n");
        }

        content.Append("Q\n");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset).Append("\n%%EOF\n");

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, pdf.ToString(), Encoding.ASCII);
    }

    private static (double X, double Y) ToPagePoint(Editor2DPoint point, Editor2DBounds bounds, double scale)
        => (Margin + ((point.X - bounds.MinX) * scale), Margin + ((point.Y - bounds.MinY) * scale));

    private static void AppendCircle(
        StringBuilder content,
        Editor2DPoint center,
        double radius,
        Editor2DBounds bounds,
        double scale)
    {
        var kappa = 0.5522847498307936;
        var centerPoint = ToPagePoint(center, bounds, scale);
        var r = radius * scale;
        content.Append(Number(centerPoint.X + r)).Append(' ').Append(Number(centerPoint.Y)).Append(" m\n");
        AppendCurve(content, centerPoint.X + r, centerPoint.Y + (kappa * r), centerPoint.X + (kappa * r), centerPoint.Y + r, centerPoint.X, centerPoint.Y + r);
        AppendCurve(content, centerPoint.X - (kappa * r), centerPoint.Y + r, centerPoint.X - r, centerPoint.Y + (kappa * r), centerPoint.X - r, centerPoint.Y);
        AppendCurve(content, centerPoint.X - r, centerPoint.Y - (kappa * r), centerPoint.X - (kappa * r), centerPoint.Y - r, centerPoint.X, centerPoint.Y - r);
        AppendCurve(content, centerPoint.X + (kappa * r), centerPoint.Y - r, centerPoint.X + r, centerPoint.Y - (kappa * r), centerPoint.X + r, centerPoint.Y);
        content.Append("h\n");
    }

    private static void AppendCurve(
        StringBuilder content,
        double control1X,
        double control1Y,
        double control2X,
        double control2Y,
        double endX,
        double endY)
        => content.Append(Number(control1X)).Append(' ').Append(Number(control1Y)).Append(' ')
            .Append(Number(control2X)).Append(' ').Append(Number(control2Y)).Append(' ')
            .Append(Number(endX)).Append(' ').Append(Number(endY)).Append(" c\n");

    private static string Number(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string EscapeText(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
