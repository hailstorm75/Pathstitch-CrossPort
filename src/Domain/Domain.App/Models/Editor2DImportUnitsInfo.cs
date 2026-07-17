namespace Domain.App.Models;

public sealed record Editor2DImportUnitsInfo(
    string SourcePath,
    int? InsUnitsCode,
    double? MillimetersPerDrawingUnit,
    double Width,
    double Height)
{
    private static readonly double[] StandardScaleFactors =
        [10.0, 25.4, 1000.0, 0.1, 1.0 / 25.4, 0.001];

    public double MaxDimension => Math.Max(Width, Height);

    public bool HasStrongUnitDeclaration => InsUnitsCode is 1 or 2 or 5 or 10;

    public bool RequiresPrompt
        => MaxDimension > 0.0
           && (HasStrongUnitDeclaration || MaxDimension > 2000.0 || MaxDimension < 1.0);

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
