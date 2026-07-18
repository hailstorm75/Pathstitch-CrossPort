namespace Domain.App.Models;

public sealed record Editor2DExportPathMetadata(
    string LayerName,
    string ColorHex,
    int Order = 0);

public sealed record Editor2DExportDocument(
    Editor2DPreviewDocument Geometry,
    IReadOnlyDictionary<string, Editor2DExportPathMetadata> PathMetadata);


public sealed record EditorDxfEntityProvenance(
    string Handle,
    string LayerName);

public sealed record EditorDxfMergeResult(
    bool Succeeded,
    IReadOnlyDictionary<string, EditorDxfEntityProvenance> ProvenanceByPathId,
    byte[]? OutputData = null,
    IReadOnlyDictionary<string, Editor2DPreviewPath>? CanonicalPathsByPathId = null,
    string? FailureCode = null)
{
    public static EditorDxfMergeResult NotSupported { get; } = new(
        false,
        new Dictionary<string, EditorDxfEntityProvenance>(),
        FailureCode: "not-supported");

    public static EditorDxfMergeResult Success(
        IReadOnlyDictionary<string, EditorDxfEntityProvenance> provenanceByPathId,
        byte[] outputData,
        IReadOnlyDictionary<string, Editor2DPreviewPath>? canonicalPathsByPathId = null)
        => new(true, provenanceByPathId, outputData, canonicalPathsByPathId);
}
