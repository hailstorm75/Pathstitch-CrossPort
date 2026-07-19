using System;
using System.Collections.Generic;
using System.IO;
using Domain.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Services;

public sealed class AvaloniaReferenceImageBackgroundRemovalService : IReferenceImageBackgroundRemovalService
{
    public string? RemoveBackground(string imageDataBase64)
    {
        if (string.IsNullOrWhiteSpace(imageDataBase64))
            return null;

        var comma = imageDataBase64.IndexOf(',');
        var encoded = imageDataBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0
            ? imageDataBase64[(comma + 1)..]
            : imageDataBase64;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return null;
        }

        using var bitmap = SKBitmap.Decode(bytes);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;

        var pixels = bitmap.Pixels;
        var transparent = new bool[pixels.Length];
        var queue = new Queue<int>();
        var corners = new[]
        {
            pixels[0],
            pixels[bitmap.Width - 1],
            pixels[(bitmap.Height - 1) * bitmap.Width],
            pixels[^1],
        };
        EnqueueIfBackground(0);
        EnqueueIfBackground(bitmap.Width - 1);
        EnqueueIfBackground((bitmap.Height - 1) * bitmap.Width);
        EnqueueIfBackground(pixels.Length - 1);

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var x = index % bitmap.Width;
            var y = index / bitmap.Width;
            foreach (var neighbor in Neighbors(x, y, bitmap.Width, bitmap.Height))
                EnqueueIfBackground(neighbor);
        }

        using var outputBitmap = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            outputBitmap.SetPixel(
                index % bitmap.Width,
                index / bitmap.Width,
                transparent[index] ? new SKColor(pixel.Red, pixel.Green, pixel.Blue, 0) : pixel);
        }

        using var encodedImage = SKImage.FromBitmap(outputBitmap);
        using var data = encodedImage.Encode(SKEncodedImageFormat.Png, 100);
        return data is null ? null : Convert.ToBase64String(data.ToArray());

        void EnqueueIfBackground(int index)
        {
            if (index < 0 || index >= pixels.Length || transparent[index])
                return;
            var x = index % bitmap.Width;
            var y = index / bitmap.Width;
            var background = InterpolateBackground(corners, x, y, bitmap.Width, bitmap.Height);
            if (!Similar(pixels[index], background))
                return;
            transparent[index] = true;
            queue.Enqueue(index);
        }
    }

    private static SKColor InterpolateBackground(
        IReadOnlyList<SKColor> corners,
        int x,
        int y,
        int width,
        int height)
    {
        var tx = width <= 1 ? 0.0 : x / (double)(width - 1);
        var ty = height <= 1 ? 0.0 : y / (double)(height - 1);
        byte Blend(Func<SKColor, byte> component)
        {
            var top = component(corners[0]) + ((component(corners[1]) - component(corners[0])) * tx);
            var bottom = component(corners[2]) + ((component(corners[3]) - component(corners[2])) * tx);
            return (byte)Math.Clamp(Math.Round(top + ((bottom - top) * ty)), byte.MinValue, byte.MaxValue);
        }

        return new SKColor(
            Blend(static color => color.Red),
            Blend(static color => color.Green),
            Blend(static color => color.Blue),
            Blend(static color => color.Alpha));
    }

    private static bool Similar(SKColor left, SKColor right)
    {
        var distance = Math.Abs(left.Red - right.Red)
            + Math.Abs(left.Green - right.Green)
            + Math.Abs(left.Blue - right.Blue);
        return left.Alpha == 0 || distance <= 72;
    }

    private static IEnumerable<int> Neighbors(int x, int y, int width, int height)
    {
        if (x > 0) yield return (y * width) + x - 1;
        if (x + 1 < width) yield return (y * width) + x + 1;
        if (y > 0) yield return ((y - 1) * width) + x;
        if (y + 1 < height) yield return ((y + 1) * width) + x;
    }
}
