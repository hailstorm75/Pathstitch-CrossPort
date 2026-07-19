using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record StepPoint2D(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y);

public sealed record StepGeometryProvenance(
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("bodyIds")] IReadOnlyList<string> BodyIds,
    [property: JsonPropertyName("faceIds")] IReadOnlyList<string> FaceIds,
    [property: JsonPropertyName("edgeIds")] IReadOnlyList<string> EdgeIds);

public sealed record StepCurve2D(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("closed")] bool Closed,
    [property: JsonPropertyName("geometry")] StepCurve2DGeometry Geometry,
    [property: JsonPropertyName("displayApproximation")] StepCurveApproximation2D? DisplayApproximation,
    [property: JsonPropertyName("provenance")] StepGeometryProvenance Provenance);

public sealed record StepCurve2DGeometry(
    [property: JsonPropertyName("scalars")] IReadOnlyDictionary<string, double> Scalars,
    [property: JsonPropertyName("poles")] IReadOnlyList<StepPoint2D> Poles,
    [property: JsonPropertyName("knots")] IReadOnlyList<double> Knots,
    [property: JsonPropertyName("multiplicities")] IReadOnlyList<int> Multiplicities,
    [property: JsonPropertyName("weights")] IReadOnlyList<double> Weights);

public sealed record StepCurveApproximation2D(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("points")] IReadOnlyList<StepPoint2D> Points);

public sealed record StepLoop2D(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("curveIds")] IReadOnlyList<string> CurveIds,
    [property: JsonPropertyName("closed")] bool Closed,
    [property: JsonPropertyName("provenance")] StepGeometryProvenance Provenance);

public sealed record StepOperationGeometry(
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("curves")] IReadOnlyList<StepCurve2D> Curves,
    [property: JsonPropertyName("loops")] IReadOnlyList<StepLoop2D> Loops,
    [property: JsonPropertyName("provenance")] StepGeometryProvenance Provenance,
    [property: JsonPropertyName("isApproximation")] bool IsApproximation,
    [property: JsonPropertyName("approximationReason")] string? ApproximationReason);
