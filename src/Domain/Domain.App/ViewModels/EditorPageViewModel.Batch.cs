namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public async Task PickBatchInputFilesAsync(CancellationToken cancellationToken = default)
    {
        var paths = await _projectFileDialogService
            .PickBatchInputFilesAsync(cancellationToken)
            .ConfigureAwait(true);
        var added = _batchWorkspace.AddFiles(paths);
        if (added > 0)
            StatusText = $"Added {added} batch input(s)";
    }

    public int AddDroppedBatchFiles(IReadOnlyList<string> filePaths)
    {
        var added = _batchWorkspace.AddFiles(filePaths);
        StatusText = added > 0
            ? $"Added {added} dropped batch input(s)"
            : "No supported batch inputs were added";
        return added;
    }

    public async Task ChooseBatchOutputFolderAsync(CancellationToken cancellationToken = default)
    {
        var folder = await _projectFileDialogService
            .PickBatchOutputFolderAsync(cancellationToken)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(folder))
            return;
        _batchWorkspace.OutputDirectory = folder;
        StatusText = $"Batch export destination: {folder}";
    }

    public async Task RevealBatchOutputAsync(CancellationToken cancellationToken = default)
    {
        var outputPath = _batchWorkspace.TryGetExistingOutputPath();
        if (outputPath is null)
            return;
        await _editorOutputLauncherService.RevealOutputAsync(outputPath, cancellationToken).ConfigureAwait(true);
        StatusText = "Revealed batch output";
    }
}
