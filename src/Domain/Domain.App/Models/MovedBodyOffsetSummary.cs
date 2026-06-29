using System.Globalization;

namespace Domain.App.Models;

public sealed record MovedBodyOffsetSummary(
    int BodyIndex,
    string BodyName,
    double X,
    double Y,
    double Z,
    bool IsSelected)
{
    public string OffsetSummary
        => $"X {Format(X)} / Y {Format(Y)} / Z {Format(Z)}";

    public string DistanceText
        => $"Distance {Math.Sqrt((X * X) + (Y * Y) + (Z * Z)):0.###} mm";

    private static string Format(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
