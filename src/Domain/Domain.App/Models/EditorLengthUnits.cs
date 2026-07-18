using UnitsNet;

namespace Domain.App.Models;

public sealed record EditorLengthUnitDefinition(
    int InsUnitsCode,
    string Name,
    double MillimetersPerDrawingUnit);

/// <summary>
/// Canonical length-unit contract for editor geometry and DXF interchange.
/// Editor coordinates are millimetres. UnitsNet owns standard conversions;
/// survey variants derive from its exact U.S. survey-foot definition.
/// </summary>
public static class EditorLengthUnits
{
    public const int MillimeterInsUnitsCode = 4;
    public const int MetricMeasurementCode = 1;

    private static readonly IReadOnlyList<EditorLengthUnitDefinition> Definitions =
    [
        Dxf(1, "inches", Length.FromInches(1)),
        Dxf(2, "feet", Length.FromFeet(1)),
        Dxf(3, "miles", Length.FromMiles(1)),
        Dxf(4, "millimetres", Length.FromMillimeters(1)),
        Dxf(5, "centimetres", Length.FromCentimeters(1)),
        Dxf(6, "metres", Length.FromMeters(1)),
        Dxf(7, "kilometres", Length.FromKilometers(1)),
        Dxf(8, "microinches", Length.FromMicroinches(1)),
        Dxf(9, "mils", Length.FromMils(1)),
        Dxf(10, "yards", Length.FromYards(1)),
        Dxf(11, "angstroms", Length.FromAngstroms(1)),
        Dxf(12, "nanometres", Length.FromNanometers(1)),
        Dxf(13, "microns", Length.FromMicrometers(1)),
        Dxf(14, "decimetres", Length.FromDecimeters(1)),
        Dxf(15, "decametres", Length.FromDecameters(1)),
        Dxf(16, "hectometres", Length.FromHectometers(1)),
        Dxf(17, "gigametres", Length.FromGigameters(1)),
        Dxf(18, "astronomical units", Length.FromAstronomicalUnits(1)),
        Dxf(19, "light years", Length.FromLightYears(1)),
        Dxf(20, "parsecs", Length.FromParsecs(1)),
        Dxf(21, "US survey feet", Length.FromUsSurveyFeet(1)),
        Dxf(22, "US survey inches", Length.FromUsSurveyFeet(1.0 / 12.0)),
        Dxf(23, "US survey yards", Length.FromUsSurveyFeet(3)),
        Dxf(24, "US survey miles", Length.FromUsSurveyFeet(5280)),
    ];

    private static readonly IReadOnlyDictionary<int, EditorLengthUnitDefinition> DefinitionsByCode =
        Definitions.ToDictionary(static definition => definition.InsUnitsCode);

    public static IReadOnlyList<EditorLengthUnitDefinition> DxfDefinitions => Definitions;

    public static bool TryGetDxfUnit(int insUnitsCode, out EditorLengthUnitDefinition definition)
        => DefinitionsByCode.TryGetValue(insUnitsCode, out definition!);

    public static double? MillimetersPerDrawingUnit(int insUnitsCode)
        => TryGetDxfUnit(insUnitsCode, out var definition)
            ? definition.MillimetersPerDrawingUnit
            : null;

    public static string DxfUnitName(int? insUnitsCode)
        => insUnitsCode switch
        {
            null or 0 => "unitless",
            { } code when TryGetDxfUnit(code, out var definition) => definition.Name,
            _ => "CAD units",
        };

    public static bool TryConvertExpressionUnitToMillimeters(
        double value,
        string suffix,
        out double millimeters)
    {
        if (!double.IsFinite(value))
        {
            millimeters = 0;
            return false;
        }

        var length = suffix.ToLowerInvariant() switch
        {
            "mm" => Length.FromMillimeters(value),
            "cm" => Length.FromCentimeters(value),
            "m" => Length.FromMeters(value),
            "in" or "inch" or "inches" => Length.FromInches(value),
            _ => (Length?)null,
        };
        millimeters = length?.Millimeters ?? 0;
        return length is not null && double.IsFinite(millimeters);
    }

    private static EditorLengthUnitDefinition Dxf(int code, string name, Length length)
        => new(code, name, length.Millimeters);
}