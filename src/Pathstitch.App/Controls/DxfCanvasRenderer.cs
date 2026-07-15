using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal sealed class DxfCanvasRenderer
{
    private readonly DxfCanvasRenderPlanner _planner = new();

    public IReadOnlyList<Editor2DPreviewPath> VisiblePaths(
        Editor2DPreviewDocument? document,
        IReadOnlyList<string> hiddenPathIds)
        => _planner.VisiblePaths(document, hiddenPathIds);

    public void DrawPaths(
        DrawingContext context,
        IReadOnlyList<Editor2DPreviewPath> paths,
        IReadOnlyList<string> selectedPathIds,
        string? hoveredPathId,
        Func<Editor2DPoint, Point> worldToScreen,
        Func<DxfCanvasPathVisualRole, Pen> resolvePen,
        Func<Editor2DPreviewPath, IBrush?> resolveFill,
        Func<Editor2DPreviewPath, Pen, bool> tryDrawSemanticPrimitive)
    {
        var selected = selectedPathIds.Count == 0
            ? EmptyIds.Instance
            : new HashSet<string>(selectedPathIds, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            if (path.Points.Count < 2)
                continue;

            var pen = resolvePen(_planner.ResolvePathRole(path, selected, hoveredPathId));
            if (tryDrawSemanticPrimitive(path, pen))
                continue;

            var geometry = new StreamGeometry();
            using (var geometryContext = geometry.Open())
            {
                geometryContext.BeginFigure(worldToScreen(path.Points[0]), false);
                for (var pointIndex = 1; pointIndex < path.Points.Count; pointIndex++)
                    geometryContext.LineTo(worldToScreen(path.Points[pointIndex]));
                if (path.IsClosed)
                    geometryContext.EndFigure(true);
            }
            context.DrawGeometry(resolveFill(path), pen, geometry);
        }
    }

    private sealed class EmptyIds : HashSet<string>
    {
        public static EmptyIds Instance { get; } = new();
        private EmptyIds() : base(StringComparer.Ordinal) { }
    }
}
