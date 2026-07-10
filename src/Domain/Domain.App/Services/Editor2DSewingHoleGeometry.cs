using Domain.App.Models;

namespace Domain.App.Services;

public static class Editor2DSewingHoleGeometry
{
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
        var radius = Math.Max(0.01, parameters.Diameter / 2.0);

        foreach (var source in sources)
        {
            var vertices = GetVertices(source);
            if (vertices.Count < 2)
                continue;

            var closed = source.IsClosed || source.Radius is > 0;
            var distances = BuildPlacementDistances(vertices, closed, parameters);
            foreach (var distance in distances)
            {
                var sample = Sample(vertices, closed, distance);
                var normalSign = closed && SignedArea(vertices) < 0 ? -1.0 : 1.0;
                var center = new Editor2DPoint(
                    sample.Point.X + (-sample.TangentY * parameters.Margin * normalSign),
                    sample.Point.Y + (sample.TangentX * parameters.Margin * normalSign));
                if (parameters.AvoidanceEnabled
                    && avoidPaths.Any(path => DistanceToPath(center, GetVertices(path), path.IsClosed) < parameters.AvoidanceClearance + radius))
                {
                    continue;
                }

                result.Add(CreateCircle($"sew-preview-{operationId}-{result.Count}", center, radius));
            }
        }

        return result;
    }

    private static IReadOnlyList<double> BuildPlacementDistances(
        IReadOnlyList<Editor2DPoint> vertices,
        bool closed,
        Editor2DSewingHoleParameters parameters)
    {
        var segmentLengths = GetSegmentLengths(vertices, closed);
        var length = segmentLengths.Sum();
        if (length <= 1e-9)
            return [];

        var pitch = Math.Max(0.1, parameters.Pitch);
        var distances = new List<double>();
        if (parameters.SymmetricDistribution)
        {
            var intervals = Math.Max(1, (int)Math.Round(length / pitch));
            var count = closed ? intervals : intervals + 1;
            var actualPitch = length / intervals;
            for (var index = 0; index < count; index++)
                distances.Add(index * actualPitch);
        }
        else
        {
            for (var distance = 0.0; distance < length - 1e-9; distance += pitch)
                distances.Add(distance);
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

    private static double Distance(Editor2DPoint first, Editor2DPoint second)
        => Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
}
