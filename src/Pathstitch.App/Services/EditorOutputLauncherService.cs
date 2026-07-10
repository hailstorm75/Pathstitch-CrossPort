using System.Threading;
using System.Threading.Tasks;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class EditorOutputLauncherService(IFileIntegrationService fileIntegrationService)
    : IEditorOutputLauncherService
{
    public Task OpenOutputAsync(string outputPath, CancellationToken cancellationToken = default)
        => fileIntegrationService.OpenFileAsync(outputPath, cancellationToken);

    public Task RevealOutputAsync(string outputPath, CancellationToken cancellationToken = default)
        => fileIntegrationService.RevealFileAsync(outputPath, cancellationToken);
}
