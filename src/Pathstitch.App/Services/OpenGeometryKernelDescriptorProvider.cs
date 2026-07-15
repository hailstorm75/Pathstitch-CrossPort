using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class OpenGeometryKernelDescriptorProvider : IGeometryKernelDescriptorProvider
{
    public GeometryKernelDescriptor Current { get; } = new(
        DisplayName: "OpenGeometry",
        ImplementationName: "OpenGeometry WASM bridge",
        RuntimeSummary: "The Avalonia editor uses the OpenGeometry CAD kernel surface through a local WASM worker and a managed mesh bridge for imported source models.",
        CapabilitySummary: "OBJ/STL mesh import, STEP/STP B-rep import through the packaged OCCT worker, mesh and B-rep projection, OpenGeometry-backed 2D thickness outlines, distortion metrics, flattening, DXF preview, and generated OpenGeometry workflows run without the previous native CAD runtime.",
        RequirementSummary: "No Python worker, conda environment, bundled Python runtime, or previous CAD-kernel native runtime is required. Node/npm restore the OpenGeometry package and run the local WASM worker.",
        SupportedSourceModelSummary: "Supported source files: .obj, .stl, .step, and .stp. STEP/STP files use the packaged OCCT B-rep worker; OBJ/STL files use the OpenGeometry mesh bridge.");
}
