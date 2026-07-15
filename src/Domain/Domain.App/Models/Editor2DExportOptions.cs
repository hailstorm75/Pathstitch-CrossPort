namespace Domain.App.Models;

public sealed record Editor2DExportOptions(
    int SvgPrecision = 3,
    double SvgStrokeWidth = 0.5)
{
    public static Editor2DExportOptions Defaults { get; } = new();

    public int NormalizedSvgPrecision => Math.Clamp(SvgPrecision, 0, 15);

    public double NormalizedSvgStrokeWidth
        => double.IsFinite(SvgStrokeWidth) ? Math.Max(0.0, SvgStrokeWidth) : Defaults.SvgStrokeWidth;
}
