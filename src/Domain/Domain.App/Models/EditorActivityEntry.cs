using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record EditorActivityEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("details")] string Details,
    [property: JsonPropertyName("layerId")] string? LayerId = null)
{
    [JsonIgnore]
    public string TimestampText => TimestampUtc.ToLocalTime().ToString("t");

    [JsonIgnore]
    public string AccessibilitySummary => string.IsNullOrWhiteSpace(LayerId)
        ? $"{TimestampText}, {Action}, {Details}"
        : $"{TimestampText}, {Action}, {Details}, layer {LayerId}";
}
