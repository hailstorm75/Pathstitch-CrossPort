namespace Domain.App.Models;

/// <summary>
/// Immutable 2D affine transform. Matrix uses x' = M11*x + M21*y + OffsetX
/// and y' = M12*x + M22*y + OffsetY.
/// </summary>
public sealed record Editor2DAffineTransform(
    double M11,
    double M12,
    double M21,
    double M22,
    double OffsetX,
    double OffsetY)
{
    public static Editor2DAffineTransform Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsFinite
        => double.IsFinite(M11) && double.IsFinite(M12)
           && double.IsFinite(M21) && double.IsFinite(M22)
           && double.IsFinite(OffsetX) && double.IsFinite(OffsetY);

    public double Determinant => (M11 * M22) - (M12 * M21);

    public Editor2DPoint TransformPoint(Editor2DPoint point)
        => new(
            (M11 * point.X) + (M21 * point.Y) + OffsetX,
            (M12 * point.X) + (M22 * point.Y) + OffsetY);

    public Editor2DPoint TransformVector(Editor2DPoint vector)
        => new(
            (M11 * vector.X) + (M21 * vector.Y),
            (M12 * vector.X) + (M22 * vector.Y));

    public double TransformDirectionDegrees(double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var direction = TransformVector(new Editor2DPoint(Math.Cos(radians), Math.Sin(radians)));
        var transformed = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;
        transformed %= 360.0;
        return transformed < 0.0 ? transformed + 360.0 : transformed;
    }

    public bool TryGetUniformScale(out double scale)
    {
        var scaleX = Math.Sqrt((M11 * M11) + (M12 * M12));
        var scaleY = Math.Sqrt((M21 * M21) + (M22 * M22));
        var orthogonality = (M11 * M21) + (M12 * M22);
        var isUniform = Math.Abs(scaleX - scaleY) <= 1e-9
                        && Math.Abs(orthogonality) <= 1e-9;
        scale = isUniform ? (scaleX + scaleY) / 2.0 : 1.0;
        return isUniform;
    }

    public Editor2DAffineTransform Then(Editor2DAffineTransform next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return new Editor2DAffineTransform(
            (next.M11 * M11) + (next.M21 * M12),
            (next.M12 * M11) + (next.M22 * M12),
            (next.M11 * M21) + (next.M21 * M22),
            (next.M12 * M21) + (next.M22 * M22),
            (next.M11 * OffsetX) + (next.M21 * OffsetY) + next.OffsetX,
            (next.M12 * OffsetX) + (next.M22 * OffsetY) + next.OffsetY);
    }

    public static Editor2DAffineTransform CreateTranslation(double deltaX, double deltaY)
        => new(1, 0, 0, 1, deltaX, deltaY);

    public static Editor2DAffineTransform CreateScale(Editor2DPoint pivot, double factor)
        => new(
            factor, 0, 0, factor,
            pivot.X - (factor * pivot.X),
            pivot.Y - (factor * pivot.Y));

    public static Editor2DAffineTransform CreateRotation(Editor2DPoint pivot, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new Editor2DAffineTransform(
            cosine,
            sine,
            -sine,
            cosine,
            pivot.X - (cosine * pivot.X) + (sine * pivot.Y),
            pivot.Y - (sine * pivot.X) - (cosine * pivot.Y));
    }
}
