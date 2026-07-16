using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Tests;

public sealed class SewingHolePatternGeometryTests
{
    [Fact]
    public void Saddle_OpenLine_UsesTwoHalfPitchStaggeredRows()
    {
        var holes = Build(Line(), new(
            Pitch: 4,
            Margin: 2,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            SymmetricDistribution: true,
            Pattern: Editor2DSewingPattern.Saddle,
            SaddleSpacing: 3));

        AssertCenters(holes, 0.5, [0, 4, 8, 12, 16, 20]);
        AssertCenters(holes, 3.5, [2, 6, 10, 14, 18]);
    }

    [Fact]
    public void Saddle_OpenCountMode_IgnoresPhase()
    {
        var holes = Build(Line(), new(
            Margin: 2,
            DistributionMode: Editor2DSewingDistributionMode.Count,
            Count: 5,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            Pattern: Editor2DSewingPattern.Saddle,
            SaddleSpacing: 3));

        AssertCenters(holes, 0.5, [0, 5, 10, 15, 20]);
        AssertCenters(holes, 3.5, [0, 5, 10, 15, 20]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedSide_IsWindingIndependent(bool clockwise)
    {
        var points = clockwise
            ? new Editor2DPoint[] { new(0, 0), new(0, 10), new(10, 10), new(10, 0) }
            : [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        var square = new Editor2DPreviewPath("square", "LWPOLYLINE", points, true);
        var left = Build(square, new(
            Margin: 2,
            DistributionMode: Editor2DSewingDistributionMode.Count,
            Count: 4,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            Side: Editor2DSewingSide.Left));
        var right = Build(square, new(
            Margin: 2,
            DistributionMode: Editor2DSewingDistributionMode.Count,
            Count: 4,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            Side: Editor2DSewingSide.Right));

        Assert.All(left, hole => Assert.InRange(hole.Center!.X, 0, 10));
        Assert.All(left, hole => Assert.InRange(hole.Center!.Y, 0, 10));
        Assert.All(right, hole => Assert.True(
            hole.Center!.X < 0 || hole.Center.X > 10 || hole.Center.Y < 0 || hole.Center.Y > 10));
    }

    [Fact]
    public void ClosedCircle_LeftIsInnerAndRightIsOuter()
    {
        var circle = new Editor2DPreviewPath(
            "circle", "CIRCLE", [], true, Center: new(5, 5), Radius: 10);
        var left = Build(circle, new(
            Margin: 2,
            DistributionMode: Editor2DSewingDistributionMode.Count,
            Count: 8,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            Side: Editor2DSewingSide.Left));
        var right = Build(circle, new(
            Margin: 2,
            DistributionMode: Editor2DSewingDistributionMode.Count,
            Count: 8,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            Side: Editor2DSewingSide.Right));

        Assert.All(left, hole => Assert.Equal(8, RadiusFrom(hole.Center!, circle.Center!), 8));
        Assert.All(right, hole => Assert.Equal(12, RadiusFrom(hole.Center!, circle.Center!), 8));
    }

    [Fact]
    public void BothSaddle_ProducesFourRows()
    {
        var holes = Build(Line(), new(
            Pitch: 4,
            Margin: 2,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            Pattern: Editor2DSewingPattern.Saddle,
            Side: Editor2DSewingSide.Both,
            SaddleSpacing: 3));

        Assert.Equal([-3.5, -0.5, 0.5, 3.5], holes.Select(hole => hole.Center!.Y).Distinct().Order().ToArray());
        Assert.Equal(22, holes.Count);
    }

    [Fact]
    public void SaddleGapBeyondMargin_ReflectsNearRowAndClampsZero()
    {
        var reflected = Build(Line(), new(
            Pitch: 4,
            Margin: 1,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            Pattern: Editor2DSewingPattern.Saddle,
            SaddleSpacing: 4));
        var clamped = Build(Line(), new(
            Pitch: 4,
            Margin: 1,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            Pattern: Editor2DSewingPattern.Saddle,
            SaddleSpacing: 2));

        Assert.Equal([1.0, 3.0], reflected.Select(hole => hole.Center!.Y).Distinct().Order().ToArray());
        Assert.Equal([0.25, 2.0], clamped.Select(hole => hole.Center!.Y).Distinct().Order().ToArray());
    }

    [Fact]
    public void Saddle_ForcedPolylineCornersIgnoreLongitudinalPhase()
    {
        var polyline = new Editor2DPreviewPath(
            "polyline",
            "LWPOLYLINE",
            [new(0, 0), new(10, 0), new(10, 10)],
            false);
        var holes = Build(polyline, new(
            Pitch: 5,
            Margin: 2,
            CornerMode: Editor2DSewingCornerMode.IncludeCorners,
            Pattern: Editor2DSewingPattern.Saddle,
            SaddleSpacing: 2));

        var near = holes.Where(hole => Math.Abs(DistanceToPath(hole.Center!, polyline.Points) - 1) < 1e-6).Count();
        var far = holes.Where(hole => Math.Abs(DistanceToPath(hole.Center!, polyline.Points) - 3) < 1e-6).Count();
        Assert.Equal(near, far);
    }

    private static IReadOnlyList<Editor2DPreviewPath> Build(
        Editor2DPreviewPath source,
        Editor2DSewingHoleParameters parameters)
        => Editor2DSewingHoleGeometry.BuildPreview(
            new Editor2DPreviewDocument([source], new(-10, -10, 40, 40), new Dictionary<string, int>(), []),
            [source.Id], parameters, "golden");

    private static Editor2DPreviewPath Line()
        => new("line", "LINE", [new(0, 0), new(20, 0)], false);

    private static void AssertCenters(
        IReadOnlyList<Editor2DPreviewPath> holes,
        double y,
        double[] expectedX)
    {
        var actual = holes
            .Where(hole => Math.Abs(hole.Center!.Y - y) < 1e-8)
            .Select(hole => hole.Center!.X)
            .Order()
            .ToArray();
        Assert.Equal(expectedX, actual);
    }

    private static double RadiusFrom(Editor2DPoint point, Editor2DPoint center)
        => Math.Sqrt(Math.Pow(point.X - center.X, 2) + Math.Pow(point.Y - center.Y, 2));

    private static double DistanceToPath(Editor2DPoint point, IReadOnlyList<Editor2DPoint> points)
        => Enumerable.Range(0, points.Count - 1).Min(index =>
        {
            var start = points[index];
            var end = points[index + 1];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared <= 1e-12 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
            var x = start.X + t * dx;
            var y = start.Y + t * dy;
            return Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2));
        });
}
