namespace Domain.App.Models;

public sealed record ProjectSession(
    Guid SessionId,
    string ProjectName,
    string ProjectFilePath,
    ProjectTemplateDefinition Template,
    ProjectSessionOrigin Origin,
    DateTimeOffset CreatedAtUtc,
    bool TrackInRecentProjects = true);
