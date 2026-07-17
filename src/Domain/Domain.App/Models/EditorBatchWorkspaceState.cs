namespace Domain.App.Models;

public enum EditorBatchNamingOption
{
    Original,
    CustomIndex,
}

public sealed record EditorBatchWorkspaceState(
    IReadOnlyList<EditorBatchItemState>? Items = null,
    bool ContinueOnError = true,
    string? OutputDirectory = null,
    bool ExportSelectedOnly = false,
    EditorBatchExportFormat SelectedExportFormat = EditorBatchExportFormat.Dxf,
    EditorBatchAction SelectedAction = EditorBatchAction.ValidateProjects,
    EditorBatchNamingOption SelectedNamingOption = EditorBatchNamingOption.Original,
    string CustomExportName = "BatchExport");

public sealed record EditorBatchItemState(
    string FileName,
    string? OriginalSourcePath = null,
    string? SourceDataBase64 = null,
    Editor2DPreviewDocument? Document = null,
    bool IsSelected = true);
