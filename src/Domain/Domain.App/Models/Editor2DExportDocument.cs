namespace Domain.App.Models;

public sealed record Editor2DExportPathMetadata(
    string LayerName,
    string ColorHex,
    int Order = 0);

public sealed record Editor2DExportDocument(
    Editor2DPreviewDocument Geometry,
    IReadOnlyDictionary<string, Editor2DExportPathMetadata> PathMetadata);
