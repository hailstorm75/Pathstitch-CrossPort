using System.Linq;
using System.Text.Json;
using Domain.App.Models;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private EditorFaceDistortionSummary? _faceDistortionSummary;
    private const double NegligibleDistortionThreshold = 0.01;
    private const double LowDistortionThreshold = 0.05;
    private const double ModerateDistortionThreshold = 0.15;

    public int DistortionModeIndex
    {
        get => _distortionModeIndex;
        set
        {
            if (!SetProperty(ref _distortionModeIndex, value))
                return;

            OnPropertyChanged(nameof(DistortionModeLabel));
            OnPropertyChanged(nameof(UnfoldConfigurationSummary));
            OnPropertyChanged(nameof(FaceDistortionInspectorHint));
            OnPropertyChanged(nameof(FaceDistortionStatusText));
            RequestDistortionRefresh(TimeSpan.FromMilliseconds(150));
            RequestLiveRecompute();
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public EditorFaceDistortionSummary? FaceDistortionSummary
    {
        get => _faceDistortionSummary;
        private set
        {
            if (!SetProperty(ref _faceDistortionSummary, value))
                return;

            OnPropertyChanged(nameof(HasFaceDistortionSummary));
            OnPropertyChanged(nameof(FaceDistortionInspectorHint));
            OnPropertyChanged(nameof(MeasuredFaceTitle));
            OnPropertyChanged(nameof(MeasuredFaceSubtitle));
            OnPropertyChanged(nameof(FaceDistortionAverageText));
            OnPropertyChanged(nameof(FaceDistortionMaximumText));
            OnPropertyChanged(nameof(FaceDistortionRangeText));
            OnPropertyChanged(nameof(FaceDistortionSampleCountText));
            OnPropertyChanged(nameof(FaceDistortionStatusText));
            OnPropertyChanged(nameof(FaceDistortionRefreshButtonText));
            OnPropertyChanged(nameof(FaceDistortionSeverityLabel));
            OnPropertyChanged(nameof(FaceDistortionSurfaceBehaviorSummary));
            OnPropertyChanged(nameof(FaceDistortionModeSummary));
        }
    }

    public bool HasFaceDistortionSummary => FaceDistortionSummary is not null;

    public bool CanInspectFaceDistortion => SelectedFaces.Count == 1 && HasUsableSourceModelAsset;

    public bool CanRefreshFaceDistortion => SelectedFaces.Count == 1 && HasUsableSourceModelAsset;

    public string DistortionModeLabel => DistortionModeIndex switch
    {
        1 => "Equal-Area",
        2 => "Equidistant",
        3 => "Balanced",
        _ => "Conformal",
    };

    public string FaceDistortionInspectorHint => SelectedFaces.Count switch
    {
        0 => "Select one face in the viewport or face list. Measure keeps a single active face.",
        _ when !HasUsableSourceModelAsset => "This restored 3D workspace has no OpenGeometry mesh source asset available for distortion analysis. Re-import the model or reopen a .stch with embedded 3D data.",
        _ when HasFaceDistortionSummary => $"{DistortionModeLabel} heatmap applied to the current face. Blue is lower distortion, red is higher.",
        _ => $"Computing {DistortionModeLabel.ToLowerInvariant()} distortion for the current face.",
    };

    public string FaceDistortionRefreshButtonText => HasFaceDistortionSummary
        ? "Recompute Distortion"
        : "Analyze Face";

    public string FaceDistortionStatusText => !HasUsableSourceModelAsset && HasLoadedModel
        ? "Distortion analysis is unavailable until the OpenGeometry mesh source asset is restored."
        : FaceDistortionSummary is null
        ? "Distortion values appear after a face is triangulated and analyzed."
        : $"{DistortionModeLabel} mode across {FaceDistortionSummary.SampleCount} mesh samples.";

    public string MeasuredFaceTitle => SelectedFaceDetails.Count == 1
        ? $"{SelectedFaceDetails[0].BodyName} / Face {SelectedFaceDetails[0].FaceIndex}"
        : "No measured face";

    public string MeasuredFaceSubtitle => SelectedFaceDetails.Count == 1
        ? $"{SelectedFaceDetails[0].FaceType} - Area {SelectedFaceDetails[0].Area:0.###} mm²"
        : "Select a single face to populate distortion stats.";

    public string FaceDistortionAverageText => FaceDistortionSummary is null
        ? "--"
        : FaceDistortionSummary.Average.ToString("0.###");

    public string FaceDistortionMaximumText => FaceDistortionSummary is null
        ? "--"
        : FaceDistortionSummary.Maximum.ToString("0.###");

    public string FaceDistortionRangeText => FaceDistortionSummary is null
        ? "--"
        : $"{FaceDistortionSummary.Minimum:0.###} to {FaceDistortionSummary.Maximum:0.###}";

    public string FaceDistortionSampleCountText => FaceDistortionSummary is null
        ? "--"
        : FaceDistortionSummary.SampleCount.ToString();

    public string FaceDistortionSeverityLabel => CreateFaceDistortionAssessment()?.SeverityLabel ?? "--";

    public string FaceDistortionSurfaceBehaviorSummary
        => CreateFaceDistortionAssessment()?.SurfaceBehaviorSummary
           ?? "Select a single face to evaluate how well it will flatten.";

    public string FaceDistortionModeSummary
        => CreateFaceDistortionAssessment()?.ModeSummary
           ?? (!HasUsableSourceModelAsset && HasLoadedModel
               ? "OpenGeometry mesh source asset is required before distortion analysis can run."
               : "Distortion mode comparison appears after analysis completes.");

    public async Task RefreshFaceDistortionAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedFaces.Count != 1)
        {
            StatusText = "Measure requires one face";
            return;
        }

        if (!HasUsableSourceModelAsset)
        {
            StatusText = "OpenGeometry mesh source asset required";
            return;
        }

        StatusText = $"Analyzing {DistortionModeLabel.ToLowerInvariant()} distortion";

        var nextCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previousCancellationTokenSource = _distortionRefreshCancellationTokenSource;
        _distortionRefreshCancellationTokenSource = nextCancellationTokenSource;
        previousCancellationTokenSource?.Cancel();
        previousCancellationTokenSource?.Dispose();

        await RefreshDistortionAsync(nextCancellationTokenSource, TimeSpan.Zero).ConfigureAwait(true);

        StatusText = HasFaceDistortionSummary
            ? $"Distortion updated: {FaceDistortionSeverityLabel}"
            : "Distortion unavailable";
    }

    private void RequestDistortionRefresh(TimeSpan? delay = null)
    {
        var nextCancellationTokenSource = new CancellationTokenSource();
        var previousCancellationTokenSource = _distortionRefreshCancellationTokenSource;
        _distortionRefreshCancellationTokenSource = nextCancellationTokenSource;
        previousCancellationTokenSource?.Cancel();
        previousCancellationTokenSource?.Dispose();

        _ = RefreshDistortionAsync(nextCancellationTokenSource, delay ?? TimeSpan.Zero);
    }

    private async Task RefreshDistortionAsync(CancellationTokenSource cancellationTokenSource, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationTokenSource.Token).ConfigureAwait(true);

            if (!HasUsableSourceModelAsset
                || string.IsNullOrWhiteSpace(_sourceModelPath)
                || SelectedFaces.Count != 1)
            {
                SetDistortionData(string.Empty);
                FaceDistortionSummary = null;
                return;
            }

            var result = await _editor3DOperationService.ComputeFaceDistortionAsync(
                _sourceModelPath,
                SelectedFaces[0],
                GetDistortionModeValue(),
                cancellationTokenSource.Token).ConfigureAwait(true);

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.DistortionJson))
            {
                SetDistortionData(string.Empty);
                FaceDistortionSummary = null;
                return;
            }

            SetDistortionData(result.DistortionJson);
            FaceDistortionSummary = TryCreateFaceDistortionSummary(result.DistortionJson);
        }
        catch (OperationCanceledException)
        {
            // A newer selection or mode change superseded this distortion request.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh viewport distortion state.");
            SetDistortionData(string.Empty);
            FaceDistortionSummary = null;
        }
        finally
        {
            if (ReferenceEquals(_distortionRefreshCancellationTokenSource, cancellationTokenSource))
                _distortionRefreshCancellationTokenSource = null;

            cancellationTokenSource.Dispose();
        }
    }

    private void SetDistortionData(string payload)
    {
        payload ??= string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
            FaceDistortionSummary = null;

        if (string.Equals(_distortionDataJson, payload, StringComparison.Ordinal))
            return;

        _distortionDataJson = payload;
        RequestViewportScript(BuildSetFaceDistortionScript(payload));
    }

    private static EditorFaceDistortionSummary? TryCreateFaceDistortionSummary(string distortionJson)
    {
        using var document = JsonDocument.Parse(distortionJson);
        if (!document.RootElement.TryGetProperty("distortion", out var valuesElement)
            || valuesElement.ValueKind != JsonValueKind.Array)
            return null;

        var values = valuesElement
            .EnumerateArray()
            .Where(static value => value.ValueKind == JsonValueKind.Number)
            .Select(static value => value.GetDouble())
            .ToArray();

        if (values.Length == 0)
            return null;

        var minimum = values.Min();
        var maximum = values.Max();
        return new EditorFaceDistortionSummary(
            values.Length,
            minimum,
            maximum,
            values.Average(),
            maximum - minimum);
    }

    private EditorFaceDistortionAssessment? CreateFaceDistortionAssessment()
    {
        if (FaceDistortionSummary is not { } summary)
            return null;

        var faceType = SelectedFaceDetails.Count == 1
            ? SelectedFaceDetails[0].FaceType
            : null;

        var isDevelopableLike = summary.Maximum <= NegligibleDistortionThreshold
                                && faceType is not null
                                && (string.Equals(faceType, "Plane", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(faceType, "Cylinder", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(faceType, "Cone", StringComparison.OrdinalIgnoreCase));

        var severityLabel = isDevelopableLike
            ? "Developable"
            : summary.Maximum switch
            {
                <= NegligibleDistortionThreshold => "Very Low",
                <= LowDistortionThreshold => "Low",
                <= ModerateDistortionThreshold => "Moderate",
                _ => "High",
            };

        var surfaceBehaviorSummary = isDevelopableLike
            ? $"{faceType} face behaves as a developable surface in the OpenGeometry mesh flattening pass."
            : summary.Maximum <= NegligibleDistortionThreshold
                ? "Sampled distortion stays near zero across the selected face."
                : $"Sampled distortion ranges from {summary.Minimum:0.###} to {summary.Maximum:0.###} across the selected face.";

        var modeSummary = $"{DistortionModeLabel} mode: average {summary.Average:0.###}, spread {summary.Spread:0.###}.";

        return new EditorFaceDistortionAssessment(
            severityLabel,
            surfaceBehaviorSummary,
            modeSummary,
            isDevelopableLike);
    }
}
