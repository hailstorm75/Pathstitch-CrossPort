using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private sealed record BatchItemEditSession(
        string ItemId,
        string FileName,
        Editor2DWorkspaceSessionSnapshot WorkspaceSnapshot);

    private BatchItemEditSession? _batchItemEditSession;

    public bool IsBatchItemEditActive => _batchItemEditSession is not null;

    public string BatchItemEditTitle => _batchItemEditSession is null
        ? string.Empty
        : $"Editing Batch item: {_batchItemEditSession.FileName}";

    public async Task<bool> BeginBatchItemEditAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        if (_batchItemEditSession is not null)
            return false;

        var item = await _batchWorkspace
            .EnsureDocumentAsync(itemId, _editorOutputPreviewService, cancellationToken)
            .ConfigureAwait(true);
        if (item?.Document is null)
        {
            StatusText = "Batch item preview could not be loaded";
            return false;
        }

        _batchItemEditSession = new(
            item.Id,
            item.FileName,
            _twoDWorkspace.CaptureSessionSnapshot());
        _twoDWorkspace.ClearHistory();
        _twoDWorkspace.Apply(
            new Editor2DWorkspaceState(
                EditorBatchWorkspaceViewModel.CloneDocument(item.Document)),
            recordHistory: false);
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        ActiveEditorMode = EditorMode.TwoD;
        OnPropertyChanged(nameof(IsBatchItemEditActive));
        OnPropertyChanged(nameof(BatchItemEditTitle));
        StatusText = $"Editing Batch item: {item.FileName}";
        return true;
    }

    public bool SaveBatchItemEditAndReturn()
    {
        var session = _batchItemEditSession;
        if (session is null)
            return false;

        var saved = _batchWorkspace.ReplaceEditedDocument(
            session.ItemId,
            _twoDWorkspace.Document);
        RestoreWorkspaceAfterBatchEdit(session);
        StatusText = saved
            ? $"Batch item updated: {session.FileName}"
            : "Batch item was removed; edit was not saved";
        return saved;
    }

    public bool CancelBatchItemEditAndReturn()
    {
        var session = _batchItemEditSession;
        if (session is null)
            return false;

        RestoreWorkspaceAfterBatchEdit(session);
        StatusText = $"Batch edit canceled: {session.FileName}";
        return true;
    }

    private void RestoreWorkspaceAfterBatchEdit(BatchItemEditSession session)
    {
        _twoDWorkspace.RestoreSessionSnapshot(session.WorkspaceSnapshot);
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        _batchItemEditSession = null;
        ActiveEditorMode = EditorMode.Batch;
        OnPropertyChanged(nameof(IsBatchItemEditActive));
        OnPropertyChanged(nameof(BatchItemEditTitle));
    }

    private Editor2DWorkspaceState GetPersistableTwoDWorkspaceState()
        => _batchItemEditSession?.WorkspaceSnapshot.State
            ?? BuildPersistedTwoDWorkspaceState();
}
