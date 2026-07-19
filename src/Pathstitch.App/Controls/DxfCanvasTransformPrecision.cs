using System;
using System.Globalization;

namespace Pathstitch.App.Controls;

internal enum DxfCanvasPrecisionAxis
{
    X,
    Y,
}

internal enum DxfCanvasTransformPrecisionKind
{
    None,
    X,
    Y,
    Rotation,
}

internal readonly record struct DxfCanvasTransformPrecisionState(
    double AppliedX,
    double AppliedY,
    double CumulativeRotation)
{
    public static DxfCanvasTransformPrecisionState Empty { get; } = new(0.0, 0.0, 0.0);

    public bool TryApplyTranslationTotal(
        DxfCanvasPrecisionAxis axis,
        string? rawTotal,
        out DxfCanvasTransformPrecisionState next,
        out double correction)
    {
        next = this;
        correction = 0.0;
        if (!TryParseFinite(rawTotal, out var target))
            return false;

        var applied = axis == DxfCanvasPrecisionAxis.X ? AppliedX : AppliedY;
        correction = target - applied;
        next = axis == DxfCanvasPrecisionAxis.X
            ? this with { AppliedX = target }
            : this with { AppliedY = target };
        return true;
    }

    public bool TryApplyAbsoluteRotation(
        string? rawAngle,
        out DxfCanvasTransformPrecisionState next,
        out double correction)
    {
        next = this;
        correction = 0.0;
        if (!TryParseFinite(rawAngle, out var parsedTarget))
            return false;

        var target = WrapRotation(parsedTarget);
        var current = WrapRotation(CumulativeRotation);
        correction = target - current;
        next = this with { CumulativeRotation = target };
        return true;
    }

    public DxfCanvasTransformPrecisionState ResetForSelection()
        => Empty;

    public DxfCanvasTransformPrecisionState RecordTranslationDrag(
        DxfCanvasPrecisionAxis axis,
        double appliedTotal)
        => axis == DxfCanvasPrecisionAxis.X
            ? this with { AppliedX = appliedTotal }
            : this with { AppliedY = appliedTotal };

    public DxfCanvasTransformPrecisionState RecordRotationDrag(double modelDeltaDegrees)
        => this with { CumulativeRotation = WrapRotation(CumulativeRotation + modelDeltaDegrees) };

    public static string FormatTranslation(double value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string FormatRotation(double value)
        => WrapRotation(value).ToString("0.0", CultureInfo.InvariantCulture);

    public static double WrapRotation(double angleDegrees)
    {
        if (!double.IsFinite(angleDegrees))
            return 0.0;

        var wrapped = angleDegrees % 360.0;
        return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
    }

    private static bool TryParseFinite(string? rawValue, out double value)
        => double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           && double.IsFinite(value);
}
