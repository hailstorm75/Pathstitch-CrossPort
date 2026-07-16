using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record Editor2DConvertLineSource(
    [property: JsonPropertyName("sourcePath")] Editor2DPreviewPath SourcePath,
    [property: JsonPropertyName("sourceLayerId")] string? SourceLayerId,
    [property: JsonPropertyName("sourceLayerPathIndex")] int SourceLayerPathIndex,
    [property: JsonPropertyName("sourceDocumentPathIndex")] int SourceDocumentPathIndex,
    [property: JsonPropertyName("generatedPathIds")] IReadOnlyList<string> GeneratedPathIds);

public sealed record Editor2DConvertLineGroup(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("style")] string Style,
    [property: JsonPropertyName("settings")] IReadOnlyDictionary<string, double> Settings,
    [property: JsonPropertyName("sources")] IReadOnlyList<Editor2DConvertLineSource> Sources)
{
    public IReadOnlyList<string> GeneratedPathIds
        => Sources.SelectMany(source => source.GeneratedPathIds).ToArray();
}
