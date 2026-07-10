namespace Domain.App.Models;

public sealed record ProjectLaunchRequest(
    ProjectSession Session,
    IReadOnlyList<string> PendingSourceModelPaths)
{
    public static ProjectLaunchRequest ForProject(ProjectSession session)
        => new(session, []);
}
