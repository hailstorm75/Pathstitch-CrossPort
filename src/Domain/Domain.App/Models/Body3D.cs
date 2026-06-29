using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record Body3D(
    [property: JsonPropertyName("body_index")] int BodyIndex,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("faces")] IReadOnlyList<Face3D> Faces)
{
    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonIgnore]
    public string Id => Name;

    [JsonIgnore]
    public string VisibilityLabel => Visible ? "On" : "Off";

    [JsonIgnore]
    public bool IsSelected { get; init; }
}
