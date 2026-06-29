namespace Domain.App.Models;

public sealed record EditorFaceDistortionSummary(
    int SampleCount,
    double Minimum,
    double Maximum,
    double Average,
    double Spread);
