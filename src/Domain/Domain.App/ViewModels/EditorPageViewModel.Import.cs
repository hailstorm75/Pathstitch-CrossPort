using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Models;
using Domain.MVVM.Navigation;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private static readonly HashSet<string> SupportedImportedDrawingExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dxf", ".svg",
    };
    private static readonly HashSet<string> SupportedImportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".avif", ".heic", ".heif",
    };
    private readonly SemaphoreSlim _importFilesGate = new(1, 1);

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
                    projectPaths[0], sourcePaths, drawingPaths, imagePaths, cancellationToken).ConfigureAwait(true);
                return;
            }

            if (sourcePaths.Length > 0)
                await OpenSourceModelsAsync(sourcePaths, cancellationToken).ConfigureAwait(true);
            if (drawingPaths.Length > 0)
                await ImportTwoDDrawingsAsync(drawingPaths, cancellationToken).ConfigureAwait(true);
            if (imagePaths.Length > 0)
                await ImportReferenceImagesAsync(imagePaths, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _importFilesGate.Release();
            ImportFilesCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ImportProjectWithAssetsAsync(
        string projectPath,
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<string> drawingPaths,
        IReadOnlyList<string> imagePaths,
        CancellationToken cancellationToken)
    {
        var service = _projectSessionService;
        if (service is null)
            return;
        var session = await service.PrepareOpenProjectAsync(projectPath, cancellationToken).ConfigureAwait(true);
        if (session is null || !await ConfirmCanLeaveDocumentAsync(cancellationToken).ConfigureAwait(true))
            return;

        var request = CreateEditorNavigationRequest(new ProjectLaunchRequest(session, sourcePaths)
        {
            PendingTwoDFilePaths = drawingPaths,
            PendingReferenceImagePaths = imagePaths,
        });
        service.ActivateSession(session);
        _preapprovedNavigationRequest = request;
        WeakReferenceMessenger.Default.Send(request);
    }

    private async Task<int> ImportTwoDDrawingsAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        var importedDrawings = new List<Editor2DImportedDrawing>();
        foreach (var filePath in filePaths)
        {
            var importedDocument = await _editorOutputPreviewService
                .LoadPreviewDocumentAsync(filePath, cancellationToken)
                .ConfigureAwait(true);
            if (importedDocument is null)
                continue;

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
            if (importedDocument.Paths.Count > 0)
                importedDrawings.Add(new Editor2DImportedDrawing(
                    Path.GetFullPath(filePath), appliedUnitScale, importedDocument));
        }

        if (importedDrawings.Count == 0)
            return 0;

        if (!CompleteTwoDWorkspaceOperation(_twoDWorkspace.AddImportedDrawings(importedDrawings)))
            return 0;
        MarkDocumentDirty();
        return importedDrawings.Count;
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
        CancellationToken cancellationToken)
    {
        var importedCount = 0;
        foreach (var imagePath in imagePaths)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(true);
                if (!Editor2DReferenceImageMetadata.TryReadPixelSize(bytes, out var pixelWidth, out var pixelHeight))
                    continue;
                if (!TwoDWorkspace.IsInitialized)
                    TwoDDocument = Editor2DWorkspaceState.Empty.Document;
                _twoDWorkspace.ImportReferenceImage(
                    Path.GetFileName(imagePath),
                    Convert.ToBase64String(bytes),
                    pixelWidth,
                    pixelHeight);
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
            StatusText = $"Imported {importedCount} reference image(s)";
            MarkDocumentDirty();
        }
        return importedCount;
    }
}
