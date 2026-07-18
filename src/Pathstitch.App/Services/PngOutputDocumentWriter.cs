using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using Domain.App.Models;
using SkiaSharp;

namespace Pathstitch.App.Services;

internal static class PngOutputDocumentWriter
{
    public static void Save(string outputPath, Editor2DPreviewDocument document, Editor2DExportOptions options)
        => Save(outputPath, document, options, null);

    public static void Save(string outputPath, Editor2DExportDocument document, Editor2DExportOptions options)
        => Save(outputPath, document.Geometry, options, document.PathMetadata);

    public static byte[] Render(Editor2DExportDocument document, Editor2DExportOptions options)
        => Render(document.Geometry, options, document.PathMetadata);

    private static void Save(
        string outputPath,
        Editor2DPreviewDocument document,
        Editor2DExportOptions options,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(outputPath, Render(document, options, pathMetadata));
    }

    private static byte[] Render(
        Editor2DPreviewDocument document,
        Editor2DExportOptions options,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata)
    {
        var bounds = document.Bounds;
        var width = Math.Max(bounds.Width, 1.0);
        var height = Math.Max(bounds.Height, 1.0);
        var scale = options.NormalizedPngLongestEdge / Math.Max(width, height);
        var pixelWidth = Math.Max(1, (int)Math.Round(width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Round(height * scale));

        using var bitmap = new SKBitmap(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(options.PngTransparent ? SKColors.Transparent : SKColors.White);
        canvas.Scale((float)scale, (float)-scale);
        canvas.Translate((float)-bounds.MinX, (float)-bounds.MaxY);

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Black,
            StrokeWidth = Math.Max(0.0f, (float)options.NormalizedSvgStrokeWidth),
            IsAntialias = true,
        };
        foreach (var path in document.Paths)
        {
            paint.Color = ExportPathColor.ResolveSkia(path, pathMetadata);
            paint.Style = path.IsFilled ? SKPaintStyle.StrokeAndFill : SKPaintStyle.Stroke;
            if (string.Equals(path.EntityType, "TEXT", StringComparison.OrdinalIgnoreCase)
                && path.Start is Editor2DPoint textStart
                && !string.IsNullOrWhiteSpace(path.Text))
            {
                DrawText(canvas, path, textStart, bounds, scale, paint.Color);
                continue;
            }

            if (path.Center is Editor2DPoint center && path.Radius is double radius && radius > 0)
            {
                canvas.DrawCircle((float)center.X, (float)center.Y, (float)radius, paint);
                continue;
            }

            if (path.Points.Count < 2)
                continue;
            using var geometry = new SKPath { FillType = SKPathFillType.EvenOdd };
            var figures = path.IsFilled && path.FillLoops is { Count: > 0 }
                ? path.FillLoops
                : [path.Points];
            foreach (var figure in figures)
            {
                if (figure.Count < 2)
                    continue;
                geometry.MoveTo((float)figure[0].X, (float)figure[0].Y);
                for (var index = 1; index < figure.Count; index++)
                    geometry.LineTo((float)figure[index].X, (float)figure[index].Y);
                if (path.IsClosed)
                    geometry.Close();
            }
            canvas.DrawPath(geometry, paint);
        }

        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    private static void DrawText(
        SKCanvas canvas,
        Editor2DPreviewPath path,
        Editor2DPoint start,
        Editor2DBounds bounds,
        double scale,
        SKColor color)
    {
        var style = path.IsBold
            ? path.IsItalic ? SKFontStyle.BoldItalic : SKFontStyle.Bold
            : path.IsItalic ? SKFontStyle.Italic : SKFontStyle.Normal;
        using var typeface = SKTypeface.FromFamilyName(
            string.IsNullOrWhiteSpace(path.FontFamily) ? null : path.FontFamily,
            style);
        using var font = new SKFont(typeface, (float)(Math.Max(path.TextHeight ?? 5.0, 0.1) * scale));
        using var textPaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        var sourceWidthFactor = path.WidthFactor ?? 1.0;
        var widthMagnitude = Math.Max(Math.Abs(sourceWidthFactor), 0.1);
        var widthFactor = sourceWidthFactor < 0.0 ? -widthMagnitude : widthMagnitude;
        var spacing = double.IsFinite(path.CharacterSpacing)
            ? path.CharacterSpacing * scale / widthMagnitude
            : 0.0;
        var lines = path.Text!.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var lineAdvance = Math.Max(path.TextHeight ?? 5.0, 0.1) * 1.2 * scale;
        var pixelX = (start.X - bounds.MinX) * scale;
        var pixelY = (bounds.MaxY - start.Y) * scale;

        canvas.Save();
        canvas.ResetMatrix();
        canvas.Translate((float)pixelX, (float)pixelY);
        canvas.RotateDegrees((float)-(path.RotationDegrees ?? 0.0));
        canvas.Scale((float)widthFactor, 1.0f);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var baseline = -(lines.Length - 1 - lineIndex) * lineAdvance;
            var x = 0.0;
            var elements = TextElements(lines[lineIndex]);
            foreach (var element in elements)
            {
                canvas.DrawText(element, (float)x, (float)baseline, SKTextAlign.Left, font, textPaint);
                x += font.MeasureText(element) + spacing;
            }
            if (path.IsUnderline && elements.Count > 0)
            {
                var width = Math.Max(0.0, x - spacing);
                using var underline = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(1.0f, font.Size / 14.0f),
                    IsAntialias = true,
                };
                canvas.DrawLine(0.0f, (float)(baseline + (font.Size * 0.08)), (float)width, (float)(baseline + (font.Size * 0.08)), underline);
            }
        }
        canvas.Restore();
    }

    private static IReadOnlyList<string> TextElements(string text)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            elements.Add(enumerator.GetTextElement());
        return elements;
    }
}
