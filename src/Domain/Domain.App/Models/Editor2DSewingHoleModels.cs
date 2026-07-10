using System.Text.Json.Serialization;

namespace Domain.App.Models;

public enum Editor2DSewingCornerMode
{
    Continuous,
    IncludeCorners,
    AvoidCorners,
}

public sealed record Editor2DSewingHoleParameters(
    [property: JsonPropertyName("diameter")] double Diameter = 1.0,
    [property: JsonPropertyName("pitch")] double Pitch = 4.0,
    [property: JsonPropertyName("margin")] double Margin = 2.0,
    [property: JsonPropertyName("cornerMode")] Editor2DSewingCornerMode CornerMode = Editor2DSewingCornerMode.IncludeCorners,
    [property: JsonPropertyName("cornerClearance")] double CornerClearance = 2.0,
    [property: JsonPropertyName("avoidanceEnabled")] bool AvoidanceEnabled = false,
    [property: JsonPropertyName("avoidanceClearance")] double AvoidanceClearance = 3.0,
    [property: JsonPropertyName("avoidPathIds")] IReadOnlyList<string>? AvoidPathIds = null,
    [property: JsonPropertyName("symmetricDistribution")] bool SymmetricDistribution = true)
{
    public static Editor2DSewingHoleParameters Default { get; } = new();
}

public sealed record Editor2DSewingHoleOperation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sourcePathIds")] IReadOnlyList<string> SourcePathIds,
    [property: JsonPropertyName("generatedPathIds")] IReadOnlyList<string> GeneratedPathIds,
    [property: JsonPropertyName("parameters")] Editor2DSewingHoleParameters Parameters)
{
    public string DisplayName => $"Sewing holes ({GeneratedPathIds.Count})";
}
