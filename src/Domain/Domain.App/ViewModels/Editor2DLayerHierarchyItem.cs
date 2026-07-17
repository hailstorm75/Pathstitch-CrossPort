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

    public double IndentWidth => Depth * 14.0;

    public string ExpansionGlyph => IsExpanded ? "▾" : "▸";
}
