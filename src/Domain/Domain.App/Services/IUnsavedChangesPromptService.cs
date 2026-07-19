namespace Domain.App.Services;

public enum UnsavedChangesPromptResult
{
    Save,
    Discard,
    Cancel,
}

public interface IUnsavedChangesPromptService
{
    Task<UnsavedChangesPromptResult> PromptToSaveAsync(
        string documentName,
        CancellationToken cancellationToken = default);
}
