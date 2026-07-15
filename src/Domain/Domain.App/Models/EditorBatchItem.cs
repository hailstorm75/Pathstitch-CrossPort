using CommunityToolkit.Mvvm.ComponentModel;

namespace Domain.App.Models;

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

public sealed class EditorBatchItem(string filePath) : ObservableObject
{
    private EditorBatchItemStatus _status;
    private string _message = "Ready";
    private string? _outputPath;
    private Editor2DPreviewDocument? _document;
    private bool _isSelected = true;

    public string FilePath { get; } = Path.GetFullPath(filePath);

    public string FileName => Path.GetFileName(FilePath);

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
}
