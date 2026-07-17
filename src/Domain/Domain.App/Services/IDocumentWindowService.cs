using Domain.App.Models;

namespace Domain.App.Services;

public interface IDocumentWindowService
{
    Task OpenDocumentAsync(
        ProjectLaunchRequest launchRequest,
        CancellationToken cancellationToken = default);

    Task CloseCurrentDocumentAsync(CancellationToken cancellationToken = default);
}
