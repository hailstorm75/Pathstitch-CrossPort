using CommunityToolkit.Mvvm.ComponentModel;

namespace Domain.App.Models;

public enum EditorBatchExportFormat
{
    Dxf,
    Svg,
    Pdf,
    Png,
}

public enum EditorBatchAction
{
    ValidateProjects = 0,
}

public enum EditorBatchItemStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

public sealed class EditorBatchItem(
    string filePath,
    string? originalSourcePath = null,
    string? displayFileName = null,
    string? id = null) : ObservableObject
{
    private EditorBatchItemStatus _status;
    private string _message = "Ready";
    private string? _outputPath;
    private Editor2DPreviewDocument? _document;
    private bool _isSelected = true;
    private bool _isDocumentModified;

    public string FilePath { get; } = Path.GetFullPath(filePath);

    public string Id { get; } = string.IsNullOrWhiteSpace(id)
        ? Guid.NewGuid().ToString("N")
        : id.Trim();

    public string OriginalSourcePath { get; } = string.IsNullOrWhiteSpace(originalSourcePath)
        ? Path.GetFullPath(filePath)
        : Path.GetFullPath(originalSourcePath);

    public string FileName { get; } = string.IsNullOrWhiteSpace(displayFileName)
        ? Path.GetFileName(filePath)
        : Path.GetFileName(displayFileName);

    public EditorBatchItemStatus Status
    {
        get => _status;
        internal set => SetProperty(ref _status, value);
    }

    public string Message
    {
        get => _message;
        internal set => SetProperty(ref _message, value);
    }

    public string? OutputPath
    {
        get => _outputPath;
        internal set => SetProperty(ref _outputPath, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public Editor2DPreviewDocument? Document
    {
        get => _document;
        internal set => SetProperty(ref _document, value);
    }

    public bool IsDocumentModified
    {
        get => _isDocumentModified;
        internal set => SetProperty(ref _isDocumentModified, value);
    }
}
