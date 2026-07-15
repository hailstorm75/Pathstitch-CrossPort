using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Pathstitch.App.Services;

internal static class SvgPreviewDocumentParser
{
    private static readonly Regex NumberPattern = new(@"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    public static DxfPreviewDocument Load(string path)
    {
        try
        {
            var root = XDocument.Load(path).Root;
            if (root is null)
                return Empty();

            var paths = new List<DxfPreviewPath>();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var unsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var element in root.Descendants())
            {
                var type = element.Name.LocalName.ToUpperInvariant();
                if (type is "SVG" or "G" or "DEFS" or "TITLE" or "DESC" or "STYLE")
                    continue;

                counts[type] = counts.TryGetValue(type, out var count) ? count + 1 : 1;
                var parsed = type switch
                {
                    "LINE" => ParseLine(element, index),
                    "POLYLINE" => ParsePoints(element, index, false),
                    "POLYGON" => ParsePoints(element, index, true),
                    "RECT" => ParseRect(element, index),
                    "CIRCLE" => ParseCircle(element, index, false),
                    "ELLIPSE" => ParseCircle(element, index, true),
                    _ => null,
                };
                if (parsed is null)
                    unsupported.Add(type);
                else
                    paths.Add(parsed);
                index++;
            }

            return new DxfPreviewDocument(paths, counts, unsupported.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return Empty();
        }
    }

    private static DxfPreviewPath? ParseLine(XElement element, int index)
        => TryDouble(element, "x1", out var x1) && TryDouble(element, "y1", out var y1)
            && TryDouble(element, "x2", out var x2) && TryDouble(element, "y2", out var y2)
            ? new DxfPreviewPath($"svg-line-{index}", "LINE", [new(x1, y1), new(x2, y2)], false)
            : null;

    private static DxfPreviewPath? ParsePoints(XElement element, int index, bool closed)
    {
        var numbers = ParseNumbers((string?)element.Attribute("points"));
        var points = new List<DxfPoint>();
        for (var i = 0; i + 1 < numbers.Count; i += 2)
            points.Add(new DxfPoint(numbers[i], numbers[i + 1]));
        return points.Count >= 2
            ? new DxfPreviewPath($"svg-points-{index}", closed ? "POLYGON" : "POLYLINE", points, closed)
            : null;
    }

    private static DxfPreviewPath? ParseRect(XElement element, int index)
    {
        var x = TryDouble(element, "x", out var resolvedX) ? resolvedX : 0;
        var y = TryDouble(element, "y", out var resolvedY) ? resolvedY : 0;
        if (!TryDouble(element, "width", out var width) || !TryDouble(element, "height", out var height)
            || width <= 0 || height <= 0)
            return null;

        return new DxfPreviewPath($"svg-rect-{index}", "RECTANGLE",
            [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)], true,
            IsAxisAlignedRectangle: true);
    }

    private static DxfPreviewPath? ParseCircle(XElement element, int index, bool ellipse)
    {
        if (!TryDouble(element, "cx", out var cx) || !TryDouble(element, "cy", out var cy)
            || !TryDouble(element, ellipse ? "rx" : "r", out var rx)
            || !TryDouble(element, ellipse ? "ry" : "r", out var ry)
            || rx <= 0 || ry <= 0)
            return null;

        var points = Enumerable.Range(0, 32)
            .Select(i =>
            {
                var angle = i * Math.PI * 2 / 32;
                return new DxfPoint(cx + Math.Cos(angle) * rx, cy + Math.Sin(angle) * ry);
            })
            .ToArray();
        return new DxfPreviewPath($"svg-{(ellipse ? "ellipse" : "circle")}-{index}", ellipse ? "ELLIPSE" : "CIRCLE", points, true,
            Center: new DxfPoint(cx, cy), Radius: ellipse ? null : rx);
    }

    private static List<double> ParseNumbers(string? value)
        => NumberPattern.Matches(value ?? string.Empty)
            .Select(match => double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN)
            .Where(number => !double.IsNaN(number))
            .ToList();

    private static bool TryDouble(XElement element, string name, out double value)
        => double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static DxfPreviewDocument Empty()
        => new([], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), ["SVG"]);
}
