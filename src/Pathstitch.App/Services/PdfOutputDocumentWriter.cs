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
    {
        var bounds = document.Bounds;
        var width = Math.Max(bounds.MaxX - bounds.MinX, 1.0);
        var height = Math.Max(bounds.MaxY - bounds.MinY, 1.0);
        var scale = Math.Min((PageWidth - (2 * Margin)) / width, (PageHeight - (2 * Margin)) / height);
        var content = new StringBuilder()
            .Append("q\n0 0 0 RG 0 0 0 rg 1 w\n");

        foreach (var path in document.Paths)
        {
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
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}endstream",
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

    private static string Number(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
