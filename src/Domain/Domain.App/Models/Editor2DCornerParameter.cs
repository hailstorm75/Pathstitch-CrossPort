using System.Text.Json.Serialization;

namespace Domain.App.Models;

public enum Editor2DCornerKind
{
    Fillet = 0,
    Chamfer = 1,
}

public sealed record Editor2DCornerParameter(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("pathId")] string PathId,
    [property: JsonPropertyName("cornerIndex")] int CornerIndex,
    [property: JsonPropertyName("kind")] Editor2DCornerKind Kind,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("sourcePoints")] IReadOnlyList<Editor2DPoint> SourcePoints);

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
            var replacement = BuildCorner(previous, source[index], next, parameter.Kind, parameter.Value);
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

    private static IReadOnlyList<Editor2DPoint> BuildCorner(
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
        var maximum = Math.Min(Distance(previous, corner), Distance(corner, next)) * 0.5;
        if (angle <= 1e-3 || angle >= Math.PI - 1e-3 || maximum <= 1e-6)
            return [];

        var setback = kind == Editor2DCornerKind.Chamfer
            ? Math.Min(requestedValue, maximum)
            : Math.Min(requestedValue / Math.Tan(angle / 2), maximum);
        if (setback <= 1e-6)
            return [];

        var start = new Editor2DPoint(corner.X + towardPrevious.X * setback, corner.Y + towardPrevious.Y * setback);
        var end = new Editor2DPoint(corner.X + towardNext.X * setback, corner.Y + towardNext.Y * setback);
        if (kind == Editor2DCornerKind.Chamfer)
            return [start, end];

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

    private static (double X, double Y) Normalize(double x, double y)
    {
        var length = Math.Sqrt(x * x + y * y);
        return length <= 1e-9 ? (0, 0) : (x / length, y / length);
    }

    private static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));
}
