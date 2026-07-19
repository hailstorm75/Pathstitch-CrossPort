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

    Task<EditorDxfMergeResult> TrySaveMergedDxfDocumentAsync(
        ReadOnlyMemory<byte> sourceData,
        Editor2DExportDocument document,
        string outputPath,
        Editor2DExportOptions options,
        CancellationToken cancellationToken = default)
        => Task.FromResult(EditorDxfMergeResult.NotSupported);

    Task CopyDxfPreservingStructureAsync(
        string sourcePath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.Copy(sourcePath, outputPath, overwrite: true);
        return Task.CompletedTask;
    }

    async Task CopyDxfPreservingStructureAsync(
        ReadOnlyMemory<byte> sourceData,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"pathstitch-preserved-{Guid.NewGuid():N}.dxf");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, sourceData.ToArray(), cancellationToken).ConfigureAwait(false);
            await CopyDxfPreservingStructureAsync(temporaryPath, outputPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
    Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default);
}
