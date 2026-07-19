using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed record Editor2DLayerHierarchyItem(
    string Id,
    string Name,
    int Depth,
    bool IsFolder,
    bool IsExpanded,
    Editor2DLayer? Layer = null,
    Editor2DLayerFolder? Folder = null)
{
    public bool IsLayer => !IsFolder;

    public string HierarchyGuide => Depth == 0
        ? string.Empty
        : string.Concat(Enumerable.Repeat("│  ", Math.Max(0, Depth - 1))) + "└─";

    public string ExpansionGlyph => IsExpanded ? "▾" : "▸";
}
