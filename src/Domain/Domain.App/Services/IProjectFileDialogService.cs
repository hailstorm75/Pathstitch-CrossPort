namespace Domain.App.Services;

public interface IProjectFileDialogService
{
    Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default);

    Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default);

    Task<string?> PickProjectSaveAsFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
        => PickNewProjectFileAsync(suggestedFileName, cancellationToken);

    Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default);

    Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default);

    Task<string?> PickReferenceImageFileAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    Task<string?> PickDxfExportFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    Task<string?> PickSvgExportFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    Task<string?> PickPngExportFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    Task<string?> PickPdfExportFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
