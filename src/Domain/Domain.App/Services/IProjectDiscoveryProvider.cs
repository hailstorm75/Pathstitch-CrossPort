using System.Runtime.CompilerServices;

namespace Domain.App.Services;

public sealed record DiscoveredProject(
    string ProjectFilePath,
    DateTimeOffset LastModifiedAtUtc);

public interface IProjectDiscoveryProvider
{
    Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(
        CancellationToken cancellationToken = default);

    async IAsyncEnumerable<IReadOnlyList<DiscoveredProject>> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await DiscoverAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EmptyProjectDiscoveryProvider : IProjectDiscoveryProvider
{
    public Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DiscoveredProject>>([]);
    }
}