using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record Editor2DImportGroup(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sourceFilePath")] string SourceFilePath,
    [property: JsonPropertyName("appliedUnitScale")] double AppliedUnitScale,
    [property: JsonPropertyName("generatedPathIds")] IReadOnlyList<string> GeneratedPathIds,
    [property: JsonPropertyName("owningLayerId")] string OwningLayerId,
    [property: JsonPropertyName("owningLayerPathIndex")] int OwningLayerPathIndex,
    [property: JsonPropertyName("documentPathIndex")] int DocumentPathIndex,
    [property: JsonPropertyName("unsupportedEntityTypes")] IReadOnlyList<string>? UnsupportedEntityTypes = null,
    [property: JsonPropertyName("generatedLayerIds")] IReadOnlyList<string>? GeneratedLayerIds = null);

public sealed record Editor2DImportedDrawing(
    string SourceFilePath,
    double AppliedUnitScale,
    Editor2DPreviewDocument Document);
