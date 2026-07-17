using Domain.App.Models;

namespace Domain.App.Services;

public interface IPsdImportService
{
    Task<PsdImportData> ParseAsync(string sourcePath, CancellationToken cancellationToken = default);
}

public interface IPsdImportModePromptService
{
    Task<PsdImportMode?> PromptAsync(PsdImportData import, CancellationToken cancellationToken = default);
}
