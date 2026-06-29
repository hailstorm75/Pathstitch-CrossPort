using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record EditorWorkspaceState(
    [property: JsonPropertyName("activeTool")] Editor3DTool ActiveTool,
    [property: JsonPropertyName("threeDOrthographic")] bool ThreeDOrthographic,
    [property: JsonPropertyName("showGeneratedOutputWorkspace")] bool ShowGeneratedOutputWorkspace,
    [property: JsonPropertyName("generatedOutputActiveTool")] Editor2DTool GeneratedOutputActiveTool = Editor2DTool.Select,
    [property: JsonPropertyName("generatedOutputPolygonSides")] int GeneratedOutputPolygonSides = 6,
    [property: JsonPropertyName("generatedOutputViewportZoom")] double GeneratedOutputViewportZoom = 0.0,
    [property: JsonPropertyName("generatedOutputViewportOffsetX")] double GeneratedOutputViewportOffsetX = 0.0,
    [property: JsonPropertyName("generatedOutputViewportOffsetY")] double GeneratedOutputViewportOffsetY = 0.0,
    [property: JsonPropertyName("generatedOutputExpandedRectanglePathIds")] IReadOnlyList<string>? GeneratedOutputExpandedRectanglePathIds = null);
