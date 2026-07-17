using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
    private EditorBatchAction _selectedAction = EditorBatchAction.ValidateProjects;
    private EditorBatchNamingOption _selectedNamingOption = EditorBatchNamingOption.Original;
    private string _customExportName = "BatchExport";
    private bool _suppressStateChanged;

    public event Action? StateChanged;

    public ObservableCollection<EditorBatchItem> Items { get; } = [];

    public IReadOnlyList<EditorBatchAction> AvailableActions { get; } = Enum.GetValues<EditorBatchAction>();

    public EditorBatchAction SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (SetProperty(ref _selectedAction, value))
                NotifyStateChanged();
        }
    }

    public string InputPath
    {
        get => _inputPath;
        set => SetProperty(ref _inputPath, value ?? string.Empty);
    }

    public bool ContinueOnError
    {
        get => _continueOnError;
        set
        {
            if (SetProperty(ref _continueOnError, value))
                NotifyStateChanged();
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value ?? string.Empty))
                NotifyStateChanged();
        }
    }

    public bool ExportSelectedOnly
    {
        get => _exportSelectedOnly;
        set
        {
            if (SetProperty(ref _exportSelectedOnly, value))
            {
                OnPropertyChanged(nameof(CanExport));
                NotifyStateChanged();
            }
        }
    }

    public IReadOnlyList<EditorBatchExportFormat> ExportFormats { get; } = Enum.GetValues<EditorBatchExportFormat>();

    public EditorBatchExportFormat SelectedExportFormat
    {
        get => _selectedExportFormat;
        set
        {
            if (SetProperty(ref _selectedExportFormat, value))
                NotifyStateChanged();
        }
    }

    public IReadOnlyList<string> NamingOptionLabels { get; } = ["Original Names", "Custom Name + Index"];

    public EditorBatchNamingOption SelectedNamingOption
    {
        get => _selectedNamingOption;
        set
        {
            if (SetProperty(ref _selectedNamingOption, value))
            {
                OnPropertyChanged(nameof(SelectedNamingOptionIndex));
                OnPropertyChanged(nameof(UsesCustomExportName));
                NotifyStateChanged();
            }
        }
    }

    public int SelectedNamingOptionIndex
    {
        get => (int)SelectedNamingOption;
        set
        {
            if (Enum.IsDefined(typeof(EditorBatchNamingOption), value))
                SelectedNamingOption = (EditorBatchNamingOption)value;
        }
    }

    public bool UsesCustomExportName => SelectedNamingOption == EditorBatchNamingOption.CustomIndex;

    public string CustomExportName
    {
        get => _customExportName;
        set
        {
            if (SetProperty(ref _customExportName, value ?? string.Empty))
                NotifyStateChanged();
        }
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

    public bool HasExportedOutput => TryGetExistingOutputPath() is not null;

    public int SelectedItemCount => Items.Count(item => item.IsSelected);

    public string? TryGetExistingOutputPath()
        => Items.Select(item => item.OutputPath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));

    public void SetAllSelected(bool selected)
    {
        foreach (var item in Items)
            item.IsSelected = selected;
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanOperateSelected));
        OnPropertyChanged(nameof(SelectedItemCount));
        OnPropertyChanged(nameof(HasExportedOutput));
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public async Task<EditorBatchWorkspaceState> CaptureStateAsync(
        string? activeProjectPath,
        CancellationToken cancellationToken = default)
    {
        var itemSnapshots = Items.Select(item => new
        {
            item.FileName,
            item.FilePath,
            item.OriginalSourcePath,
            item.Document,
            item.IsSelected,
        }).ToArray();
        var continueOnError = ContinueOnError;
        var outputDirectory = OutputDirectory;
        var exportSelectedOnly = ExportSelectedOnly;
        var selectedExportFormat = SelectedExportFormat;
        var selectedAction = SelectedAction;
        var selectedNamingOption = SelectedNamingOption;
        var customExportName = CustomExportName;
        var itemStates = new List<EditorBatchItemState>(itemSnapshots.Length);
        foreach (var item in itemSnapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? sourceDataBase64 = null;
            var isActiveProject = !string.IsNullOrWhiteSpace(activeProjectPath)
                                  && string.Equals(
                                      Path.GetFullPath(activeProjectPath),
                                      item.FilePath,
                                      StringComparison.OrdinalIgnoreCase);
            if (File.Exists(item.FilePath))
            {
                try
                {
                    sourceDataBase64 = Convert.ToBase64String(
                        await ReadEmbeddableSourceAsync(item.FilePath, isActiveProject, cancellationToken)
                            .ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    sourceDataBase64 = null;
                }
            }

            itemStates.Add(new(
                item.FileName,
                item.OriginalSourcePath,
                sourceDataBase64,
                item.Document,
                item.IsSelected,
                Path.GetExtension(item.FilePath)));
        }

        return new(
            itemStates,
            continueOnError,
            outputDirectory,
            exportSelectedOnly,
            selectedExportFormat,
            selectedAction,
            selectedNamingOption,
            customExportName);
    }

    public async Task RestoreStateAsync(
        EditorBatchWorkspaceState? state,
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        _suppressStateChanged = true;
        try
        {
            ClearItems();
            ContinueOnError = state?.ContinueOnError ?? true;
            OutputDirectory = state?.OutputDirectory ?? string.Empty;
            ExportSelectedOnly = state?.ExportSelectedOnly ?? false;
            var restoredExportFormat = state?.SelectedExportFormat ?? EditorBatchExportFormat.Dxf;
            SelectedExportFormat = Enum.IsDefined(restoredExportFormat)
                ? restoredExportFormat
                : EditorBatchExportFormat.Dxf;
            var restoredAction = state?.SelectedAction ?? EditorBatchAction.ValidateProjects;
            SelectedAction = Enum.IsDefined(restoredAction)
                ? restoredAction
                : EditorBatchAction.ValidateProjects;
            var restoredNamingOption = state?.SelectedNamingOption ?? EditorBatchNamingOption.Original;
            SelectedNamingOption = Enum.IsDefined(restoredNamingOption)
                ? restoredNamingOption
                : EditorBatchNamingOption.Original;
            CustomExportName = state?.CustomExportName ?? "BatchExport";

            var cacheRoot = BuildRecoveryDirectory(projectFilePath);
            var index = 0;
            foreach (var itemState in state?.Items ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (itemState is null)
                    continue;
                var fileName = Path.GetFileName(itemState.FileName);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                var recoveredFileName = GetRecoveredFileName(itemState, fileName);
                if (recoveredFileName is null)
                    continue;

                byte[]? sourceData = null;
                if (!string.IsNullOrWhiteSpace(itemState.SourceDataBase64))
                {
                    try
                    {
                        sourceData = Convert.FromBase64String(itemState.SourceDataBase64);
                    }
                    catch (FormatException)
                    {
                        sourceData = null;
                    }
                }

                if (sourceData is null && itemState.Document is null)
                    continue;

                Directory.CreateDirectory(cacheRoot);
                var recoveredPath = Path.Combine(cacheRoot, $"{index++:D4}-{Guid.NewGuid():N}-{recoveredFileName}");
                await File.WriteAllBytesAsync(recoveredPath, sourceData ?? [], cancellationToken).ConfigureAwait(true);
                var originalSourcePath = TryNormalizeOriginalPath(itemState.OriginalSourcePath) ?? recoveredPath;
                var item = new EditorBatchItem(recoveredPath, originalSourcePath, fileName)
                {
                    IsSelected = itemState.IsSelected,
                    Document = itemState.Document,
                };
                item.PropertyChanged += OnItemPropertyChanged;
                Items.Add(item);
            }

            Summary = Items.Count == 0 ? "No files queued." : $"{Items.Count} file(s) restored.";
            NotifyDerivedStateChanged();
        }
        finally
        {
            _suppressStateChanged = false;
        }
    }

    public bool AddFile(string path)
        => AddFiles([path]) == 1;

    public int AddFiles(IReadOnlyList<string> paths)
    {
        var existing = Items.Select(item => item.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accepted = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim().Trim('"')))
            .Where(IsAcceptedInput)
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
        NotifyDerivedStateChanged();
        NotifyStateChanged();
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
        NotifyDerivedStateChanged();
        NotifyStateChanged();
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
        var destinationRoot = string.IsNullOrWhiteSpace(OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(firstDrawing.FilePath)!, "batch-output")
            : Path.GetFullPath(OutputDirectory.Trim().Trim('"'));
        var outputDirectory = Path.Combine(destinationRoot, SanitizeExportName(
            CustomExportName,
            "Pathstitch_Batch_Export"));
        Directory.CreateDirectory(outputDirectory);
        var succeeded = 0;
        var failed = 0;
        try
        {
            var exportItems = Items.Where(item => (!ExportSelectedOnly || item.IsSelected)
                && IsSupportedDrawingInput(item.FilePath)).ToArray();
            for (var index = 0; index < exportItems.Length; index++)
            {
                var item = exportItems[index];
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
                    var baseName = SelectedNamingOption == EditorBatchNamingOption.CustomIndex
                        ? $"{SanitizeExportName(CustomExportName, "Export")}_{index + 1}"
                        : SanitizeExportName(Path.GetFileNameWithoutExtension(item.FileName), "Export");
                    var outputPath = Path.Combine(outputDirectory, $"{baseName}{extension}");
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

    private static string SanitizeExportName(string? value, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
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

    private static bool IsAcceptedInput(string path)
        => Path.GetExtension(path).Equals(".stch", StringComparison.OrdinalIgnoreCase)
           || IsSupportedDrawingInput(path);

    private static string? GetRecoveredFileName(EditorBatchItemState itemState, string fileName)
    {
        var sourceExtension = itemState.SourceFileExtension?.Trim();
        if (string.IsNullOrWhiteSpace(sourceExtension))
            return IsAcceptedInput(fileName) ? fileName : null;

        if (!sourceExtension.StartsWith(".", StringComparison.Ordinal))
            sourceExtension = $".{sourceExtension}";
        if (!IsAcceptedInput($"embedded{sourceExtension}"))
            return null;

        var displayStem = Path.GetFileNameWithoutExtension(fileName);
        return $"{(string.IsNullOrWhiteSpace(displayStem) ? "batch-item" : displayStem)}{sourceExtension}";
    }

    private static async Task<byte[]> ReadEmbeddableSourceAsync(
        string path,
        bool extractProjectMetadataOnly,
        CancellationToken cancellationToken)
    {
        if (!extractProjectMetadataOnly)
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

        try
        {
            await using var fileStream = File.OpenRead(path);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
            var projectEntry = archive.GetEntry("project.json");
            if (projectEntry is null)
                return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            await using var projectStream = projectEntry.Open();
            using var memory = new MemoryStream();
            await projectStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            return memory.ToArray();
        }
        catch (InvalidDataException)
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildRecoveryDirectory(string projectFilePath)
    {
        var fullPath = Path.GetFullPath(projectFilePath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)))[..12];
        return Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "RecoveredBatchInputs", hash);
    }

    private static string? TryNormalizeOriginalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private void ClearItems()
    {
        foreach (var item in Items)
            item.PropertyChanged -= OnItemPropertyChanged;
        Items.Clear();
    }

    private void NotifyDerivedStateChanged()
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanOperateSelected));
        OnPropertyChanged(nameof(SelectedItemCount));
    }

    private void NotifyStateChanged()
    {
        if (!_suppressStateChanged)
            StateChanged?.Invoke();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorBatchItem.IsSelected) or nameof(EditorBatchItem.OutputPath))
            NotifyDerivedStateChanged();
        if (e.PropertyName is nameof(EditorBatchItem.IsSelected) or nameof(EditorBatchItem.Document))
            NotifyStateChanged();
    }
}
