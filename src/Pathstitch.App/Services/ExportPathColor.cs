using System;
using System.Collections.Generic;
using Domain.App.Models;
using SkiaSharp;

namespace Pathstitch.App.Services;

internal static class ExportPathColor
{
    public static SKColor ResolveSkia(
        Editor2DPreviewPath path,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata)
    {
        var (red, green, blue) = ResolveBytes(path, pathMetadata);
        return new SKColor(red, green, blue);
    }

    public static (double Red, double Green, double Blue) ResolvePdf(
        Editor2DPreviewPath path,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata)
    {
        var (red, green, blue) = ResolveBytes(path, pathMetadata);
        return (red / 255.0, green / 255.0, blue / 255.0);
    }

    private static (byte Red, byte Green, byte Blue) ResolveBytes(
        Editor2DPreviewPath path,
        IReadOnlyDictionary<string, Editor2DExportPathMetadata>? pathMetadata)
    {
        if (path.IsConstruction)
            return (128, 128, 128);
        if (pathMetadata is null
            || !pathMetadata.TryGetValue(path.Id, out var metadata)
            || metadata.ColorHex is not { Length: 7 } color
            || color[0] != '#'
            || !byte.TryParse(color.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var red)
            || !byte.TryParse(color.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var green)
            || !byte.TryParse(color.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var blue))
        {
            return (0, 0, 0);
        }

        return (red, green, blue);
    }
}
