using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Domain.App.Models;

namespace Pathstitch.App.Services;

internal static class SvgPreviewDocumentParser
{
    public static bool ConsolidateStrokes { get; set; }
    public static string FillMode { get; set; } = "strokes";
    public static double ImportThickness { get; set; }
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
                if (type == "PATH")
                {
                    var parsedPaths = ParsePath(element, index);
                    if (parsedPaths.Count == 0)
                        unsupported.Add(type);
                    else
                    {
                        foreach (var parsedPath in parsedPaths)
                        {
                            var parsed = parsedPath.IsClosed ? parsedPath with { IsFilled = PreserveFill(element) } : parsedPath;
                            parsed = ConsolidateStrokes ? ConsolidateStroke(parsed) : parsed;
                            parsed = ThickenStroke(parsed);
                            paths.Add(parsed);
                        }
                    }
                }
                else
                {
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
                    {
                        if (parsed.IsClosed)
                            parsed = parsed with { IsFilled = PreserveFill(element) };
                        parsed = ConsolidateStrokes ? ConsolidateStroke(parsed) : parsed;
                        parsed = ThickenStroke(parsed);
                        paths.Add(parsed);
                    }
                }
                index++;
            }

            var transform = ResolveRootTransform(root);
            if (transform is not null)
                paths = paths.Select(svgPath => TransformPath(svgPath, transform.Value)).ToList();

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

    private static IReadOnlyList<DxfPreviewPath> ParsePath(XElement element, int index)
    {
        var d = (string?)element.Attribute("d");
        if (string.IsNullOrWhiteSpace(d))
            return [];

        var subpaths = ParsePathData(d).ToList();
        if (subpaths.Count == 0)
            return [];

        return subpaths
            .Where(subpath => subpath.Points.Count >= 2)
            .Select((subpath, subpathIndex) => new DxfPreviewPath(
                subpaths.Count == 1 ? $"svg-path-{index}" : $"svg-path-{index}-{subpathIndex}",
                "PATH",
                subpath.Points,
                subpath.IsClosed))
            .ToArray();
    }

    private static IEnumerable<(IReadOnlyList<DxfPoint> Points, bool IsClosed)> ParsePathData(string d)
    {
        var tokens = Regex.Matches(d, @"[AaCcHhLlMmQqSsTtVvZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")
            .Select(match => match.Value)
            .ToList();

        var i = 0;
        var current = new DxfPoint(0, 0);
        var subpathStart = new DxfPoint(0, 0);
        List<DxfPoint>? points = null;
        char command = '\0';
        DxfPoint? previousCubicControl = null;
        DxfPoint? previousQuadraticControl = null;

        void AddPoint(DxfPoint point)
        {
            points ??= [];
            if (points.Count == 0 || !AreSamePoint(points[^1], point))
                points.Add(point);
            current = point;
        }

        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                command = token[0];
                i++;
            }
            else if (command == '\0')
            {
                i++;
                continue;
            }

            switch (command)
            {
                case 'M':
                case 'm':
                {
                    if (i + 1 >= tokens.Count || !TryParseDouble(tokens[i], out var x) || !TryParseDouble(tokens[i + 1], out var y))
                    {
                        i = tokens.Count;
                        break;
                    }

                    if (points is { Count: > 0 })
                    {
                        yield return (points, false);
                        points = null;
                    }

                    current = command == 'm' ? new DxfPoint(current.X + x, current.Y + y) : new DxfPoint(x, y);
                    subpathStart = current;
                    points = [current];
                    previousCubicControl = null;
                    previousQuadraticControl = null;
                    i += 2;

                    while (i + 1 < tokens.Count && !IsCommandToken(tokens[i]))
                    {
                        if (!TryParseDouble(tokens[i], out x) || !TryParseDouble(tokens[i + 1], out y))
                            break;
                        current = command == 'm' ? new DxfPoint(current.X + x, current.Y + y) : new DxfPoint(x, y);
                        AddPoint(current);
                        i += 2;
                    }

                    command = command == 'm' ? 'l' : 'L';
                    break;
                }
                case 'L':
                case 'l':
                    while (i + 1 < tokens.Count && !IsCommandToken(tokens[i]))
                    {
                        if (!TryParseDouble(tokens[i], out var x) || !TryParseDouble(tokens[i + 1], out var y))
                            break;
                        current = command == 'l' ? new DxfPoint(current.X + x, current.Y + y) : new DxfPoint(x, y);
                        AddPoint(current);
                        i += 2;
                    }
                    previousCubicControl = null;
                    previousQuadraticControl = null;
                    break;
                case 'H':
                case 'h':
                    while (i < tokens.Count && !IsCommandToken(tokens[i]))
                    {
                        if (!TryParseDouble(tokens[i], out var x))
                            break;
                        current = command == 'h' ? new DxfPoint(current.X + x, current.Y) : new DxfPoint(x, current.Y);
                        AddPoint(current);
                        i++;
                    }
                    previousCubicControl = null;
                    previousQuadraticControl = null;
                    break;
                case 'V':
                case 'v':
                    while (i < tokens.Count && !IsCommandToken(tokens[i]))
                    {
                        if (!TryParseDouble(tokens[i], out var y))
                            break;
                        current = command == 'v' ? new DxfPoint(current.X, current.Y + y) : new DxfPoint(current.X, y);
                        AddPoint(current);
                        i++;
                    }
                    previousCubicControl = null;
                    previousQuadraticControl = null;
                    break;
                case 'C':
                case 'c':
                    while (i + 5 < tokens.Count && !IsCommandToken(tokens[i]))
                    {
                        if (!TryParseDouble(tokens[i], out var x1) || !TryParseDouble(tokens[i + 1], out var y1)
                            || !TryParseDouble(tokens[i + 2], out var x2) || !TryParseDouble(tokens[i + 3], out var y2)
                            || !TryParseDouble(tokens[i + 4], out var x) || !TryParseDouble(tokens[i + 5], out var y))
                            break;
                        var control1 = command == 'c' ? new DxfPoint(current.X + x1, current.Y + y1) : new DxfPoint(x1, y1);
                        var control2 = command == 'c' ? new DxfPoint(current.X + x2, current.Y + y2) : new DxfPoint(x2, y2);
                        var end = command == 'c' ? new DxfPoint(current.X + x, current.Y + y) : new DxfPoint(x, y);
                        AppendCubic(points ??= [current], current, control1, control2, end);
                        current = end;
                        previousCubicControl = control2;
                        previousQuadraticControl = null;
                        i += 6;
                    }
                    break;
                case 'S':
                case 's':
                    while (i + 3 < tokens.Count && !IsCommandToken(tokens[i]))
                    {
                        if (!TryParseDouble(tokens[i], out var x2) || !TryParseDouble(tokens[i + 1], out var y2)
                            || !TryParseDouble(tokens[i + 2], out var x) || !TryParseDouble(tokens[i + 3], out var y))
                            break;
                        var control1 = previousCubicControl is { } prevCubic ? ReflectPoint(prevCubic, current) : current;
                        var control2 = command == 's' ? new DxfPoint(current.X + x2, current.Y + y2) : new DxfPoint(x2, y2);
                        var end = command == 's' ? new DxfPoint(current.X + x, current.Y + y) : new DxfPoint(x, y);
                        AppendCubic(points ??= [current], current, control1, control2, end);
                        current = end;
                        previousCubicControl = control2;
                        previousQuadraticControl = null;
                        i += 4;
                    }
                    break;
                case 'Q':
                case 'q':
                    while (i + 3 < tokens.Count && !IsCommandToken(tokens[i]))
                    {
                        if (!TryParseDouble(tokens[i], out var x1) || !TryParseDouble(tokens[i + 1], out var y1)
                            || !TryParseDouble(tokens[i + 2], out var x) || !TryParseDouble(tokens[i + 3], out var y))
                            break;
                        var control = command == 'q' ? new DxfPoint(current.X + x1, current.Y + y1) : new DxfPoint(x1, y1);
                        var end = command == 'q' ? new DxfPoint(current.X + x, current.Y + y) : new DxfPoint(x, y);
                        AppendQuadratic(points ??= [current], current, control, end);
                        current = end;
                        previousQuadraticControl = control;
                        previousCubicControl = null;
                        i += 4;
                    }
                    break;
                case 'T':
                case 't':
                    while (i + 1 < tokens.Count && !IsCommandToken(tokens[i]))
                    {
                        if (!TryParseDouble(tokens[i], out var x) || !TryParseDouble(tokens[i + 1], out var y))
                            break;
                        var control = previousQuadraticControl is { } prevQuadratic ? ReflectPoint(prevQuadratic, current) : current;
                        var end = command == 't' ? new DxfPoint(current.X + x, current.Y + y) : new DxfPoint(x, y);
                        AppendQuadratic(points ??= [current], current, control, end);
                        current = end;
                        previousQuadraticControl = control;
                        previousCubicControl = null;
                        i += 2;
                    }
                    break;
                case 'A':
                case 'a':
                    while (i + 6 < tokens.Count && !IsCommandToken(tokens[i]))
                    {
                        if (!TryParseDouble(tokens[i], out var rx) || !TryParseDouble(tokens[i + 1], out var ry)
                            || !TryParseDouble(tokens[i + 2], out var angle) || !TryParseDouble(tokens[i + 3], out var largeArcFlag)
                            || !TryParseDouble(tokens[i + 4], out var sweepFlag) || !TryParseDouble(tokens[i + 5], out var x)
                            || !TryParseDouble(tokens[i + 6], out var y))
                            break;
                        var end = command == 'a' ? new DxfPoint(current.X + x, current.Y + y) : new DxfPoint(x, y);
                        points ??= [current];
                        AppendArc(points, current, rx, ry, angle, largeArcFlag != 0, sweepFlag != 0, end);
                        current = end;
                        previousCubicControl = null;
                        previousQuadraticControl = null;
                        i += 7;
                    }
                    break;
                case 'Z':
                case 'z':
                    if (points is { Count: > 0 })
                    {
                        if (!AreSamePoint(points[0], current))
                            points.Add(points[0]);
                        yield return (points, true);
                    }
                    points = null;
                    current = subpathStart;
                    previousCubicControl = null;
                    previousQuadraticControl = null;
                    i++;
                    break;
                default:
                    i++;
                    break;
            }
        }

        if (points is { Count: > 0 })
            yield return (points, false);
    }

    private static bool IsCommandToken(string token)
        => token.Length == 1 && char.IsLetter(token[0]);

    private static DxfPoint ReflectPoint(DxfPoint point, DxfPoint across)
        => new((across.X * 2.0) - point.X, (across.Y * 2.0) - point.Y);

    private static void AppendQuadratic(List<DxfPoint> points, DxfPoint start, DxfPoint control, DxfPoint end)
    {
        const int segments = 12;
        for (var segment = 1; segment <= segments; segment++)
        {
            var t = (double)segment / segments;
            var oneMinusT = 1.0 - t;
            points.Add(new DxfPoint(
                oneMinusT * oneMinusT * start.X + 2.0 * oneMinusT * t * control.X + t * t * end.X,
                oneMinusT * oneMinusT * start.Y + 2.0 * oneMinusT * t * control.Y + t * t * end.Y));
        }
    }

    private static void AppendCubic(List<DxfPoint> points, DxfPoint start, DxfPoint control1, DxfPoint control2, DxfPoint end)
    {
        const int segments = 16;
        for (var segment = 1; segment <= segments; segment++)
        {
            var t = (double)segment / segments;
            var oneMinusT = 1.0 - t;
            points.Add(new DxfPoint(
                (oneMinusT * oneMinusT * oneMinusT * start.X)
                + (3.0 * oneMinusT * oneMinusT * t * control1.X)
                + (3.0 * oneMinusT * t * t * control2.X)
                + (t * t * t * end.X),
                (oneMinusT * oneMinusT * oneMinusT * start.Y)
                + (3.0 * oneMinusT * oneMinusT * t * control1.Y)
                + (3.0 * oneMinusT * t * t * control2.Y)
                + (t * t * t * end.Y)));
        }
    }

    private static void AppendArc(
        List<DxfPoint> points,
        DxfPoint start,
        double rx,
        double ry,
        double angleDegrees,
        bool largeArc,
        bool sweep,
        DxfPoint end)
    {
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx <= 1e-9 || ry <= 1e-9)
        {
            points.Add(end);
            return;
        }

        var phi = angleDegrees * Math.PI / 180.0;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);
        var dx = (start.X - end.X) / 2.0;
        var dy = (start.Y - end.Y) / 2.0;
        var x1p = (cosPhi * dx) + (sinPhi * dy);
        var y1p = (-sinPhi * dx) + (cosPhi * dy);

        var rxSq = rx * rx;
        var rySq = ry * ry;
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var lambda = (x1pSq / rxSq) + (y1pSq / rySq);
        if (lambda > 1.0)
        {
            var scale = Math.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        var sign = largeArc == sweep ? -1.0 : 1.0;
        var numerator = (rxSq * rySq) - (rxSq * y1pSq) - (rySq * x1pSq);
        var denominator = (rxSq * y1pSq) + (rySq * x1pSq);
        var coef = denominator <= 1e-9 ? 0.0 : sign * Math.Sqrt(Math.Max(0.0, numerator / denominator));
        var cxp = coef * ((rx * y1p) / ry);
        var cyp = coef * (-(ry * x1p) / rx);
        var cx = (cosPhi * cxp) - (sinPhi * cyp) + ((start.X + end.X) / 2.0);
        var cy = (sinPhi * cxp) + (cosPhi * cyp) + ((start.Y + end.Y) / 2.0);

        double Angle(double ux, double uy, double vx, double vy)
        {
            var dot = (ux * vx) + (uy * vy);
            var det = (ux * vy) - (uy * vx);
            return Math.Atan2(det, dot);
        }

        var theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var deltaTheta = Angle(
            (x1p - cxp) / rx,
            (y1p - cyp) / ry,
            (-x1p - cxp) / rx,
            (-y1p - cyp) / ry);

        if (!sweep && deltaTheta > 0)
            deltaTheta -= Math.PI * 2.0;
        else if (sweep && deltaTheta < 0)
            deltaTheta += Math.PI * 2.0;

        var segments = Math.Clamp((int)Math.Ceiling(Math.Abs(deltaTheta) / (Math.PI / 18.0)), 6, 64);
        for (var segment = 1; segment <= segments; segment++)
        {
            var t = (double)segment / segments;
            var theta = theta1 + (deltaTheta * t);
            var x = (cosPhi * rx * Math.Cos(theta)) - (sinPhi * ry * Math.Sin(theta)) + cx;
            var y = (sinPhi * rx * Math.Cos(theta)) + (cosPhi * ry * Math.Sin(theta)) + cy;
            points.Add(new DxfPoint(x, y));
        }
    }

    private static DxfPreviewPath ConsolidateStroke(DxfPreviewPath path)
    {
        if (!path.IsClosed || path.Points.Count < 4
            || (path.EntityType is not "RECTANGLE" and not "POLYGON"))
            return path;

        var minX = path.Points.Min(point => point.X);
        var maxX = path.Points.Max(point => point.X);
        var minY = path.Points.Min(point => point.Y);
        var maxY = path.Points.Max(point => point.Y);
        var width = maxX - minX;
        var height = maxY - minY;
        const double maxRibbonWidth = 5.0;
        if (width <= 0 || height <= 0 || (width > height && height > maxRibbonWidth) || (height >= width && width > maxRibbonWidth))
            return path;

        var centerline = width >= height
            ? new[] { new DxfPoint(minX, (minY + maxY) / 2.0), new DxfPoint(maxX, (minY + maxY) / 2.0) }
            : new[] { new DxfPoint((minX + maxX) / 2.0, minY), new DxfPoint((minX + maxX) / 2.0, maxY) };
        return path with { EntityType = "POLYLINE", Points = centerline, IsClosed = false, IsAxisAlignedRectangle = false };
    }

    private static bool PreserveFill(XElement element)
    {
        if (!string.Equals(FillMode, "preserve", StringComparison.OrdinalIgnoreCase))
            return false;

        var fill = (string?)element.Attribute("fill")
            ?? ParseStyle((string?)element.Attribute("style"), "fill");
        if (string.IsNullOrWhiteSpace(fill))
            return false;
        if (string.Equals(fill?.Trim(), "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fill?.Trim(), "transparent", StringComparison.OrdinalIgnoreCase))
            return false;

        var opacity = (string?)element.Attribute("fill-opacity")
            ?? ParseStyle((string?)element.Attribute("style"), "fill-opacity");
        return !double.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value > 0;
    }

    private static DxfPreviewPath ThickenStroke(DxfPreviewPath path)
    {
        if (ImportThickness <= 0 || path.IsFilled)
            return path;

        var sourceEntityType = path.EntityType is "RECTANGLE" or "POLYGON" ? "POLYLINE" : path.EntityType;
        var source = new Editor2DPreviewPath(
            path.Id,
            sourceEntityType,
            path.Points.Select(point => new Editor2DPoint(point.X, point.Y)).ToArray(),
            path.IsClosed);

        Editor2DPreviewPath thickened;
        if (path.IsClosed)
        {
            if (!Editor2DGeometry.TryBuildCurveOffsetPath(source, ImportThickness / 2.0, true, out thickened))
                return path;
        }
        else if (!Editor2DGeometry.TryBuildThicknessPath(source, ImportThickness, out thickened))
            return path;

        return path with
        {
            EntityType = thickened.EntityType,
            Points = thickened.Points.Select(point => new DxfPoint(point.X, point.Y)).ToArray(),
            IsClosed = thickened.IsClosed,
            IsAxisAlignedRectangle = thickened.IsAxisAlignedRectangle,
            Center = thickened.Center is { } center ? new DxfPoint(center.X, center.Y) : path.Center,
            Radius = thickened.Radius ?? path.Radius,
        };
    }

    private static string? ParseStyle(string? style, string property)
        => style?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(':', 2))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0].Trim(), property, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim())
            .FirstOrDefault();

    private static (double ScaleX, double ScaleY, double OffsetX, double OffsetY)? ResolveRootTransform(XElement root)
    {
        var viewBox = ParseNumbers((string?)root.Attribute("viewBox"));
        if (viewBox.Count != 4 || viewBox[2] <= 0 || viewBox[3] <= 0)
            return null;

        var parsedWidth = ParsePhysicalLength((string?)root.Attribute("width"));
        var parsedHeight = ParsePhysicalLength((string?)root.Attribute("height"));
        if (parsedWidth is null && parsedHeight is null)
            return null;

        var scaleX = parsedWidth is { } width
            ? width / viewBox[2]
            : 1.0;
        var scaleY = parsedHeight is { } height
            ? height / viewBox[3]
            : scaleX;
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
            return null;

        return (scaleX, scaleY, -viewBox[0] * scaleX, -viewBox[1] * scaleY);
    }

    private static DxfPreviewPath TransformPath(
        DxfPreviewPath path,
        (double ScaleX, double ScaleY, double OffsetX, double OffsetY) transform)
    {
        DxfPoint Transform(DxfPoint point)
            => new(point.X * transform.ScaleX + transform.OffsetX, point.Y * transform.ScaleY + transform.OffsetY);

        return path with
        {
            Points = path.Points.Select(Transform).ToArray(),
            Start = path.Start is { } start ? Transform(start) : null,
            Center = path.Center is { } center ? Transform(center) : null,
            Radius = path.Radius is { } radius ? radius * (transform.ScaleX + transform.ScaleY) / 2.0 : null,
            TextHeight = path.TextHeight is { } textHeight ? textHeight * transform.ScaleY : null,
        };
    }

    private static double? ParsePhysicalLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = Regex.Match(value.Trim(), @"^([-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?)([a-zA-Z]*)$");
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return null;

        var unit = match.Groups[2].Value.ToLowerInvariant();
        var factor = unit switch
        {
            "mm" => 1.0,
            "cm" => 10.0,
            "in" => 25.4,
            "pt" => 25.4 / 72.0,
            "pc" => 25.4 / 6.0,
            "px" => 25.4 / 96.0,
            "" => 1.0,
            _ => double.NaN,
        };
        return double.IsFinite(factor) && double.IsFinite(number) && number > 0 ? number * factor : null;
    }

    private static List<double> ParseNumbers(string? value)
        => NumberPattern.Matches(value ?? string.Empty)
            .Select(match => double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN)
            .Where(number => !double.IsNaN(number))
            .ToList();

    private static bool TryDouble(XElement element, string name, out double value)
        => double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool AreSamePoint(DxfPoint left, DxfPoint right)
        => Math.Abs(left.X - right.X) <= 1e-6
           && Math.Abs(left.Y - right.Y) <= 1e-6;

    private static bool TryParseDouble(string raw, out double value)
        => double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private static DxfPreviewDocument Empty()
        => new([], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), ["SVG"]);
}
