using System.Collections.ObjectModel;
using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed class EditorBatchWorkspaceViewModel : ObservableObject
{
    private string _inputPath = string.Empty;
    private bool _continueOnError = true;
    private bool _isRunning;
    private string _summary = "No projects queued.";

    public ObservableCollection<EditorBatchItem> Items { get; } = [];

    public IReadOnlyList<EditorBatchAction> AvailableActions { get; } = Enum.GetValues<EditorBatchAction>();

    public EditorBatchAction SelectedAction { get; set; } = EditorBatchAction.ValidateProjects;

    public string InputPath
    {
        get => _inputPath;
        set => SetProperty(ref _inputPath, value ?? string.Empty);
    }

    public bool ContinueOnError
    {
        get => _continueOnError;
        set => SetProperty(ref _continueOnError, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
                OnPropertyChanged(nameof(CanRun));
        }
    }

    public bool CanRun => !IsRunning && Items.Count > 0;

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool AddProject(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        if (!Path.GetExtension(fullPath).Equals(".stch", StringComparison.OrdinalIgnoreCase)
            || Items.Any(item => string.Equals(item.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Items.Add(new EditorBatchItem(fullPath));
        Summary = $"{Items.Count} project(s) queued.";
        OnPropertyChanged(nameof(CanRun));
        return true;
    }

    public bool AddInputProject()
    {
        if (!AddProject(InputPath))
            return false;

        InputPath = string.Empty;
        return true;
    }

    public void RemoveProject(string filePath)
    {
        var item = Items.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        Items.Remove(item);
        Summary = Items.Count == 0 ? "No projects queued." : $"{Items.Count} project(s) queued.";
        OnPropertyChanged(nameof(CanRun));
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRun)
            return;

        IsRunning = true;
        var succeeded = 0;
        var failed = 0;
        try
        {
            foreach (var item in Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Status = EditorBatchItemStatus.Running;
                item.Message = "Validating project";

                try
                {
                    await ValidateProjectAsync(item.FilePath, cancellationToken).ConfigureAwait(false);
                    item.Status = EditorBatchItemStatus.Succeeded;
                    item.Message = "Valid Pathstitch project";
                    succeeded++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    item.Status = EditorBatchItemStatus.Failed;
                    item.Message = exception.Message;
                    failed++;
                    if (!ContinueOnError)
                        break;
                }
            }
        }
        finally
        {
            IsRunning = false;
            Summary = $"Batch complete: {succeeded} succeeded, {failed} failed.";
        }
    }

    private static async Task ValidateProjectAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Project file was not found.", path);

        await using var stream = File.OpenRead(path);
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.GetEntry("project.json") is null)
                throw new InvalidDataException("Archive does not contain project.json.");
        }
        catch (InvalidDataException)
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, leaveOpen: true);
            var firstCharacter = new char[1];
            if (await reader.ReadAsync(firstCharacter.AsMemory(), cancellationToken).ConfigureAwait(false) == 0
                || firstCharacter[0] != '{')
            {
                throw new InvalidDataException("File is not a Pathstitch project archive or JSON seed.");
            }
        }
    }
}
