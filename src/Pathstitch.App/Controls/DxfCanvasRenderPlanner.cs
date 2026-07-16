using System;
using System.Collections.Generic;
using System.Linq;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal enum DxfCanvasPathVisualRole
{
    Open,
    Closed,
    Construction,
    Hovered,
    Selected,
}

internal sealed class DxfCanvasRenderPlanner
{
    public IReadOnlyList<Editor2DPreviewPath> VisiblePaths(
        Editor2DPreviewDocument? document,
        IReadOnlyList<string> hiddenPathIds)
    {
        if (document is null)
            return [];
        if (hiddenPathIds.Count == 0)
            return document.Paths;

        var hidden = hiddenPathIds.ToHashSet(StringComparer.Ordinal);
        return document.Paths.Where(path => !hidden.Contains(path.Id)).ToArray();
    }

    public DxfCanvasPathVisualRole ResolvePathRole(
        Editor2DPreviewPath path,
        IReadOnlySet<string> selectedPathIds,
        string? hoveredPathId)
        => selectedPathIds.Contains(path.Id)
            ? DxfCanvasPathVisualRole.Selected
            : string.Equals(path.Id, hoveredPathId, StringComparison.Ordinal)
                ? DxfCanvasPathVisualRole.Hovered
                : path.IsConstruction
                    ? DxfCanvasPathVisualRole.Construction
                    : path.IsClosed
                        ? DxfCanvasPathVisualRole.Closed
                        : DxfCanvasPathVisualRole.Open;
}
