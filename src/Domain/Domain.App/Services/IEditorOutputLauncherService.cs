namespace Domain.App.Services;

public interface IEditorOutputLauncherService
{
    Task OpenOutputAsync(string outputPath, CancellationToken cancellationToken = default);

    Task RevealOutputAsync(string outputPath, CancellationToken cancellationToken = default);
}
