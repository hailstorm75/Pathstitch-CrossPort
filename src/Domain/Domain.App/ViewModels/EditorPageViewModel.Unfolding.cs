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

            if (!SetWorkspaceFacadeValue(_liveRecomputeEnabled, value, updated => _liveRecomputeEnabled = updated))
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
            ? "This restored 3D workspace is view-only until a 3D source asset is available. Re-import the model or reopen a .stch with embedded 3D data."
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
            recordActivity: true,
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
            recordActivity: true,
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
            recordActivity: false,
            cancellationToken).ConfigureAwait(true);
    }

    private void CancelLiveRecompute()
        => _threeDWorkspace.CancelLiveRecompute();

    private void RequestLiveRecompute(TimeSpan? delay = null)
    {
        if (!LiveRecomputeEnabled || !CanUseLiveRecompute)
            return;

        var nextCancellationTokenSource = _threeDWorkspace.BeginLiveRecompute();

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
                recordActivity: false,
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
            _threeDWorkspace.CompleteLiveRecompute(cancellationTokenSource);
        }
    }

    private async Task RunUnfoldAsync(
        bool wholeBody,
        string actionLabel,
        bool activatePreviewWorkspace,
        bool recordActivity,
        CancellationToken cancellationToken)
    {
        StatusText = $"{actionLabel} running";
        ErrorMessage = null;

        var appendContext = await StageExistingTwoDDocumentAsync(cancellationToken).ConfigureAwait(true);
        EditorOperationResult result;
        try
        {
            result = await _threeDWorkspace.UnfoldAsync(
                BuildUnfoldRequest(wholeBody, appendContext?.StagingPath),
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            DeleteStagedTwoDDocument(appendContext?.StagingPath);
        }

        StatusText = result.IsSuccess ? $"{actionLabel} completed" : $"{actionLabel} failed";
        ViewportStateText = result.Message;
        ErrorMessage = result.IsSuccess ? null : result.Message;

        if (!result.IsSuccess)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        if (activatePreviewWorkspace)
        {
            await HandleSuccessfulGeneratedOutputAsync(
                result.OutputPath,
                BuildUnfoldOutputContext(wholeBody, actionLabel),
                appendContext,
                cancellationToken).ConfigureAwait(true);
            if (recordActivity)
                RecordActivity("Flatten 3D Geometry", wholeBody ? "Flattened entire body" : "Flattened selected faces");
            return;
        }

        GeneratedOutputContext = BuildUnfoldOutputContext(wholeBody, actionLabel);
        await UpdateGeneratedOutputPreviewAsync(
            result.OutputPath,
            activatePreviewWorkspace: false,
            cancellationToken,
            appendContext: appendContext,
            generatedLayerName: "Unfolded 3D",
            replaceGeneratedPreviewLayer: true).ConfigureAwait(true);
    }

    private EditorGeneratedOutputContext BuildUnfoldOutputContext(bool wholeBody, string actionLabel)
        => new(
            SourceTool: "Unfold",
            TriggerLabel: actionLabel,
            ScopeSummary: wholeBody ? SeparateFlattenWholeBodySummary : SeparateFlattenSelectionSummary,
            ConfigurationSummary: UnfoldConfigurationSummary,
            CreatedUtc: DateTimeOffset.UtcNow);
}
