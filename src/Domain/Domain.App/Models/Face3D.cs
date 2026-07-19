using System.Globalization;
using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record Face3D(
    [property: JsonPropertyName("face_index")] int FaceIndex,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("area")] double Area)
{
    [JsonIgnore]
    public string Id => FaceIndex.ToString(CultureInfo.InvariantCulture);

    [JsonIgnore]
    public int BodyIndex { get; init; }

    [JsonIgnore]
    public string FaceKey => $"{BodyIndex}:{FaceIndex}";

    [JsonIgnore]
    public bool IsSelected { get; init; }
}
