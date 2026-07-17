using System;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Models;
using Domain.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Services;

public sealed class ProjectPreviewRenderer : IProjectPreviewRenderer
{
    public Task<byte[]?> RenderAsync(
        Editor2DExportDocument? document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = document is { Geometry.Paths.Count: > 0 }
            ? RenderGeometry(AddFramePadding(document))
            : RenderPlaceholder();
        return Task.FromResult<byte[]?>(bytes);
    }

    private static byte[] RenderGeometry(Editor2DExportDocument document)
        => PngOutputDocumentWriter.Render(
            document,
            new Editor2DExportOptions(
                SvgStrokeWidth: 0.75,
                PngLongestEdge: 640,
                PngTransparent: false));

    private static Editor2DExportDocument AddFramePadding(Editor2DExportDocument document)
    {
        var bounds = document.Geometry.Bounds;
        var padding = Math.Max(Math.Max(bounds.Width, bounds.Height) * 0.06, 1.0);
        return document with
        {
            Geometry = document.Geometry with
            {
                Bounds = new Editor2DBounds(
                    bounds.MinX - padding,
                    bounds.MinY - padding,
                    bounds.MaxX + padding,
                    bounds.MaxY + padding),
            },
        };
    }

    private static byte[] RenderPlaceholder()
    {
        using var bitmap = new SKBitmap(640, 400, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var markPaint = new SKPaint
        {
            Color = new SKColor(77, 127, 255),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 12,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };
        canvas.DrawLine(236, 190, 286, 140, markPaint);
        canvas.DrawLine(286, 140, 336, 190, markPaint);
        canvas.DrawLine(336, 190, 386, 140, markPaint);
        using var textPaint = new SKPaint
        {
            Color = new SKColor(30, 35, 48),
            IsAntialias = true,
        };
        using var font = new SKFont(SKTypeface.Default, 34);
        canvas.DrawText("Pathstitch", 320, 260, SKTextAlign.Center, font, textPaint);
        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
