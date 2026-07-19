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
        Assert.Equal(5, near);
        Assert.Equal(3, far);
    }

    [Fact]
    public void ProximityFilter_MergesNearbyPitchPlacements()
    {
        var source = Line();
        var filtered = Build(source, new(
            Pitch: 1,
            Margin: 2,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            ProximityFilterEnabled: true,
            ProximityFilterDistance: 3));
        var unfiltered = Build(source, new(
            Pitch: 1,
            Margin: 2,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            ProximityFilterEnabled: false,
            ProximityFilterDistance: 3));

        Assert.Equal(7, filtered.Count);
        Assert.Equal(21, unfiltered.Count);
    }

    [Fact]
    public void ProximityFilter_CountModePreservesExactRequestedCount()
    {
        var holes = Build(Line(), new(
            Margin: 2,
            DistributionMode: Editor2DSewingDistributionMode.Count,
            Count: 5,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            ProximityFilterEnabled: true,
            ProximityFilterDistance: 100));

        Assert.Equal(5, holes.Count);
    }

    [Fact]
    public void LineProximityFilter_RejectsCrossingLine()
    {
        var source = Line();
        var crossing = new Editor2DPreviewPath(
            "crossing", "LINE", [new(10.75, -5), new(10.75, 5)], false);
        var document = new Editor2DPreviewDocument(
            [source, crossing], new(-10, -10, 40, 40), new Dictionary<string, int>(), []);
        var parameters = new Editor2DSewingHoleParameters(
            Pitch: 5,
            Margin: 2,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            ProximityFilterEnabled: false,
            LineProximityFilterEnabled: true,
            LineProximityThreshold: 1);

        var filtered = Editor2DSewingHoleGeometry.BuildPreview(document, [source.Id], parameters, "filtered");
        var unfiltered = Editor2DSewingHoleGeometry.BuildPreview(
            document, [source.Id], parameters with { LineProximityFilterEnabled = false }, "unfiltered");

        Assert.Equal(4, filtered.Count);
        Assert.Equal(5, unfiltered.Count);
        Assert.DoesNotContain(filtered, hole => Math.Abs(hole.Center!.X - 10) < 1e-8);
    }

    [Fact]
    public void HoleRadius_RejectsNearbyObstacleWhenLineFilterIsDisabled()
    {
        var source = Line();
        var obstacle = new Editor2DPreviewPath(
            "nearby", "LINE", [new(10.25, -5), new(10.25, 5)], false);
        var document = new Editor2DPreviewDocument(
            [source, obstacle], new(-10, -10, 40, 40), new Dictionary<string, int>(), []);
        var holes = Editor2DSewingHoleGeometry.BuildPreview(
            document,
            [source.Id],
            new Editor2DSewingHoleParameters(
                Diameter: 1,
                Pitch: 5,
                Margin: 2,
                CornerMode: Editor2DSewingCornerMode.Continuous,
                LineProximityFilterEnabled: false),
            "radius-filter");

        Assert.Equal(4, holes.Count);
        Assert.DoesNotContain(holes, hole => Math.Abs(hole.Center!.X - 10) < 1e-8);
    }

    [Fact]
    public void ClosedObstacle_RejectsContainedHoleWhenLineFilterIsDisabled()
    {
        var source = Line();
        var obstacle = new Editor2DPreviewPath(
            "closed-obstacle",
            "LWPOLYLINE",
            [new(8, 1), new(12, 1), new(12, 3), new(8, 3)],
            true);
        var document = new Editor2DPreviewDocument(
            [source, obstacle], new(-10, -10, 40, 40), new Dictionary<string, int>(), []);
        var holes = Editor2DSewingHoleGeometry.BuildPreview(
            document,
            [source.Id],
            new Editor2DSewingHoleParameters(
                Diameter: 1,
                Pitch: 5,
                Margin: 2,
                CornerMode: Editor2DSewingCornerMode.Continuous,
                LineProximityFilterEnabled: false),
            "closed-filter");

        Assert.Equal(4, holes.Count);
        Assert.DoesNotContain(holes, hole => Math.Abs(hole.Center!.X - 10) < 1e-8);
    }

    [Fact]
    public void ExistingCircle_UsesConfiguredProximityDistance()
    {
        var source = Line();
        var existing = new Editor2DPreviewPath(
            "existing", "CIRCLE", [], true, Center: new(10, 2), Radius: 0.5);
        var document = new Editor2DPreviewDocument(
            [source, existing], new(-10, -10, 40, 40), new Dictionary<string, int>(), []);
        var holes = Editor2DSewingHoleGeometry.BuildPreview(
            document,
            [source.Id],
            new Editor2DSewingHoleParameters(
                Pitch: 5,
                Margin: 2,
                CornerMode: Editor2DSewingCornerMode.Continuous,
                ProximityFilterEnabled: true,
                LineProximityFilterEnabled: false,
                ProximityFilterDistance: 1),
            "circle-filter");

        Assert.Equal(4, holes.Count);
        Assert.DoesNotContain(holes, hole => Math.Abs(hole.Center!.X - 10) < 1e-8);
    }

    [Fact]
    public void OuterRoundedCorner_ChangesOffsetCurveArcLengthSampling()
    {
        var polyline = new Editor2DPreviewPath(
            "corner",
            "LWPOLYLINE",
            [new(0, 0), new(10, 0), new(10, 10)],
            false);
        var parameters = new Editor2DSewingHoleParameters(
            Margin: 2,
            CornerMode: Editor2DSewingCornerMode.Continuous,
            DistributionMode: Editor2DSewingDistributionMode.Count,
            Count: 3,
            Side: Editor2DSewingSide.Right,
            OffsetCornerFillet: true);

        var rounded = Build(polyline, parameters);
        var sharp = Build(polyline, parameters with { OffsetCornerFillet = false });

        Assert.Equal(3, rounded.Count);
        Assert.Equal(3, sharp.Count);
        Assert.Equal(12, sharp[1].Center!.X, 8);
        Assert.Equal(-2, sharp[1].Center!.Y, 8);
        var roundedMiddle = rounded[1].Center!;
        var distanceFromCorner = Math.Sqrt(
            Math.Pow(roundedMiddle.X - 10, 2) + Math.Pow(roundedMiddle.Y, 2));
        Assert.Equal(2, distanceFromCorner, 6);
        Assert.True(Math.Abs(roundedMiddle.X - 12) > 1e-3 || Math.Abs(roundedMiddle.Y + 2) > 1e-3);
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
