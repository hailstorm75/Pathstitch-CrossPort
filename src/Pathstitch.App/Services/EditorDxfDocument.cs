using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Domain.App.Models;

namespace Pathstitch.App.Services;

internal readonly record struct DxfPoint(double X, double Y);

internal sealed record DxfPolyline(IReadOnlyList<DxfPoint> Points, bool IsClosed);

internal readonly record struct DxfVertex(double X, double Y, double Bulge);

internal sealed record DxfPreviewPath(
    string Id,
    string EntityType,
    IReadOnlyList<DxfPoint> Points,
    bool IsClosed,
    bool IsAxisAlignedRectangle = false,
    DxfPoint? Start = null,
    string? Text = null,
    double? TextHeight = null,
    double? RotationDegrees = null,
    double? WidthFactor = null,
    DxfPoint? Center = null,
    double? Radius = null,
    double? StartAngleDegrees = null,
    double? EndAngleDegrees = null);

internal sealed record DxfPreviewDocument(
    IReadOnlyList<DxfPreviewPath> Paths,
    IReadOnlyDictionary<string, int> EntityCounts,
    IReadOnlyList<string> UnsupportedEntityTypes);

internal static class EditorDxfDocument
{
    public static void SavePreviewDocument(
        string outputPath,
        Editor2DPreviewDocument document,
        string layerName = "EDITED_OUTPUT")
    {
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var builder = new StringBuilder();
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "HEADER");
        AppendPair(builder, 0, "ENDSEC");
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "ENTITIES");

        foreach (var path in document.Paths)
        {
            if (string.Equals(path.EntityType, "TEXT", StringComparison.OrdinalIgnoreCase)
                && path.Start is Editor2DPoint textStart
                && !string.IsNullOrWhiteSpace(path.Text))
            {
                AppendText(
                    builder,
                    layerName,
                    textStart,
                    path.Text!,
                    path.TextHeight ?? 5.0,
                    path.RotationDegrees ?? 0.0,
                    path.WidthFactor ?? 1.0);
                continue;
            }

            if (string.Equals(path.EntityType, "CIRCLE", StringComparison.OrdinalIgnoreCase)
                && path.Center is Editor2DPoint circleCenter
                && path.Radius is double circleRadius
                && circleRadius > 1e-9)
            {
                AppendCircle(builder, layerName, circleCenter, circleRadius);
                continue;
            }

            if (string.Equals(path.EntityType, "ARC", StringComparison.OrdinalIgnoreCase)
                && path.Center is Editor2DPoint arcCenter
                && path.Radius is double arcRadius
                && path.StartAngleDegrees is double startAngleDegrees
                && path.EndAngleDegrees is double endAngleDegrees
                && arcRadius > 1e-9)
            {
                AppendArc(builder, layerName, arcCenter, arcRadius, startAngleDegrees, endAngleDegrees);
                continue;
            }

            AppendLwPolyline(
                builder,
                layerName,
                new DxfPolyline(
                    path.Points.Select(static point => new DxfPoint(point.X, point.Y)).ToArray(),
                    path.IsClosed));
        }

        AppendPair(builder, 0, "ENDSEC");
        AppendPair(builder, 0, "EOF");
        File.WriteAllText(outputPath, builder.ToString(), Encoding.ASCII);
    }

    public static void SaveLwPolylines(
        string outputPath,
        string layerName,
        IReadOnlyList<DxfPolyline> polylines)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var builder = new StringBuilder();
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "HEADER");
        AppendPair(builder, 0, "ENDSEC");
        AppendPair(builder, 0, "SECTION");
        AppendPair(builder, 2, "ENTITIES");

        foreach (var polyline in polylines)
        {
            AppendLwPolyline(builder, layerName, polyline);
        }

        AppendPair(builder, 0, "ENDSEC");
        AppendPair(builder, 0, "EOF");
        File.WriteAllText(outputPath, builder.ToString(), Encoding.ASCII);
    }

    public static IReadOnlyList<DxfPolyline> LoadPolylines(string dxfPath)
        => LoadPreviewDocument(dxfPath).Paths
            .Select(static path => new DxfPolyline(path.Points, path.IsClosed))
            .ToArray();

    public static DxfPreviewDocument LoadPreviewDocument(string dxfPath)
    {
        if (string.IsNullOrWhiteSpace(dxfPath) || !File.Exists(dxfPath))
            return new DxfPreviewDocument([], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), []);

        if (Path.GetExtension(dxfPath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            return SvgPreviewDocumentParser.Load(dxfPath);

        var lines = File.ReadAllLines(dxfPath);
        var previewPaths = new List<DxfPreviewPath>();
        var entityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unsupportedEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inEntitiesSection = false;
        var entityIndex = 0;

        for (var i = 0; i + 1 < lines.Length; i += 2)
        {
            var code = lines[i].Trim();
            var value = lines[i + 1].Trim();

            if (code == "0"
                && string.Equals(value, "SECTION", StringComparison.OrdinalIgnoreCase)
                && i + 3 < lines.Length
                && string.Equals(lines[i + 2].Trim(), "2", StringComparison.Ordinal))
            {
                inEntitiesSection = string.Equals(lines[i + 3].Trim(), "ENTITIES", StringComparison.OrdinalIgnoreCase);
                i += 2;
                continue;
            }

            if (code == "0" && string.Equals(value, "ENDSEC", StringComparison.OrdinalIgnoreCase))
            {
                inEntitiesSection = false;
                continue;
            }

            if (!inEntitiesSection || code != "0")
                continue;

            IncrementEntityCount(entityCounts, value);

            if (string.Equals(value, "LWPOLYLINE", StringComparison.OrdinalIgnoreCase))
            {
                var entity = ParseLwPolyline(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(entity);
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "POLYLINE", StringComparison.OrdinalIgnoreCase))
            {
                var entity = ParsePolyline(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(entity);
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "LINE", StringComparison.OrdinalIgnoreCase))
            {
                var entity = ParseLine(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(entity);
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "ARC", StringComparison.OrdinalIgnoreCase))
            {
                var entity = ParseArc(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(entity);
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "CIRCLE", StringComparison.OrdinalIgnoreCase))
            {
                var entity = ParseCircle(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(entity);
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "ELLIPSE", StringComparison.OrdinalIgnoreCase))
            {
                var entity = ParseEllipse(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(entity);
                entityIndex++;
                continue;
            }

            if (string.Equals(value, "TEXT", StringComparison.OrdinalIgnoreCase))
            {
                var entity = ParseText(lines, ref i, entityIndex);
                if (entity is not null)
                    previewPaths.Add(entity);
                entityIndex++;
                continue;
            }

            unsupportedEntityTypes.Add(value.ToUpperInvariant());
            entityIndex++;
        }

        return new DxfPreviewDocument(
            previewPaths,
            entityCounts.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            unsupportedEntityTypes.OrderBy(static type => type, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static DxfPreviewPath? ParseLwPolyline(string[] lines, ref int index, int entityIndex)
    {
        var vertices = new List<DxfVertex>();
        var isClosed = false;
        double? currentX = null;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "70" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
                isClosed = (flags & 1) != 0;

            if (code == "10" && TryParseDouble(value, out var x))
                currentX = x;
            else if (code == "20" && currentX is double resolvedX && TryParseDouble(value, out var y))
            {
                vertices.Add(new DxfVertex(resolvedX, y, 0.0));
                currentX = null;
            }
            else if (code == "42"
                     && vertices.Count > 0
                     && TryParseDouble(value, out var bulge))
            {
                vertices[^1] = vertices[^1] with { Bulge = bulge };
            }

            cursor += 2;
        }

        index = cursor - 2;
        return CreatePreviewPath($"lwpolyline-{entityIndex}", "LWPOLYLINE", vertices, isClosed);
    }

    private static DxfPreviewPath? ParsePolyline(string[] lines, ref int index, int entityIndex)
    {
        var isClosed = false;
        var vertices = new List<DxfVertex>();

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();
            if (code == "0")
                break;

            if (code == "70" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
                isClosed = (flags & 1) != 0;

            cursor += 2;
        }

        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code != "0")
            {
                cursor += 2;
                continue;
            }

            if (string.Equals(value, "VERTEX", StringComparison.OrdinalIgnoreCase))
            {
                var vertex = ParseVertex(lines, ref cursor);
                if (vertex is not null)
                    vertices.Add(vertex.Value);
                continue;
            }

            if (string.Equals(value, "SEQEND", StringComparison.OrdinalIgnoreCase))
            {
                cursor += 2;
                break;
            }

            break;
        }

        index = cursor - 2;
        return CreatePreviewPath($"polyline-{entityIndex}", "POLYLINE", vertices, isClosed);
    }

    private static DxfVertex? ParseVertex(string[] lines, ref int index)
    {
        double? x = null;
        double? y = null;
        var bulge = 0.0;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedX))
                x = resolvedX;
            else if (code == "20" && TryParseDouble(value, out var resolvedY))
                y = resolvedY;
            else if (code == "42" && TryParseDouble(value, out var resolvedBulge))
                bulge = resolvedBulge;

            cursor += 2;
        }

        index = cursor - 2;
        return x is double vertexX && y is double vertexY
            ? new DxfVertex(vertexX, vertexY, bulge)
            : null;
    }

    private static DxfPreviewPath? ParseLine(string[] lines, ref int index, int entityIndex)
    {
        double? x1 = null;
        double? y1 = null;
        double? x2 = null;
        double? y2 = null;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedX1))
                x1 = resolvedX1;
            else if (code == "20" && TryParseDouble(value, out var resolvedY1))
                y1 = resolvedY1;
            else if (code == "11" && TryParseDouble(value, out var resolvedX2))
                x2 = resolvedX2;
            else if (code == "21" && TryParseDouble(value, out var resolvedY2))
                y2 = resolvedY2;

            cursor += 2;
        }

        index = cursor - 2;
        return x1 is double lineX1 && y1 is double lineY1 && x2 is double lineX2 && y2 is double lineY2
            ? new DxfPreviewPath(
                Id: $"line-{entityIndex}",
                EntityType: "LINE",
                Points: [new DxfPoint(lineX1, lineY1), new DxfPoint(lineX2, lineY2)],
                IsClosed: false,
                IsAxisAlignedRectangle: false)
            : null;
    }

    private static DxfPreviewPath? ParseArc(string[] lines, ref int index, int entityIndex)
    {
        double? centerX = null;
        double? centerY = null;
        double? radius = null;
        double? startAngleDegrees = null;
        double? endAngleDegrees = null;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedCenterX))
                centerX = resolvedCenterX;
            else if (code == "20" && TryParseDouble(value, out var resolvedCenterY))
                centerY = resolvedCenterY;
            else if (code == "40" && TryParseDouble(value, out var resolvedRadius))
                radius = resolvedRadius;
            else if (code == "50" && TryParseDouble(value, out var resolvedStartAngle))
                startAngleDegrees = resolvedStartAngle;
            else if (code == "51" && TryParseDouble(value, out var resolvedEndAngle))
                endAngleDegrees = resolvedEndAngle;

            cursor += 2;
        }

        index = cursor - 2;
        if (centerX is not double arcCenterX
            || centerY is not double arcCenterY
            || radius is not double arcRadius
            || startAngleDegrees is not double arcStartAngleDegrees
            || endAngleDegrees is not double arcEndAngleDegrees)
        {
            return null;
        }

        var points = ApproximateArcDegrees(
            arcCenterX,
            arcCenterY,
            arcRadius,
            arcStartAngleDegrees,
            arcEndAngleDegrees,
            isClosed: false);

        return points.Count >= 2
            ? new DxfPreviewPath(
                Id: $"arc-{entityIndex}",
                EntityType: "ARC",
                Points: points,
                IsClosed: false,
                IsAxisAlignedRectangle: false,
                Center: new DxfPoint(arcCenterX, arcCenterY),
                Radius: arcRadius,
                StartAngleDegrees: arcStartAngleDegrees,
                EndAngleDegrees: arcEndAngleDegrees)
            : null;
    }

    private static DxfPreviewPath? ParseCircle(string[] lines, ref int index, int entityIndex)
    {
        double? centerX = null;
        double? centerY = null;
        double? radius = null;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedCenterX))
                centerX = resolvedCenterX;
            else if (code == "20" && TryParseDouble(value, out var resolvedCenterY))
                centerY = resolvedCenterY;
            else if (code == "40" && TryParseDouble(value, out var resolvedRadius))
                radius = resolvedRadius;

            cursor += 2;
        }

        index = cursor - 2;
        if (centerX is not double circleCenterX
            || centerY is not double circleCenterY
            || radius is not double circleRadius)
        {
            return null;
        }

        var points = ApproximateArcDegrees(
            circleCenterX,
            circleCenterY,
            circleRadius,
            startAngleDegrees: 0.0,
            endAngleDegrees: 360.0,
            isClosed: true);

        return points.Count >= 3
            ? new DxfPreviewPath(
                Id: $"circle-{entityIndex}",
                EntityType: "CIRCLE",
                Points: points,
                IsClosed: true,
                IsAxisAlignedRectangle: false,
                Center: new DxfPoint(circleCenterX, circleCenterY),
                Radius: circleRadius,
                StartAngleDegrees: 0.0,
                EndAngleDegrees: 360.0)
            : null;
    }

    private static DxfPreviewPath? ParseEllipse(string[] lines, ref int index, int entityIndex)
    {
        double? centerX = null;
        double? centerY = null;
        double? majorAxisX = null;
        double? majorAxisY = null;
        double? ratio = null;
        double? startParameter = null;
        double? endParameter = null;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1].Trim();

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value, out var resolvedCenterX))
                centerX = resolvedCenterX;
            else if (code == "20" && TryParseDouble(value, out var resolvedCenterY))
                centerY = resolvedCenterY;
            else if (code == "11" && TryParseDouble(value, out var resolvedMajorAxisX))
                majorAxisX = resolvedMajorAxisX;
            else if (code == "21" && TryParseDouble(value, out var resolvedMajorAxisY))
                majorAxisY = resolvedMajorAxisY;
            else if (code == "40" && TryParseDouble(value, out var resolvedRatio))
                ratio = resolvedRatio;
            else if (code == "41" && TryParseDouble(value, out var resolvedStartParameter))
                startParameter = resolvedStartParameter;
            else if (code == "42" && TryParseDouble(value, out var resolvedEndParameter))
                endParameter = resolvedEndParameter;

            cursor += 2;
        }

        index = cursor - 2;
        if (centerX is not double ellipseCenterX
            || centerY is not double ellipseCenterY
            || majorAxisX is not double ellipseMajorAxisX
            || majorAxisY is not double ellipseMajorAxisY
            || ratio is not double ellipseRatio)
        {
            return null;
        }

        var normalizedRatio = Math.Abs(ellipseRatio);
        if (normalizedRatio <= 1e-9)
            return null;

        var majorLength = Math.Sqrt((ellipseMajorAxisX * ellipseMajorAxisX) + (ellipseMajorAxisY * ellipseMajorAxisY));
        if (majorLength <= 1e-9)
            return null;

        var start = startParameter ?? 0.0;
        var end = endParameter ?? Math.PI * 2.0;
        var isClosed = Math.Abs(NormalizeParameterSweep(start, end) - (Math.PI * 2.0)) <= 1e-6;
        var points = ApproximateEllipse(
            ellipseCenterX,
            ellipseCenterY,
            ellipseMajorAxisX,
            ellipseMajorAxisY,
            normalizedRatio,
            start,
            end,
            isClosed);

        return points.Count >= 2
            ? new DxfPreviewPath(
                Id: $"ellipse-{entityIndex}",
                EntityType: "ELLIPSE",
                Points: points,
                IsClosed: isClosed,
                IsAxisAlignedRectangle: false)
            : null;
    }

    private static DxfPreviewPath? ParseText(string[] lines, ref int index, int entityIndex)
    {
        double? startX = null;
        double? startY = null;
        double? height = null;
        double? rotation = null;
        double? widthFactor = null;
        string? text = null;

        var cursor = index + 2;
        while (cursor + 1 < lines.Length)
        {
            var code = lines[cursor].Trim();
            var value = lines[cursor + 1];

            if (code == "0")
                break;

            if (code == "10" && TryParseDouble(value.Trim(), out var resolvedStartX))
                startX = resolvedStartX;
            else if (code == "20" && TryParseDouble(value.Trim(), out var resolvedStartY))
                startY = resolvedStartY;
            else if (code == "40" && TryParseDouble(value.Trim(), out var resolvedHeight))
                height = resolvedHeight;
            else if (code == "41" && TryParseDouble(value.Trim(), out var parsedWidthFactor))
                widthFactor = parsedWidthFactor;
            else if (code == "1")
                text = value;
            else if (code == "50" && TryParseDouble(value.Trim(), out var resolvedRotation))
                rotation = resolvedRotation;

            cursor += 2;
        }

        index = cursor - 2;
        if (startX is not double resolvedX || startY is not double resolvedY || string.IsNullOrWhiteSpace(text))
            return null;

        var resolvedHeightValue = Math.Max(height ?? 5.0, 0.1);
        var resolvedRotationValue = rotation ?? 0.0;
        var resolvedWidthFactor = NormalizeWidthFactor(widthFactor ?? 1.0);
        var start = new DxfPoint(resolvedX, resolvedY);
        var points = BuildTextBoundsPoints(start, text, resolvedHeightValue, resolvedRotationValue, resolvedWidthFactor);
        return new DxfPreviewPath(
            Id: $"text-{entityIndex}",
            EntityType: "TEXT",
            Points: points,
            IsClosed: false,
            IsAxisAlignedRectangle: false,
            Start: start,
            Text: text,
            TextHeight: resolvedHeightValue,
            RotationDegrees: resolvedRotationValue,
            WidthFactor: resolvedWidthFactor);
    }

    private static DxfPreviewPath? CreatePreviewPath(string id, string entityType, IReadOnlyList<DxfVertex> vertices, bool isClosed)
    {
        if (vertices.Count < 2)
            return null;

        var points = BuildPolylinePoints(vertices, isClosed);
        var editorPoints = points
            .Select(static point => new Editor2DPoint(point.X, point.Y))
            .ToArray();
        return points.Count >= 2
            ? new DxfPreviewPath(
                Id: id,
                EntityType: entityType,
                Points: points,
                IsClosed: isClosed,
                IsAxisAlignedRectangle: Editor2DGeometry.IsAxisAlignedRectangle(editorPoints, isClosed))
            : null;
    }

    private static void AppendLwPolyline(StringBuilder builder, string layerName, DxfPolyline polyline)
    {
        if (polyline.Points.Count < 2)
            return;

        AppendPair(builder, 0, "LWPOLYLINE");
        AppendPair(builder, 8, layerName);
        AppendPair(builder, 90, polyline.Points.Count.ToString(CultureInfo.InvariantCulture));
        AppendPair(builder, 70, (polyline.IsClosed ? 1 : 0).ToString(CultureInfo.InvariantCulture));

        foreach (var point in polyline.Points)
        {
            AppendPair(builder, 10, Format(point.X));
            AppendPair(builder, 20, Format(point.Y));
        }
    }

    private static void AppendCircle(StringBuilder builder, string layerName, Editor2DPoint center, double radius)
    {
        AppendPair(builder, 0, "CIRCLE");
        AppendPair(builder, 8, layerName);
        AppendPair(builder, 10, Format(center.X));
        AppendPair(builder, 20, Format(center.Y));
        AppendPair(builder, 40, Format(radius));
    }

    private static void AppendArc(
        StringBuilder builder,
        string layerName,
        Editor2DPoint center,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees)
    {
        AppendPair(builder, 0, "ARC");
        AppendPair(builder, 8, layerName);
        AppendPair(builder, 10, Format(center.X));
        AppendPair(builder, 20, Format(center.Y));
        AppendPair(builder, 40, Format(radius));
        AppendPair(builder, 50, Format(startAngleDegrees));
        AppendPair(builder, 51, Format(endAngleDegrees));
    }

    private static void AppendText(
        StringBuilder builder,
        string layerName,
        Editor2DPoint start,
        string text,
        double height,
        double rotationDegrees,
        double widthFactor)
    {
        AppendPair(builder, 0, "TEXT");
        AppendPair(builder, 8, layerName);
        AppendPair(builder, 10, Format(start.X));
        AppendPair(builder, 20, Format(start.Y));
        AppendPair(builder, 40, Format(Math.Max(height, 0.1)));
        if (Math.Abs(widthFactor - 1.0) > 1e-9)
            AppendPair(builder, 41, Format(NormalizeWidthFactor(widthFactor)));
        AppendPair(builder, 1, text);
        if (Math.Abs(rotationDegrees) > 1e-9)
            AppendPair(builder, 50, Format(rotationDegrees));
    }

    private static DxfPoint[] BuildTextBoundsPoints(DxfPoint start, string text, double height, double rotationDegrees, double widthFactor)
        => Editor2DGeometry.BuildTextBoundsPoints(
                new Editor2DPoint(start.X, start.Y),
                text,
                height,
                rotationDegrees,
                widthFactor)
            .Select(static point => new DxfPoint(point.X, point.Y))
            .ToArray();

    private static IReadOnlyList<DxfPoint> BuildPolylinePoints(IReadOnlyList<DxfVertex> vertices, bool isClosed)
    {
        var points = new List<DxfPoint>(vertices.Count + 12)
        {
            new(vertices[0].X, vertices[0].Y),
        };

        for (var i = 0; i < vertices.Count - 1; i++)
            AppendSegment(points, vertices[i], vertices[i + 1]);

        if (isClosed)
        {
            AppendSegment(points, vertices[^1], vertices[0]);
            if (points.Count > 1 && AreSamePoint(points[0], points[^1]))
                points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    private static void AppendSegment(List<DxfPoint> points, DxfVertex start, DxfVertex end)
    {
        if (Math.Abs(start.Bulge) <= 1e-9)
        {
            var endPoint = new DxfPoint(end.X, end.Y);
            if (points.Count == 0 || !AreSamePoint(points[^1], endPoint))
                points.Add(endPoint);
            return;
        }

        foreach (var point in ApproximateBulgeArc(start, end))
        {
            if (points.Count == 0 || !AreSamePoint(points[^1], point))
                points.Add(point);
        }
    }

    private static IReadOnlyList<DxfPoint> ApproximateBulgeArc(DxfVertex start, DxfVertex end)
    {
        var startPoint = new DxfPoint(start.X, start.Y);
        var endPoint = new DxfPoint(end.X, end.Y);
        var dx = endPoint.X - startPoint.X;
        var dy = endPoint.Y - startPoint.Y;
        var chordLength = Math.Sqrt((dx * dx) + (dy * dy));
        if (chordLength <= 1e-9 || Math.Abs(start.Bulge) <= 1e-9)
            return [endPoint];

        var sweep = 4.0 * Math.Atan(start.Bulge);
        var perpendicularX = -dy / chordLength;
        var perpendicularY = dx / chordLength;
        var midpointX = (startPoint.X + endPoint.X) / 2.0;
        var midpointY = (startPoint.Y + endPoint.Y) / 2.0;
        var centerOffset = chordLength * (1.0 - (start.Bulge * start.Bulge)) / (4.0 * start.Bulge);
        var centerX = midpointX + (perpendicularX * centerOffset);
        var centerY = midpointY + (perpendicularY * centerOffset);
        var radius = Math.Sqrt(((startPoint.X - centerX) * (startPoint.X - centerX)) + ((startPoint.Y - centerY) * (startPoint.Y - centerY)));
        if (radius <= 1e-9)
            return [endPoint];

        var startAngle = Math.Atan2(startPoint.Y - centerY, startPoint.X - centerX);
        var segmentCount = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 12.0)), 4, 96);
        var points = new List<DxfPoint>(segmentCount);

        for (var segmentIndex = 1; segmentIndex <= segmentCount; segmentIndex++)
        {
            var angle = startAngle + (sweep * segmentIndex / segmentCount);
            points.Add(new DxfPoint(
                centerX + (radius * Math.Cos(angle)),
                centerY + (radius * Math.Sin(angle))));
        }

        return points;
    }

    private static IReadOnlyList<DxfPoint> ApproximateArcDegrees(
        double centerX,
        double centerY,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees,
        bool isClosed)
    {
        if (radius <= 1e-9)
            return [];

        var startRadians = DegreesToRadians(startAngleDegrees);
        var endRadians = DegreesToRadians(endAngleDegrees);
        while (endRadians <= startRadians)
            endRadians += Math.PI * 2.0;

        var sweep = isClosed ? Math.PI * 2.0 : endRadians - startRadians;
        var segmentCount = isClosed
            ? 72
            : Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 12.0)), 6, 96);
        var points = new List<DxfPoint>(segmentCount);

        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var angle = startRadians + (sweep * segmentIndex / segmentCount);
            points.Add(new DxfPoint(
                centerX + (radius * Math.Cos(angle)),
                centerY + (radius * Math.Sin(angle))));
        }

        if (!isClosed)
        {
            points.Add(new DxfPoint(
                centerX + (radius * Math.Cos(endRadians)),
                centerY + (radius * Math.Sin(endRadians))));
        }

        return points;
    }

    private static IReadOnlyList<DxfPoint> ApproximateEllipse(
        double centerX,
        double centerY,
        double majorAxisX,
        double majorAxisY,
        double ratio,
        double startParameter,
        double endParameter,
        bool isClosed)
    {
        var sweep = NormalizeParameterSweep(startParameter, endParameter);
        if (sweep <= 1e-9)
            return [];

        var minorAxisX = -majorAxisY * ratio;
        var minorAxisY = majorAxisX * ratio;
        var segmentCount = isClosed
            ? 96
            : Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 18.0)), 8, 128);
        var points = new List<DxfPoint>(segmentCount + (isClosed ? 0 : 1));

        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var parameter = startParameter + (sweep * segmentIndex / segmentCount);
            points.Add(EvaluateEllipsePoint(centerX, centerY, majorAxisX, majorAxisY, minorAxisX, minorAxisY, parameter));
        }

        if (!isClosed)
            points.Add(EvaluateEllipsePoint(centerX, centerY, majorAxisX, majorAxisY, minorAxisX, minorAxisY, startParameter + sweep));

        return points;
    }

    private static DxfPoint EvaluateEllipsePoint(
        double centerX,
        double centerY,
        double majorAxisX,
        double majorAxisY,
        double minorAxisX,
        double minorAxisY,
        double parameter)
        => new(
            centerX + (majorAxisX * Math.Cos(parameter)) + (minorAxisX * Math.Sin(parameter)),
            centerY + (majorAxisY * Math.Cos(parameter)) + (minorAxisY * Math.Sin(parameter)));

    private static double NormalizeParameterSweep(double startParameter, double endParameter)
    {
        var sweep = endParameter - startParameter;
        while (sweep <= 0.0)
            sweep += Math.PI * 2.0;

        return sweep;
    }

    private static bool AreSamePoint(DxfPoint left, DxfPoint right)
        => Math.Abs(left.X - right.X) <= 1e-6
           && Math.Abs(left.Y - right.Y) <= 1e-6;

    private static void IncrementEntityCount(IDictionary<string, int> entityCounts, string entityType)
    {
        var key = entityType.ToUpperInvariant();
        entityCounts[key] = entityCounts.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private static double DegreesToRadians(double degrees)
        => degrees * Math.PI / 180.0;

    private static double NormalizeWidthFactor(double widthFactor)
    {
        var magnitude = Math.Max(Math.Abs(widthFactor), 0.1);
        return widthFactor < 0.0 ? -magnitude : magnitude;
    }

    private static bool TryParseDouble(string raw, out double value)
        => double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private static void AppendPair(StringBuilder builder, int code, string value)
    {
        builder.Append(code.ToString(CultureInfo.InvariantCulture));
        builder.Append('\n');
        builder.Append(value);
        builder.Append('\n');
    }

    private static string Format(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
