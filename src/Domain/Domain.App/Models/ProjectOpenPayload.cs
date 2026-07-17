namespace Domain.App.Models;

public sealed record ProjectOpenPayload(
    string? ProjectName,
    string? TemplateId,
    Project3DState State);
