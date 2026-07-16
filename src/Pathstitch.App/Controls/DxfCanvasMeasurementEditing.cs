using Domain.App.Models;
using System.Globalization;

namespace Pathstitch.App.Controls;

internal static class DxfCanvasMeasurementEditing
{
    public static string FormatLabel(Editor2DMeasurement measurement)
    {
        var value = measurement.EvaluatedValue ?? measurement.Distance;
        var formatted = value.ToString("0.00", CultureInfo.InvariantCulture);
        var expression = measurement.Expression?.Trim() ?? string.Empty;
        var isFormula = measurement.IsParametric
            && expression.Length > 0
            && !double.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        if (measurement.Driven)
            return $"({formatted} mm)";
        return isFormula ? $"fx: {formatted}" : $"{formatted} mm";
    }

    public static Editor2DMeasurement MoveEndpoint(
        Editor2DMeasurement measurement,
        Editor2DPoint point,
        bool start)
        => start
            ? measurement with { Start = point, EvaluatedValue = null }
            : measurement with { End = point, EvaluatedValue = null };
}
