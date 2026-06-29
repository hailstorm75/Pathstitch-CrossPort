using Domain.App.Models;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public bool LiveRecomputeEnabled
    {
        get => _liveRecomputeEnabled;
        set
        {
            if (value && !CanUseLiveRecompute)
                value = false;

            if (!SetProperty(ref _liveRecomputeEnabled, value))
                return;

            OnPropertyChanged(nameof(UnfoldPreviewModeSummary));

            if (!value)
            {
                CancelLiveRecompute();
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
                return;
            }

            RequestLiveRecompute();
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public bool CanUnfoldSelected => SelectedFaces.Count > 0 && HasUsableSourceModelAsset;

    public bool CanUnfoldEntireBody => VisibleBodyCount > 0 && HasUsableSourceModelAsset;

    public string UnfoldSelectedButtonText => "Flatten Selected";

    public string WholeBodyActionLabel => "Flatten Entire Body";

    public string UnfoldHintText => !HasLoadedModel
        ? "Load a model to begin flattening faces."
        : !HasUsableSourceModelAsset
            ? "This restored 3D workspace is view-only until a native source asset is available. Re-import the model or reopen a .stch with embedded 3D data."
        : SelectedFaces.Count == 0
            ? "Select faces in the list or viewport (Shift-click for multiple)."
            : $"{SelectedFaces.Count} face(s) selected for separate flattening.";

    public async Task RequestUnfoldSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUnfoldSelected)
            return;

        CancelLiveRecompute();
        SetUnfoldPreviewScope(false);
        await RunUnfoldAsync(
            wholeBody: false,
            actionLabel: UnfoldSelectedButtonText,
            activatePreviewWorkspace: true,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task RequestUnfoldEntireBodyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUnfoldEntireBody)
            return;

        CancelLiveRecompute();
        SetUnfoldPreviewScope(true);
        await RunUnfoldAsync(
            wholeBody: true,
            actionLabel: WholeBodyActionLabel,
            activatePreviewWorkspace: true,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task RefreshActiveUnfoldPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRefreshActiveUnfoldPreview)
            return;

        CancelLiveRecompute();
        await RunUnfoldAsync(
            wholeBody: _wholeBodyRecompute,
            actionLabel: UnfoldPreviewRefreshButtonText,
            activatePreviewWorkspace: false,
            cancellationToken).ConfigureAwait(true);
    }

    private void CancelLiveRecompute()
    {
        var cancellationTokenSource = _liveRecomputeCancellationTokenSource;
        _liveRecomputeCancellationTokenSource = null;
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    private void RequestLiveRecompute(TimeSpan? delay = null)
    {
        if (!LiveRecomputeEnabled || !CanUseLiveRecompute)
            return;

        var nextCancellationTokenSource = new CancellationTokenSource();
        var previousCancellationTokenSource = _liveRecomputeCancellationTokenSource;
        _liveRecomputeCancellationTokenSource = nextCancellationTokenSource;
        previousCancellationTokenSource?.Cancel();
        previousCancellationTokenSource?.Dispose();

        _ = RunLiveRecomputeAsync(nextCancellationTokenSource, delay ?? TimeSpan.FromMilliseconds(300));
    }

    private async Task RunLiveRecomputeAsync(CancellationTokenSource cancellationTokenSource, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationTokenSource.Token).ConfigureAwait(true);

            if (!HasUsableSourceModelAsset || string.IsNullOrWhiteSpace(_sourceModelPath))
                return;

            if (!CanUseLiveRecompute)
                return;

            if (!_wholeBodyRecompute && SelectedFaces.Count == 0)
                return;

            await RunUnfoldAsync(
                wholeBody: _wholeBodyRecompute,
                actionLabel: "Live recompute",
                activatePreviewWorkspace: false,
                cancellationTokenSource.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A newer setting or selection change superseded this recompute.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh the generated 2D preview from live recompute.");
            StatusText = "Live recompute failed";
            ViewportStateText = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_liveRecomputeCancellationTokenSource, cancellationTokenSource))
                _liveRecomputeCancellationTokenSource = null;

            cancellationTokenSource.Dispose();
        }
    }

    private async Task RunUnfoldAsync(
        bool wholeBody,
        string actionLabel,
        bool activatePreviewWorkspace,
        CancellationToken cancellationToken)
    {
        StatusText = $"{actionLabel} running";
        ErrorMessage = null;

        var result = await _editor3DOperationService.UnfoldAsync(
            BuildUnfoldRequest(wholeBody),
            cancellationToken).ConfigureAwait(true);

        StatusText = result.IsSuccess ? $"{actionLabel} completed" : $"{actionLabel} failed";
        ViewportStateText = result.Message;
        ErrorMessage = result.IsSuccess ? null : result.Message;

        if (!result.IsSuccess)
            return;

        if (activatePreviewWorkspace)
        {
            await HandleSuccessfulGeneratedOutputAsync(
                result.OutputPath,
                BuildUnfoldOutputContext(wholeBody, actionLabel),
                cancellationToken).ConfigureAwait(true);
            return;
        }

        GeneratedOutputContext = BuildUnfoldOutputContext(wholeBody, actionLabel);
        await UpdateGeneratedOutputPreviewAsync(
            result.OutputPath,
            activatePreviewWorkspace: false,
            cancellationToken).ConfigureAwait(true);
    }

    private EditorGeneratedOutputContext BuildUnfoldOutputContext(bool wholeBody, string actionLabel)
        => new(
            SourceTool: "Unfold",
            TriggerLabel: actionLabel,
            ScopeSummary: wholeBody ? SeparateFlattenWholeBodySummary : SeparateFlattenSelectionSummary,
            ConfigurationSummary: UnfoldConfigurationSummary,
            CreatedUtc: DateTimeOffset.UtcNow);
}
