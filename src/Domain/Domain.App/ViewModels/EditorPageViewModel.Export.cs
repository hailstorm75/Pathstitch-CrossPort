using Domain.App.Models;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private bool _twoDExportSelectedOnly;

    public bool CanExportTwoDDxf => TwoDDocument is not null;

    public bool CanExportTwoDSvg => TwoDDocument is not null;

    public bool TwoDExportSelectedOnly
    {
        get => _twoDExportSelectedOnly;
        set
        {
            if (_twoDExportSelectedOnly == value)
                return;

            _twoDExportSelectedOnly = value;
            OnPropertyChanged();
        }
    }

    public async Task ExportTwoDDxfAsync(CancellationToken cancellationToken = default)
    {
        var document = TwoDDocument;
        if (document is null)
            return;

        try
        {
            ErrorMessage = null;
            var suggestedFileName = string.IsNullOrWhiteSpace(ProjectName)
                ? "Pathstitch Export.dxf"
                : $"{ProjectName}.dxf";
            var outputPath = await _projectFileDialogService
                .PickDxfExportFileAsync(suggestedFileName, cancellationToken)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            await _editorOutputPreviewService
                .SavePreviewDocumentAsync(BuildExportDocument(document), outputPath, cancellationToken)
                .ConfigureAwait(true);
            StatusText = $"Exported DXF to {Path.GetFileName(outputPath)}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to export 2D workspace DXF.");
            ErrorMessage = $"Could not export DXF: {exception.Message}";
        }
    }

    public async Task ExportTwoDSvgAsync(CancellationToken cancellationToken = default)
    {
        var document = TwoDDocument;
        if (document is null)
            return;

        try
        {
            ErrorMessage = null;
            var suggestedFileName = string.IsNullOrWhiteSpace(ProjectName)
                ? "Pathstitch Export.svg"
                : $"{ProjectName}.svg";
            var outputPath = await _projectFileDialogService
                .PickSvgExportFileAsync(suggestedFileName, cancellationToken)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            await _editorOutputPreviewService
                .SavePreviewDocumentAsync(BuildExportDocument(document), outputPath, cancellationToken)
                .ConfigureAwait(true);
            StatusText = $"Exported SVG to {Path.GetFileName(outputPath)}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to export 2D workspace SVG.");
            ErrorMessage = $"Could not export SVG: {exception.Message}";
        }
    }

    private Editor2DPreviewDocument BuildExportDocument(Editor2DPreviewDocument document)
    {
        if (!TwoDExportSelectedOnly || TwoDSelectedPathIds.Count == 0)
            return document;

        return CreateUpdatedTwoDDocument(document, GetSelectedTwoDPaths());
    }
}
