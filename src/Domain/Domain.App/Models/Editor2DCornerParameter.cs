using System.Text.Json.Serialization;

namespace Domain.App.Models;

public enum Editor2DCornerKind
{
    Fillet = 0,
    Chamfer = 1,
}

public enum Editor2DFilletContinuity
{
    G1 = 0,
    G2 = 1,
}

public sealed record Editor2DCornerParameter(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("pathId")] string PathId,
    [property: JsonPropertyName("cornerIndex")] int CornerIndex,
    [property: JsonPropertyName("kind")] Editor2DCornerKind Kind,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("sourcePoints")] IReadOnlyList<Editor2DPoint> SourcePoints,
    [property: JsonPropertyName("continuity")] Editor2DFilletContinuity Continuity = Editor2DFilletContinuity.G1);

public static class Editor2DCornerGeometry
{
    public static Editor2DPreviewPath Apply(
        Editor2DPreviewPath path,
        IReadOnlyList<Editor2DCornerParameter> parameters)
    {
        var applicable = parameters
            .Where(parameter => parameter.PathId == path.Id)
            .OrderBy(parameter => parameter.CornerIndex)
            .ToArray();
        if (applicable.Length == 0)
            return path;

        var source = applicable[0].SourcePoints.ToArray();
        if (source.Length < 3)
            return path;

        var byIndex = applicable.ToDictionary(parameter => parameter.CornerIndex);
        var setbacks = new double[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            if (!byIndex.TryGetValue(index, out var parameter)
                || (!path.IsClosed && (index == 0 || index == source.Length - 1)))
                continue;

            var previous = source[index == 0 ? source.Length - 1 : index - 1];
            var next = source[index == source.Length - 1 ? 0 : index + 1];
            setbacks[index] = DesiredSetback(previous, source[index], next, parameter.Kind, parameter.Value);
        }

        var edges = path.IsClosed
            ? Enumerable.Range(0, source.Length).Select(index => (Left: index, Right: (index + 1) % source.Length)).ToArray()
            : Enumerable.Range(0, source.Length - 1).Select(index => (Left: index, Right: index + 1)).ToArray();
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var (left, right) in edges)
            {
                if (setbacks[left] <= 1e-9 || setbacks[right] <= 1e-9)
                    continue;
                var edgeLength = Distance(source[left], source[right]);
                if (setbacks[left] + setbacks[right] <= edgeLength + 1e-9)
                    continue;
                var half = edgeLength / 2.0;
                setbacks[left] = Math.Min(setbacks[left], half);
                setbacks[right] = Math.Min(setbacks[right], half);
            }
        }

        var result = new List<Editor2DPoint>();
        for (var index = 0; index < source.Length; index++)
        {
            if (!byIndex.TryGetValue(index, out var parameter)
                || (!path.IsClosed && (index == 0 || index == source.Length - 1)))
            {
                result.Add(source[index]);
                continue;
            }

            var previous = source[index == 0 ? source.Length - 1 : index - 1];
            var next = source[index == source.Length - 1 ? 0 : index + 1];
            var replacement = BuildCorner(
                previous,
                source[index],
                next,
                parameter.Kind,
                setbacks[index],
                parameter.Continuity);
            if (replacement.Count == 0)
                result.Add(source[index]);
            else
                result.AddRange(replacement);
        }

        return path with { Points = result, IsAxisAlignedRectangle = false };
    }

    public static double DefaultValue(
        Editor2DPoint previous,
        Editor2DPoint corner,
        Editor2DPoint next,
        Editor2DCornerKind kind)
    {
        var shortest = Math.Min(Distance(previous, corner), Distance(corner, next));
        return kind == Editor2DCornerKind.Chamfer ? shortest * 0.2 : shortest * 0.15;
    }

    public static double ValueFromPoint(
        Editor2DPoint previous,
        Editor2DPoint corner,
        Editor2DPoint next,
        Editor2DPoint point,
        Editor2DCornerKind kind)
    {
        var towardPrevious = Normalize(previous.X - corner.X, previous.Y - corner.Y);
        var towardNext = Normalize(next.X - corner.X, next.Y - corner.Y);
        var dot = Math.Clamp((towardPrevious.X * towardNext.X) + (towardPrevious.Y * towardNext.Y), -1, 1);
        var angle = Math.Acos(dot);
        var bisector = Normalize(towardPrevious.X + towardNext.X, towardPrevious.Y + towardNext.Y);
        var setback = Math.Max(0, ((point.X - corner.X) * bisector.X) + ((point.Y - corner.Y) * bisector.Y));
        var rawValue = kind == Editor2DCornerKind.Chamfer
            ? setback
            : setback * Math.Tan(angle / 2);
        var maximumSetback = Math.Min(Distance(previous, corner), Distance(corner, next)) * 0.5;
        var maximumValue = kind == Editor2DCornerKind.Chamfer
            ? maximumSetback
            : maximumSetback * Math.Tan(angle / 2);
        return Math.Clamp(rawValue, 0.001, Math.Max(0.001, maximumValue));
    }

    private static IReadOnlyList<Editor2DPoint> BuildCorner(
        Editor2DPoint previous,
        Editor2DPoint corner,
        Editor2DPoint next,
        Editor2DCornerKind kind,
        double setback,
        Editor2DFilletContinuity continuity)
    {
        var towardPrevious = Normalize(previous.X - corner.X, previous.Y - corner.Y);
        var towardNext = Normalize(next.X - corner.X, next.Y - corner.Y);
        var dot = Math.Clamp((towardPrevious.X * towardNext.X) + (towardPrevious.Y * towardNext.Y), -1, 1);
        var angle = Math.Acos(dot);
        if (angle <= 1e-3 || angle >= Math.PI - 1e-3 || setback <= 1e-9)
            return [];

        var start = new Editor2DPoint(corner.X + towardPrevious.X * setback, corner.Y + towardPrevious.Y * setback);
        var end = new Editor2DPoint(corner.X + towardNext.X * setback, corner.Y + towardNext.Y * setback);
        if (kind == Editor2DCornerKind.Chamfer)
            return [start, end];

        if (continuity == Editor2DFilletContinuity.G2)
            return BuildLegacyG2Fillet(start, corner, end);

        var bisector = Normalize(towardPrevious.X + towardNext.X, towardPrevious.Y + towardNext.Y);
        var radius = setback * Math.Tan(angle / 2);
        var centerDistance = radius / Math.Sin(angle / 2);
        var center = new Editor2DPoint(corner.X + bisector.X * centerDistance, corner.Y + bisector.Y * centerDistance);
        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var sweep = Math.Atan2(end.Y - center.Y, end.X - center.X) - startAngle;
        while (sweep <= -Math.PI) sweep += Math.PI * 2;
        while (sweep > Math.PI) sweep -= Math.PI * 2;

        return Enumerable.Range(0, 11)
            .Select(index =>
            {
                var current = startAngle + sweep * index / 10;
                return new Editor2DPoint(center.X + radius * Math.Cos(current), center.Y + radius * Math.Sin(current));
            })
            .ToArray();
    }

    private static double DesiredSetback(
        Editor2DPoint previous,
        Editor2DPoint corner,
        Editor2DPoint next,
        Editor2DCornerKind kind,
        double requestedValue)
    {
        var towardPrevious = Normalize(previous.X - corner.X, previous.Y - corner.Y);
        var towardNext = Normalize(next.X - corner.X, next.Y - corner.Y);
        var dot = Math.Clamp((towardPrevious.X * towardNext.X) + (towardPrevious.Y * towardNext.Y), -1, 1);
        var angle = Math.Acos(dot);
        if (angle < 1e-3 || angle > Math.PI - 1e-3 || requestedValue <= 1e-9)
            return 0.0;

        var raw = kind == Editor2DCornerKind.Chamfer
            ? requestedValue
            : requestedValue / Math.Tan(angle / 2.0);
        return Math.Max(0.0, Math.Min(raw, Math.Min(Distance(previous, corner), Distance(corner, next))));
    }

    private static IReadOnlyList<Editor2DPoint> BuildLegacyG2Fillet(
        Editor2DPoint start,
        Editor2DPoint corner,
        Editor2DPoint end)
    {
        // Legacy macOS behavior: build a curvature-eased cubic, sample it at
        // i/8, then replace each pair of sample intervals with its circumcircle
        // arc. This is intentionally the existing four-arc construction, not a
        // newer quintic or classical two-arc biarc approximation.
        const double controlFactor = 0.62;
        var control1 = Lerp(start, corner, controlFactor);
        var control2 = Lerp(end, corner, controlFactor);
        var samples = Enumerable.Range(0, 9)
            .Select(index => CubicBezier(start, control1, control2, end, index / 8.0))
            .ToArray();

        var result = new List<Editor2DPoint>(41);
        for (var index = 0; index < 8; index += 2)
        {
            var arc = FlattenArcThroughThreePoints(samples[index], samples[index + 1], samples[index + 2]);
            if (arc.Count == 0)
                continue;
            result.AddRange(result.Count == 0 ? arc : arc.Skip(1));
        }

        return result;
    }

    private static IReadOnlyList<Editor2DPoint> FlattenArcThroughThreePoints(
        Editor2DPoint start,
        Editor2DPoint through,
        Editor2DPoint end)
    {
        var determinant = 2.0 * (
            start.X * (through.Y - end.Y)
            + through.X * (end.Y - start.Y)
            + end.X * (start.Y - through.Y));
        if (Math.Abs(determinant) < 1e-12)
            return [start, end];

        var startNorm = (start.X * start.X) + (start.Y * start.Y);
        var throughNorm = (through.X * through.X) + (through.Y * through.Y);
        var endNorm = (end.X * end.X) + (end.Y * end.Y);
        var center = new Editor2DPoint(
            (startNorm * (through.Y - end.Y)
             + throughNorm * (end.Y - start.Y)
             + endNorm * (start.Y - through.Y)) / determinant,
            (startNorm * (end.X - through.X)
             + throughNorm * (start.X - end.X)
             + endNorm * (through.X - start.X)) / determinant);

        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var sweep = Math.Atan2(end.Y - center.Y, end.X - center.X) - startAngle;
        var orientation = ((through.X - start.X) * (end.Y - start.Y))
                          - ((through.Y - start.Y) * (end.X - start.X));
        if (orientation > 0.0)
        {
            while (sweep < 0.0) sweep += Math.PI * 2.0;
        }
        else
        {
            while (sweep > 0.0) sweep -= Math.PI * 2.0;
        }

        var radius = Distance(start, center);
        return Enumerable.Range(0, 11)
            .Select(index =>
            {
                var angle = startAngle + (sweep * index / 10.0);
                return new Editor2DPoint(
                    center.X + (radius * Math.Cos(angle)),
                    center.Y + (radius * Math.Sin(angle)));
            })
            .ToArray();
    }

    private static Editor2DPoint CubicBezier(
        Editor2DPoint start,
        Editor2DPoint control1,
        Editor2DPoint control2,
        Editor2DPoint end,
        double parameter)
    {
        var inverse = 1.0 - parameter;
        var startWeight = inverse * inverse * inverse;
        var control1Weight = 3.0 * inverse * inverse * parameter;
        var control2Weight = 3.0 * inverse * parameter * parameter;
        var endWeight = parameter * parameter * parameter;
        return new Editor2DPoint(
            (startWeight * start.X) + (control1Weight * control1.X) + (control2Weight * control2.X) + (endWeight * end.X),
            (startWeight * start.Y) + (control1Weight * control1.Y) + (control2Weight * control2.Y) + (endWeight * end.Y));
    }

    private static Editor2DPoint Lerp(Editor2DPoint from, Editor2DPoint to, double amount)
        => new(
            from.X + ((to.X - from.X) * amount),
            from.Y + ((to.Y - from.Y) * amount));

    private static (double X, double Y) Normalize(double x, double y)
    {
        var length = Math.Sqrt(x * x + y * y);
        return length <= 1e-9 ? (0, 0) : (x / length, y / length);
    }

    private static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));
}
