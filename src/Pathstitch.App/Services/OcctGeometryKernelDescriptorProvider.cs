using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class OcctGeometryKernelDescriptorProvider : IGeometryKernelDescriptorProvider
{
    public GeometryKernelDescriptor Current { get; } = new(
        DisplayName: "OpenCASCADE .NET",
        ImplementationName: "Occt.NET",
        RuntimeSummary: "The Avalonia editor loads OpenCASCADE directly in-process through the Occt.NET NuGet package.",
        CapabilitySummary: "STEP/OBJ/STL import, projection, separate-piece flattening, distortion analysis, and DXF preview all run natively inside the .NET editor.",
        RequirementSummary: "No Python worker, conda environment, or bundled Python runtime is required for the Avalonia editor.",
        SupportedSourceModelSummary: "Supported 3D source files: .step, .stp, .obj, .stl.");
}
