namespace Domain.App.Services;

public enum ProjectOpenDisposition
{
    Combine,
    NewWindow,
    Cancel,
}

public interface IProjectOpenDispositionPromptService
{
    Task<ProjectOpenDisposition> PromptAsync(
        string incomingProjectName,
        CancellationToken cancellationToken = default);
}
