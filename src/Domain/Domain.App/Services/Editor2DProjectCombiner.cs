using Domain.App.Models;

namespace Domain.App.Services;

public static class Editor2DProjectCombiner
{
    public static Editor2DWorkspaceState Combine(
        Editor2DWorkspaceState target,
        Editor2DWorkspaceState incoming)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(incoming);

        var targetPaths = target.Document.Paths.ToList();
        var usedPathIds = targetPaths.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        var dx = 10.0 - incoming.Document.Bounds.MinX;
        var dy = 10.0 - incoming.Document.Bounds.MinY;
        var pathIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in incoming.Document.Paths)
        {
            var id = UniqueId(path.Id, usedPathIds);
            pathIds[path.Id] = id;
            targetPaths.Add(Translate(path with { Id = id }, dx, dy));
        }

        var folders = (target.Folders ?? []).ToList();
        var folderIds = folders.Select(folder => folder.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var folder in incoming.Folders ?? [])
        {
            if (folderIds.Add(folder.Id))
                folders.Add(folder);
        }

        var layers = (target.Layers ?? []).ToList();
        var usedLayerIds = layers.Select(layer => layer.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var layer in incoming.Layers ?? [])
        {
            if (layer.Kind != Editor2DLayerKind.Geometry)
                continue;

            var mappedPaths = layer.PathIds
                .Where(pathIds.ContainsKey)
                .Select(id => pathIds[id])
                .ToArray();
            var existingIndex = layers.FindIndex(candidate =>
                candidate.Kind == Editor2DLayerKind.Geometry
                && string.Equals(candidate.Name, layer.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                var existing = layers[existingIndex];
                layers[existingIndex] = existing with
                {
                    PathIds = existing.PathIds.Concat(mappedPaths).Distinct(StringComparer.Ordinal).ToArray(),
                };
                continue;
            }

            layers.Add(layer with
            {
                Id = UniqueId(layer.Id, usedLayerIds),
                PathIds = mappedPaths,
                ReferenceImage = null,
                ParentFolderId = layer.ParentFolderId is not null && folderIds.Contains(layer.ParentFolderId)
                    ? layer.ParentFolderId
                    : null,
                Order = layers.Count,
            });
        }

        var assigned = layers.SelectMany(layer => layer.PathIds).ToHashSet(StringComparer.Ordinal);
        var unassigned = pathIds.Values.Where(id => !assigned.Contains(id)).ToArray();
        if (unassigned.Length > 0)
        {
            var fallbackIndex = layers.FindIndex(layer => layer.Kind == Editor2DLayerKind.Geometry);
            if (fallbackIndex < 0)
            {
                layers.Add(new Editor2DLayer(
                    UniqueId("layer-combined", usedLayerIds),
                    "Combined",
                    unassigned,
                    Order: layers.Count));
            }
            else
            {
                var fallback = layers[fallbackIndex];
                layers[fallbackIndex] = fallback with
                {
                    PathIds = fallback.PathIds.Concat(unassigned).Distinct(StringComparer.Ordinal).ToArray(),
                };
            }
        }

        var measurements = (target.Measurements ?? []).ToList();
        var measurementIds = measurements.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var measurement in incoming.Measurements ?? [])
        {
            if (!measurementIds.Add(measurement.Id))
                continue;
            measurements.Add(measurement with
            {
                Start = Translate(measurement.Start, dx, dy),
                End = Translate(measurement.End, dx, dy),
                RectP1 = TranslateOptional(measurement.RectP1, dx, dy),
                RectP2 = TranslateOptional(measurement.RectP2, dx, dy),
                EntityPathId = measurement.EntityPathId is not null
                    && pathIds.TryGetValue(measurement.EntityPathId, out var mappedId)
                        ? mappedId
                        : null,
            });
        }

        var document = new Editor2DPreviewDocument(
            targetPaths,
            Bounds(targetPaths),
            targetPaths.GroupBy(path => path.EntityType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            target.Document.UnsupportedEntityTypes
                .Concat(incoming.Document.UnsupportedEntityTypes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());

        return target with
        {
            Document = document,
            Measurements = measurements,
            Layers = layers,
            Folders = folders,
            IsInitialized = true,
        };
    }

    private static string UniqueId(string preferred, ISet<string> used)
    {
        var id = string.IsNullOrWhiteSpace(preferred) ? Guid.NewGuid().ToString("N") : preferred;
        while (!used.Add(id))
            id = Guid.NewGuid().ToString("N");
        return id;
    }

    private static Editor2DPreviewPath Translate(Editor2DPreviewPath path, double dx, double dy)
        => path with
        {
            Points = path.Points.Select(point => Translate(point, dx, dy)).ToArray(),
            Start = TranslateOptional(path.Start, dx, dy),
            Center = TranslateOptional(path.Center, dx, dy),
            BezierAnchors = path.BezierAnchors?.Select(anchor => anchor with
            {
                Point = Translate(anchor.Point, dx, dy),
                HandleIn = TranslateOptional(anchor.HandleIn, dx, dy),
                HandleOut = TranslateOptional(anchor.HandleOut, dx, dy),
            }).ToArray(),
        };

    private static Editor2DPoint Translate(Editor2DPoint point, double dx, double dy)
        => new(point.X + dx, point.Y + dy);

    private static Editor2DPoint? TranslateOptional(Editor2DPoint? point, double dx, double dy)
        => point is null ? null : Translate(point, dx, dy);

    private static Editor2DBounds Bounds(IReadOnlyList<Editor2DPreviewPath> paths)
    {
        var points = paths.SelectMany(path => path.Points
            .Concat(path.Start is null ? [] : [path.Start])
            .Concat(path.Center is null ? [] : [path.Center]))
            .ToArray();
        return points.Length == 0
            ? new Editor2DBounds(0, 0, 0, 0)
            : new Editor2DBounds(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.X),
                points.Max(point => point.Y));
    }
}
