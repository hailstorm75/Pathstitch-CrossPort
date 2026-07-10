namespace Domain.App.Services;

/// <summary>
/// Opens files with their associated application and reveals them in the host
/// platform's file manager without exposing platform-specific commands.
/// </summary>
public interface IFileIntegrationService
{
    Task OpenFileAsync(string filePath, CancellationToken cancellationToken = default);

    Task RevealFileAsync(string filePath, CancellationToken cancellationToken = default);
}
