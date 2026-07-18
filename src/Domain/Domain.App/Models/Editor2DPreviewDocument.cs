using System.Text.Json.Serialization;

namespace Domain.App.Models;

public enum Editor2DTool
{
    Select = 0,
    Move = 1,
    Pan = 2,
    Measure = 3,
    SketchLine = 4,
    SketchRectangle = 5,
    SketchCircle = 6,
    SketchPolygon = 7,
    SketchText = 8,
    Pen = 9,
    Scale = 10,
    Mirror = 11,
    Dimension = 12,
    Trim = 13,
    Fillet = 14,
    Chamfer = 15,
    ConvertLines = 16,
    Offset = 17,
    AddThickness = 18,
    Cleanup = 19,
    Patterning = 20,
    PaperFolding = 21,
    AddSewingHoles = 22,
}

public sealed record Editor2DPoint(double X, double Y);

/// <summary>
/// Physical world-space basis for text local X/Y coordinates. Columns U and V preserve
/// arbitrary shear, reflection, and non-uniform affine transforms exactly.
/// </summary>
public sealed record Editor2DTextBasis(
    double Ux,
    double Uy,
    double Vx,
    double Vy)
{
    [JsonIgnore]
    public bool IsFinite
        => double.IsFinite(Ux)
           && double.IsFinite(Uy)
           && double.IsFinite(Vx)
           && double.IsFinite(Vy);

    [JsonIgnore]
    public double Determinant => (Ux * Vy) - (Uy * Vx);
}

public sealed record Editor2DBezierAnchor(
    Editor2DPoint Point,
    Editor2DPoint? HandleIn = null,
    Editor2DPoint? HandleOut = null);

public sealed record Editor2DPreviewPath(
    string Id,
    string EntityType,
    IReadOnlyList<Editor2DPoint> Points,
    bool IsClosed,
    bool IsAxisAlignedRectangle = false,
    Editor2DPoint? Start = null,
    string? Text = null,
    double? TextHeight = null,
    double? RotationDegrees = null,
    double? WidthFactor = null,
    Editor2DPoint? Center = null,
    double? Radius = null,
    double? StartAngleDegrees = null,
    double? EndAngleDegrees = null,
    string? FontFamily = null,
    double CharacterSpacing = 0.0,
    bool IsBold = false,
    bool IsItalic = false,
    bool IsUnderline = false,
    IReadOnlyList<Editor2DBezierAnchor>? BezierAnchors = null,
    bool IsFilled = false,
    bool IsConstruction = false,
    string? SourceLayerName = null,
    string? SourceEntityHandle = null,
    IReadOnlyList<IReadOnlyList<Editor2DPoint>>? FillLoops = null,
    Editor2DTextBasis? TextBasis = null);

public sealed record Editor2DMeasurement(
    string Id,
    Editor2DPoint Start,
    Editor2DPoint End,
    bool IsAutoDimension = false,
    string? EntityPathId = null,
    string? DimensionType = null,
    Editor2DPoint? RectP1 = null,
    Editor2DPoint? RectP2 = null,
    double FilletRadius = 0.0,
    double OffsetDistance = 0.0,
    double? PlacementAngleDegrees = null,
    string? VarName = null,
    string? Expression = null,
    bool Driven = false,
    bool IsParametric = false,
    [property: JsonPropertyName("evaluatedValue")] double? EvaluatedValue = null)
{
    public double Distance
        => Math.Sqrt(Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2));
}

public sealed record Editor2DBounds(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY)
{
    public double Width => Math.Max(0.0, MaxX - MinX);

    public double Height => Math.Max(0.0, MaxY - MinY);

    public double CenterX => MinX + (Width / 2.0);

    public double CenterY => MinY + (Height / 2.0);
}

public sealed record Editor2DPreviewDocument(
    IReadOnlyList<Editor2DPreviewPath> Paths,
    Editor2DBounds Bounds,
    IReadOnlyDictionary<string, int> EntityCounts,
    IReadOnlyList<string> UnsupportedEntityTypes);
