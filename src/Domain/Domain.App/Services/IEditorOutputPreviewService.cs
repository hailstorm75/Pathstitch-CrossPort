using Domain.App.Models;

namespace Domain.App.Services;

public interface IEditorOutputPreviewService
{
    Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default);

    Task<Editor2DImportUnitsInfo?> InspectImportUnitsAsync(string outputPath, CancellationToken cancellationToken = default)
        => Task.FromResult<Editor2DImportUnitsInfo?>(null);

    Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default);

    Task SavePreviewDocumentAsync(
        Editor2DPreviewDocument document,
        string outputPath,
        Editor2DExportOptions options,
        CancellationToken cancellationToken = default)
        => SavePreviewDocumentAsync(document, outputPath, cancellationToken);

    Task SaveExportDocumentAsync(
        Editor2DExportDocument document,
        string outputPath,
        Editor2DExportOptions options,
        CancellationToken cancellationToken = default)
        => SavePreviewDocumentAsync(document.Geometry, outputPath, options, cancellationToken);

    Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default);
}
