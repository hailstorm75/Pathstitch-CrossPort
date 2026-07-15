namespace Domain.App.Models;

public sealed record Editor2DExportOptions(
    int SvgPrecision = 3,
    double SvgStrokeWidth = 0.5,
    bool IncludeMeasurementLines = false,
    string DxfVersion = "R2010")
{
    public static Editor2DExportOptions Defaults { get; } = new();

    public static IReadOnlyList<string> DxfVersionOptions { get; } = ["R2018", "R2013", "R2010", "R2007", "R2000"];

    public int NormalizedSvgPrecision => Math.Clamp(SvgPrecision, 0, 15);

    public double NormalizedSvgStrokeWidth
        => double.IsFinite(SvgStrokeWidth) ? Math.Max(0.0, SvgStrokeWidth) : Defaults.SvgStrokeWidth;

    public string NormalizedDxfVersion
        => DxfVersionOptions.Contains(DxfVersion, StringComparer.OrdinalIgnoreCase)
            ? DxfVersion.ToUpperInvariant()
            : Defaults.DxfVersion;
}
