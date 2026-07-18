using System.Text.Json.Serialization;

namespace Domain.App.Models;

public enum EditorToolbarContainer
{
    Main,
    Shapes,
    More,
}

public sealed record EditorToolCustomization(
    [property: JsonPropertyName("identifier")] string Identifier,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("shortcutText")] string? ShortcutText,
    [property: JsonPropertyName("container")] EditorToolbarContainer? Container = null);

public sealed record EditorWorkspaceState(
    [property: JsonPropertyName("activeTool")] Editor3DTool ActiveTool,
    [property: JsonPropertyName("threeDOrthographic")] bool ThreeDOrthographic,
    [property: JsonPropertyName("showGeneratedOutputWorkspace")] bool ShowTwoDWorkspace,
    [property: JsonPropertyName("generatedOutputActiveTool")] Editor2DTool TwoDActiveTool = Editor2DTool.Select,
    [property: JsonPropertyName("generatedOutputPolygonSides")] int TwoDPolygonSides = 6,
    [property: JsonPropertyName("generatedOutputViewportZoom")] double TwoDViewportZoom = 0.0,
    [property: JsonPropertyName("generatedOutputViewportOffsetX")] double TwoDViewportOffsetX = 0.0,
    [property: JsonPropertyName("generatedOutputViewportOffsetY")] double TwoDViewportOffsetY = 0.0,
    [property: JsonPropertyName("generatedOutputExpandedRectanglePathIds")] IReadOnlyList<string>? TwoDExpandedRectanglePathIds = null,
    [property: JsonPropertyName("activeEditorMode")] EditorMode? ActiveEditorMode = null,
    [property: JsonPropertyName("toolCustomizations")] IReadOnlyList<EditorToolCustomization>? ToolCustomizations = null);
