using System;
using System.IO;
using Domain.App.Models;
using SkiaSharp;

namespace Pathstitch.App.Services;

internal static class PngOutputDocumentWriter
{
    public static void Save(string outputPath, Editor2DPreviewDocument document, Editor2DExportOptions options)
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
        canvas.Scale((float)scale, (float)scale);
        canvas.Translate((float)-bounds.MinX, (float)-bounds.MinY);

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Black,
            StrokeWidth = Math.Max(0.0f, (float)options.NormalizedSvgStrokeWidth),
            IsAntialias = true,
        };
        foreach (var path in document.Paths)
        {
            if (path.Center is Editor2DPoint center && path.Radius is double radius && radius > 0)
            {
                canvas.DrawCircle((float)center.X, (float)center.Y, (float)radius, paint);
                continue;
            }

            if (path.Points.Count < 2)
                continue;
            using var geometry = new SKPath();
            geometry.MoveTo((float)path.Points[0].X, (float)path.Points[0].Y);
            for (var index = 1; index < path.Points.Count; index++)
                geometry.LineTo((float)path.Points[index].X, (float)path.Points[index].Y);
            if (path.IsClosed)
                geometry.Close();
            canvas.DrawPath(geometry, paint);
        }

        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(outputPath, data.ToArray());
    }
}
