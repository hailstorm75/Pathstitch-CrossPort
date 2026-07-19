namespace Domain.App.Models;

public sealed record RecentProjectSummary(
    string ProjectName,
    string ProjectFilePath,
    string TemplateId,
    string TemplateDisplayName,
    DateTimeOffset LastOpenedAtUtc,
    DateTimeOffset? LastModifiedAtUtc,
    bool IsAvailable,
    bool IsSelected = false)
{
    public string? ThumbnailDataBase64 { get; init; }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailDataBase64);

    public string ProjectFileName => Path.GetFileName(ProjectFilePath);

    public string ProjectDirectory => Path.GetDirectoryName(ProjectFilePath) ?? string.Empty;

    public string LastOpenedLabel => LastOpenedAtUtc.ToLocalTime().ToString("g");

    public string LastModifiedLabel => LastModifiedAtUtc?.ToLocalTime().ToString("g") ?? "Unavailable";

    public bool IsMissing => !IsAvailable;
}
