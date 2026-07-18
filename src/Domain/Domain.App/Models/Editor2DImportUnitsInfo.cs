namespace Domain.App.Models;

public sealed record Editor2DImportUnitsInfo(
    string SourcePath,
    int? InsUnitsCode,
    double? MillimetersPerDrawingUnit,
    double Width,
    double Height,
    bool HasMalformedUnitDeclaration = false)
{
    private static readonly double[] StandardScaleFactors =
    [
        RequiredDxfScale(5),
        RequiredDxfScale(1),
        RequiredDxfScale(6),
        1.0 / RequiredDxfScale(5),
        1.0 / RequiredDxfScale(1),
        1.0 / RequiredDxfScale(6),
    ];

    public double MaxDimension => Math.Max(Width, Height);

    // Metres are deliberately weak: ezdxf and several exporters commonly stamp
    // code 6 onto millimetre drawings. Other non-mm official declarations are
    // unusual enough to require an explicit physical-scale choice.
    public bool HasStrongUnitDeclaration
        => InsUnitsCode is { } code
           && code != EditorLengthUnits.MillimeterInsUnitsCode
           && code != 6
           && EditorLengthUnits.TryGetDxfUnit(code, out _);

    public bool RequiresPrompt
        => MaxDimension > 0.0
           && (HasMalformedUnitDeclaration
               || HasStrongUnitDeclaration
               || MaxDimension > 2000.0
               || MaxDimension < 1.0);

    public IReadOnlyList<double> GetScaleFactorChoices()
    {
        var choices = new List<double> { 1.0 };
        if (HasStrongUnitDeclaration
            && MillimetersPerDrawingUnit is { } declaredFactor
            && double.IsFinite(declaredFactor)
            && declaredFactor > 0.0
            && Math.Abs(declaredFactor - 1.0) > 1e-9)
        {
            choices.Add(declaredFactor);
        }

        foreach (var factor in StandardScaleFactors)
        {
            if (choices.All(choice => Math.Abs(choice - factor) > 1e-9))
                choices.Add(factor);
        }

        return choices;
    }

    public double RecommendedScaleFactor
    {
        get
        {
            var choices = GetScaleFactorChoices();
            if (HasStrongUnitDeclaration
                && MillimetersPerDrawingUnit is { } declaredFactor
                && choices.Any(choice => Math.Abs(choice - declaredFactor) <= 1e-9))
            {
                return declaredFactor;
            }

            if (MaxDimension is <= 0.0 or >= 1.0 and <= 2000.0)
                return 1.0;

            const double targetDimension = 150.0;
            var bestFactor = 1.0;
            var bestError = Math.Abs(MaxDimension - targetDimension);
            foreach (var factor in choices)
            {
                var scaledDimension = MaxDimension * factor;
                if (scaledDimension is < 1.0 or > 2000.0)
                    continue;

                var error = Math.Abs(scaledDimension - targetDimension);
                if (error >= bestError)
                    continue;

                bestError = error;
                bestFactor = factor;
            }

            return bestFactor;
        }
    }

    public string DeclaredUnit => EditorLengthUnits.DxfUnitName(InsUnitsCode);

    private static double RequiredDxfScale(int code)
        => EditorLengthUnits.MillimetersPerDrawingUnit(code)
           ?? throw new InvalidOperationException($"Missing DXF unit definition for code {code}.");
}
