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
        double threshold);
}
