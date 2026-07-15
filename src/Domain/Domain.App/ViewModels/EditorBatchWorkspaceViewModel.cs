using System.Collections.ObjectModel;
using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using Domain.App.Models;
using Domain.App.Services;

namespace Domain.App.ViewModels;

public sealed class EditorBatchWorkspaceViewModel : ObservableObject
{
    private string _inputPath = string.Empty;
    private bool _continueOnError = true;
    private bool _isRunning;
    private string _summary = "No files queued.";
    private string _outputDirectory = string.Empty;

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

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value ?? string.Empty);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanRun));
                OnPropertyChanged(nameof(CanExport));
            }
        }
    }

    public bool CanRun => !IsRunning && Items.Count > 0;

    public bool CanExport => !IsRunning && Items.Any(item =>
        Path.GetExtension(item.FilePath).Equals(".dxf", StringComparison.OrdinalIgnoreCase));

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool AddFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        var extension = Path.GetExtension(fullPath);
        if (!(extension.Equals(".stch", StringComparison.OrdinalIgnoreCase)
              || extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
            || Items.Any(item => string.Equals(item.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Items.Add(new EditorBatchItem(fullPath));
        Summary = $"{Items.Count} file(s) queued.";
        OnPropertyChanged(nameof(CanRun));
        return true;
    }

    public bool AddProject(string path) => AddFile(path);

    public bool AddInputFile()
    {
        if (!AddFile(InputPath))
            return false;

        InputPath = string.Empty;
        return true;
    }

    public bool AddInputProject() => AddInputFile();

    public void RemoveProject(string filePath)
    {
        var item = Items.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        Items.Remove(item);
        Summary = Items.Count == 0 ? "No files queued." : $"{Items.Count} file(s) queued.";
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
                item.Message = "Validating input";

                try
                {
                    await ValidateInputAsync(item.FilePath, cancellationToken).ConfigureAwait(false);
                    item.Status = EditorBatchItemStatus.Succeeded;
                    item.Message = Path.GetExtension(item.FilePath).Equals(".dxf", StringComparison.OrdinalIgnoreCase)
                        ? "Valid DXF input"
                        : "Valid Pathstitch project";
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

    public async Task ExportDxfAsync(
        IEditorOutputPreviewService outputPreviewService,
        CancellationToken cancellationToken = default)
    {
        if (!CanExport)
            return;

        IsRunning = true;
        var firstDxf = Items.First(item => Path.GetExtension(item.FilePath).Equals(".dxf", StringComparison.OrdinalIgnoreCase));
        var outputDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(firstDxf.FilePath)!, "batch-output")
            : Path.GetFullPath(OutputDirectory.Trim().Trim('"'));
        Directory.CreateDirectory(outputDirectory);
        var succeeded = 0;
        var failed = 0;
        try
        {
            foreach (var item in Items.Where(item => Path.GetExtension(item.FilePath).Equals(".dxf", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Status = EditorBatchItemStatus.Running;
                item.Message = "Exporting DXF";
                try
                {
                    var document = item.Document
                        ?? await outputPreviewService.LoadPreviewDocumentAsync(item.FilePath, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("DXF preview could not be loaded.");
                    var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(item.FilePath)}-batch.dxf");
                    await outputPreviewService.SavePreviewDocumentAsync(document, outputPath, cancellationToken).ConfigureAwait(false);
                    item.OutputPath = outputPath;
                    item.Status = EditorBatchItemStatus.Succeeded;
                    item.Message = "DXF exported";
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
            Summary = $"DXF export complete: {succeeded} succeeded, {failed} failed.";
        }
    }

    public async Task ApplyOffsetAsync(
        IEditorOutputPreviewService outputPreviewService,
        IEditor2DGeometryKernelService geometryKernel,
        double distance,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning || distance <= 0)
            return;

        IsRunning = true;
        var succeeded = 0;
        var failed = 0;
        try
        {
            foreach (var item in Items.Where(item => Path.GetExtension(item.FilePath).Equals(".dxf", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Status = EditorBatchItemStatus.Running;
                item.Message = "Applying offset";
                try
                {
                    var source = item.Document
                        ?? await outputPreviewService.LoadPreviewDocumentAsync(item.FilePath, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("DXF preview could not be loaded.");
                    var result = await geometryKernel.BuildCurveOffsetPathsAsync(
                        source.Paths, distance, offsetOutward: true, cancellationToken).ConfigureAwait(false);
                    if (!result.IsSuccess || result.Paths.Count == 0)
                        throw new InvalidDataException(result.Error ?? "No offsettable paths were found.");

                    item.Document = source with
                    {
                        Paths = result.Paths,
                        EntityCounts = CountEntities(result.Paths),
                    };
                    item.Status = EditorBatchItemStatus.Succeeded;
                    item.Message = "Offset applied";
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
            Summary = $"Batch offset complete: {succeeded} succeeded, {failed} failed.";
        }
    }

    private static IReadOnlyDictionary<string, int> CountEntities(IReadOnlyList<Editor2DPreviewPath> paths)
        => paths.GroupBy(path => path.EntityType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static async Task ValidateInputAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Batch input file was not found.", path);

        if (Path.GetExtension(path).Equals(".dxf", StringComparison.OrdinalIgnoreCase))
        {
            var info = new FileInfo(path);
            if (info.Length == 0)
                throw new InvalidDataException("DXF input is empty.");
            return;
        }

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
