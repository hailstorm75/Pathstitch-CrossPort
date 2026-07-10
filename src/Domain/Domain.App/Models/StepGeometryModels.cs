using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record StepGeometryProtocolInfo(
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("backend")] string Backend,
    [property: JsonPropertyName("backendVersion")] string BackendVersion,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities);

public sealed record StepGeometryDocument(
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("sourceUnits")] string SourceUnits,
    [property: JsonPropertyName("linearTolerance")] double LinearTolerance,
    [property: JsonPropertyName("angularTolerance")] double AngularTolerance,
    [property: JsonPropertyName("bodies")] IReadOnlyList<StepBodyTopology> Bodies,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<string> Diagnostics);

public sealed record StepBodyTopology(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("orientation")] string Orientation,
    [property: JsonPropertyName("shells")] IReadOnlyList<StepShellTopology> Shells,
    [property: JsonPropertyName("faces")] IReadOnlyList<StepFaceTopology> Faces,
    [property: JsonPropertyName("wires")] IReadOnlyList<StepWireTopology> Wires,
    [property: JsonPropertyName("edges")] IReadOnlyList<StepEdgeTopology> Edges);

public sealed record StepShellTopology(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("orientation")] string Orientation,
    [property: JsonPropertyName("faceIds")] IReadOnlyList<string> FaceIds);

public sealed record StepWireTopology(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("orientation")] string Orientation,
    [property: JsonPropertyName("edges")] IReadOnlyList<StepOrientedEdgeReference> Edges);

public sealed record StepOrientedEdgeReference(
    [property: JsonPropertyName("edgeId")] string EdgeId,
    [property: JsonPropertyName("orientation")] string Orientation);

public sealed record StepFaceTopology(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("orientation")] string Orientation,
    [property: JsonPropertyName("surfaceKind")] string SurfaceKind,
    [property: JsonPropertyName("surfaceParameters")] IReadOnlyDictionary<string, double> SurfaceParameters,
    [property: JsonPropertyName("wireIds")] IReadOnlyList<string> WireIds,
    [property: JsonPropertyName("edgeIds")] IReadOnlyList<string> EdgeIds,
    [property: JsonPropertyName("area")] double Area);

public sealed record StepEdgeTopology(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("orientation")] string Orientation,
    [property: JsonPropertyName("curveKind")] string CurveKind,
    [property: JsonPropertyName("firstParameter")] double FirstParameter,
    [property: JsonPropertyName("lastParameter")] double LastParameter,
    [property: JsonPropertyName("curveData")] StepCurveGeometry CurveData,
    [property: JsonPropertyName("adjacentFaceIds")] IReadOnlyList<string> AdjacentFaceIds,
    [property: JsonPropertyName("pcurves")] IReadOnlyList<StepPCurve> PCurves);

public sealed record StepPCurve(
    [property: JsonPropertyName("faceId")] string FaceId,
    [property: JsonPropertyName("firstParameter")] double FirstParameter,
    [property: JsonPropertyName("lastParameter")] double LastParameter,
    [property: JsonPropertyName("curveKind")] string CurveKind,
    [property: JsonPropertyName("curveData")] StepCurveGeometry CurveData);

public sealed record StepCurveGeometry(
    [property: JsonPropertyName("scalars")] IReadOnlyDictionary<string, double> Scalars,
    [property: JsonPropertyName("poles")] IReadOnlyList<StepPoint3D> Poles,
    [property: JsonPropertyName("knots")] IReadOnlyList<double> Knots,
    [property: JsonPropertyName("multiplicities")] IReadOnlyList<int> Multiplicities,
    [property: JsonPropertyName("weights")] IReadOnlyList<double> Weights);

public sealed record StepPoint3D(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("z")] double Z);

public sealed record StepGeometryImportResult(
    bool IsSuccess,
    string Message,
    StepGeometryProtocolInfo? Protocol = null,
    StepGeometryDocument? Document = null,
    string? ViewportJson = null,
    IReadOnlyList<Body3D>? ViewportBodies = null,
    GeometryKernelFailure? Failure = null);
