namespace Domain.App.Models;

public static class Editor2DGeometry
{
    private const double RectangleTolerance = 1e-6;
    private const double MeasurementTolerance = 1e-6;
    private const double PolylineTolerance = 1e-6;

    public static IReadOnlyList<Editor2DPreviewPath> ExplodeCompoundPath(Editor2DPreviewPath path)
    {
        if (!path.IsClosed
            || path.Points.Count < 3
            || (!path.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
                && !path.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase)))
            return [];

        var nodes = new List<Editor2DPoint>();
        var segments = new List<List<(double Parameter, int Node)>>();
        var segmentCount = path.Points.Count;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = path.Points[index];
            var end = path.Points[(index + 1) % segmentCount];
            var points = new List<(double Parameter, int Node)>
            {
                (0.0, GetOrAddNode(nodes, start)),
                (1.0, GetOrAddNode(nodes, end)),
            };
            segments.Add(points);
        }

        for (var first = 0; first < segmentCount; first++)
        for (var second = first + 1; second < segmentCount; second++)
        {
            if (AreAdjacentSegments(first, second, segmentCount))
                continue;
            if (!TrySegmentIntersection(
                    path.Points[first],
                    path.Points[(first + 1) % segmentCount],
                    path.Points[second],
                    path.Points[(second + 1) % segmentCount],
                    out var firstParameter,
                    out var secondParameter,
                    out var intersection))
                continue;

            segments[first].Add((firstParameter, GetOrAddNode(nodes, intersection)));
            segments[second].Add((secondParameter, GetOrAddNode(nodes, intersection)));
        }

        var adjacency = Enumerable.Range(0, nodes.Count)
            .Select(static _ => new HashSet<int>())
            .ToArray();
        foreach (var segment in segments)
        {
            var ordered = segment
                .OrderBy(item => item.Parameter)
                .Select(item => item.Node)
                .Distinct()
                .ToArray();
            for (var index = 0; index + 1 < ordered.Length; index++)
            {
                if (ordered[index] == ordered[index + 1])
                    continue;
                adjacency[ordered[index]].Add(ordered[index + 1]);
                adjacency[ordered[index + 1]].Add(ordered[index]);
            }
        }

        var orderedAdjacency = adjacency
            .Select((neighbors, node) => neighbors
                .OrderBy(neighbor => Math.Atan2(nodes[neighbor].Y - nodes[node].Y, nodes[neighbor].X - nodes[node].X))
                .ToArray())
            .ToArray();
        var visited = new HashSet<(int From, int To)>();
        var faces = new List<IReadOnlyList<Editor2DPoint>>();
        for (var from = 0; from < orderedAdjacency.Length; from++)
        foreach (var to in orderedAdjacency[from])
        {
            if (visited.Contains((from, to)))
                continue;

            var loop = new List<Editor2DPoint>();
            var currentFrom = from;
            var currentTo = to;
            var closed = false;
            for (var guard = 0; guard < visited.Count + nodes.Count * 4 + 8; guard++)
            {
                if (!visited.Add((currentFrom, currentTo)))
                    break;
                loop.Add(nodes[currentFrom]);
                var neighbors = orderedAdjacency[currentTo];
                var reverseIndex = Array.IndexOf(neighbors, currentFrom);
                if (reverseIndex < 0 || neighbors.Length == 0)
                    break;
                var nextIndex = (reverseIndex - 1 + neighbors.Length) % neighbors.Length;
                var next = neighbors[nextIndex];
                currentFrom = currentTo;
                currentTo = next;
                if (currentFrom == from && currentTo == to)
                {
                    closed = true;
                    break;
                }
            }

            if (closed && loop.Count >= 3 && Math.Abs(SignedArea(loop)) > PolylineTolerance)
                faces.Add(loop);
        }

        if (faces.Count < 2)
            return [];

        var positive = faces.Where(face => SignedArea(face) > 0).ToArray();
        var negative = faces.Where(face => SignedArea(face) < 0).ToArray();
        var boundedFaces = positive.Length >= negative.Length ? positive : negative;
        if (boundedFaces.Length < 2)
            return [];

        return boundedFaces.Select((points, index) => new Editor2DPreviewPath(
            $"{path.Id}:explode:{index}:{Guid.NewGuid():N}",
            "LWPOLYLINE",
            points,
            IsClosed: true,
            IsAxisAlignedRectangle: IsAxisAlignedRectangle(points, isClosed: true))).ToArray();
    }

    public static bool IsAxisAlignedRectangle(IReadOnlyList<Editor2DPoint> points, bool isClosed)
    {
        if (!isClosed || points.Count != 4)
            return false;

        var uniqueXs = DistinctCoordinateCount(points.Select(static point => point.X), RectangleTolerance);
        var uniqueYs = DistinctCoordinateCount(points.Select(static point => point.Y), RectangleTolerance);
        if (uniqueXs != 2 || uniqueYs != 2)
            return false;

        for (var index = 0; index < points.Count; index++)
        {
            var nextIndex = (index + 1) % points.Count;
            var deltaX = Math.Abs(points[nextIndex].X - points[index].X);
            var deltaY = Math.Abs(points[nextIndex].Y - points[index].Y);
            var isHorizontal = deltaY <= RectangleTolerance && deltaX > RectangleTolerance;
            var isVertical = deltaX <= RectangleTolerance && deltaY > RectangleTolerance;
            if (!isHorizontal && !isVertical)
                return false;
        }

        return true;
    }

    public static Editor2DPoint[] BuildTextBoundsPoints(
        Editor2DPoint start,
        string? text,
        double height,
        double rotationDegrees = 0.0,
        double widthFactor = 1.0,
        double characterSpacing = 0.0)
    {
        var normalizedHeight = Math.Max(height, 0.1);
        var normalizedWidthFactor = Math.Max(Math.Abs(widthFactor), 0.1);
        var lines = (text ?? string.Empty)
            .Replace("\r", string.Empty)
            .Split('\n');
        var longestLineLength = Math.Max(lines.Max(static line => line.Length), 1);
        var spacingWidth = Math.Max(longestLineLength - 1, 0) * characterSpacing;
        var width = Math.Max(
            (longestLineLength * normalizedHeight * 0.6 * normalizedWidthFactor) + spacingWidth,
            normalizedHeight * 0.6 * normalizedWidthFactor);
        var totalHeight = Math.Max(lines.Length, 1) * normalizedHeight * 1.2;
        var angleRadians = rotationDegrees * Math.PI / 180.0;
        var cosAngle = Math.Cos(angleRadians);
        var sinAngle = Math.Sin(angleRadians);

        return
        [
            RotateTextPoint(start, 0.0, 0.0, cosAngle, sinAngle),
            RotateTextPoint(start, width, 0.0, cosAngle, sinAngle),
            RotateTextPoint(start, width, totalHeight, cosAngle, sinAngle),
            RotateTextPoint(start, 0.0, totalHeight, cosAngle, sinAngle),
        ];
    }

    public static bool TryBuildAttachedMeasurement(
        Editor2DPreviewPath path,
        string? dimensionType,
        double offsetDistance,
        double? placementAngleDegrees,
        out Editor2DPoint start,
        out Editor2DPoint end)
    {
        var normalizedType = dimensionType?.Trim().ToLowerInvariant();
        switch (normalizedType)
        {
            case "length":
                if (TryGetLineEndpoints(path, out var lineStart, out var lineEnd))
                {
                    var deltaX = lineEnd.X - lineStart.X;
                    var deltaY = lineEnd.Y - lineStart.Y;
                    var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                    if (length <= MeasurementTolerance)
                        break;

                    var normalX = -deltaY / length;
                    var normalY = deltaX / length;
                    start = new Editor2DPoint(lineStart.X + (normalX * offsetDistance), lineStart.Y + (normalY * offsetDistance));
                    end = new Editor2DPoint(lineEnd.X + (normalX * offsetDistance), lineEnd.Y + (normalY * offsetDistance));
                    return true;
                }

                break;

            case "width":
            case "height":
                if (path.IsAxisAlignedRectangle && TryGetRectangleBounds(path, out var minX, out var minY, out var maxX, out var maxY))
                {
                    if (normalizedType == "width")
                    {
                        start = new Editor2DPoint(minX, minY + offsetDistance);
                        end = new Editor2DPoint(maxX, minY + offsetDistance);
                        return true;
                    }

                    start = new Editor2DPoint(minX + offsetDistance, minY);
                    end = new Editor2DPoint(minX + offsetDistance, maxY);
                    return true;
                }

                break;

            case "radius":
                if (TryGetCircleOrArcGeometry(path, out var center, out var radius))
                {
                    var angleDegrees = placementAngleDegrees ?? 0.0;
                    var angleRadians = angleDegrees * Math.PI / 180.0;
                    start = center;
                    end = new Editor2DPoint(
                        center.X + (radius * Math.Cos(angleRadians)),
                        center.Y + (radius * Math.Sin(angleRadians)));
                    return true;
                }

                break;
        }

        start = default!;
        end = default!;
        return false;
    }

    public static bool TryResizeForAttachedDimension(
        Editor2DPreviewPath path,
        string? dimensionType,
        double value,
        out Editor2DPreviewPath updatedPath)
    {
        updatedPath = path;
        if (!double.IsFinite(value) || value <= MeasurementTolerance)
            return false;

        var normalizedType = dimensionType?.Trim().ToLowerInvariant();
        if (normalizedType == "length"
            && path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
            && TryGetLineEndpoints(path, out var lineStart, out var lineEnd))
        {
            var deltaX = lineEnd.X - lineStart.X;
            var deltaY = lineEnd.Y - lineStart.Y;
            var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (length <= MeasurementTolerance)
                return false;
            var nextEnd = new Editor2DPoint(
                lineStart.X + ((deltaX / length) * value),
                lineStart.Y + ((deltaY / length) * value));
            updatedPath = path with
            {
                Points = [lineStart, nextEnd],
                Start = lineStart,
            };
            return true;
        }

        if (normalizedType != "radius"
            || path.Center is not Editor2DPoint center
            || path.Radius is null)
        {
            return false;
        }

        if (path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase))
        {
            updatedPath = path with
            {
                Radius = value,
                Points = BuildCirclePoints(center, value),
            };
            return true;
        }

        if (path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
            && path.StartAngleDegrees is double startAngleDegrees
            && path.EndAngleDegrees is double endAngleDegrees)
        {
            updatedPath = path with
            {
                Radius = value,
                Points = BuildArcPoints(center, value, startAngleDegrees, endAngleDegrees),
            };
            return true;
        }

        return false;
    }

    public static bool IsConvertibleLinePath(Editor2DPreviewPath path)
        => path.Points.Count >= 2
           && (path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
               || path.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
               || path.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<Editor2DPreviewPath> BuildConvertedLinePaths(
        Editor2DPreviewPath sourcePath,
        string? style,
        IReadOnlyDictionary<string, double> settings)
    {
        if (!IsConvertibleLinePath(sourcePath))
            return [];

        var points = sourcePath.Points.ToArray();
        var isClosed = sourcePath.IsClosed && points.Length >= 3;
        var totalLength = MeasurePolylineLength(points, isClosed);
        if (totalLength <= PolylineTolerance)
            return [];

        var normalizedStyle = style?.Trim().ToLowerInvariant() ?? "dashed";
        return normalizedStyle switch
        {
            "dashed" => BuildDashedConvertedPaths(sourcePath.Id, points, isClosed, totalLength, settings),
            "dotted" => BuildDottedConvertedPaths(sourcePath.Id, points, isClosed, totalLength, settings),
            "zigzag" => BuildZigzagConvertedPaths(sourcePath.Id, points, isClosed, totalLength, settings),
            "wave" => BuildWaveConvertedPaths(sourcePath.Id, points, isClosed, totalLength, settings),
            "striped" => BuildStripedConvertedPaths(sourcePath.Id, points, isClosed, totalLength, settings),
            "square" => BuildSquareConvertedPaths(sourcePath.Id, points, isClosed, totalLength, settings),
            "triangle" => BuildTriangleConvertedPaths(sourcePath.Id, points, isClosed, totalLength, settings),
            _ => [CreateConvertedStrokePath($"{sourcePath.Id}:converted:0", points, isClosed)],
        };
    }

    public static Editor2DPoint[] BuildCirclePoints(Editor2DPoint center, double radius, int segmentCount = 48)
    {
        var normalizedRadius = Math.Max(radius, 0.0);
        var resolvedSegmentCount = Math.Max(segmentCount, 12);
        var points = new Editor2DPoint[resolvedSegmentCount];
        for (var index = 0; index < resolvedSegmentCount; index++)
        {
            var angleRadians = (Math.PI * 2.0 * index) / resolvedSegmentCount;
            points[index] = new Editor2DPoint(
                center.X + (normalizedRadius * Math.Cos(angleRadians)),
                center.Y + (normalizedRadius * Math.Sin(angleRadians)));
        }

        return points;
    }

    public static Editor2DPoint[] BuildArcPoints(
        Editor2DPoint center,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees,
        int minimumSegmentCount = 12)
    {
        var normalizedRadius = Math.Max(radius, 0.0);
        var sweepDegrees = NormalizeAngleSweepDegrees(startAngleDegrees, endAngleDegrees);
        var segmentCount = Math.Max(minimumSegmentCount, (int)Math.Ceiling(sweepDegrees / 15.0));
        var points = new Editor2DPoint[segmentCount + 1];
        for (var index = 0; index <= segmentCount; index++)
        {
            var angleDegrees = startAngleDegrees + ((sweepDegrees * index) / segmentCount);
            var angleRadians = angleDegrees * Math.PI / 180.0;
            points[index] = new Editor2DPoint(
                center.X + (normalizedRadius * Math.Cos(angleRadians)),
                center.Y + (normalizedRadius * Math.Sin(angleRadians)));
        }

        return points;
    }

    public static bool IsCurveOffsettablePath(Editor2DPreviewPath path)
        => path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase);

    public static bool IsThicknessSourcePath(Editor2DPreviewPath path)
        => !path.IsClosed
           && IsConvertibleLinePath(path)
           && path.Points.Count >= 2;

    public static bool IsCleanupSourcePath(Editor2DPreviewPath path)
        => !path.IsClosed
           && IsConvertibleLinePath(path)
           && path.Points.Count >= 2;

    public static bool IsGlueTabSourcePath(Editor2DPreviewPath path)
        => path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
           && path.Points.Count >= 2;

    public static bool TryBuildCurveOffsetPath(
        Editor2DPreviewPath sourcePath,
        double offsetDistance,
        bool offsetOutward,
        out Editor2DPreviewPath offsetPath)
    {
        if (TryGetCircleOrArcGeometry(sourcePath, out var center, out var radius))
        {
            var signedDistance = offsetOutward ? offsetDistance : -offsetDistance;
            var nextRadius = radius + signedDistance;
            if (nextRadius <= MeasurementTolerance)
            {
                offsetPath = default!;
                return false;
            }

            if (sourcePath.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase))
            {
                offsetPath = sourcePath with
                {
                    Id = $"{sourcePath.Id}:offset:{Guid.NewGuid():N}",
                    Radius = nextRadius,
                    Points = BuildCirclePoints(center, nextRadius),
                };
                return true;
            }

            if (sourcePath.StartAngleDegrees is not double startAngleDegrees
                || sourcePath.EndAngleDegrees is not double endAngleDegrees)
            {
                offsetPath = default!;
                return false;
            }

            offsetPath = sourcePath with
            {
                Id = $"{sourcePath.Id}:offset:{Guid.NewGuid():N}",
                Radius = nextRadius,
                Points = BuildArcPoints(center, nextRadius, startAngleDegrees, endAngleDegrees),
            };
            return true;
        }

        if (!IsConvertibleLinePath(sourcePath))
        {
            offsetPath = default!;
            return false;
        }

        var points = sourcePath.Points.ToArray();
        var isClosed = sourcePath.IsClosed && points.Length >= 3;
        if (!TryBuildOffsetPolylinePoints(points, isClosed, offsetDistance, offsetOutward, out var nextPoints))
        {
            offsetPath = default!;
            return false;
        }

        var entityType = !isClosed && nextPoints.Length == 2
            ? "LINE"
            : sourcePath.EntityType;
        offsetPath = sourcePath with
        {
            Id = $"{sourcePath.Id}:offset:{Guid.NewGuid():N}",
            EntityType = entityType,
            Points = nextPoints,
            IsAxisAlignedRectangle = Editor2DGeometry.IsAxisAlignedRectangle(nextPoints, isClosed),
        };
        return true;
    }

    public static Editor2DPreviewPath? BuildBoundingBoxOffsetPath(
        IReadOnlyList<Editor2DPreviewPath> selectedPaths,
        double offsetDistance,
        double cornerRadius)
    {
        var points = selectedPaths.SelectMany(static path => path.Points).ToArray();
        if (points.Length == 0)
            return null;

        var minX = points.Min(static point => point.X) - offsetDistance;
        var minY = points.Min(static point => point.Y) - offsetDistance;
        var maxX = points.Max(static point => point.X) + offsetDistance;
        var maxY = points.Max(static point => point.Y) + offsetDistance;
        var width = maxX - minX;
        var height = maxY - minY;
        if (width <= MeasurementTolerance || height <= MeasurementTolerance)
            return null;

        var clampedCornerRadius = Math.Max(Math.Min(cornerRadius, Math.Min(width, height) / 2.0), 0.0);
        var nextPoints = clampedCornerRadius <= MeasurementTolerance
            ? new[]
            {
                new Editor2DPoint(minX, minY),
                new Editor2DPoint(maxX, minY),
                new Editor2DPoint(maxX, maxY),
                new Editor2DPoint(minX, maxY),
            }
            : BuildRoundedRectanglePoints(minX, minY, maxX, maxY, clampedCornerRadius);
        return new Editor2DPreviewPath(
            Id: $"bbox-offset-{Guid.NewGuid():N}",
            EntityType: "LWPOLYLINE",
            Points: nextPoints,
            IsClosed: true,
            IsAxisAlignedRectangle: clampedCornerRadius <= MeasurementTolerance);
    }

    public static bool TryBuildThicknessPath(
        Editor2DPreviewPath sourcePath,
        double thickness,
        out Editor2DPreviewPath thickenedPath)
    {
        if (!IsThicknessSourcePath(sourcePath))
        {
            thickenedPath = default!;
            return false;
        }

        var halfThickness = Math.Max(thickness, MeasurementTolerance) / 2.0;
        var points = sourcePath.Points.ToArray();
        if (!TryBuildOffsetPolylinePoints(points, isClosed: false, signedDistance: halfThickness, out var leftOffsetPoints)
            || !TryBuildOffsetPolylinePoints(points, isClosed: false, signedDistance: -halfThickness, out var rightOffsetPoints))
        {
            thickenedPath = default!;
            return false;
        }

        var outlinePoints = new List<Editor2DPoint>(leftOffsetPoints.Length + rightOffsetPoints.Length);
        foreach (var point in leftOffsetPoints)
            AddPointIfDistinct(outlinePoints, point);
        foreach (var point in rightOffsetPoints.Reverse())
            AddPointIfDistinct(outlinePoints, point);

        if (outlinePoints.Count < 3)
        {
            thickenedPath = default!;
            return false;
        }

        thickenedPath = new Editor2DPreviewPath(
            Id: $"{sourcePath.Id}:thick:{Guid.NewGuid():N}",
            EntityType: "LWPOLYLINE",
            Points: outlinePoints.ToArray(),
            IsClosed: true,
            IsAxisAlignedRectangle: IsAxisAlignedRectangle(outlinePoints, isClosed: true));
        return true;
    }

    public static IReadOnlyList<Editor2DPreviewPath> BuildCleanupPaths(
        IReadOnlyList<Editor2DPreviewPath> sourcePaths,
        double tolerance)
    {
        var normalizedTolerance = Math.Max(tolerance, MeasurementTolerance);
        var chains = sourcePaths
            .Where(IsCleanupSourcePath)
            .Select(path => new CleanupChain(
                Id: path.Id,
                Points: NormalizeCleanupPoints(path.Points, normalizedTolerance),
                IsClosed: false))
            .Where(chain => chain.Points.Count >= 2)
            .ToList();

        var merged = true;
        while (merged)
        {
            merged = false;
            for (var leftIndex = 0; leftIndex < chains.Count && !merged; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < chains.Count; rightIndex++)
                {
                    if (!TryMergeCleanupChains(chains[leftIndex], chains[rightIndex], normalizedTolerance, out var nextChain))
                        continue;

                    chains[leftIndex] = nextChain;
                    chains.RemoveAt(rightIndex);
                    merged = true;
                    break;
                }
            }
        }

        return chains
            .Select(chain => FinalizeCleanupChain(chain, normalizedTolerance))
            .Where(static path => path.Points.Count >= 2)
            .ToArray();
    }

    public static bool TryBuildGlueTabPath(
        Editor2DPreviewPath sourcePath,
        double height,
        string? tabType,
        string? side,
        double startOffset,
        double endOffset,
        out Editor2DPreviewPath glueTabPath)
    {
        if (!IsGlueTabSourcePath(sourcePath)
            || !TryGetLineEndpoints(sourcePath, out var start, out var end))
        {
            glueTabPath = default!;
            return false;
        }

        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length <= MeasurementTolerance)
        {
            glueTabPath = default!;
            return false;
        }

        var normalizedHeight = Math.Max(height, 0.1);
        var normalizedStartOffset = Math.Max(startOffset, 0.0);
        var normalizedEndOffset = Math.Max(endOffset, 0.0);
        if (normalizedStartOffset + normalizedEndOffset >= length)
        {
            glueTabPath = default!;
            return false;
        }

        var ux = deltaX / length;
        var uy = deltaY / length;
        var normalizedSide = string.Equals(side, "Right", StringComparison.OrdinalIgnoreCase)
            ? "Right"
            : "Left";
        var normalX = normalizedSide == "Left" ? uy : -uy;
        var normalY = normalizedSide == "Left" ? -ux : ux;

        var tabStart = new Editor2DPoint(
            start.X + (normalizedStartOffset * ux),
            start.Y + (normalizedStartOffset * uy));
        var tabEnd = new Editor2DPoint(
            end.X - (normalizedEndOffset * ux),
            end.Y - (normalizedEndOffset * uy));
        var tabLength = length - normalizedStartOffset - normalizedEndOffset;
        if (tabLength <= MeasurementTolerance)
        {
            glueTabPath = default!;
            return false;
        }

        var points = new List<Editor2DPoint>(4)
        {
            tabStart,
        };

        if (string.Equals(tabType, "Triangle", StringComparison.OrdinalIgnoreCase))
        {
            var midX = (tabStart.X + tabEnd.X) / 2.0;
            var midY = (tabStart.Y + tabEnd.Y) / 2.0;
            points.Add(new Editor2DPoint(
                midX + (normalizedHeight * normalX),
                midY + (normalizedHeight * normalY)));
        }
        else
        {
            var shoulderOffset = Math.Min(normalizedHeight, tabLength / 2.1);
            points.Add(new Editor2DPoint(
                tabStart.X + (shoulderOffset * ux) + (normalizedHeight * normalX),
                tabStart.Y + (shoulderOffset * uy) + (normalizedHeight * normalY)));
            points.Add(new Editor2DPoint(
                tabEnd.X - (shoulderOffset * ux) + (normalizedHeight * normalX),
                tabEnd.Y - (shoulderOffset * uy) + (normalizedHeight * normalY)));
        }

        points.Add(tabEnd);
        glueTabPath = new Editor2DPreviewPath(
            Id: $"{sourcePath.Id}:glue-tab:{Guid.NewGuid():N}",
            EntityType: "LWPOLYLINE",
            Points: points,
            IsClosed: false,
            IsAxisAlignedRectangle: false);
        return true;
    }

    public static Editor2DPreviewPath TranslatePath(Editor2DPreviewPath path, double deltaX, double deltaY, string? id = null)
        => TransformPath(path, Editor2DAffineTransform.CreateTranslation(deltaX, deltaY), id);

    public static Editor2DPreviewPath ScalePath(Editor2DPreviewPath path, Editor2DPoint pivot, double factor, string? id = null)
        => TransformPath(path, Editor2DAffineTransform.CreateScale(pivot, factor), id);

    public static Editor2DPreviewPath RotatePath(Editor2DPreviewPath path, Editor2DPoint pivot, double angleDegrees, string? id = null)
        => TransformPath(path, Editor2DAffineTransform.CreateRotation(pivot, angleDegrees), id);

    public static Editor2DPreviewPath TransformPath(
        Editor2DPreviewPath path,
        Editor2DAffineTransform transform,
        string? id = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(transform);
        if (!transform.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(transform), "Transform values must be finite.");

        var transformedPoints = path.Points.Select(transform.TransformPoint).ToArray();
        var transformedCenter = path.Center is { } center ? transform.TransformPoint(center) : null;
        var transformedRotation = path.RotationDegrees;
        var preservesDirection = transform.M11 == 1.0 && transform.M12 == 0.0
                                 && transform.M21 == 0.0 && transform.M22 == 1.0;
        if (!preservesDirection && transformedRotation is double rotationDegrees)
            transformedRotation = transform.TransformDirectionDegrees(rotationDegrees);

        var transformedStartAngle = path.StartAngleDegrees;
        var transformedEndAngle = path.EndAngleDegrees;
        if (path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
            && path.StartAngleDegrees is double startAngle
            && path.EndAngleDegrees is double endAngle)
        {
            double TransformAngle(double angle)
                => transform.TransformDirectionDegrees(angle);

            if (transform.Determinant < 0)
            {
                transformedStartAngle = TransformAngle(endAngle);
                transformedEndAngle = TransformAngle(startAngle);
            }
            else
            {
                transformedStartAngle = TransformAngle(startAngle);
                transformedEndAngle = TransformAngle(endAngle);
            }
        }

        var isUniformScale = transform.TryGetUniformScale(out var uniformScale);
        return path with
        {
            Id = id ?? path.Id,
            Start = path.Start is { } start ? transform.TransformPoint(start) : null,
            Center = transformedCenter,
            RotationDegrees = transformedRotation,
            StartAngleDegrees = transformedStartAngle,
            EndAngleDegrees = transformedEndAngle,
            Radius = isUniformScale && path.Radius is double radius ? radius * uniformScale : path.Radius,
            TextHeight = isUniformScale && path.TextHeight is double textHeight ? textHeight * uniformScale : path.TextHeight,
            WidthFactor = transform.Determinant < 0 && path.WidthFactor is double widthFactor ? -widthFactor : path.WidthFactor,
            Points = transformedPoints,
            BezierAnchors = path.BezierAnchors?.Select(anchor => Editor2DBezierGeometry.Transform(
                anchor, transform.TransformPoint)).ToArray(),
            IsAxisAlignedRectangle = IsAxisAlignedRectangle(transformedPoints, path.IsClosed),
        };
    }

    public static Editor2DPreviewPath ReflectPath(
        Editor2DPreviewPath path,
        Editor2DPoint axisStart,
        Editor2DPoint axisEnd,
        string? id = null)
    {
        Editor2DPoint Transform(Editor2DPoint point)
            => ReflectPoint(point, axisStart, axisEnd);

        var reflectedStart = path.Start is Editor2DPoint start ? Transform(start) : null;
        var reflectedCenter = path.Center is Editor2DPoint center ? Transform(center) : null;
        var reflectedRotation = path.RotationDegrees;
        var reflectedWidthFactor = path.WidthFactor;
        if (path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
            && path.Start is Editor2DPoint textStart
            && path.RotationDegrees is double rotationDegrees)
        {
            var directionPoint = new Editor2DPoint(
                textStart.X + Math.Cos(rotationDegrees * Math.PI / 180.0),
                textStart.Y + Math.Sin(rotationDegrees * Math.PI / 180.0));
            var reflectedDirectionPoint = Transform(directionPoint);
            var resolvedStart = reflectedStart ?? Transform(textStart);
            reflectedRotation = Math.Atan2(
                reflectedDirectionPoint.Y - resolvedStart.Y,
                reflectedDirectionPoint.X - resolvedStart.X) * 180.0 / Math.PI;
            reflectedWidthFactor = -(path.WidthFactor ?? 1.0);
        }

        var reflectedPoints = path.Points.Select(Transform).ToArray();
        var reflectedStartAngle = path.StartAngleDegrees;
        var reflectedEndAngle = path.EndAngleDegrees;
        if (path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
            && path.Center is Editor2DPoint originalCenter
            && path.Radius is double radius
            && path.StartAngleDegrees is double startAngleDegrees
            && path.EndAngleDegrees is double endAngleDegrees
            && reflectedCenter is Editor2DPoint resolvedCenter)
        {
            var originalArcStart = new Editor2DPoint(
                originalCenter.X + radius * Math.Cos(startAngleDegrees * Math.PI / 180.0),
                originalCenter.Y + radius * Math.Sin(startAngleDegrees * Math.PI / 180.0));
            var originalArcEnd = new Editor2DPoint(
                originalCenter.X + radius * Math.Cos(endAngleDegrees * Math.PI / 180.0),
                originalCenter.Y + radius * Math.Sin(endAngleDegrees * Math.PI / 180.0));
            var reflectedArcStart = Transform(originalArcStart);
            var reflectedArcEnd = Transform(originalArcEnd);
            reflectedStartAngle = NormalizeAngleDegrees(Math.Atan2(
                reflectedArcEnd.Y - resolvedCenter.Y,
                reflectedArcEnd.X - resolvedCenter.X) * 180.0 / Math.PI);
            reflectedEndAngle = NormalizeAngleDegrees(Math.Atan2(
                reflectedArcStart.Y - resolvedCenter.Y,
                reflectedArcStart.X - resolvedCenter.X) * 180.0 / Math.PI);
        }

        return path with
        {
            Id = id ?? path.Id,
            Start = reflectedStart,
            Center = reflectedCenter,
            RotationDegrees = reflectedRotation,
            WidthFactor = reflectedWidthFactor,
            StartAngleDegrees = reflectedStartAngle,
            EndAngleDegrees = reflectedEndAngle,
            Points = reflectedPoints,
            BezierAnchors = path.BezierAnchors?.Select(anchor => Editor2DBezierGeometry.Transform(anchor, Transform)).ToArray(),
            IsAxisAlignedRectangle = IsAxisAlignedRectangle(reflectedPoints, path.IsClosed),
        };
    }

    public static Editor2DPreviewPath CreateMirrorCopy(
        Editor2DPreviewPath path,
        Editor2DPoint axisStart,
        Editor2DPoint axisEnd,
        bool flip,
        string? id = null)
    {
        if (flip)
            return ReflectPath(path, axisStart, axisEnd, id);

        var boundsPoints = path.Points.Count > 0
            ? path.Points
            : path.Start is Editor2DPoint start
                ? [start]
                : path.Center is Editor2DPoint center
                    ? [center]
                    : [];
        if (boundsPoints.Count == 0)
            return path with { Id = id ?? path.Id };

        var centroid = new Editor2DPoint(
            (boundsPoints.Min(static point => point.X) + boundsPoints.Max(static point => point.X)) / 2.0,
            (boundsPoints.Min(static point => point.Y) + boundsPoints.Max(static point => point.Y)) / 2.0);
        var reflectedCentroid = ReflectPoint(centroid, axisStart, axisEnd);
        return TranslatePath(
            path,
            reflectedCentroid.X - centroid.X,
            reflectedCentroid.Y - centroid.Y,
            id);
    }

    public static Editor2DPoint ReflectPoint(Editor2DPoint point, Editor2DPoint axisStart, Editor2DPoint axisEnd)
    {
        var deltaX = axisEnd.X - axisStart.X;
        var deltaY = axisEnd.Y - axisStart.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= 1e-9)
            return point;

        var projectedFactor = (((point.X - axisStart.X) * deltaX) + ((point.Y - axisStart.Y) * deltaY)) / lengthSquared;
        var projectedX = axisStart.X + (projectedFactor * deltaX);
        var projectedY = axisStart.Y + (projectedFactor * deltaY);
        return new Editor2DPoint((2.0 * projectedX) - point.X, (2.0 * projectedY) - point.Y);
    }

    public static double CalculateSignedLineDimensionOffset(
        Editor2DPoint start,
        Editor2DPoint end,
        Editor2DPoint referencePoint,
        double preferredMagnitude)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        var magnitude = Math.Max(Math.Abs(preferredMagnitude), 1.0);
        if (length <= MeasurementTolerance)
            return magnitude;

        var normalX = -deltaY / length;
        var normalY = deltaX / length;
        var midpoint = new Editor2DPoint((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
        var side = ((referencePoint.X - midpoint.X) * normalX) + ((referencePoint.Y - midpoint.Y) * normalY);
        return side < 0.0 ? -magnitude : magnitude;
    }

    private static Editor2DPoint RotateTextPoint(
        Editor2DPoint start,
        double localX,
        double localY,
        double cosAngle,
        double sinAngle)
        => new(
            start.X + (localX * cosAngle) - (localY * sinAngle),
            start.Y + (localX * sinAngle) + (localY * cosAngle));

    private static IReadOnlyList<Editor2DPreviewPath> BuildDashedConvertedPaths(
        string sourcePathId,
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double totalLength,
        IReadOnlyDictionary<string, double> settings)
    {
        var dashLength = Math.Max(GetSetting(settings, "dash_length", 4.0), 0.1);
        var gap = Math.Max(GetSetting(settings, "gap", 3.0), 0.1);
        var nextPaths = new List<Editor2DPreviewPath>();
        var distance = 0.0;
        var pathIndex = 0;

        while (distance < totalLength - PolylineTolerance)
        {
            var dashEnd = Math.Min(distance + dashLength, totalLength);
            var dashPoints = ExtractPolylineSlice(points, isClosed, distance, dashEnd);
            if (dashPoints.Count >= 2)
                nextPaths.Add(CreateConvertedStrokePath($"{sourcePathId}:converted:{pathIndex++}", dashPoints, isClosed: false));

            distance += dashLength + gap;
        }

        return nextPaths;
    }

    private static IReadOnlyList<Editor2DPreviewPath> BuildDottedConvertedPaths(
        string sourcePathId,
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double totalLength,
        IReadOnlyDictionary<string, double> settings)
    {
        var spacing = Math.Max(GetSetting(settings, "spacing", 3.0), 0.2);
        var radius = Math.Max(GetSetting(settings, "dot_radius", 0.5), 0.05);
        var distances = BuildDistributedDistances(totalLength, spacing, includeEnd: !isClosed);
        var nextPaths = new List<Editor2DPreviewPath>();
        var pathIndex = 0;

        foreach (var distance in distances)
        {
            if (!TrySampleAlongPolyline(points, isClosed, distance, out var point, out _))
                continue;

            nextPaths.Add(CreateConvertedCirclePath($"{sourcePathId}:converted:{pathIndex++}", point, radius));
        }

        return nextPaths;
    }

    private static IReadOnlyList<Editor2DPreviewPath> BuildSquareConvertedPaths(
        string sourcePathId,
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double totalLength,
        IReadOnlyDictionary<string, double> settings)
    {
        var spacing = Math.Max(GetSetting(settings, "spacing", 4.0), 0.3);
        var size = Math.Max(GetSetting(settings, "size", 1.5), 0.1);
        var halfSize = size / 2.0;
        var distances = BuildDistributedDistances(totalLength, spacing, includeEnd: !isClosed);
        var nextPaths = new List<Editor2DPreviewPath>();
        var pathIndex = 0;

        foreach (var distance in distances)
        {
            if (!TrySampleAlongPolyline(points, isClosed, distance, out var point, out var tangent))
                continue;

            var normal = new Editor2DPoint(-tangent.Y, tangent.X);
            var squarePoints = new[]
            {
                TranslatePoint(TranslatePoint(point, tangent, -halfSize), normal, -halfSize),
                TranslatePoint(TranslatePoint(point, tangent, halfSize), normal, -halfSize),
                TranslatePoint(TranslatePoint(point, tangent, halfSize), normal, halfSize),
                TranslatePoint(TranslatePoint(point, tangent, -halfSize), normal, halfSize),
            };
            nextPaths.Add(CreateConvertedStrokePath($"{sourcePathId}:converted:{pathIndex++}", squarePoints, isClosed: true));
        }

        return nextPaths;
    }

    private static IReadOnlyList<Editor2DPreviewPath> BuildTriangleConvertedPaths(
        string sourcePathId,
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double totalLength,
        IReadOnlyDictionary<string, double> settings)
    {
        var spacing = Math.Max(GetSetting(settings, "spacing", 5.0), 0.3);
        var size = Math.Max(GetSetting(settings, "size", 2.0), 0.1);
        var distances = BuildDistributedDistances(totalLength, spacing, includeEnd: !isClosed);
        var nextPaths = new List<Editor2DPreviewPath>();
        var pathIndex = 0;

        foreach (var distance in distances)
        {
            if (!TrySampleAlongPolyline(points, isClosed, distance, out var point, out var tangent))
                continue;

            var normal = new Editor2DPoint(-tangent.Y, tangent.X);
            var trianglePoints = new[]
            {
                TranslatePoint(point, tangent, -size / 2.0),
                TranslatePoint(point, tangent, size / 2.0),
                TranslatePoint(point, normal, size),
            };
            nextPaths.Add(CreateConvertedStrokePath($"{sourcePathId}:converted:{pathIndex++}", trianglePoints, isClosed: true));
        }

        return nextPaths;
    }

    private static IReadOnlyList<Editor2DPreviewPath> BuildZigzagConvertedPaths(
        string sourcePathId,
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double totalLength,
        IReadOnlyDictionary<string, double> settings)
    {
        var wavelength = Math.Max(GetSetting(settings, "wavelength", 6.0), 0.5);
        var amplitude = Math.Abs(GetSetting(settings, "amplitude", 2.0));
        var sampleCount = Math.Max(2, (int)Math.Round(totalLength / Math.Max(wavelength / 2.0, PolylineTolerance)));
        var zigzagPoints = new List<Editor2DPoint>(sampleCount + 1);

        for (var index = 0; index <= sampleCount; index++)
        {
            var distance = totalLength * index / sampleCount;
            if (!TrySampleAlongPolyline(points, isClosed, distance, out var point, out var tangent))
                continue;

            var offset = index == 0 || index == sampleCount
                ? 0.0
                : index % 2 == 0
                    ? -amplitude
                    : amplitude;
            var normal = new Editor2DPoint(-tangent.Y, tangent.X);
            zigzagPoints.Add(TranslatePoint(point, normal, offset));
        }

        return zigzagPoints.Count >= 2
            ? [CreateConvertedStrokePath($"{sourcePathId}:converted:0", zigzagPoints, isClosed: false)]
            : [];
    }

    private static IReadOnlyList<Editor2DPreviewPath> BuildWaveConvertedPaths(
        string sourcePathId,
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double totalLength,
        IReadOnlyDictionary<string, double> settings)
    {
        var wavelength = Math.Max(GetSetting(settings, "wavelength", 6.0), 0.5);
        var amplitude = Math.Abs(GetSetting(settings, "amplitude", 2.0));
        var samplesPerWave = Math.Max((int)Math.Round(GetSetting(settings, "samples_per_wave", 12.0)), 4);
        var sampleCount = Math.Max(samplesPerWave, (int)Math.Round((totalLength / wavelength) * samplesPerWave));
        var wavePoints = new List<Editor2DPoint>(sampleCount + 1);

        for (var index = 0; index <= sampleCount; index++)
        {
            var distance = totalLength * index / sampleCount;
            if (!TrySampleAlongPolyline(points, isClosed, distance, out var point, out var tangent))
                continue;

            var normal = new Editor2DPoint(-tangent.Y, tangent.X);
            var offset = amplitude * Math.Sin((Math.PI * 2.0 * distance) / wavelength);
            wavePoints.Add(TranslatePoint(point, normal, offset));
        }

        return wavePoints.Count >= 2
            ? [CreateConvertedStrokePath($"{sourcePathId}:converted:0", wavePoints, isClosed: false)]
            : [];
    }

    private static IReadOnlyList<Editor2DPreviewPath> BuildStripedConvertedPaths(
        string sourcePathId,
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double totalLength,
        IReadOnlyDictionary<string, double> settings)
    {
        var dashLength = Math.Max(GetSetting(settings, "dash_length", 3.0), 0.1);
        var gap = Math.Max(GetSetting(settings, "gap", 3.0), 0.1);
        var tiltDegrees = GetSetting(settings, "tilt", 45.0);
        var nextPaths = new List<Editor2DPreviewPath>();
        var distance = 0.0;
        var pathIndex = 0;

        while (distance < totalLength - PolylineTolerance)
        {
            if (!TrySampleAlongPolyline(points, isClosed, distance, out var point, out var tangent))
            {
                distance += gap;
                continue;
            }

            var direction = RotateVector(tangent, tiltDegrees);
            var halfLength = dashLength / 2.0;
            nextPaths.Add(CreateConvertedStrokePath(
                $"{sourcePathId}:converted:{pathIndex++}",
                new[]
                {
                    TranslatePoint(point, direction, -halfLength),
                    TranslatePoint(point, direction, halfLength),
                },
                isClosed: false));
            distance += gap;
        }

        return nextPaths;
    }

    private static IReadOnlyList<double> BuildDistributedDistances(double totalLength, double spacing, bool includeEnd)
    {
        var resolvedSpacing = Math.Max(spacing, PolylineTolerance);
        var count = Math.Max(1, (int)Math.Round(totalLength / resolvedSpacing));
        var limit = includeEnd ? count : count - 1;
        var distances = new List<double>(limit + 1);
        for (var index = 0; index <= limit; index++)
            distances.Add(totalLength * index / count);

        return distances;
    }

    private static double GetSetting(IReadOnlyDictionary<string, double> settings, string key, double defaultValue)
        => settings.TryGetValue(key, out var value) ? value : defaultValue;

    private static double MeasurePolylineLength(IReadOnlyList<Editor2DPoint> points, bool isClosed)
    {
        var totalLength = 0.0;
        foreach (var (_, start, end) in EnumeratePolylineSegments(points, isClosed))
            totalLength += DistanceBetween(start, end);

        return totalLength;
    }

    private static IReadOnlyList<Editor2DPoint> ExtractPolylineSlice(
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double startDistance,
        double endDistance)
    {
        var normalizedStart = Math.Max(startDistance, 0.0);
        var normalizedEnd = Math.Max(endDistance, normalizedStart);
        var slicePoints = new List<Editor2DPoint>();
        if (!TrySampleAlongPolyline(points, isClosed, normalizedStart, out var startPoint, out _)
            || !TrySampleAlongPolyline(points, isClosed, normalizedEnd, out var endPoint, out _))
        {
            return slicePoints;
        }

        AddPointIfDistinct(slicePoints, startPoint);
        var traversed = 0.0;
        foreach (var (_, segmentStart, segmentEnd) in EnumeratePolylineSegments(points, isClosed))
        {
            var segmentLength = DistanceBetween(segmentStart, segmentEnd);
            if (segmentLength <= PolylineTolerance)
                continue;

            var nextDistance = traversed + segmentLength;
            if (nextDistance <= normalizedStart + PolylineTolerance)
            {
                traversed = nextDistance;
                continue;
            }

            if (traversed >= normalizedEnd - PolylineTolerance)
                break;

            if (nextDistance < normalizedEnd - PolylineTolerance)
                AddPointIfDistinct(slicePoints, segmentEnd);

            traversed = nextDistance;
        }

        AddPointIfDistinct(slicePoints, endPoint);
        return slicePoints;
    }

    private static IReadOnlyList<Editor2DPoint> NormalizeCleanupPoints(
        IReadOnlyList<Editor2DPoint> points,
        double tolerance)
    {
        var normalizedPoints = new List<Editor2DPoint>(points.Count);
        foreach (var point in points)
            AddPointIfDistinct(normalizedPoints, point, tolerance);

        return normalizedPoints;
    }

    private static bool TrySampleAlongPolyline(
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double distance,
        out Editor2DPoint point,
        out Editor2DPoint tangent)
    {
        var totalLength = MeasurePolylineLength(points, isClosed);
        var clampedDistance = Math.Clamp(distance, 0.0, totalLength);
        var traversed = 0.0;
        Editor2DPoint? lastStart = null;
        Editor2DPoint? lastEnd = null;

        foreach (var (_, segmentStart, segmentEnd) in EnumeratePolylineSegments(points, isClosed))
        {
            var segmentLength = DistanceBetween(segmentStart, segmentEnd);
            if (segmentLength <= PolylineTolerance)
                continue;

            if (traversed + segmentLength >= clampedDistance - PolylineTolerance)
            {
                var localDistance = Math.Clamp(clampedDistance - traversed, 0.0, segmentLength);
                var factor = segmentLength <= PolylineTolerance ? 0.0 : localDistance / segmentLength;
                point = new Editor2DPoint(
                    segmentStart.X + ((segmentEnd.X - segmentStart.X) * factor),
                    segmentStart.Y + ((segmentEnd.Y - segmentStart.Y) * factor));
                tangent = NormalizeVector(new Editor2DPoint(segmentEnd.X - segmentStart.X, segmentEnd.Y - segmentStart.Y));
                return true;
            }

            traversed += segmentLength;
            lastStart = segmentStart;
            lastEnd = segmentEnd;
        }

        if (lastStart is Editor2DPoint resolvedStart && lastEnd is Editor2DPoint resolvedEnd)
        {
            point = resolvedEnd;
            tangent = NormalizeVector(new Editor2DPoint(resolvedEnd.X - resolvedStart.X, resolvedEnd.Y - resolvedStart.Y));
            return true;
        }

        point = default!;
        tangent = default!;
        return false;
    }

    private static IEnumerable<(int SegmentIndex, Editor2DPoint Start, Editor2DPoint End)> EnumeratePolylineSegments(
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed)
    {
        if (points.Count < 2)
            yield break;

        var segmentCount = isClosed ? points.Count : points.Count - 1;
        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var start = points[segmentIndex];
            var end = segmentIndex == points.Count - 1 ? points[0] : points[segmentIndex + 1];
            yield return (segmentIndex, start, end);
        }
    }

    private static bool TryBuildOffsetPolylinePoints(
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double offsetDistance,
        bool offsetOutward,
        out Editor2DPoint[] offsetPoints)
    {
        if (points.Count < 2)
        {
            offsetPoints = [];
            return false;
        }

        var signedDistance = ResolveSignedOffsetDistance(points, isClosed, offsetDistance, offsetOutward);
        return TryBuildOffsetPolylinePoints(points, isClosed, signedDistance, out offsetPoints);
    }

    private static bool TryBuildOffsetPolylinePoints(
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double signedDistance,
        out Editor2DPoint[] offsetPoints)
    {
        if (points.Count < 2)
        {
            offsetPoints = [];
            return false;
        }

        if (!isClosed && points.Count == 2)
        {
            var normal = GetLeftNormal(points[0], points[1]);
            if (Math.Abs(normal.X) <= PolylineTolerance && Math.Abs(normal.Y) <= PolylineTolerance)
            {
                offsetPoints = [];
                return false;
            }

            offsetPoints =
            [
                TranslatePoint(points[0], normal, signedDistance),
                TranslatePoint(points[1], normal, signedDistance),
            ];
            return true;
        }

        var nextPoints = new List<Editor2DPoint>(points.Count);
        if (isClosed)
        {
            for (var index = 0; index < points.Count; index++)
            {
                var previousPoint = points[(index - 1 + points.Count) % points.Count];
                var currentPoint = points[index];
                var nextPoint = points[(index + 1) % points.Count];
                if (!TryBuildOffsetVertex(previousPoint, currentPoint, nextPoint, signedDistance, out var offsetPoint))
                {
                    offsetPoints = [];
                    return false;
                }

                nextPoints.Add(offsetPoint);
            }

            offsetPoints = nextPoints.ToArray();
            return true;
        }

        nextPoints.Add(TranslatePoint(points[0], GetLeftNormal(points[0], points[1]), signedDistance));
        for (var index = 1; index < points.Count - 1; index++)
        {
            if (!TryBuildOffsetVertex(points[index - 1], points[index], points[index + 1], signedDistance, out var offsetPoint))
            {
                offsetPoints = [];
                return false;
            }

            nextPoints.Add(offsetPoint);
        }

        nextPoints.Add(TranslatePoint(points[^1], GetLeftNormal(points[^2], points[^1]), signedDistance));
        offsetPoints = nextPoints.ToArray();
        return true;
    }

    private static bool TryBuildOffsetVertex(
        Editor2DPoint previousPoint,
        Editor2DPoint currentPoint,
        Editor2DPoint nextPoint,
        double signedDistance,
        out Editor2DPoint offsetPoint)
    {
        var previousNormal = GetLeftNormal(previousPoint, currentPoint);
        var nextNormal = GetLeftNormal(currentPoint, nextPoint);
        if ((Math.Abs(previousNormal.X) <= PolylineTolerance && Math.Abs(previousNormal.Y) <= PolylineTolerance)
            || (Math.Abs(nextNormal.X) <= PolylineTolerance && Math.Abs(nextNormal.Y) <= PolylineTolerance))
        {
            offsetPoint = default!;
            return false;
        }

        var previousLineStart = TranslatePoint(previousPoint, previousNormal, signedDistance);
        var previousLineEnd = TranslatePoint(currentPoint, previousNormal, signedDistance);
        var nextLineStart = TranslatePoint(currentPoint, nextNormal, signedDistance);
        var nextLineEnd = TranslatePoint(nextPoint, nextNormal, signedDistance);
        if (TryIntersectLines(previousLineStart, previousLineEnd, nextLineStart, nextLineEnd, out var intersection))
        {
            offsetPoint = intersection;
            return true;
        }

        var averageNormal = NormalizeVector(new Editor2DPoint(previousNormal.X + nextNormal.X, previousNormal.Y + nextNormal.Y));
        offsetPoint = TranslatePoint(currentPoint, averageNormal, signedDistance);
        return true;
    }

    private static double ResolveSignedOffsetDistance(
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double offsetDistance,
        bool offsetOutward)
    {
        var magnitude = Math.Max(Math.Abs(offsetDistance), MeasurementTolerance);
        if (!isClosed)
            return offsetOutward ? magnitude : -magnitude;

        var signedArea = CalculateSignedArea(points);
        var outwardUsesLeftNormal = signedArea < 0.0;
        return offsetOutward == outwardUsesLeftNormal
            ? magnitude
            : -magnitude;
    }

    private static double CalculateSignedArea(IReadOnlyList<Editor2DPoint> points)
    {
        if (points.Count < 3)
            return 0.0;

        var signedArea = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            signedArea += (current.X * next.Y) - (next.X * current.Y);
        }

        return signedArea / 2.0;
    }

    private static Editor2DPoint[] BuildRoundedRectanglePoints(
        double minX,
        double minY,
        double maxX,
        double maxY,
        double radius)
    {
        var points = new List<Editor2DPoint>();
        AppendArc(points, new Editor2DPoint(maxX - radius, minY + radius), radius, 270.0, 360.0);
        AppendArc(points, new Editor2DPoint(maxX - radius, maxY - radius), radius, 0.0, 90.0);
        AppendArc(points, new Editor2DPoint(minX + radius, maxY - radius), radius, 90.0, 180.0);
        AppendArc(points, new Editor2DPoint(minX + radius, minY + radius), radius, 180.0, 270.0);
        return points.ToArray();
    }

    private static void AppendArc(
        ICollection<Editor2DPoint> target,
        Editor2DPoint center,
        double radius,
        double startAngleDegrees,
        double endAngleDegrees)
    {
        foreach (var point in BuildArcPoints(center, radius, startAngleDegrees, endAngleDegrees, minimumSegmentCount: 4))
            AddPointIfDistinct(target, point);
    }

    private static Editor2DPoint GetLeftNormal(Editor2DPoint start, Editor2DPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length <= PolylineTolerance)
            return new Editor2DPoint(0.0, 0.0);

        return new Editor2DPoint(-deltaY / length, deltaX / length);
    }

    private static bool TryIntersectLines(
        Editor2DPoint leftStart,
        Editor2DPoint leftEnd,
        Editor2DPoint rightStart,
        Editor2DPoint rightEnd,
        out Editor2DPoint intersection)
    {
        var leftDeltaX = leftEnd.X - leftStart.X;
        var leftDeltaY = leftEnd.Y - leftStart.Y;
        var rightDeltaX = rightEnd.X - rightStart.X;
        var rightDeltaY = rightEnd.Y - rightStart.Y;
        var denominator = (leftDeltaX * rightDeltaY) - (leftDeltaY * rightDeltaX);
        if (Math.Abs(denominator) <= PolylineTolerance)
        {
            intersection = default!;
            return false;
        }

        var startDeltaX = rightStart.X - leftStart.X;
        var startDeltaY = rightStart.Y - leftStart.Y;
        var leftFactor = ((startDeltaX * rightDeltaY) - (startDeltaY * rightDeltaX)) / denominator;
        intersection = new Editor2DPoint(
            leftStart.X + (leftDeltaX * leftFactor),
            leftStart.Y + (leftDeltaY * leftFactor));
        return true;
    }

    private static double NormalizeAngleDegrees(double angleDegrees)
    {
        var normalized = angleDegrees % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static double NormalizeAngleSweepDegrees(double startAngleDegrees, double endAngleDegrees)
    {
        var sweep = NormalizeAngleDegrees(endAngleDegrees) - NormalizeAngleDegrees(startAngleDegrees);
        while (sweep <= 0.0)
            sweep += 360.0;

        return sweep;
    }

    private static Editor2DPreviewPath CreateConvertedStrokePath(
        string id,
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed)
    {
        var nextPoints = points.ToArray();
        return new Editor2DPreviewPath(
            Id: id,
            EntityType: !isClosed && nextPoints.Length == 2 ? "LINE" : "LWPOLYLINE",
            Points: nextPoints,
            IsClosed: isClosed,
            IsAxisAlignedRectangle: false);
    }

    private static Editor2DPreviewPath CreateConvertedCirclePath(string id, Editor2DPoint center, double radius)
        => new(
            Id: id,
            EntityType: "CIRCLE",
            Points: BuildCirclePoints(center, radius),
            IsClosed: true,
            IsAxisAlignedRectangle: false,
            Center: center,
            Radius: radius,
            StartAngleDegrees: 0.0,
            EndAngleDegrees: 360.0);

    private static void AddPointIfDistinct(ICollection<Editor2DPoint> points, Editor2DPoint point)
    {
        AddPointIfDistinct(points, point, PolylineTolerance);
    }

    private static void AddPointIfDistinct(ICollection<Editor2DPoint> points, Editor2DPoint point, double tolerance)
    {
        if (points.Count == 0)
        {
            points.Add(point);
            return;
        }

        var lastPoint = points.Last();
        if (DistanceBetween(lastPoint, point) <= tolerance)
            return;

        points.Add(point);
    }

    private static Editor2DPoint TranslatePoint(Editor2DPoint point, Editor2DPoint direction, double distance)
        => new(
            point.X + (direction.X * distance),
            point.Y + (direction.Y * distance));

    private static Editor2DPoint RotateVector(Editor2DPoint vector, double angleDegrees)
    {
        var angleRadians = angleDegrees * Math.PI / 180.0;
        var cosAngle = Math.Cos(angleRadians);
        var sinAngle = Math.Sin(angleRadians);
        return NormalizeVector(new Editor2DPoint(
            (vector.X * cosAngle) - (vector.Y * sinAngle),
            (vector.X * sinAngle) + (vector.Y * cosAngle)));
    }

    private static Editor2DPoint NormalizeVector(Editor2DPoint vector)
    {
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        if (length <= PolylineTolerance)
            return new Editor2DPoint(1.0, 0.0);

        return new Editor2DPoint(vector.X / length, vector.Y / length);
    }

    private static double DistanceBetween(Editor2DPoint start, Editor2DPoint end)
        => Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));

    private static bool TryMergeCleanupChains(
        CleanupChain left,
        CleanupChain right,
        double tolerance,
        out CleanupChain merged)
    {
        static IReadOnlyList<Editor2DPoint> ReversePoints(IReadOnlyList<Editor2DPoint> points)
            => points.Reverse().ToArray();

        var candidates = new[]
        {
            TryCreateMergedCleanupPoints(left.Points, right.Points, tolerance),
            TryCreateMergedCleanupPoints(left.Points, ReversePoints(right.Points), tolerance),
            TryCreateMergedCleanupPoints(ReversePoints(left.Points), right.Points, tolerance),
            TryCreateMergedCleanupPoints(ReversePoints(left.Points), ReversePoints(right.Points), tolerance),
        };

        var mergedPoints = candidates.FirstOrDefault(static candidate => candidate is not null);
        if (mergedPoints is null)
        {
            merged = default!;
            return false;
        }

        merged = new CleanupChain(
            Id: $"{left.Id}|{right.Id}",
            Points: mergedPoints,
            IsClosed: false);
        return true;
    }

    private static IReadOnlyList<Editor2DPoint>? TryCreateMergedCleanupPoints(
        IReadOnlyList<Editor2DPoint> leftPoints,
        IReadOnlyList<Editor2DPoint> rightPoints,
        double tolerance)
    {
        if (leftPoints.Count < 2 || rightPoints.Count < 2)
            return null;

        if (DistanceBetween(leftPoints[^1], rightPoints[0]) > tolerance)
            return null;

        var mergedPoints = new List<Editor2DPoint>(leftPoints.Count + rightPoints.Count - 1);
        foreach (var point in leftPoints)
            AddPointIfDistinct(mergedPoints, point, tolerance);
        foreach (var point in rightPoints.Skip(1))
            AddPointIfDistinct(mergedPoints, point, tolerance);

        return mergedPoints.Count >= 2 ? mergedPoints : null;
    }

    private static Editor2DPreviewPath FinalizeCleanupChain(CleanupChain chain, double tolerance)
    {
        var points = chain.Points.ToList();
        var isClosed = points.Count >= 3 && DistanceBetween(points[0], points[^1]) <= tolerance;
        if (isClosed)
            points.RemoveAt(points.Count - 1);

        return new Editor2DPreviewPath(
            Id: $"cleanup-{Guid.NewGuid():N}",
            EntityType: points.Count == 2 && !isClosed ? "LINE" : "LWPOLYLINE",
            Points: points.ToArray(),
            IsClosed: isClosed,
            IsAxisAlignedRectangle: IsAxisAlignedRectangle(points, isClosed));
    }

    private sealed record CleanupChain(string Id, IReadOnlyList<Editor2DPoint> Points, bool IsClosed);

    private static bool TryGetLineEndpoints(Editor2DPreviewPath path, out Editor2DPoint start, out Editor2DPoint end)
    {
        if (path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
            && path.Points.Count >= 2)
        {
            start = path.Points[0];
            end = path.Points[^1];
            return true;
        }

        start = default!;
        end = default!;
        return false;
    }

    private static bool TryGetCircleOrArcGeometry(Editor2DPreviewPath path, out Editor2DPoint center, out double radius)
    {
        if ((path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
             || path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase))
            && path.Center is Editor2DPoint resolvedCenter
            && path.Radius is double resolvedRadius
            && resolvedRadius > MeasurementTolerance)
        {
            center = resolvedCenter;
            radius = resolvedRadius;
            return true;
        }

        center = default!;
        radius = 0.0;
        return false;
    }

    private static bool TryGetRectangleBounds(
        Editor2DPreviewPath path,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        if (path.Points.Count < 4)
        {
            minX = minY = maxX = maxY = 0.0;
            return false;
        }

        minX = path.Points.Min(static point => point.X);
        minY = path.Points.Min(static point => point.Y);
        maxX = path.Points.Max(static point => point.X);
        maxY = path.Points.Max(static point => point.Y);
        return (maxX - minX) > RectangleTolerance && (maxY - minY) > RectangleTolerance;
    }

    private static int DistinctCoordinateCount(IEnumerable<double> values, double tolerance)
    {
        var distinct = new List<double>();
        foreach (var value in values)
        {
            if (distinct.All(existing => Math.Abs(existing - value) > tolerance))
                distinct.Add(value);
        }

        return distinct.Count;
    }

    private static int GetOrAddNode(ICollection<Editor2DPoint> nodes, Editor2DPoint point)
    {
        var index = 0;
        foreach (var existing in nodes)
        {
            if (Math.Abs(existing.X - point.X) <= PolylineTolerance
                && Math.Abs(existing.Y - point.Y) <= PolylineTolerance)
                return index;
            index++;
        }

        nodes.Add(point);
        return index;
    }

    private static bool AreAdjacentSegments(int first, int second, int count)
        => second == first + 1 || (first == 0 && second == count - 1);

    private static bool TrySegmentIntersection(
        Editor2DPoint firstStart,
        Editor2DPoint firstEnd,
        Editor2DPoint secondStart,
        Editor2DPoint secondEnd,
        out double firstParameter,
        out double secondParameter,
        out Editor2DPoint intersection)
    {
        var firstX = firstEnd.X - firstStart.X;
        var firstY = firstEnd.Y - firstStart.Y;
        var secondX = secondEnd.X - secondStart.X;
        var secondY = secondEnd.Y - secondStart.Y;
        var denominator = (firstX * secondY) - (firstY * secondX);
        if (Math.Abs(denominator) <= PolylineTolerance)
        {
            firstParameter = secondParameter = 0.0;
            intersection = default!;
            return false;
        }

        var offsetX = secondStart.X - firstStart.X;
        var offsetY = secondStart.Y - firstStart.Y;
        firstParameter = ((offsetX * secondY) - (offsetY * secondX)) / denominator;
        secondParameter = ((offsetX * firstY) - (offsetY * firstX)) / denominator;
        if (firstParameter <= PolylineTolerance || firstParameter >= 1.0 - PolylineTolerance
            || secondParameter <= PolylineTolerance || secondParameter >= 1.0 - PolylineTolerance)
        {
            intersection = default!;
            return false;
        }

        intersection = new Editor2DPoint(
            firstStart.X + (firstX * firstParameter),
            firstStart.Y + (firstY * firstParameter));
        return true;
    }

    private static double SignedArea(IReadOnlyList<Editor2DPoint> points)
    {
        var area = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            area += (points[index].X * next.Y) - (next.X * points[index].Y);
        }

        return area / 2.0;
    }
}
