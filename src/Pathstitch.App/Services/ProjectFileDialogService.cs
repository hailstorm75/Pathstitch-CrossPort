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
        Patterns = ["*.stch", "*.step", "*.stp", "*.obj", "*.stl"],
    };

    private static readonly FilePickerFileType ReferenceImageFileType = new("Reference Image")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"],
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

    public async Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is null)
            return [];

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Open Pathstitch Project or 3D Models",
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

    private static TopLevel? GetTopLevel()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
