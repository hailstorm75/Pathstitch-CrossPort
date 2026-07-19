using System.Text.Json;
using Domain.App.Models;

namespace Pathstitch.App.Tests;

public sealed class Editor2DCornerGeometryTests
{
    private static readonly Editor2DPoint[] RightAngleSource =
    [
        new(-10, 0),
        new(0, 0),
        new(0, 10),
    ];

    [Fact]
    public void G1_RightAngleKeepsCurrentCircularSample()
    {
        var points = ApplyRightAngle(Editor2DFilletContinuity.G1).Points;

        Assert.Equal(13, points.Count);
        AssertPoint(points[1], -2, 0);
        AssertPoint(points[6], -0.585786437627, 0.585786437627);
        AssertPoint(points[11], 0, 2);
    }

    [Fact]
    public void G2_RightAngleMatchesLegacyFourArcGolden()
    {
        var points = ApplyRightAngle(Editor2DFilletContinuity.G2).Points;

        Assert.Equal(43, points.Count);
        AssertPoint(points[1], -2, 0);
        AssertPoint(points[6], -1.576066086126, 0.032029331);
        AssertPoint(points[11], -1.164375, 0.138125);
        AssertPoint(points[16], -0.828025960735, 0.302211051118);
        AssertPoint(points[21], -0.535, 0.535);
        AssertPoint(points[26], -0.302211051118, 0.828025960735);
        AssertPoint(points[31], -0.138125, 1.164375);
        AssertPoint(points[36], -0.032029331, 1.576066086126);
        AssertPoint(points[41], 0, 2);
    }

    [Fact]
    public void ChamferIgnoresContinuity()
    {
        var g1 = ApplyRightAngle(Editor2DFilletContinuity.G1, Editor2DCornerKind.Chamfer);
        var g2 = ApplyRightAngle(Editor2DFilletContinuity.G2, Editor2DCornerKind.Chamfer);

        Assert.Equal(g1.Points, g2.Points);
        Assert.Equal(4, g1.Points.Count);
    }

    [Fact]
    public void MissingContinuityInLegacyJsonDefaultsToG1()
    {
        const string json = """
            {
              "id": "shape:1",
              "pathId": "shape",
              "cornerIndex": 1,
              "kind": 0,
              "value": 2,
              "sourcePoints": [{"X":-10,"Y":0},{"X":0,"Y":0},{"X":0,"Y":10}]
            }
            """;

        var parameter = JsonSerializer.Deserialize<Editor2DCornerParameter>(json);

        Assert.NotNull(parameter);
        Assert.Equal(Editor2DFilletContinuity.G1, parameter.Continuity);
    }

    [Fact]
    public void G2ContinuityRoundTripsThroughJson()
    {
        var parameter = CreateParameter(Editor2DFilletContinuity.G2);

        var restored = JsonSerializer.Deserialize<Editor2DCornerParameter>(JsonSerializer.Serialize(parameter));

        Assert.NotNull(restored);
        Assert.Equal(parameter.Id, restored.Id);
        Assert.Equal(parameter.PathId, restored.PathId);
        Assert.Equal(parameter.CornerIndex, restored.CornerIndex);
        Assert.Equal(parameter.Kind, restored.Kind);
        Assert.Equal(parameter.Value, restored.Value);
        Assert.Equal(parameter.SourcePoints, restored.SourcePoints);
        Assert.Equal(Editor2DFilletContinuity.G2, restored!.Continuity);
    }

    [Fact]
    public void DegenerateAndOpenEndpointCornersStaySharp()
    {
        var straight = new Editor2DPreviewPath("straight", "LWPOLYLINE", [new(-10, 0), new(0, 0), new(10, 0)], false);
        var straightParameter = new Editor2DCornerParameter(
            "straight:1", straight.Id, 1, Editor2DCornerKind.Fillet, 2, straight.Points, Editor2DFilletContinuity.G2);
        var endpointParameter = new Editor2DCornerParameter(
            "straight:0", straight.Id, 0, Editor2DCornerKind.Fillet, 2, straight.Points, Editor2DFilletContinuity.G2);

        Assert.Equal(straight.Points, Editor2DCornerGeometry.Apply(straight, [straightParameter]).Points);
        Assert.Equal(straight.Points, Editor2DCornerGeometry.Apply(straight, [endpointParameter]).Points);
    }

    [Theory]
    [InlineData(Editor2DCornerKind.Fillet, Editor2DFilletContinuity.G1)]
    [InlineData(Editor2DCornerKind.Fillet, Editor2DFilletContinuity.G2)]
    [InlineData(Editor2DCornerKind.Chamfer, Editor2DFilletContinuity.G1)]
    [InlineData(Editor2DCornerKind.Chamfer, Editor2DFilletContinuity.G2)]
    public void LoneOversizedCornerMayReachFarAdjacentVertices(
        Editor2DCornerKind kind,
        Editor2DFilletContinuity continuity)
    {
        var path = new Editor2DPreviewPath("shape", "LWPOLYLINE", RightAngleSource, false);
        var parameter = new Editor2DCornerParameter(
            "shape:1", path.Id, 1, kind, 20, path.Points, continuity);

        var rendered = Editor2DCornerGeometry.Apply(path, [parameter]);

        AssertPoint(rendered.Points[1], -10, 0);
        AssertPoint(rendered.Points[^2], 0, 10);
    }

    [Theory]
    [InlineData(Editor2DCornerKind.Fillet, Editor2DFilletContinuity.G1)]
    [InlineData(Editor2DCornerKind.Fillet, Editor2DFilletContinuity.G2)]
    [InlineData(Editor2DCornerKind.Chamfer, Editor2DFilletContinuity.G1)]
    [InlineData(Editor2DCornerKind.Chamfer, Editor2DFilletContinuity.G2)]
    public void AdjacentOversizedCornersSplitSharedEdgeAtMidpoint(
        Editor2DCornerKind kind,
        Editor2DFilletContinuity continuity)
    {
        Editor2DPoint[] source = [new(0, 0), new(10, 0), new(10, 10), new(20, 10)];
        var path = new Editor2DPreviewPath("double", "LWPOLYLINE", source, false);
        var first = new Editor2DCornerParameter("double:1", path.Id, 1, kind, 20, source, continuity);
        var second = new Editor2DCornerParameter("double:2", path.Id, 2, kind, 20, source, continuity);

        var rendered = Editor2DCornerGeometry.Apply(path, [first, second]);

        Assert.Equal(2, rendered.Points.Count(point =>
            Math.Abs(point.X - 10) <= 1e-9 && Math.Abs(point.Y - 5) <= 1e-9));
    }

    private static Editor2DPreviewPath ApplyRightAngle(
        Editor2DFilletContinuity continuity,
        Editor2DCornerKind kind = Editor2DCornerKind.Fillet)
    {
        var path = new Editor2DPreviewPath("shape", "LWPOLYLINE", RightAngleSource, false);
        return Editor2DCornerGeometry.Apply(path, [CreateParameter(continuity, kind)]);
    }

    private static Editor2DCornerParameter CreateParameter(
        Editor2DFilletContinuity continuity,
        Editor2DCornerKind kind = Editor2DCornerKind.Fillet)
        => new("shape:1", "shape", 1, kind, 2, RightAngleSource, continuity);

    private static void AssertPoint(Editor2DPoint actual, double expectedX, double expectedY)
    {
        Assert.Equal(expectedX, actual.X, 9);
        Assert.Equal(expectedY, actual.Y, 9);
    }
}
