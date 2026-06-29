using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record EditorProjectionWorkspaceState(
    [property: JsonPropertyName("planeSelectionModeType")] string PlaneSelectionModeType,
    [property: JsonPropertyName("selectedProjectionPlane")] string? SelectedProjectionPlane,
    [property: JsonPropertyName("selectedProjectionFaceIndex")] int? SelectedProjectionFaceIndex,
    [property: JsonPropertyName("selectedProjectionBodyIndex")] int? SelectedProjectionBodyIndex,
    [property: JsonPropertyName("planeOffset")] double PlaneOffset);
