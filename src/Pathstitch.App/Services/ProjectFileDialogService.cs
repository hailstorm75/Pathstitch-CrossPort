using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class ProjectFileDialogService : IProjectFileDialogService
{
    private static readonly FilePickerFileType ProjectFileType = new("Pathstitch Project")
    {
        Patterns = ["*.stch"],
    };

    private static readonly FilePickerFileType SourceModelFileType = new("3D Source Model")
    {
        Patterns = ["*.step", "*.stp", "*.obj", "*.stl"],
    };

    private static readonly FilePickerFileType WorkspaceFileType = new("Pathstitch Workspace")
    {
        Patterns = ["*.stch", "*.dxf", "*.svg", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.tif", "*.tiff", "*.avif", "*.heic", "*.heif", "*.step", "*.stp", "*.obj", "*.stl"],
    };

    private static readonly FilePickerFileType ReferenceImageFileType = new("Reference Image")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.tif", "*.tiff", "*.avif", "*.heic", "*.heif"],
    };

    private static readonly FilePickerFileType DxfFileType = new("DXF Drawing")
    {
        Patterns = ["*.dxf"],
    };

    private static readonly FilePickerFileType SvgFileType = new("SVG Drawing")
    {
        Patterns = ["*.svg"],
    };

    private static readonly FilePickerFileType PngFileType = new("PNG Image")
    {
        Patterns = ["*.png"],
    };

    private static readonly FilePickerFileType PdfFileType = new("PDF Document")
    {
        Patterns = ["*.pdf"],
    };

    public async Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return null;

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open Template Project",
            FileTypeFilter = [ProjectFileType],
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return null;

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create Template Project",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "stch",
            FileTypeChoices = [ProjectFileType],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result?.TryGetLocalPath();
    }

    public async Task<string?> PickProjectSaveAsFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return null;

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Pathstitch Project As",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "stch",
            FileTypeChoices = [ProjectFileType],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result?.TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return [];

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Open Pathstitch Project, Drawing, Image, or 3D Model",
            FileTypeFilter = [WorkspaceFileType],
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return [];

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Open 3D Source Models",
            FileTypeFilter = [SourceModelFileType],
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray()!;
    }

    public async Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default)
    {
        var sourceModelPaths = await PickSourceModelFilesAsync(cancellationToken).ConfigureAwait(true);
        return sourceModelPaths.Count > 0 ? sourceModelPaths[0] : null;
    }

    public async Task<string?> PickReferenceImageFileAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return null;

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Import 2D Reference Image",
            FileTypeFilter = [ReferenceImageFileType],
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickDxfExportFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return null;

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export 2D Workspace as DXF",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "dxf",
            FileTypeChoices = [DxfFileType],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result?.TryGetLocalPath();
    }

    public async Task<string?> PickSvgExportFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return null;

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export 2D Workspace as SVG",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "svg",
            FileTypeChoices = [SvgFileType],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result?.TryGetLocalPath();
    }

    public async Task<string?> PickPngExportFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return null;

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export 2D Workspace as PNG",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "png",
            FileTypeChoices = [PngFileType],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result?.TryGetLocalPath();
    }

    public async Task<string?> PickPdfExportFileAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return null;

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export 2D Workspace as PDF",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "pdf",
            FileTypeChoices = [PdfFileType],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        return result?.TryGetLocalPath();
    }

    private static TopLevel? GetTopLevel()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
