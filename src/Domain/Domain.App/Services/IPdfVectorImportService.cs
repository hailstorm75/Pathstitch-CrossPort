namespace Domain.App.Services;

public interface IPdfVectorImportService
{
    Task<string> ConvertToDxfAsync(string sourcePath, CancellationToken cancellationToken = default);
}
