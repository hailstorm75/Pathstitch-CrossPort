using System;
using Domain.App.Models;
using Domain.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Services;

public sealed class AvaloniaReferenceImagePreparationService : IReferenceImagePreparationService
{
    public bool TryPrepare(byte[] sourceData, bool cropTransparentMargins, out PreparedReferenceImage? image)
    {
        image = null;
        if (!Editor2DReferenceImageMetadata.TryReadPixelSize(sourceData, out var sourceWidth, out var sourceHeight))
            return false;

        if (!cropTransparentMargins)
        {
            image = new PreparedReferenceImage(sourceData, sourceWidth, sourceHeight);
            return true;
        }

        using var bitmap = SKBitmap.Decode(sourceData);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            image = new PreparedReferenceImage(sourceData, sourceWidth, sourceHeight);
            return true;
        }

        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top
            || left == 0 && top == 0 && right == bitmap.Width - 1 && bottom == bitmap.Height - 1)
        {
            image = new PreparedReferenceImage(sourceData, sourceWidth, sourceHeight);
            return true;
        }

        var cropBounds = new SKRectI(left, top, right + 1, bottom + 1);
        using var cropped = new SKBitmap(
            cropBounds.Width,
            cropBounds.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        if (!bitmap.ExtractSubset(cropped, cropBounds))
        {
            image = new PreparedReferenceImage(sourceData, sourceWidth, sourceHeight);
            return true;
        }

        using var croppedImage = SKImage.FromBitmap(cropped);
        using var encoded = croppedImage.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
        {
            image = new PreparedReferenceImage(sourceData, sourceWidth, sourceHeight);
            return true;
        }

        image = new PreparedReferenceImage(encoded.ToArray(), cropped.Width, cropped.Height);
        return true;
    }
}
