using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Models;
using Domain.App.Services;
using Domain.MVVM.Navigation;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private static readonly HashSet<string> SupportedImportedDrawingExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dxf", ".svg", ".pdf",
    };
    private static readonly HashSet<string> SupportedImportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".avif", ".heic", ".heif", ".psd",
    };
    private readonly SemaphoreSlim _importFilesGate = new(1, 1);
    private double _twoDViewportPixelWidth;
    private double _twoDViewportPixelHeight;

    public void UpdateTwoDViewportSize(double pixelWidth, double pixelHeight)
    {
        if (!double.IsFinite(pixelWidth)
            || !double.IsFinite(pixelHeight)
            || pixelWidth <= 0.0
            || pixelHeight <= 0.0)
        {
            return;
        }

        _twoDViewportPixelWidth = pixelWidth;
        _twoDViewportPixelHeight = pixelHeight;
    }

    private Editor2DViewportPlacement? CurrentTwoDViewportPlacement
        => _twoDViewportPixelWidth > 0.0 && _twoDViewportPixelHeight > 0.0
            ? new Editor2DViewportPlacement(
                _twoDViewportPixelWidth,
                _twoDViewportPixelHeight,
                TwoDViewportZoom,
                TwoDViewportOffsetX,
                TwoDViewportOffsetY)
            : null;

    private bool CanImportFiles()
        => ProjectSession is not null
           && !IsSaving
           && !IsReplacingProject
           && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanImportFiles))]
    public async Task ImportFilesAsync(CancellationToken cancellationToken)
    {
        if (!await _importFilesGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
            return;

        try
        {
            var selectedPaths = await _projectFileDialogService
                .PickWorkspaceFilesAsync(cancellationToken)
                .ConfigureAwait(true);
            await ImportFilesCoreAsync(selectedPaths, insertionPoint: null, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _importFilesGate.Release();
            ImportFilesCommand.NotifyCanExecuteChanged();
        }
    }

    public Task OpenActivatedFilesAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
        => OpenFilesAtPointAsync(filePaths, insertionPoint: null, cancellationToken);

    public Task OpenDroppedFilesAsync(
        IReadOnlyList<string> filePaths,
        Editor2DPoint insertionPoint,
        CancellationToken cancellationToken = default)
        => OpenFilesAtPointAsync(filePaths, insertionPoint, cancellationToken);

    private async Task OpenFilesAtPointAsync(
        IReadOnlyList<string> filePaths,
        Editor2DPoint? insertionPoint,
        CancellationToken cancellationToken)
    {
        await _importFilesGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await ImportFilesCoreAsync(filePaths, insertionPoint, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _importFilesGate.Release();
            ImportFilesCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ImportFilesCoreAsync(
        IReadOnlyList<string> selectedPaths,
        Editor2DPoint? insertionPoint,
        CancellationToken cancellationToken)
    {
        var paths = selectedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
            return;

        var projectPaths = paths.Where(path => Path.GetExtension(path).Equals(".stch", StringComparison.OrdinalIgnoreCase)).ToArray();
        var sourcePaths = paths.Where(path => SupportedSourceModelExtensions.Contains(Path.GetExtension(path))).ToArray();
        var drawingPaths = paths.Where(path => SupportedImportedDrawingExtensions.Contains(Path.GetExtension(path))).ToArray();
        var imagePaths = paths.Where(path => SupportedImportedImageExtensions.Contains(Path.GetExtension(path))).ToArray();
        var supported = projectPaths.Concat(sourcePaths).Concat(drawingPaths).Concat(imagePaths).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (paths.Any(path => !supported.Contains(path)) || projectPaths.Length > 1)
        {
            ErrorMessage = projectPaths.Length > 1
                ? "Import one Pathstitch project at a time."
                : "One or more selected files use an unsupported import format.";
            return;
        }

        if (projectPaths.Length == 1)
        {
            await ImportProjectWithAssetsAsync(
                projectPaths[0], sourcePaths, drawingPaths, imagePaths, insertionPoint, cancellationToken).ConfigureAwait(true);
            return;
        }

        if (sourcePaths.Length > 0)
            await OpenSourceModelsAsync(sourcePaths, cancellationToken).ConfigureAwait(true);
        if (drawingPaths.Length > 0)
            await RouteImportedTwoDDrawingsAsync(drawingPaths, cancellationToken).ConfigureAwait(true);
        if (imagePaths.Length > 0)
            await ImportReferenceImagesAsync(imagePaths, insertionPoint, cancellationToken).ConfigureAwait(true);
    }

    private async Task ImportProjectWithAssetsAsync(
        string projectPath,
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<string> drawingPaths,
        IReadOnlyList<string> imagePaths,
        Editor2DPoint? insertionPoint,
        CancellationToken cancellationToken)
    {
        var service = _projectSessionService;
        if (service is null)
            return;
        var launchRequest = await service
            .PrepareOpenProjectLaunchAsync(projectPath, cancellationToken)
            .ConfigureAwait(true);
        if (launchRequest is null)
            return;
        var session = launchRequest.Session;
        var launchWithAssets = launchRequest with
        {
            PendingSourceModelPaths = sourcePaths,
            PendingTwoDFilePaths = drawingPaths,
            PendingReferenceImagePaths = imagePaths,
        };

        if (_documentWindowService is not null)
        {
            var disposition = await _projectOpenDispositionPromptService
                .PromptAsync(Path.GetFileName(projectPath), cancellationToken)
                .ConfigureAwait(true);
            if (disposition == ProjectOpenDisposition.Cancel)
                return;
            if (disposition == ProjectOpenDisposition.NewWindow)
            {
                await _documentWindowService
                    .OpenDocumentAsync(launchWithAssets, cancellationToken)
                    .ConfigureAwait(true);
                return;
            }

            CombinePreparedProject(launchRequest.PreparedProjectState!, projectPath);
            if (sourcePaths.Count > 0)
                await OpenSourceModelsAsync(sourcePaths, cancellationToken).ConfigureAwait(true);
            if (drawingPaths.Count > 0)
                await RouteImportedTwoDDrawingsAsync(drawingPaths, cancellationToken).ConfigureAwait(true);
            if (imagePaths.Count > 0)
                await ImportReferenceImagesAsync(imagePaths, insertionPoint, cancellationToken).ConfigureAwait(true);
            return;
        }

        if (!await ConfirmCanLeaveDocumentAsync(cancellationToken).ConfigureAwait(true))
            return;

        var request = CreateEditorNavigationRequest(launchWithAssets);
        service.ActivateSession(session);
        _preapprovedNavigationRequest = request;
        Messenger.Send(request);
    }

    private async Task<int> ImportTwoDDrawingsAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        var importedDrawings = new List<Editor2DImportedDrawing>();
        foreach (var filePath in filePaths)
        {
            try
            {
                var importedDocument = await _editorOutputPreviewService
                    .LoadPreviewDocumentAsync(filePath, cancellationToken)
                    .ConfigureAwait(true);
                if (importedDocument is not { Paths.Count: > 0 })
                {
                    ErrorMessage = $"Could not import {Path.GetFileName(filePath)}: no importable geometry found";
                    return 0;
                }

                var units = await _editorOutputPreviewService
                    .InspectImportUnitsAsync(filePath, cancellationToken)
                    .ConfigureAwait(true);
                var appliedUnitScale = 1.0;
                if (units?.RequiresPrompt == true)
                {
                    var factor = await _importUnitsPromptService
                        .PromptAsync(units, cancellationToken)
                        .ConfigureAwait(true);
                    if (factor is > 0)
                        appliedUnitScale = factor.Value;
                }
                if (Math.Abs(appliedUnitScale - 1.0) > 1e-12)
                    importedDocument = ScaleImportedTwoDDocument(importedDocument, appliedUnitScale);
                importedDrawings.Add(new Editor2DImportedDrawing(
                    Path.GetFullPath(filePath), appliedUnitScale, importedDocument));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not import {Path.GetFileName(filePath)}: {ex.Message}";
                return 0;
            }
        }

        if (importedDrawings.Count == 0)
            return 0;

        if (!CompleteTwoDWorkspaceOperation(_twoDWorkspace.AddImportedDrawings(importedDrawings)))
            return 0;
        ErrorMessage = null;
        MarkDocumentDirty();
        return importedDrawings.Count;
    }

    private async Task<int> RouteImportedTwoDDrawingsAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
            return 0;

        if (ActiveEditorMode == EditorMode.Batch || paths.Length >= 5)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var added = _batchWorkspace.AddFiles(paths);
            await SetActiveEditorModeAsync(EditorMode.Batch, CancellationToken.None).ConfigureAwait(true);
            StatusText = added == 1
                ? "Queued 1 drawing for batch processing"
                : $"Queued {added} drawings for batch processing";
            return added;
        }

        var imported = await ImportTwoDDrawingsAsync(paths, cancellationToken).ConfigureAwait(true);
        if (imported > 0)
            await SetActiveEditorModeAsync(EditorMode.TwoD, CancellationToken.None).ConfigureAwait(true);
        return imported;
    }

    public bool HasSelectedTwoDImportGroup => _twoDWorkspace.GetSelectedImportGroups().Count > 0;

    public async Task<bool> ReloadSelectedTwoDImportsFromDiskAsync(CancellationToken cancellationToken = default)
    {
        if (!await _importFilesGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
            return false;
        try
        {
            return await ReloadSelectedTwoDImportsFromDiskCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _importFilesGate.Release();
            ImportFilesCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task<bool> ReloadSelectedTwoDImportsFromDiskCoreAsync(CancellationToken cancellationToken)
    {
        var groups = _twoDWorkspace.GetSelectedImportGroups();
        if (groups.Count == 0)
        {
            StatusText = "Select imported drawing geometry before reloading from disk";
            return false;
        }
        var expectedState = _twoDWorkspace.State;

        var replacements = new Dictionary<string, Editor2DPreviewDocument>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            Editor2DPreviewDocument? document;
            try
            {
                document = await _editorOutputPreviewService
                    .LoadPreviewDocumentAsync(group.SourceFilePath, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not reload {Path.GetFileName(group.SourceFilePath)}: {ex.Message}";
                return false;
            }

            if (document is not { Paths.Count: > 0 })
            {
                ErrorMessage = $"Could not reload {Path.GetFileName(group.SourceFilePath)}";
                return false;
            }
            replacements[group.Id] = Math.Abs(group.AppliedUnitScale - 1.0) > 1e-12
                ? ScaleImportedTwoDDocument(document, group.AppliedUnitScale)
                : document;
        }

        if (!ReferenceEquals(expectedState, _twoDWorkspace.State))
        {
            ErrorMessage = "Workspace changed while imported drawings were loading; reload again";
            return false;
        }
        if (!CompleteTwoDWorkspaceOperation(_twoDWorkspace.ReloadImportGroups(replacements)))
            return false;
        ErrorMessage = null;
        MarkDocumentDirty();
        return true;
    }

    private async Task<int> ImportReferenceImagesAsync(
        IReadOnlyList<string> imagePaths,
        Editor2DPoint? insertionPoint,
        CancellationToken cancellationToken)
    {
        var importedCount = 0;
        foreach (var imagePath in imagePaths)
        {
            if (Path.GetExtension(imagePath).Equals(".psd", StringComparison.OrdinalIgnoreCase))
            {
                importedCount += await ImportPsdAsync(imagePath, insertionPoint, cancellationToken).ConfigureAwait(true);
                continue;
            }
            try
            {
                var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(true);
                if (!TryPrepareReferenceImage(bytes, out var image) || image is null)
                    continue;
                if (!TwoDWorkspace.IsInitialized)
                    TwoDDocument = Editor2DWorkspaceState.Empty.Document;
                _twoDWorkspace.ImportReferenceImage(
                    Path.GetFileName(imagePath),
                    Convert.ToBase64String(image.Data),
                    image.PixelWidth,
                    image.PixelHeight,
                    insertionPoint);
                importedCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                ErrorMessage = $"Could not import {Path.GetFileName(imagePath)}: {ex.Message}";
            }
        }

        if (importedCount > 0)
        {
            RefreshTwoDLayerFacade();
            await SetActiveEditorModeAsync(EditorMode.TwoD, CancellationToken.None).ConfigureAwait(true);
            StatusText = $"Imported {importedCount} image file(s)";
            RecordActivity(
                "Import Reference Images",
                importedCount == 1 ? "Imported 1 image" : $"Imported {importedCount} images");
            MarkDocumentDirty();
        }
        return importedCount;
    }

    private async Task<int> ImportPsdAsync(
        string psdPath,
        Editor2DPoint? insertionPoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var import = await _psdImportService.ParseAsync(psdPath, cancellationToken).ConfigureAwait(true);
            var mode = await _psdImportModePromptService.PromptAsync(import, cancellationToken).ConfigureAwait(true);
            if (mode is null)
                return 0;
            var result = _twoDWorkspace.ImportPsd(
                import,
                mode.Value,
                insertionPoint,
                CurrentTwoDViewportPlacement);
            if (!CompleteTwoDWorkspaceOperation(result))
                return 0;
            return 1;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not import {Path.GetFileName(psdPath)}: {ex.Message}";
            return 0;
        }
    }
}
