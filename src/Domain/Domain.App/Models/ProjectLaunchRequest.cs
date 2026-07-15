namespace Domain.App.Models;

public sealed record ProjectLaunchRequest(
    ProjectSession Session,
    IReadOnlyList<string> PendingSourceModelPaths)
{
    public IReadOnlyList<string> PendingTwoDFilePaths { get; init; } = [];
    public IReadOnlyList<string> PendingReferenceImagePaths { get; init; } = [];

    public static ProjectLaunchRequest ForProject(ProjectSession session)
        => new(session, []);
}
