using System.Text.Json.Serialization;

namespace Domain.App.Models;

public enum Editor2DLayerKind
{
    Geometry = 0,
    ReferenceImage = 1,
}

public sealed record Editor2DReferenceImage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("dataBase64")] string DataBase64,
    [property: JsonPropertyName("pixelWidth")] int PixelWidth,
    [property: JsonPropertyName("pixelHeight")] int PixelHeight,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("width")] double Width,
    [property: JsonPropertyName("height")] double Height,
    [property: JsonPropertyName("rotationDegrees")] double RotationDegrees = 0.0,
    [property: JsonPropertyName("opacity")] double Opacity = 0.65,
    [property: JsonPropertyName("calibrationUnitsPerPixel")] double CalibrationUnitsPerPixel = 1.0,
    [property: JsonPropertyName("traceThreshold")] double TraceThreshold = 0.5,
    [property: JsonPropertyName("originalDataBase64")] string? OriginalDataBase64 = null,
    [property: JsonPropertyName("backgroundRemoved")] bool BackgroundRemoved = false)
{
    public string SizeSummary => $"{Width:0.###} × {Height:0.###} units";
}

public sealed record Editor2DLayer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pathIds")] IReadOnlyList<string> PathIds,
    [property: JsonPropertyName("isVisible")] bool IsVisible = true,
    [property: JsonPropertyName("isLocked")] bool IsLocked = false,
    [property: JsonPropertyName("order")] int Order = 0,
    [property: JsonPropertyName("kind")] Editor2DLayerKind Kind = Editor2DLayerKind.Geometry,
    [property: JsonPropertyName("referenceImage")] Editor2DReferenceImage? ReferenceImage = null,
    [property: JsonPropertyName("colorHex")] string ColorHex = "#4D7FFF",
    [property: JsonPropertyName("parentFolderId")] string? ParentFolderId = null)
{
    public bool IsReferenceImage => Kind == Editor2DLayerKind.ReferenceImage && ReferenceImage is not null;

    public string ContentSummary => IsReferenceImage
        ? ReferenceImage!.SizeSummary
        : $"{PathIds.Count} entities";
}

public sealed record Editor2DLayerFolder(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parentFolderId")] string? ParentFolderId = null);

public static class Editor2DReferenceImageMetadata
{
    public static bool TryReadPixelSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length >= 24
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            width = ReadBigEndianInt32(data[16..20]);
            height = ReadBigEndianInt32(data[20..24]);
            return width > 0 && height > 0;
        }

        if (data.Length >= 10
            && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
        {
            width = data[6] | data[7] << 8;
            height = data[8] | data[9] << 8;
            return width > 0 && height > 0;
        }

        if (data.Length >= 26 && data[0] == (byte)'B' && data[1] == (byte)'M')
        {
            width = Math.Abs(BitConverter.ToInt32(data[18..22]));
            height = Math.Abs(BitConverter.ToInt32(data[22..26]));
            return width > 0 && height > 0;
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var offset = 2;
            while (offset + 9 < data.Length)
            {
                if (data[offset] != 0xFF)
                {
                    offset++;
                    continue;
                }

                var marker = data[offset + 1];
                offset += 2;
                if (marker is 0xD8 or 0xD9)
                    continue;
                if (offset + 2 > data.Length)
                    break;
                var segmentLength = data[offset] << 8 | data[offset + 1];
                if (segmentLength < 2 || offset + segmentLength > data.Length)
                    break;
                if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
                {
                    height = data[offset + 3] << 8 | data[offset + 4];
                    width = data[offset + 5] << 8 | data[offset + 6];
                    return width > 0 && height > 0;
                }

                offset += segmentLength;
            }
        }

        return false;
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes)
        => bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3];
}
