using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class OpenGeometryEditor2DGeometryKernelServiceTests
{
    [Fact]
    public async Task BuildCurveOffsetPathsAsync_UsesOpenGeometryPolylineOffset()
    {
        var service = new OpenGeometryEditor2DGeometryKernelService(
            NullLogger<OpenGeometryEditor2DGeometryKernelService>.Instance,
            new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance));

        var result = await service.BuildCurveOffsetPathsAsync(
            [
                new Editor2DPreviewPath(
                    Id: "line",
                    EntityType: "LINE",
                    Points:
                    [
                        new Editor2DPoint(0, 0),
                        new Editor2DPoint(10, 0),
                    ],
                    IsClosed: false),
            ],
            offsetDistance: 2,
            offsetOutward: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var offsetPath = Assert.Single(result.Paths);
        Assert.StartsWith("line:offset:", offsetPath.Id, StringComparison.Ordinal);
        Assert.Equal("LINE", offsetPath.EntityType);
        Assert.False(offsetPath.IsClosed);
        Assert.Equal(2, offsetPath.Points.Count);
        AssertPoint(offsetPath.Points[0], 0, 2);
        AssertPoint(offsetPath.Points[1], 10, 2);
    }

    [Fact]
    public async Task BuildCurveOffsetPathsAsync_ExpandsCounterClockwiseClosedLoopOutward()
    {
        var service = new OpenGeometryEditor2DGeometryKernelService(
            NullLogger<OpenGeometryEditor2DGeometryKernelService>.Instance,
            new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance));

        var result = await service.BuildCurveOffsetPathsAsync(
            [
                new Editor2DPreviewPath(
                    Id: "square",
                    EntityType: "LWPOLYLINE",
                    Points:
                    [
                        new Editor2DPoint(0, 0),
                        new Editor2DPoint(10, 0),
                        new Editor2DPoint(10, 10),
                        new Editor2DPoint(0, 10),
                    ],
                    IsClosed: true),
            ],
            offsetDistance: 1,
            offsetOutward: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var offsetPath = Assert.Single(result.Paths);
        Assert.StartsWith("square:offset:", offsetPath.Id, StringComparison.Ordinal);
        Assert.Equal("LWPOLYLINE", offsetPath.EntityType);
        Assert.True(offsetPath.IsClosed);
        Assert.Equal(4, offsetPath.Points.Count);
        AssertPoint(offsetPath.Points[0], -1, -1);
        AssertPoint(offsetPath.Points[1], 11, -1);
        AssertPoint(offsetPath.Points[2], 11, 11);
        AssertPoint(offsetPath.Points[3], -1, 11);
    }

    [Fact]
    public async Task BuildCurveOffsetPathsAsync_OffsetsCircleThroughOpenGeometryArcPrimitive()
    {
        var service = new OpenGeometryEditor2DGeometryKernelService(
            NullLogger<OpenGeometryEditor2DGeometryKernelService>.Instance,
            new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance));

        var result = await service.BuildCurveOffsetPathsAsync(
            [
                new Editor2DPreviewPath(
                    Id: "circle",
                    EntityType: "CIRCLE",
                    Points: [],
                    IsClosed: true,
                    Center: new Editor2DPoint(0, 0),
                    Radius: 10,
                    StartAngleDegrees: 0,
                    EndAngleDegrees: 360),
            ],
            offsetDistance: 2,
            offsetOutward: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var offsetPath = Assert.Single(result.Paths);
        Assert.StartsWith("circle:offset:", offsetPath.Id, StringComparison.Ordinal);
        Assert.Equal("LWPOLYLINE", offsetPath.EntityType);
        Assert.True(offsetPath.IsClosed);
        Assert.True(offsetPath.Points.Count >= 32);
        Assert.All(offsetPath.Points, point =>
        {
            var radius = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
            Assert.InRange(radius, 11.98, 12.03);
        });
    }

    [Fact]
    public async Task BuildCurveOffsetPathsAsync_OffsetsArcThroughOpenGeometryArcPrimitive()
    {
        var service = new OpenGeometryEditor2DGeometryKernelService(
            NullLogger<OpenGeometryEditor2DGeometryKernelService>.Instance,
            new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance));

        var result = await service.BuildCurveOffsetPathsAsync(
            [
                new Editor2DPreviewPath(
                    Id: "arc",
                    EntityType: "ARC",
                    Points: [],
                    IsClosed: false,
                    Center: new Editor2DPoint(0, 0),
                    Radius: 10,
                    StartAngleDegrees: 0,
                    EndAngleDegrees: 90),
            ],
            offsetDistance: 2,
            offsetOutward: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var offsetPath = Assert.Single(result.Paths);
        Assert.StartsWith("arc:offset:", offsetPath.Id, StringComparison.Ordinal);
        Assert.Equal("LWPOLYLINE", offsetPath.EntityType);
        Assert.False(offsetPath.IsClosed);
        Assert.True(offsetPath.Points.Count >= 7);
        Assert.All(offsetPath.Points, point =>
        {
            var radius = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
            Assert.True(radius > 10.0, $"Expected outward arc point outside radius 10, got {radius}.");
        });
    }

    [Fact]
    public async Task BuildThicknessOutlinesAsync_UsesOpenGeometryOffsetRegions()
    {
        var service = new OpenGeometryEditor2DGeometryKernelService(
            NullLogger<OpenGeometryEditor2DGeometryKernelService>.Instance,
            new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance));

        var result = await service.BuildThicknessOutlinesAsync(
            [
                new Editor2DPreviewPath(
                    Id: "centerline",
                    EntityType: "LWPOLYLINE",
                    Points:
                    [
                        new Editor2DPoint(0, 0),
                        new Editor2DPoint(10, 0),
                        new Editor2DPoint(10, 10),
                    ],
                    IsClosed: false),
            ],
            thickness: 2,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var outline = Assert.Single(result.Paths);
        Assert.StartsWith("opengeometry-thickness-", outline.Id, StringComparison.Ordinal);
        Assert.Equal("LWPOLYLINE", outline.EntityType);
        Assert.True(outline.IsClosed);
        Assert.True(outline.Points.Count >= 6);
        Assert.Contains(outline.Points, point => Math.Abs(point.X - 9) < 1e-6 && Math.Abs(point.Y - 10) < 1e-6);
        Assert.Contains(outline.Points, point => Math.Abs(point.X - 11) < 1e-6 && Math.Abs(point.Y - 10) < 1e-6);
    }

    [Fact]
    public async Task BuildBooleanPathsAsync_UnionPreservesOpenGeometryLoop()
    {
        var result = await CreateService().BuildBooleanPathsAsync(
            [Square("left", 0, 10), Square("right", 5, 10)],
            Editor2DBooleanOperation.Union);

        Assert.True(result.IsSuccess, result.Error);
        var path = Assert.Single(result.Paths);
        Assert.True(path.IsClosed);
        Assert.Equal(4, path.Points.Count);
        Assert.InRange(Math.Abs(SignedArea(path.Points)), 149.99, 150.01);
    }

    [Fact]
    public async Task BuildBooleanPathsAsync_SubtractUsesLargestSelectedPathAsBase()
    {
        var result = await CreateService().BuildBooleanPathsAsync(
            [Square("small", 3, 4), Square("large", 0, 10)],
            Editor2DBooleanOperation.Subtract);

        Assert.True(result.IsSuccess, result.Error);
        var path = Assert.Single(result.Paths);
        Assert.InRange(Math.Abs(SignedArea(path.Points)), 83.99, 84.01);
    }

    private static OpenGeometryEditor2DGeometryKernelService CreateService()
        => new(
            NullLogger<OpenGeometryEditor2DGeometryKernelService>.Instance,
            new OpenGeometryKernelBridge(NullLogger<OpenGeometryKernelBridge>.Instance));

    private static Editor2DPreviewPath Square(string id, double x, double size)
        => new(id, "LWPOLYLINE", [
            new Editor2DPoint(x, 0), new Editor2DPoint(x + size, 0),
            new Editor2DPoint(x + size, size), new Editor2DPoint(x, size)], true);

    private static double SignedArea(IReadOnlyList<Editor2DPoint> points)
        => Enumerable.Range(0, points.Count).Sum(index =>
            (points[index].X * points[(index + 1) % points.Count].Y)
            - (points[(index + 1) % points.Count].X * points[index].Y)) / 2.0;

    private static void AssertPoint(Editor2DPoint actual, double expectedX, double expectedY)
    {
        Assert.True(Math.Abs(actual.X - expectedX) < 1e-6, $"Expected X {expectedX}, got {actual.X}.");
        Assert.True(Math.Abs(actual.Y - expectedY) < 1e-6, $"Expected Y {expectedY}, got {actual.Y}.");
    }
}
