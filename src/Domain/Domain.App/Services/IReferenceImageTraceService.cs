using Domain.App.Models;

namespace Domain.App.Services;

/// <summary>
/// Converts thresholded reference-image pixels into closed contours expressed
/// in source-image pixel coordinates. The workspace applies calibration and
/// transforms before adding the contours to editable geometry layers.
/// </summary>
public interface IReferenceImageTraceService
{
    IReadOnlyList<IReadOnlyList<Editor2DPoint>> TraceContours(
        string imageDataBase64,
        Editor2DReferenceImageTraceOptions options);
}

public sealed record Editor2DReferenceImageTraceOptions(
    double Threshold = 0.5,
    double Tolerance = 50.0,
    double CornerSmoothness = 50.0,
    double PathOptimization = 50.0,
    bool SilhouetteOnly = false);

public interface IReferenceImageBackgroundRemovalService
{
    string? RemoveBackground(string imageDataBase64);
}
