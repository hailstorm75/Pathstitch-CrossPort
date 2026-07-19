namespace Domain.App.Models;

public sealed record EditorFaceDistortionAssessment(
    string SeverityLabel,
    string SurfaceBehaviorSummary,
    string ModeSummary,
    bool IsDevelopableLike);
