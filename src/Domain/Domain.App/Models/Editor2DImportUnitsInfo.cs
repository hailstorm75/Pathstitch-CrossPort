namespace Domain.App.Models;

public sealed record Editor2DImportUnitsInfo(
    string SourcePath,
    int? InsUnitsCode,
    double? MillimetersPerDrawingUnit,
    double Width,
    double Height)
{
    public double MaxDimension => Math.Max(Width, Height);

    public bool RequiresPrompt
        => InsUnitsCode is 1 or 2 or 5 or 10 || MaxDimension > 2000.0 || MaxDimension < 1.0;

    public string DeclaredUnit => InsUnitsCode switch
    {
        1 => "inches",
        2 => "feet",
        3 => "miles",
        4 => "millimetres",
        5 => "centimetres",
        6 => "metres",
        10 => "yards",
        null or 0 => "unitless",
        _ => "CAD units",
    };
}
