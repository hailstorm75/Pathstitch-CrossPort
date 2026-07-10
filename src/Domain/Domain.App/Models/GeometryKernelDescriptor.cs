namespace Domain.App.Models;

public sealed record GeometryKernelDescriptor(
    string DisplayName,
    string ImplementationName,
    string RuntimeSummary,
    string CapabilitySummary,
    string RequirementSummary,
    string SupportedSourceModelSummary);
