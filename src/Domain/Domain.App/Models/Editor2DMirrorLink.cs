namespace Domain.App.Models;

/// <summary>
/// Transient relationship metadata for a mirrored source/copy pair.
/// Geometry remains independent; this record only supports link discovery and unlinking.
/// </summary>
public sealed record Editor2DMirrorLink(
    string PartnerPathId,
    Editor2DPoint AxisStart,
    Editor2DPoint AxisEnd,
    bool Mirror = true);
