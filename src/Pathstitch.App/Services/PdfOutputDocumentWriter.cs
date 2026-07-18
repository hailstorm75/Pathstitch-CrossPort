using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Domain.App.Models;
using SkiaSharp;

namespace Pathstitch.App.Services;

internal static class PdfOutputDocumentWriter
{
    private const double PageWidth = 612.0;
    private const double PageHeight = 792.0;
    private const double Margin = 36.0;
    private const string SansFontResourceName = "Pathstitch.Fonts.NotoSansCJKsc-Regular-Canonical.otf";
    private const string EmojiFontResourceName = "Pathstitch.Fonts.NotoEmoji-Variable.ttf";

    public static void Save(string outputPath, Editor2DPreviewDocument document)
        => Save(outputPath, document, null);

    public static void Save(string outputPath, Editor2DExportDocument document)
        => Save(outputPath, document.Geometry, document.PathMetadata);

    private static void Save(
        string outputPath,
        Editor2DPreviewDocument document,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata)
    {
        if (RequiresUnicodePdf(document))
        {
            SaveUnicodePdf(outputPath, document, pathMetadata);
            return;
        }

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
                AppendText(content, path, textStart, bounds, scale);
                continue;
            }

            if (path.Points.Count == 0)
                continue;

            var figures = path.IsFilled && path.FillLoops is { Count: > 0 }
                ? path.FillLoops
                : [path.Points];
            foreach (var figure in figures)
            {
                if (figure.Count == 0)
                    continue;
                var first = ToPagePoint(figure[0], bounds, scale);
                content.Append(Number(first.X)).Append(' ').Append(Number(first.Y)).Append(" m\n");
                foreach (var point in figure.Skip(1))
                {
                    var mapped = ToPagePoint(point, bounds, scale);
                    content.Append(Number(mapped.X)).Append(' ').Append(Number(mapped.Y)).Append(" l\n");
                }
                if (path.IsClosed)
                    content.Append("h\n");
            }
            content.Append(path.IsFilled ? "B*\n" : "S\n");
        }

        content.Append("Q\n");
        string[] fontNames =
        [
            "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
            "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
            "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        ];
        var fontResources = string.Join(
            ' ',
            fontNames.Select((_, index) => $"/F{index + 1} {index + 5} 0 R"));
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << {fontResources} >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}endstream",
        };
        objects.AddRange(fontNames.Select(name =>
            $"<< /Type /Font /Subtype /Type1 /BaseFont /{name} /Encoding /WinAnsiEncoding >>"));
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset).Append("\n%%EOF\n");

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        WriteFileAtomically(outputPath, temporaryPath => File.WriteAllText(temporaryPath, pdf.ToString(), Encoding.ASCII));
    }

    private static bool RequiresUnicodePdf(Editor2DPreviewDocument document)
        => document.Paths.Any(path =>
            path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
            && path.Text?.Any(character => character > 0x7f) == true);

    private static void SaveUnicodePdf(
        string outputPath,
        Editor2DPreviewDocument document,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata)
    {
        using var fonts = BundledPdfFonts.Load();
        ValidateUnicodeText(document, fonts);
        WriteFileAtomically(
            outputPath,
            temporaryPath => WriteUnicodePdf(temporaryPath, document, pathMetadata, fonts));
    }

    private static void WriteUnicodePdf(
        string outputPath,
        Editor2DPreviewDocument document,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata,
        BundledPdfFonts fonts)
    {
        var bounds = document.Bounds;
        var width = Math.Max(bounds.MaxX - bounds.MinX, 1.0);
        var height = Math.Max(bounds.MaxY - bounds.MinY, 1.0);
        var scale = Math.Min((PageWidth - (2 * Margin)) / width, (PageHeight - (2 * Margin)) / height);
        using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var pdf = SKDocument.CreatePdf(stream)
            ?? throw new InvalidOperationException("Could not create the Unicode PDF document.");
        var canvas = pdf.BeginPage((float)PageWidth, (float)PageHeight);
        using var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
        };
        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        foreach (var path in document.Paths)
        {
            var color = ResolveSkiaColor(path, pathMetadata);
            strokePaint.Color = color;
            fillPaint.Color = color;
            if (path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
                && path.Center is not null
                && path.Radius is > 0)
            {
                var center = ToSkiaPoint(path.Center, bounds, scale);
                var radius = (float)(path.Radius.Value * scale);
                if (path.IsFilled)
                    canvas.DrawCircle(center.X, center.Y, radius, fillPaint);
                canvas.DrawCircle(center.X, center.Y, radius, strokePaint);
                continue;
            }

            if (path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(path.Text)
                && (path.Start ?? path.Points.FirstOrDefault()) is { } textStart)
            {
                DrawUnicodeText(canvas, path, textStart, bounds, scale, color, fonts);
                continue;
            }

            if (path.Points.Count == 0)
                continue;
            var figures = path.IsFilled && path.FillLoops is { Count: > 0 }
                ? path.FillLoops
                : [path.Points];
            using var skiaPath = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var figure in figures)
            {
                if (figure.Count == 0)
                    continue;
                var first = ToSkiaPoint(figure[0], bounds, scale);
                skiaPath.MoveTo(first);
                foreach (var point in figure.Skip(1))
                    skiaPath.LineTo(ToSkiaPoint(point, bounds, scale));
                if (path.IsClosed)
                    skiaPath.Close();
            }
            if (path.IsFilled)
                canvas.DrawPath(skiaPath, fillPaint);
            canvas.DrawPath(skiaPath, strokePaint);
        }

        pdf.EndPage();
        pdf.Close();
    }

    private static void DrawUnicodeText(
        SKCanvas canvas,
        Editor2DPreviewPath path,
        Editor2DPoint start,
        Editor2DBounds bounds,
        double scale,
        SKColor color,
        BundledPdfFonts fonts)
    {
        var mapped = ToPagePoint(start, bounds, scale);
        var height = Math.Max(path.TextHeight ?? 5.0, 0.1);
        var textHeight = height * scale;
        var sourceWidthFactor = path.WidthFactor ?? 1.0;
        var widthMagnitude = Math.Max(Math.Abs(sourceWidthFactor), 0.1);
        var widthFactor = sourceWidthFactor < 0.0 ? -widthMagnitude : widthMagnitude;
        var angleDegrees = path.RotationDegrees ?? 0.0;
        var angle = angleDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var characterSpacing = double.IsFinite(path.CharacterSpacing)
            ? path.CharacterSpacing * scale / widthMagnitude
            : 0.0;
        var lines = path.Text!
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\t", "    ", StringComparison.Ordinal)
            .Split('\n');
        var lineAdvance = height * 1.2 * scale;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = SKPaintStyle.Fill,
        };
        for (var index = 0; index < lines.Length; index++)
        {
            var offset = (lines.Length - 1 - index) * lineAdvance;
            var lineX = mapped.X - (sine * offset);
            var lineY = mapped.Y + (cosine * offset);
            canvas.Save();
            canvas.Translate((float)lineX, (float)(PageHeight - lineY));
            canvas.RotateDegrees((float)-angleDegrees);
            canvas.Scale((float)widthFactor, 1);
            var advance = DrawUnicodeLine(
                canvas,
                lines[index],
                fonts,
                path.IsBold,
                path.IsItalic,
                (float)textHeight,
                (float)characterSpacing,
                paint);
            if (path.IsUnderline && advance > 0)
            {
                using var underlinePaint = new SKPaint
                {
                    IsAntialias = true,
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float)Math.Max(TextHeightStroke(height, scale) / widthMagnitude, 0.25),
                };
                canvas.DrawLine(0, (float)(height * scale * 0.1), advance, (float)(height * scale * 0.1), underlinePaint);
            }
            canvas.Restore();
        }
    }

    private static float DrawUnicodeLine(
        SKCanvas canvas,
        string line,
        BundledPdfFonts fonts,
        bool isBold,
        bool isItalic,
        float textHeight,
        float characterSpacing,
        SKPaint paint)
    {
        var textElements = StringInfo.GetTextElementEnumerator(line);
        var x = 0f;
        var hasPrevious = false;
        while (textElements.MoveNext())
        {
            var textElement = textElements.GetTextElement();
            if (hasPrevious)
                x += characterSpacing;
            var typeface = ResolveUnicodeTypeface(textElement, fonts);
            using var font = new SKFont(typeface, textHeight)
            {
                Embolden = isBold,
                SkewX = isItalic ? -0.25f : 0,
            };
            DrawUnicodeTextElement(canvas, textElement, x, font, paint);
            x += font.MeasureText(textElement, paint);
            hasPrevious = true;
        }
        return x;
    }

    private static void DrawUnicodeTextElement(
        SKCanvas canvas,
        string textElement,
        float x,
        SKFont font,
        SKPaint paint)
    {
        var glyphs = font.GetGlyphs(textElement);
        var utf8Text = Encoding.UTF8.GetBytes(textElement);
        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocateTextRun(
            font,
            glyphs.Length,
            0,
            0,
            utf8Text.Length,
            bounds: null);
        run.SetGlyphs(glyphs);
        run.SetText(utf8Text);
        run.SetClusters(new uint[glyphs.Length]);
        using var blob = builder.Build()
            ?? throw new InvalidOperationException("Could not construct the Unicode PDF text run.");
        canvas.DrawText(blob, x, 0, paint);
    }
    private static SKTypeface ResolveUnicodeTypeface(string textElement, BundledPdfFonts fonts)
    {
        if (fonts.SansTypeface.ContainsGlyphs(textElement))
            return fonts.SansTypeface;
        if (fonts.EmojiTypeface.ContainsGlyphs(textElement))
            return fonts.EmojiTypeface;

        var codePoints = string.Join(
            " ",
            textElement.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}"));
        throw new InvalidDataException(
            $"PDF export cannot render grapheme '{textElement}' ({codePoints}) with the bundled fonts.");
    }

    private static void ValidateUnicodeText(Editor2DPreviewDocument document, BundledPdfFonts fonts)
    {
        foreach (var path in document.Paths.Where(path =>
                     path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrEmpty(path.Text)))
        {
            ValidateUnicodeScalarSequence(path.Text!);
            var normalized = path.Text!
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\t", "    ", StringComparison.Ordinal);
            foreach (var line in normalized.Split('\n'))
            {
                var elements = StringInfo.GetTextElementEnumerator(line);
                while (elements.MoveNext())
                    _ = ResolveUnicodeTypeface(elements.GetTextElement(), fonts);
            }
        }
    }

    private static void ValidateUnicodeScalarSequence(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is '\r' or '\n' or '\t')
                continue;
            if (char.IsControl(character))
                throw new InvalidDataException($"PDF text contains unsupported control character U+{(int)character:X4}.");
            if (!char.IsSurrogate(character))
                continue;
            if (!char.IsHighSurrogate(character)
                || index + 1 >= value.Length
                || !char.IsLowSurrogate(value[index + 1]))
            {
                throw new InvalidDataException("PDF text contains an invalid UTF-16 surrogate sequence.");
            }
            index++;
        }
    }

    private static void WriteFileAtomically(string outputPath, Action<string> writeTemporaryFile)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException("The PDF output path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            writeTemporaryFile(temporaryPath);
            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed class BundledPdfFonts : IDisposable
    {
        private BundledPdfFonts(
            SKData sansData,
            SKTypeface sansTypeface,
            SKData emojiData,
            SKTypeface emojiTypeface)
        {
            SansData = sansData;
            SansTypeface = sansTypeface;
            EmojiData = emojiData;
            EmojiTypeface = emojiTypeface;
        }

        private SKData SansData { get; }
        private SKData EmojiData { get; }
        public SKTypeface SansTypeface { get; }
        public SKTypeface EmojiTypeface { get; }

        public static BundledPdfFonts Load()
        {
            SKData? sansData = null;
            SKTypeface? sansTypeface = null;
            SKData? emojiData = null;
            SKTypeface? emojiTypeface = null;
            try
            {
                sansData = LoadFontData(SansFontResourceName);
                sansTypeface = SKTypeface.FromData(sansData, 0)
                    ?? throw new InvalidDataException("The bundled Noto Sans SC PDF font is invalid.");
                emojiData = LoadFontData(EmojiFontResourceName);
                emojiTypeface = SKTypeface.FromData(emojiData, 0)
                    ?? throw new InvalidDataException("The bundled Noto Emoji PDF font is invalid.");
                return new BundledPdfFonts(sansData, sansTypeface, emojiData, emojiTypeface);
            }
            catch
            {
                emojiTypeface?.Dispose();
                emojiData?.Dispose();
                sansTypeface?.Dispose();
                sansData?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            EmojiTypeface.Dispose();
            EmojiData.Dispose();
            SansTypeface.Dispose();
            SansData.Dispose();
        }

        private static SKData LoadFontData(string resourceName)
        {
            using var resource = typeof(PdfOutputDocumentWriter).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Bundled PDF font resource '{resourceName}' was not found.");
            using var memory = new MemoryStream();
            resource.CopyTo(memory);
            return SKData.CreateCopy(memory.ToArray());
        }
    }
    private static SKColor ResolveSkiaColor(
        Editor2DPreviewPath path,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata)
    {
        var (red, green, blue) = ExportPathColor.ResolvePdf(path, pathMetadata);
        return new SKColor(
            (byte)Math.Clamp((int)Math.Round(red * byte.MaxValue), 0, byte.MaxValue),
            (byte)Math.Clamp((int)Math.Round(green * byte.MaxValue), 0, byte.MaxValue),
            (byte)Math.Clamp((int)Math.Round(blue * byte.MaxValue), 0, byte.MaxValue));
    }

    private static SKPoint ToSkiaPoint(Editor2DPoint point, Editor2DBounds bounds, double scale)
    {
        var mapped = ToPagePoint(point, bounds, scale);
        return new SKPoint((float)mapped.X, (float)(PageHeight - mapped.Y));
    }

    private static void AppendText(
        StringBuilder content,
        Editor2DPreviewPath path,
        Editor2DPoint start,
        Editor2DBounds bounds,
        double scale)
    {
        var mapped = ToPagePoint(start, bounds, scale);
        var height = Math.Max(path.TextHeight ?? 5.0, 0.1);
        var textHeight = height * scale;
        var sourceWidthFactor = path.WidthFactor ?? 1.0;
        var widthMagnitude = Math.Max(Math.Abs(sourceWidthFactor), 0.1);
        var widthFactor = sourceWidthFactor < 0.0 ? -widthMagnitude : widthMagnitude;
        var angle = (path.RotationDegrees ?? 0.0) * Math.PI / 180.0;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var matrixA = cosine * widthFactor;
        var matrixB = sine * widthFactor;
        var matrixC = -sine;
        var matrixD = cosine;
        var characterSpacing = double.IsFinite(path.CharacterSpacing)
            ? path.CharacterSpacing * scale / widthMagnitude
            : 0.0;
        var fontResource = ResolvePdfFontResource(path.FontFamily, path.IsBold, path.IsItalic);
        var lines = path.Text!
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n');
        var lineAdvance = height * 1.2 * scale;
        for (var index = 0; index < lines.Length; index++)
        {
            var offset = (lines.Length - 1 - index) * lineAdvance;
            var lineX = mapped.X - (sine * offset);
            var lineY = mapped.Y + (cosine * offset);
            content.Append("BT /").Append(fontResource).Append(' ').Append(Number(textHeight)).Append(" Tf ")
                .Append(Number(characterSpacing)).Append(" Tc ")
                .Append(Number(matrixA)).Append(' ').Append(Number(matrixB)).Append(' ')
                .Append(Number(matrixC)).Append(' ').Append(Number(matrixD)).Append(' ')
                .Append(Number(lineX)).Append(' ').Append(Number(lineY)).Append(" Tm (")
                .Append(EscapeText(lines[index])).Append(") Tj ET\n");
            if (path.IsUnderline && lines[index].Length > 0)
                AppendUnderline(content, lines[index], lineX, lineY, height, widthFactor, path.CharacterSpacing, angle, scale);
        }
    }

    private static string ResolvePdfFontResource(
        string? fontFamily,
        bool isBold,
        bool isItalic)
    {
        var normalized = fontFamily?.Trim() ?? string.Empty;
        var familyOffset = normalized.Contains("mono", StringComparison.OrdinalIgnoreCase)
                           || normalized.Contains("courier", StringComparison.OrdinalIgnoreCase)
                           || normalized.Contains("consolas", StringComparison.OrdinalIgnoreCase)
            ? 8
            : normalized.Contains("serif", StringComparison.OrdinalIgnoreCase)
              || normalized.Contains("times", StringComparison.OrdinalIgnoreCase)
              || normalized.Contains("georgia", StringComparison.OrdinalIgnoreCase)
                ? 4
                : 0;
        var styleOffset = isBold ? (isItalic ? 3 : 1) : (isItalic ? 2 : 0);
        return $"F{familyOffset + styleOffset + 1}";
    }

    private static void AppendUnderline(
        StringBuilder content,
        string line,
        double lineX,
        double lineY,
        double height,
        double widthFactor,
        double characterSpacing,
        double angle,
        double scale)
    {
        var widthMagnitude = Math.Abs(widthFactor);
        var direction = widthFactor < 0.0 ? -1.0 : 1.0;
        var width = direction * ((line.Length * height * 0.6 * widthMagnitude)
                                 + (Math.Max(line.Length - 1, 0) * characterSpacing));
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var underlineOffset = height * 0.1 * scale;
        var startX = lineX + (sine * underlineOffset);
        var startY = lineY - (cosine * underlineOffset);
        var endX = startX + (cosine * width * scale);
        var endY = startY + (sine * width * scale);
        content.Append(Number(Math.Max(TextHeightStroke(height, scale), 0.25))).Append(" w ")
            .Append(Number(startX)).Append(' ').Append(Number(startY)).Append(" m ")
            .Append(Number(endX)).Append(' ').Append(Number(endY)).Append(" l S\n");
    }

    private static double TextHeightStroke(double height, double scale)
        => height * scale * 0.06;
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
        => (Math.Abs(value) < 5e-4 ? 0.0 : value).ToString("0.###", CultureInfo.InvariantCulture);

    private static string EscapeText(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
