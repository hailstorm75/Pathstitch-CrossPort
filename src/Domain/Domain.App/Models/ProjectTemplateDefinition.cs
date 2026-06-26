namespace Domain.App.Models;

public sealed record ProjectTemplateDefinition(
    string TemplateId,
    string DisplayName,
    string DefaultProjectName,
    string? Description = null);
