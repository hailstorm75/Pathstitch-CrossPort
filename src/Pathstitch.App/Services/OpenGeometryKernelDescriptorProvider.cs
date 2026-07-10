using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class OpenGeometryKernelDescriptorProvider : IGeometryKernelDescriptorProvider
{
    public GeometryKernelDescriptor Current { get; } = new(
        DisplayName: "OpenGeometry",
        ImplementationName: "OpenGeometry WASM bridge",
        RuntimeSummary: "The Avalonia editor uses the OpenGeometry CAD kernel surface through a local WASM worker and a managed mesh bridge for imported source models.",
        CapabilitySummary: "OBJ/STL mesh import, mesh projection, OpenGeometry-backed 2D thickness outlines, projection-based mesh distortion metrics, triangle-preserving flattening, DXF preview, and generated OpenGeometry workflows run without the previous native CAD runtime.",
        RequirementSummary: "No Python worker, conda environment, bundled Python runtime, or previous CAD-kernel native runtime is required. Node/npm restore the OpenGeometry package and run the local WASM worker.",
        SupportedSourceModelSummary: "Supported mesh source files: .obj, .stl. STEP import will be enabled when OpenGeometry exposes a STEP import API for desktop use.");
}
