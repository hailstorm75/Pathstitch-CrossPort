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
        var result = new List<Editor2DPreviewPath>();
        var occupiedCenters = new Dictionary<(long X, long Y), List<Editor2DPoint>>();
        var radius = Math.Max(0.01, parameters.Diameter / 2.0);

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
                    var hasForcedCorners = parameters.CornerMode == Editor2DSewingCornerMode.IncludeCorners
                        && source.Center is null
                        && vertices.Count > 2;
                    var distances = BuildPlacementDistances(
                        vertices,
                        closed,
                        parameters,
                        hasForcedCorners ? 0.0 : row.Phase);
                    foreach (var distance in distances)
                    {
                        var sample = Sample(vertices, closed, distance);
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
                            center = new Editor2DPoint(
                                sample.Point.X + (-sample.TangentY * offset * sideNormalSign),
                                sample.Point.Y + (sample.TangentX * offset * sideNormalSign));
                        }
                        if (parameters.AvoidanceEnabled
                            && avoidPaths.Any(path => DistanceToPath(center, GetVertices(path), path.IsClosed) < parameters.AvoidanceClearance + radius))
                        {
                            continue;
                        }

                        if (!TryReserveCenter(occupiedCenters, center))
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
        double phase)
    {
        var segmentLengths = GetSegmentLengths(vertices, closed);
        var length = segmentLengths.Sum();
        if (length <= 1e-9)
            return [];

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

        var cornerDistances = new List<double>();
        var accumulated = 0.0;
        for (var index = 0; index < segmentLengths.Count - (closed ? 0 : 1); index++)
        {
            accumulated += segmentLengths[index];
            if (accumulated < length - 1e-9)
                cornerDistances.Add(accumulated);
        }

        if (parameters.CornerMode == Editor2DSewingCornerMode.IncludeCorners)
            distances.AddRange(cornerDistances);
        else if (parameters.CornerMode == Editor2DSewingCornerMode.AvoidCorners)
            distances.RemoveAll(distance => cornerDistances.Any(corner => CircularDistance(distance, corner, length, closed) < parameters.CornerClearance));

        return distances
            .Select(distance => closed ? Mod(distance, length) : Math.Clamp(distance, 0, length))
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
        Editor2DPoint center)
    {
        const double tolerance = 0.05;
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

    private static double Distance(Editor2DPoint first, Editor2DPoint second)
        => Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
}
