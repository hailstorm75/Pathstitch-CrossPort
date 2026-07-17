using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
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
    private bool _exportSelectedOnly;
    private EditorBatchExportFormat _selectedExportFormat = EditorBatchExportFormat.Dxf;

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

    public bool ExportSelectedOnly
    {
        get => _exportSelectedOnly;
        set
        {
            if (SetProperty(ref _exportSelectedOnly, value))
                OnPropertyChanged(nameof(CanExport));
        }
    }

    public IReadOnlyList<EditorBatchExportFormat> ExportFormats { get; } = Enum.GetValues<EditorBatchExportFormat>();

    public EditorBatchExportFormat SelectedExportFormat
    {
        get => _selectedExportFormat;
        set => SetProperty(ref _selectedExportFormat, value);
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
                OnPropertyChanged(nameof(CanOperateSelected));
            }
        }
    }

    public bool CanRun => !IsRunning && Items.Count > 0;

    public bool CanExport => !IsRunning && Items.Any(item =>
        (!ExportSelectedOnly || item.IsSelected) && IsSupportedDrawingInput(item.FilePath));

    public bool CanOperateSelected => !IsRunning && Items.Any(item =>
        item.IsSelected && IsSupportedDrawingInput(item.FilePath));

    public int SelectedItemCount => Items.Count(item => item.IsSelected);

    public void SetAllSelected(bool selected)
    {
        foreach (var item in Items)
            item.IsSelected = selected;
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanOperateSelected));
        OnPropertyChanged(nameof(SelectedItemCount));
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool AddFile(string path)
        => AddFiles([path]) == 1;

    public int AddFiles(IReadOnlyList<string> paths)
    {
        var existing = Items.Select(item => item.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accepted = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim().Trim('"')))
            .Where(path => Path.GetExtension(path).Equals(".stch", StringComparison.OrdinalIgnoreCase)
                           || IsSupportedDrawingInput(path))
            .Where(existing.Add)
            .ToArray();
        if (accepted.Length == 0)
            return 0;

        foreach (var fullPath in accepted)
        {
            var item = new EditorBatchItem(fullPath);
            item.PropertyChanged += OnItemPropertyChanged;
            Items.Add(item);
        }
        Summary = $"{Items.Count} file(s) queued.";
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanOperateSelected));
        OnPropertyChanged(nameof(SelectedItemCount));
        return accepted.Length;
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

        item.PropertyChanged -= OnItemPropertyChanged;
        Items.Remove(item);
        Summary = Items.Count == 0 ? "No files queued." : $"{Items.Count} file(s) queued.";
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanOperateSelected));
        OnPropertyChanged(nameof(SelectedItemCount));
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
                    item.Message = IsSupportedDrawingInput(item.FilePath)
                        ? $"Valid {Path.GetExtension(item.FilePath).TrimStart('.').ToUpperInvariant()} input"
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
        var firstDrawing = Items.First(item => (!ExportSelectedOnly || item.IsSelected)
            && IsSupportedDrawingInput(item.FilePath));
        var outputDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(firstDrawing.FilePath)!, "batch-output")
            : Path.GetFullPath(OutputDirectory.Trim().Trim('"'));
        Directory.CreateDirectory(outputDirectory);
        var succeeded = 0;
        var failed = 0;
        try
        {
            foreach (var item in Items.Where(item => (!ExportSelectedOnly || item.IsSelected)
                && IsSupportedDrawingInput(item.FilePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Status = EditorBatchItemStatus.Running;
                item.Message = $"Exporting {SelectedExportFormat}";
                try
                {
                    var document = item.Document
                        ?? await outputPreviewService.LoadPreviewDocumentAsync(item.FilePath, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("Drawing preview could not be loaded.");
                    var extension = SelectedExportFormat switch
                    {
                        EditorBatchExportFormat.Svg => ".svg",
                        EditorBatchExportFormat.Pdf => ".pdf",
                        EditorBatchExportFormat.Png => ".png",
                        _ => ".dxf",
                    };
                    var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(item.FilePath)}-batch{extension}");
                    await outputPreviewService.SavePreviewDocumentAsync(
                        document,
                        outputPath,
                        Editor2DExportOptions.Defaults,
                        cancellationToken).ConfigureAwait(false);
                    item.OutputPath = outputPath;
                    item.Status = EditorBatchItemStatus.Succeeded;
                    item.Message = $"{SelectedExportFormat} exported";
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
            Summary = $"Batch export complete: {succeeded} succeeded, {failed} failed.";
        }
    }

    public async Task ApplyOffsetAsync(
        IEditorOutputPreviewService outputPreviewService,
        IEditor2DGeometryKernelService geometryKernel,
        double distance,
        CancellationToken cancellationToken = default)
    {
        if (!CanOperateSelected || distance <= 0)
            return;

        IsRunning = true;
        var succeeded = 0;
        var failed = 0;
        try
        {
            foreach (var item in Items.Where(item => item.IsSelected
                && IsSupportedDrawingInput(item.FilePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Status = EditorBatchItemStatus.Running;
                item.Message = "Applying offset";
                try
                {
                    var source = item.Document
                        ?? await outputPreviewService.LoadPreviewDocumentAsync(item.FilePath, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("Drawing preview could not be loaded.");
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

    public async Task ApplySewingHolesAsync(
        IEditorOutputPreviewService outputPreviewService,
        Editor2DSewingHoleParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (!CanOperateSelected)
            return;

        IsRunning = true;
        var succeeded = 0;
        var failed = 0;
        try
        {
            foreach (var item in Items.Where(item => item.IsSelected
                && IsSupportedDrawingInput(item.FilePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Status = EditorBatchItemStatus.Running;
                item.Message = "Applying sewing holes";
                try
                {
                    var source = item.Document
                        ?? await outputPreviewService.LoadPreviewDocumentAsync(item.FilePath, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("Drawing preview could not be loaded.");
                    var holes = Editor2DSewingHoleGeometry.BuildPreview(
                        source,
                        source.Paths.Select(path => path.Id).ToArray(),
                        parameters,
                        $"batch-{Guid.NewGuid():N}");
                    if (holes.Count == 0)
                        throw new InvalidDataException("No sewing-hole placements were generated.");

                    var paths = source.Paths.Concat(holes).ToArray();
                    item.Document = source with
                    {
                        Paths = paths,
                        EntityCounts = CountEntities(paths),
                    };
                    item.Status = EditorBatchItemStatus.Succeeded;
                    item.Message = $"{holes.Count} sewing holes applied";
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
            Summary = $"Batch sewing holes complete: {succeeded} succeeded, {failed} failed.";
        }
    }

    private static IReadOnlyDictionary<string, int> CountEntities(IReadOnlyList<Editor2DPreviewPath> paths)
        => paths.GroupBy(path => path.EntityType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static async Task ValidateInputAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Batch input file was not found.", path);

        if (IsSupportedDrawingInput(path))
        {
            var info = new FileInfo(path);
            if (info.Length == 0)
                throw new InvalidDataException("Drawing input is empty.");
            if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                await using var svgStream = File.OpenRead(path);
                try
                {
                    var svg = await XDocument.LoadAsync(svgStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(svg.Root?.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SVG input does not contain an svg root element.");
                }
                catch (XmlException exception)
                {
                    throw new InvalidDataException("SVG input is malformed.", exception);
                }
            }
            else if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var header = new byte[5];
                await using var pdfStream = File.OpenRead(path);
                if (await pdfStream.ReadAsync(header, cancellationToken).ConfigureAwait(false) != header.Length
                    || !header.AsSpan().SequenceEqual("%PDF-"u8))
                {
                    throw new InvalidDataException("PDF input is malformed.");
                }
            }
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

    private static bool IsSupportedDrawingInput(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorBatchItem.IsSelected))
            return;
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanOperateSelected));
        OnPropertyChanged(nameof(SelectedItemCount));
    }
}
