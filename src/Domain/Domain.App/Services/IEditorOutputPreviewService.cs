using Domain.App.Models;

namespace Domain.App.Services;

public interface IEditorOutputPreviewService
{
    Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default);

    Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default);

    Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default);
}
