namespace Domain.App.Models;

public sealed record EditorGeneratedOutputSummary(
    string OutputPath,
    bool FileExists,
    long FileSizeBytes,
    DateTimeOffset? LastModifiedUtc,
    int PreviewPathCount,
    int ClosedPathCount,
    int OpenPathCount,
    int LineEntityCount,
    int PolylineEntityCount,
    int ArcEntityCount,
    int CircleEntityCount,
    int EllipseEntityCount,
    int TextEntityCount,
    int UnsupportedEntityCount,
    string UnsupportedEntityTypes,
    double Width,
    double Height);
