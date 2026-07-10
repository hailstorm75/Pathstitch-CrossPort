namespace Domain.App.Models;

public sealed record EditorGeneratedOutputContext(
    string SourceTool,
    string TriggerLabel,
    string ScopeSummary,
    string ConfigurationSummary,
    DateTimeOffset CreatedUtc);
