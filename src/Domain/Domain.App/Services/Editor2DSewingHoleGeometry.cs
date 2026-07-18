using Domain.App.Models;

namespace Domain.App.Services;

public static class Editor2DSewingHoleGeometry
{
    private const int MaxPitchPlacementsPerPath = 12_000;

    public static IReadOnlyList<Editor2DPreviewPath> BuildPreview(
        Editor2DPreviewDocument document,
        IReadOnlyList<string> sourcePathIds,
        Editor2DSewingHoleParameters parameters,
        string operationId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sourcePathIds);
        ArgumentNullException.ThrowIfNull(parameters);

        var sourceIds = sourcePathIds.ToHashSet(StringComparer.Ordinal);
        var sources = document.Paths.Where(path => sourceIds.Contains(path.Id)).ToArray();
        var avoidIds = (parameters.AvoidPathIds ?? []).ToHashSet(StringComparer.Ordinal);
        var avoidPaths = document.Paths.Where(path => avoidIds.Contains(path.Id)).ToArray();
        var generatedIdPrefix = $"sew-{operationId}-";
        var otherPaths = document.Paths.Where(path => !sourceIds.Contains(path.Id)
            && !avoidIds.Contains(path.Id)
            && !path.Id.StartsWith(generatedIdPrefix, StringComparison.Ordinal)).ToArray();
        var existingCircles = otherPaths.Where(path => path.Center is not null && path.Radius is > 0).ToArray();
        var obstaclePaths = otherPaths.Where(path => path.Center is null || path.Radius is null or <= 0).ToArray();
        var result = new List<Editor2DPreviewPath>();
        var occupiedCenters = new Dictionary<(long X, long Y), List<Editor2DPoint>>();
        var radius = Math.Max(0.01, parameters.Diameter / 2.0);
        var countMode = parameters.DistributionMode == Editor2DSewingDistributionMode.Count;
        var mergeDistance = parameters.ProximityFilterEnabled && !countMode
            ? EffectiveProximityDistance(parameters)
            : 0.05;

        foreach (var source in sources)
        {
            var vertices = GetVertices(source);
            if (vertices.Count < 2)
                continue;

            var closed = source.IsClosed || source.Radius is > 0;
            var rows = parameters.Pattern == Editor2DSewingPattern.Saddle
                ? new[]
                {
                    (Offset: parameters.Margin - parameters.SaddleSpacing / 2.0, Phase: 0.0),
                    (Offset: parameters.Margin + parameters.SaddleSpacing / 2.0, Phase: 0.5),
                }
                : [(Offset: parameters.Margin, Phase: 0.0)];
            Editor2DSewingSide[] sides = parameters.Side switch
            {
                Editor2DSewingSide.Right => new[] { Editor2DSewingSide.Right },
                Editor2DSewingSide.Both => new[] { Editor2DSewingSide.Left, Editor2DSewingSide.Right },
                _ => new[] { Editor2DSewingSide.Left },
            };
            var innerNormalSign = closed && SignedArea(vertices) < 0 ? -1.0 : 1.0;

            foreach (var side in sides)
            {
                var sideNormalSign = closed
                    ? side == Editor2DSewingSide.Left ? innerNormalSign : -innerNormalSign
                    : side == Editor2DSewingSide.Left ? 1.0 : -1.0;

                foreach (var row in rows)
                {
                    var offset = Math.Max(0.25, Math.Abs(row.Offset));
                    var isCircle = source.Center is not null && source.Radius is > 0;
                    var offsetPath = isCircle
                        ? null
                        : BuildOffsetPath(vertices, closed, offset, sideNormalSign, parameters.OffsetCornerFillet);
                    var placementVertices = offsetPath?.Vertices ?? vertices;
                    if (placementVertices.Count < 2)
                        continue;
                    var hasForcedCorners = parameters.CornerMode == Editor2DSewingCornerMode.IncludeCorners
                        && !isCircle
                        && vertices.Count > 2;
                    var distances = BuildPlacementDistances(
                        placementVertices,
                        closed,
                        parameters,
                        hasForcedCorners ? 0.0 : row.Phase,
                        offsetPath?.CornerDistances);
                    foreach (var distance in distances)
                    {
                        var sample = Sample(placementVertices, closed, distance);
                        Editor2DPoint center;
                        if (source.Center is { } circleCenter && source.Radius is > 0)
                        {
                            var radialX = sample.Point.X - circleCenter.X;
                            var radialY = sample.Point.Y - circleCenter.Y;
                            var radialLength = Math.Sqrt(radialX * radialX + radialY * radialY);
                            var radialSign = side == Editor2DSewingSide.Left ? -1.0 : 1.0;
                            center = new Editor2DPoint(
                                sample.Point.X + radialX / radialLength * offset * radialSign,
                                sample.Point.Y + radialY / radialLength * offset * radialSign);
                        }
                        else
                        {
                            center = sample.Point;
                        }
                        if (parameters.AvoidanceEnabled
                            && avoidPaths.Any(path => DistanceToPath(center, GetVertices(path), path.IsClosed) < parameters.AvoidanceClearance + radius))
                        {
                            continue;
                        }

                        if (obstaclePaths.Any(path => IsBlockedByObstacle(
                            center,
                            path,
                            parameters.LineProximityFilterEnabled,
                            parameters.LineProximityThreshold,
                            radius)))
                        {
                            continue;
                        }

                        if (existingCircles.Any(path => Distance(center, path.Center!) < parameters.ProximityFilterDistance))
                            continue;

                        if (!TryReserveCenter(occupiedCenters, center, mergeDistance))
                            continue;

                        result.Add(CreateCircle($"sew-preview-{operationId}-{result.Count}", center, radius));
                    }
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<double> BuildPlacementDistances(
        IReadOnlyList<Editor2DPoint> vertices,
        bool closed,
        Editor2DSewingHoleParameters parameters,
        double phase,
        IReadOnlyList<double>? explicitCornerDistances = null)
    {
        var segmentLengths = GetSegmentLengths(vertices, closed);
        var length = segmentLengths.Sum();
        if (length <= 1e-9)
            return [];

        var cornerDistances = explicitCornerDistances?.ToList() ?? [];
        if (explicitCornerDistances is null)
        {
            var accumulated = 0.0;
            for (var index = 0; index < segmentLengths.Count - (closed ? 0 : 1); index++)
            {
                accumulated += segmentLengths[index];
                if (accumulated < length - 1e-9)
                    cornerDistances.Add(accumulated);
            }
        }

        if (parameters.CornerMode == Editor2DSewingCornerMode.IncludeCorners
            && parameters.DistributionMode != Editor2DSewingDistributionMode.Count
            && cornerDistances.Count > 0)
        {
            return BuildCornerPlacementDistances(cornerDistances, length, closed, parameters.Pitch);
        }
        var pitch = Math.Max(0.1, parameters.Pitch);
        var distances = new List<double>();
        if (parameters.DistributionMode == Editor2DSewingDistributionMode.Count)
        {
            var count = Math.Max(1, parameters.Count);
            if (closed)
            {
                var step = length / count;
                for (var index = 0; index < count; index++)
                    distances.Add((index + phase) * step);
            }
            else if (count == 1)
            {
                distances.Add(0);
            }
            else
            {
                var step = length / (count - 1);
                for (var index = 0; index < count; index++)
                    distances.Add(index * step);
            }
        }
        else if (parameters.SymmetricDistribution)
        {
            var intervals = Math.Max(1, (int)Math.Round(length / pitch));
            if (closed && parameters.VariableSpacingEnabled)
            {
                var minimum = Math.Max(0.1, Math.Min(parameters.VariableSpacingMin, parameters.VariableSpacingMax));
                var maximum = Math.Max(minimum, Math.Max(parameters.VariableSpacingMin, parameters.VariableSpacingMax));
                var clampedPitch = length / intervals;
                if (clampedPitch < minimum && intervals > 1)
                    intervals = Math.Max(1, (int)Math.Floor(length / minimum));
                else if (clampedPitch > maximum)
                    intervals = Math.Max(1, (int)Math.Ceiling(length / maximum));
            }
            intervals = Math.Min(MaxPitchPlacementsPerPath, intervals);
            var count = closed ? intervals : intervals + 1;
            var actualPitch = length / intervals;
            for (var index = 0; index < count; index++)
            {
                var distance = (index + phase) * actualPitch;
                if (closed || distance <= length + 1e-9)
                    distances.Add(distance);
            }
        }
        else
        {
            for (var index = 0; index < MaxPitchPlacementsPerPath; index++)
            {
                var distance = (index + phase) * pitch;
                if (distance >= length - 1e-9)
                    break;
                distances.Add(distance);
            }
            if (!closed && (distances.Count == 0 || length - distances[^1] > pitch * 0.5))
                distances.Add(length);
        }

        if (parameters.CornerMode == Editor2DSewingCornerMode.AvoidCorners)
            distances.RemoveAll(distance => cornerDistances.Any(corner => CircularDistance(distance, corner, length, closed) < parameters.CornerClearance));

        return distances
            .Select(distance => closed ? Mod(distance, length) : Math.Clamp(distance, 0, length))
            .DistinctBy(distance => Math.Round(distance, 6))
            .Order()
            .ToArray();
    }

    private static IReadOnlyList<double> BuildCornerPlacementDistances(
        IReadOnlyList<double> cornerDistances,
        double length,
        bool closed,
        double pitch)
    {
        pitch = Math.Max(0.1, pitch);
        var stops = cornerDistances
            .Select(distance => closed ? Mod(distance, length) : Math.Clamp(distance, 0, length))
            .DistinctBy(distance => Math.Round(distance, 5))
            .Order()
            .ToArray();
        var distances = new List<double>();
        if (closed)
        {
            for (var index = 0; index < stops.Length; index++)
            {
                var start = stops[index];
                var run = Mod(stops[(index + 1) % stops.Length] - start, length);
                if (run <= 1e-6)
                    run = length;
                var steps = Math.Max(1, (int)Math.Round(run / pitch));
                for (var step = 0; step < steps; step++)
                    distances.Add(Mod(start + run * step / steps, length));
            }
        }
        else
        {
            var edges = new[] { 0.0 }.Concat(stops).Append(length)
                .DistinctBy(distance => Math.Round(distance, 5))
                .Order()
                .ToArray();
            for (var index = 0; index < edges.Length - 1; index++)
            {
                var start = edges[index];
                var run = edges[index + 1] - start;
                if (run <= 1e-6)
                    continue;
                var steps = Math.Max(1, (int)Math.Round(run / pitch));
                for (var step = 0; step < steps; step++)
                    distances.Add(start + run * step / steps);
            }
            distances.Add(length);
        }

        return distances
            .DistinctBy(distance => Math.Round(distance, 6))
            .Order()
            .ToArray();
    }

    private static (Editor2DPoint Point, double TangentX, double TangentY) Sample(
        IReadOnlyList<Editor2DPoint> vertices,
        bool closed,
        double distance)
    {
        var lengths = GetSegmentLengths(vertices, closed);
        var remaining = distance;
        for (var index = 0; index < lengths.Count; index++)
        {
            var nextIndex = (index + 1) % vertices.Count;
            var length = lengths[index];
            if (remaining <= length || index == lengths.Count - 1)
            {
                var ratio = length <= 1e-9 ? 0 : Math.Clamp(remaining / length, 0, 1);
                var dx = vertices[nextIndex].X - vertices[index].X;
                var dy = vertices[nextIndex].Y - vertices[index].Y;
                return (
                    new Editor2DPoint(vertices[index].X + dx * ratio, vertices[index].Y + dy * ratio),
                    length <= 1e-9 ? 1 : dx / length,
                    length <= 1e-9 ? 0 : dy / length);
            }

            remaining -= length;
        }

        return (vertices[^1], 1, 0);
    }

    private static IReadOnlyList<Editor2DPoint> GetVertices(Editor2DPreviewPath path)
    {
        if (path.Center is Editor2DPoint center && path.Radius is > 0)
        {
            return Enumerable.Range(0, 64)
                .Select(index =>
                {
                    var angle = index * Math.PI * 2.0 / 64.0;
                    return new Editor2DPoint(center.X + path.Radius.Value * Math.Cos(angle), center.Y + path.Radius.Value * Math.Sin(angle));
                })
                .ToArray();
        }

        var points = path.Points.ToList();
        if (points.Count > 1 && Distance(points[0], points[^1]) < 1e-9)
            points.RemoveAt(points.Count - 1);
        return points;
    }

    private sealed record OffsetPath(
        IReadOnlyList<Editor2DPoint> Vertices,
        IReadOnlyList<double> CornerDistances);

    private static OffsetPath BuildOffsetPath(
        IReadOnlyList<Editor2DPoint> vertices,
        bool closed,
        double offset,
        double normalSign,
        bool roundCorners)
    {
        var segmentCount = closed ? vertices.Count : vertices.Count - 1;
        var directions = new (double X, double Y)[segmentCount];
        var normals = new (double X, double Y)[segmentCount];
        for (var index = 0; index < segmentCount; index++)
        {
            var next = (index + 1) % vertices.Count;
            var length = Distance(vertices[index], vertices[next]);
            if (length <= 1e-9)
                continue;
            directions[index] = (
                (vertices[next].X - vertices[index].X) / length,
                (vertices[next].Y - vertices[index].Y) / length);
            normals[index] = (-directions[index].Y * normalSign, directions[index].X * normalSign);
        }

        var result = new List<Editor2DPoint>();
        if (!closed)
        {
            result.Add(new Editor2DPoint(
                vertices[0].X + normals[0].X * offset,
                vertices[0].Y + normals[0].Y * offset));
        }

        var firstCorner = closed ? 0 : 1;
        var lastCornerExclusive = closed ? vertices.Count : vertices.Count - 1;
        for (var cornerIndex = firstCorner; cornerIndex < lastCornerExclusive; cornerIndex++)
        {
            var previousSegment = (cornerIndex - 1 + segmentCount) % segmentCount;
            var nextSegment = cornerIndex % segmentCount;
            var corner = vertices[cornerIndex];
            var previousOffset = new Editor2DPoint(
                corner.X + normals[previousSegment].X * offset,
                corner.Y + normals[previousSegment].Y * offset);
            var nextOffset = new Editor2DPoint(
                corner.X + normals[nextSegment].X * offset,
                corner.Y + normals[nextSegment].Y * offset);
            var turn = Cross(directions[previousSegment], directions[nextSegment]);
            var isOuterCorner = turn * normalSign < -1e-9;

            if (roundCorners && isOuterCorner)
            {
                AddRoundJoin(result, corner, previousOffset, nextOffset, turn);
            }
            else if (TryIntersectOffsetLines(
                previousOffset,
                directions[previousSegment],
                nextOffset,
                directions[nextSegment],
                out var intersection)
                && Distance(corner, intersection) <= offset * 4.0 + 1e-9)
            {
                AddDistinct(result, intersection);
            }
            else
            {
                AddDistinct(result, previousOffset);
                AddDistinct(result, nextOffset);
            }
        }

        if (!closed)
        {
            var last = vertices.Count - 1;
            var normal = normals[^1];
            AddDistinct(result, new Editor2DPoint(
                vertices[last].X + normal.X * offset,
                vertices[last].Y + normal.Y * offset));
        }

        var cornerPoints = closed
            ? vertices
            : vertices.Skip(1).Take(Math.Max(0, vertices.Count - 2)).ToArray();
        var cornerDistances = cornerPoints
            .Select(corner => ProjectDistance(result, closed, corner))
            .ToArray();
        return new OffsetPath(result, cornerDistances);
    }

    private static bool TryIntersectOffsetLines(
        Editor2DPoint first,
        (double X, double Y) firstDirection,
        Editor2DPoint second,
        (double X, double Y) secondDirection,
        out Editor2DPoint intersection)
    {
        var denominator = Cross(firstDirection, secondDirection);
        if (Math.Abs(denominator) <= 1e-9)
        {
            intersection = first;
            return false;
        }

        var delta = (X: second.X - first.X, Y: second.Y - first.Y);
        var distance = Cross(delta, secondDirection) / denominator;
        intersection = new Editor2DPoint(
            first.X + firstDirection.X * distance,
            first.Y + firstDirection.Y * distance);
        return true;
    }

    private static void AddRoundJoin(
        List<Editor2DPoint> result,
        Editor2DPoint corner,
        Editor2DPoint start,
        Editor2DPoint end,
        double turn)
    {
        AddDistinct(result, start);
        var radius = Distance(corner, start);
        var startAngle = Math.Atan2(start.Y - corner.Y, start.X - corner.X);
        var endAngle = Math.Atan2(end.Y - corner.Y, end.X - corner.X);
        if (turn > 0)
        {
            while (endAngle <= startAngle)
                endAngle += Math.PI * 2.0;
        }
        else
        {
            while (endAngle >= startAngle)
                endAngle -= Math.PI * 2.0;
        }

        var sweep = endAngle - startAngle;
        var segments = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 16.0)));
        for (var index = 1; index <= segments; index++)
        {
            var angle = startAngle + sweep * index / segments;
            AddDistinct(result, new Editor2DPoint(
                corner.X + Math.Cos(angle) * radius,
                corner.Y + Math.Sin(angle) * radius));
        }
    }

    private static double ProjectDistance(
        IReadOnlyList<Editor2DPoint> vertices,
        bool closed,
        Editor2DPoint point)
    {
        var segmentCount = closed ? vertices.Count : vertices.Count - 1;
        var accumulated = 0.0;
        var bestDistanceSquared = double.PositiveInfinity;
        var bestProjectionDistance = 0.0;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = vertices[index];
            var end = vertices[(index + 1) % vertices.Count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lengthSquared = dx * dx + dy * dy;
            var length = Math.Sqrt(lengthSquared);
            var ratio = lengthSquared <= 1e-12
                ? 0
                : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
            var projectedX = start.X + ratio * dx;
            var projectedY = start.Y + ratio * dy;
            var distanceSquared = Math.Pow(point.X - projectedX, 2) + Math.Pow(point.Y - projectedY, 2);
            if (distanceSquared < bestDistanceSquared - 1e-12)
            {
                bestDistanceSquared = distanceSquared;
                bestProjectionDistance = accumulated + ratio * length;
            }
            accumulated += length;
        }
        return bestProjectionDistance;
    }

    private static void AddDistinct(List<Editor2DPoint> points, Editor2DPoint point)
    {
        if (points.Count == 0 || Distance(points[^1], point) > 1e-9)
            points.Add(point);
    }

    private static double Cross((double X, double Y) first, (double X, double Y) second)
        => first.X * second.Y - first.Y * second.X;

    private static IReadOnlyList<double> GetSegmentLengths(IReadOnlyList<Editor2DPoint> vertices, bool closed)
    {
        var count = closed ? vertices.Count : vertices.Count - 1;
        return Enumerable.Range(0, Math.Max(0, count))
            .Select(index => Distance(vertices[index], vertices[(index + 1) % vertices.Count]))
            .ToArray();
    }

    private static Editor2DPreviewPath CreateCircle(string id, Editor2DPoint center, double radius)
        => new(
            id,
            "CIRCLE",
            Enumerable.Range(0, 33).Select(index =>
            {
                var angle = index * Math.PI * 2.0 / 32.0;
                return new Editor2DPoint(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
            }).ToArray(),
            IsClosed: true,
            Center: center,
            Radius: radius);

    private static bool IsBlockedByObstacle(
        Editor2DPoint center,
        Editor2DPreviewPath path,
        bool lineProximityFilterEnabled,
        double lineProximityThreshold,
        double holeRadius)
    {
        var vertices = GetVertices(path);
        var distance = DistanceToPath(center, vertices, path.IsClosed);
        if ((lineProximityFilterEnabled && distance < lineProximityThreshold)
            || distance < holeRadius)
        {
            return true;
        }

        return path.IsClosed && IsPointInsidePolygon(center, vertices);
    }

    private static bool IsPointInsidePolygon(Editor2DPoint point, IReadOnlyList<Editor2DPoint> vertices)
    {
        if (vertices.Count < 3)
            return false;

        var inside = false;
        for (var current = 0; current < vertices.Count; current++)
        {
            var previous = current == 0 ? vertices.Count - 1 : current - 1;
            var currentPoint = vertices[current];
            var previousPoint = vertices[previous];
            if ((currentPoint.Y > point.Y) == (previousPoint.Y > point.Y))
                continue;

            var crossingX = (previousPoint.X - currentPoint.X)
                * (point.Y - currentPoint.Y)
                / (previousPoint.Y - currentPoint.Y)
                + currentPoint.X;
            if (point.X < crossingX)
                inside = !inside;
        }

        return inside;
    }

    private static double DistanceToPath(Editor2DPoint point, IReadOnlyList<Editor2DPoint> vertices, bool closed)
    {
        if (vertices.Count == 0)
            return double.PositiveInfinity;
        if (vertices.Count == 1)
            return Distance(point, vertices[0]);
        var count = closed ? vertices.Count : vertices.Count - 1;
        return Enumerable.Range(0, count)
            .Min(index => DistanceToSegment(point, vertices[index], vertices[(index + 1) % vertices.Count]));
    }

    private static double DistanceToSegment(Editor2DPoint point, Editor2DPoint start, Editor2DPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 1e-12)
            return Distance(point, start);
        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        return Distance(point, new Editor2DPoint(start.X + t * dx, start.Y + t * dy));
    }

    private static double SignedArea(IReadOnlyList<Editor2DPoint> vertices)
        => Enumerable.Range(0, vertices.Count)
            .Sum(index => vertices[index].X * vertices[(index + 1) % vertices.Count].Y
                - vertices[(index + 1) % vertices.Count].X * vertices[index].Y) / 2.0;

    private static double CircularDistance(double first, double second, double length, bool closed)
    {
        var distance = Math.Abs(first - second);
        return closed ? Math.Min(distance, length - distance) : distance;
    }

    private static double Mod(double value, double modulus) => (value % modulus + modulus) % modulus;

    private static bool TryReserveCenter(
        Dictionary<(long X, long Y), List<Editor2DPoint>> occupied,
        Editor2DPoint center,
        double tolerance)
    {
        tolerance = Math.Max(0.001, tolerance);
        var cell = ((long)Math.Floor(center.X / tolerance), (long)Math.Floor(center.Y / tolerance));
        for (var x = cell.Item1 - 1; x <= cell.Item1 + 1; x++)
        {
            for (var y = cell.Item2 - 1; y <= cell.Item2 + 1; y++)
            {
                if (occupied.TryGetValue((x, y), out var candidates)
                    && candidates.Any(candidate => Distance(candidate, center) < tolerance))
                {
                    return false;
                }
            }
        }

        if (!occupied.TryGetValue(cell, out var bucket))
            occupied[cell] = bucket = [];
        bucket.Add(center);
        return true;
    }

    private static double EffectiveProximityDistance(Editor2DSewingHoleParameters parameters)
    {
        var distance = parameters.ProximityFilterDistance;
        if (parameters.Pattern != Editor2DSewingPattern.Saddle)
            return distance;

        var nearestLegitimateNeighbor = Math.Min(
            parameters.Pitch,
            Math.Sqrt(parameters.SaddleSpacing * parameters.SaddleSpacing
                + Math.Pow(parameters.Pitch / 2.0, 2)));
        return Math.Min(distance, nearestLegitimateNeighbor * 0.9);
    }

    private static double Distance(Editor2DPoint first, Editor2DPoint second)
        => Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
}
