using System.Text.Json.Serialization;

namespace Domain.App.Models;

public enum Editor2DLayerKind
{
    Geometry = 0,
    ReferenceImage = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter<Editor2DReferenceImageDepth>))]
public enum Editor2DReferenceImageDepth
{
    [JsonStringEnumMemberName("back")]
    Back = 0,

    [JsonStringEnumMemberName("front")]
    Front = 1,
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
    [property: JsonPropertyName("opacity")] double Opacity = 0.5,
    [property: JsonPropertyName("calibrationUnitsPerPixel")] double CalibrationUnitsPerPixel = 1.0,
    [property: JsonPropertyName("traceThreshold")] double TraceThreshold = 0.5,
    [property: JsonPropertyName("traceTolerance")] double TraceTolerance = 50.0,
    [property: JsonPropertyName("traceCornerSmoothness")] double TraceCornerSmoothness = 50.0,
    [property: JsonPropertyName("tracePathOptimization")] double TracePathOptimization = 50.0,
    [property: JsonPropertyName("traceSilhouetteOnly")] bool TraceSilhouetteOnly = false,
    [property: JsonPropertyName("originalDataBase64")] string? OriginalDataBase64 = null,
    [property: JsonPropertyName("backgroundRemoved")] bool BackgroundRemoved = false,
    [property: JsonPropertyName("depth")] Editor2DReferenceImageDepth Depth = Editor2DReferenceImageDepth.Back)
{
    public string SizeSummary => $"{Width:0.###} × {Height:0.###} units";

    [JsonIgnore]
    public bool IsBack => Depth == Editor2DReferenceImageDepth.Back;

    [JsonIgnore]
    public bool IsFront => Depth == Editor2DReferenceImageDepth.Front;
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

    [JsonIgnore]
    public string VisibilityLabel => IsVisible ? "Visible" : "Hidden";

    [JsonIgnore]
    public string VisibilityGlyph => IsVisible ? "◉" : "○";

    [JsonIgnore]
    public string LockLabel => IsLocked ? "Locked" : "Unlocked";

    [JsonIgnore]
    public string LockGlyph => IsLocked ? "🔒" : "🔓";
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

        if (data.Length >= 30
            && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P'
            && data[12] == (byte)'V' && data[13] == (byte)'P' && data[14] == (byte)'8' && data[15] == (byte)'X')
        {
            width = 1 + ReadLittleEndianUInt24(data[24..27]);
            height = 1 + ReadLittleEndianUInt24(data[27..30]);
            return width > 0 && height > 0;
        }

        if (TryReadTiffPixelSize(data, out width, out height))
            return true;

        if (TryReadIsoBmffPixelSize(data, out width, out height))
            return true;

        return false;
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes)
        => bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3];

    private static int ReadLittleEndianUInt24(ReadOnlySpan<byte> bytes)
        => bytes[0] | bytes[1] << 8 | bytes[2] << 16;

    private static bool TryReadTiffPixelSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 10
            || !((data[0] == (byte)'I' && data[1] == (byte)'I')
                || (data[0] == (byte)'M' && data[1] == (byte)'M')))
            return false;

        var littleEndian = data[0] == (byte)'I';
        if (ReadUInt16(data[2..4], littleEndian) != 42)
            return false;

        var ifdOffset = ReadUInt32(data[4..8], littleEndian);
        if (ifdOffset > int.MaxValue || ifdOffset + 2 > (uint)data.Length)
            return false;

        var entryCount = ReadUInt16(data[(int)ifdOffset..], littleEndian);
        var entriesStart = (int)ifdOffset + 2;
        if (entryCount > (data.Length - entriesStart) / 12)
            return false;

        for (var index = 0; index < entryCount; index++)
        {
            var entry = data[(entriesStart + index * 12)..];
            var tag = ReadUInt16(entry[..2], littleEndian);
            if (tag is not (256 or 257))
                continue;

            var type = ReadUInt16(entry[2..4], littleEndian);
            var count = ReadUInt32(entry[4..8], littleEndian);
            if (count == 0)
                continue;

            uint value;
            if (type == 3 && count == 1)
                value = ReadUInt16(entry[8..10], littleEndian);
            else if (type == 4 && count == 1)
                value = ReadUInt32(entry[8..12], littleEndian);
            else if (type is 3 or 4)
            {
                var valueOffset = ReadUInt32(entry[8..12], littleEndian);
                var valueBytes = type == 3 ? 2u : 4u;
                if (valueOffset > int.MaxValue || valueOffset + valueBytes > (uint)data.Length)
                    continue;
                value = type == 3
                    ? ReadUInt16(data[(int)valueOffset..], littleEndian)
                    : ReadUInt32(data[(int)valueOffset..], littleEndian);
            }
            else
                continue;

            if (value == 0 || value > int.MaxValue)
                continue;
            if (tag == 256)
                width = (int)value;
            else
                height = (int)value;
        }

        return width > 0 && height > 0;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian)
        => littleEndian
            ? (ushort)(bytes[0] | bytes[1] << 8)
            : (ushort)(bytes[0] << 8 | bytes[1]);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian)
        => littleEndian
            ? (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24)
            : (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);

    private static bool TryReadIsoBmffPixelSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        for (var index = 4; index + 16 <= data.Length; index++)
        {
            if (data[index] != (byte)'i' || data[index + 1] != (byte)'s'
                || data[index + 2] != (byte)'p' || data[index + 3] != (byte)'e')
                continue;

            var parsedWidth = ReadBigEndianUInt32(data[(index + 8)..(index + 12)]);
            var parsedHeight = ReadBigEndianUInt32(data[(index + 12)..(index + 16)]);
            if (parsedWidth is > 0 and <= int.MaxValue && parsedHeight is > 0 and <= int.MaxValue)
            {
                width = (int)parsedWidth;
                height = (int)parsedHeight;
                return true;
            }
        }

        return false;
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> bytes)
        => (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
}
