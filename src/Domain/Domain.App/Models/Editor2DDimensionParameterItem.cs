namespace Domain.App.Models;

public sealed record Editor2DDimensionParameterItem(
    string Id,
    string Name,
    string ExpressionDisplay,
    string ValueDisplay,
    bool IsDriven);
