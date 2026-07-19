using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging;

namespace Pathstitch.App.Services;

public sealed class OpenGeometryEditor2DGeometryKernelService(
    ILogger<OpenGeometryEditor2DGeometryKernelService> logger,
    OpenGeometryKernelBridge openGeometryKernelBridge) : IEditor2DGeometryKernelService
{
    private const double PointTolerance = 1e-6;
    private const int CircleSegmentCount = 48;

    private readonly ILogger<OpenGeometryEditor2DGeometryKernelService> _logger = logger;
    private readonly OpenGeometryKernelBridge _openGeometryKernelBridge = openGeometryKernelBridge;

    public async Task<Editor2DGeometryKernelResult> BuildBooleanPathsAsync(
        IReadOnlyList<Editor2DPreviewPath> sourcePaths,
        Editor2DBooleanOperation operation,
        CancellationToken cancellationToken = default)
    {
        var orderedSourcePaths = sourcePaths
            .Where(static path => path.IsClosed && path.Points.Count >= 3)
            .OrderByDescending(path => operation == Editor2DBooleanOperation.Subtract
                ? Math.Abs(CalculateSignedArea(path.Points))
                : 0.0)
            .ToArray();
        var candidates = orderedSourcePaths
            .Select(static path => new DxfPolyline(
                path.Points.Select(static point => new DxfPoint(point.X, point.Y)).ToArray(), true))
            .ToArray();
        if (candidates.Length < 2)
            return Editor2DGeometryKernelResult.Failure("Select at least two closed paths.");

        var result = await _openGeometryKernelBridge.TryBooleanAsync(
            candidates,
            operation switch
            {
                Editor2DBooleanOperation.Union => "union",
                Editor2DBooleanOperation.Subtract => "subtract",
                Editor2DBooleanOperation.Intersect => "intersect",
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            },
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            return Editor2DGeometryKernelResult.Failure(result.Error ?? "OpenGeometry returned no boolean result.");

        var paths = result.Paths
            .Select(path => NormalizePoints(path.Points, true))
            .Where(static points => points.Count >= 3)
            .Select(points => new Editor2DPreviewPath(
                $"opengeometry-boolean-{Guid.NewGuid():N}", "LWPOLYLINE", points, true,
                Editor2DGeometry.IsAxisAlignedRectangle(points, true)))
            .ToArray();
        return paths.Length == 0
            ? Editor2DGeometryKernelResult.Failure("OpenGeometry returned no usable boolean paths.")
            : Editor2DGeometryKernelResult.Success(paths);
    }

    public async Task<Editor2DGeometryKernelResult> BuildCurveOffsetPathsAsync(
        IReadOnlyList<Editor2DPreviewPath> sourcePaths,
        double offsetDistance,
        bool offsetOutward,
        CancellationToken cancellationToken = default)
    {
        var offsettablePaths = new List<(Editor2DPreviewPath SourcePath, DxfCurveOffset Curve)>();
        foreach (var path in sourcePaths.Where(Editor2DGeometry.IsCurveOffsettablePath))
        {
            if (TryCreateCurveOffset(path, offsetDistance, offsetOutward, out var curve))
                offsettablePaths.Add((path, curve));
        }

        if (offsettablePaths.Count == 0)
            return Editor2DGeometryKernelResult.Success([]);

        var curves = offsettablePaths.Select(static path => path.Curve).ToArray();

        var result = await _openGeometryKernelBridge
            .TryOffsetCurvesAsync(curves, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogDebug("OpenGeometry 2D curve offset failed: {Error}", result.Error);
            return Editor2DGeometryKernelResult.Failure(result.Error ?? "OpenGeometry worker returned no 2D curve offset result.");
        }

        var paths = new List<Editor2DPreviewPath>();
        for (var index = 0; index < result.Paths.Count; index++)
        {
            var sourcePath = offsettablePaths[Math.Min(index, offsettablePaths.Count - 1)].SourcePath;
            var offsetPath = result.Paths[index];
            var points = NormalizePoints(offsetPath.Points, offsetPath.IsClosed);
            if (points.Count < (offsetPath.IsClosed ? 3 : 2))
                continue;

            paths.Add(new Editor2DPreviewPath(
                Id: $"{sourcePath.Id}:offset:{Guid.NewGuid():N}",
                EntityType: !offsetPath.IsClosed && points.Count == 2 ? "LINE" : "LWPOLYLINE",
                Points: points,
                IsClosed: offsetPath.IsClosed,
                IsAxisAlignedRectangle: Editor2DGeometry.IsAxisAlignedRectangle(points, offsetPath.IsClosed)));
        }

        return paths.Count == 0
            ? Editor2DGeometryKernelResult.Failure("OpenGeometry returned no usable curve offset paths.")
            : Editor2DGeometryKernelResult.Success(paths);
    }

    private static bool TryCreateCurveOffset(
        Editor2DPreviewPath path,
        double offsetDistance,
        bool offsetOutward,
        out DxfCurveOffset curve)
    {
        if (TryCreateArcOrCircleOffset(path, offsetDistance, offsetOutward, out curve))
            return true;

        if (path.Points.Count < 2)
        {
            curve = default!;
            return false;
        }

        var isClosed = path.IsClosed && path.Points.Count >= 3;
        curve = new DxfCurveOffset(
            Kind: "polyline",
            Points: path.Points.Select(static point => new DxfPoint(point.X, point.Y)).ToArray(),
            IsClosed: isClosed,
            Distance: ResolveSignedOffsetDistance(path.Points, isClosed, offsetDistance, offsetOutward));
        return true;
    }

    private static bool TryCreateArcOrCircleOffset(
        Editor2DPreviewPath path,
        double offsetDistance,
        bool offsetOutward,
        out DxfCurveOffset curve)
    {
        var isCircle = path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase);
        var isArc = path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase);
        if ((!isCircle && !isArc)
            || path.Center is not Editor2DPoint center
            || path.Radius is not double radius
            || radius <= PointTolerance)
        {
            curve = default!;
            return false;
        }

        var radialDistance = offsetOutward
            ? Math.Max(Math.Abs(offsetDistance), PointTolerance)
            : -Math.Max(Math.Abs(offsetDistance), PointTolerance);
        if (radius + radialDistance <= PointTolerance)
        {
            curve = default!;
            return false;
        }

        var startAngleDegrees = isCircle ? 0.0 : path.StartAngleDegrees;
        var endAngleDegrees = isCircle ? 360.0 : path.EndAngleDegrees;
        if (startAngleDegrees is not double resolvedStartAngleDegrees
            || endAngleDegrees is not double resolvedEndAngleDegrees)
        {
            curve = default!;
            return false;
        }

        var sweepDegrees = isCircle
            ? 360.0
            : NormalizeAngleSweepDegrees(resolvedStartAngleDegrees, resolvedEndAngleDegrees);
        var segments = isCircle
            ? CircleSegmentCount
            : Math.Max(12, (int)Math.Ceiling(sweepDegrees / 15.0));

        curve = new DxfCurveOffset(
            Kind: isCircle ? "circle" : "arc",
            Points: [],
            IsClosed: isCircle,
            Distance: -radialDistance,
            Center: new DxfPoint(center.X, center.Y),
            Radius: radius,
            StartAngleDegrees: resolvedStartAngleDegrees,
            EndAngleDegrees: isCircle ? 360.0 : resolvedStartAngleDegrees + sweepDegrees,
            Segments: segments);
        return true;
    }

    public async Task<Editor2DGeometryKernelResult> BuildThicknessOutlinesAsync(
        IReadOnlyList<Editor2DPreviewPath> sourcePaths,
        double thickness,
        CancellationToken cancellationToken = default)
    {
        var polylines = sourcePaths
            .Where(Editor2DGeometry.IsThicknessSourcePath)
            .Select(static path => new DxfPolyline(
                path.Points.Select(static point => new DxfPoint(point.X, point.Y)).ToArray(),
                IsClosed: false))
            .Where(static polyline => polyline.Points.Count >= 2)
            .ToArray();

        if (polylines.Length == 0)
            return Editor2DGeometryKernelResult.Success([]);

        var result = await _openGeometryKernelBridge
            .TryOffsetPolylinesAsync(polylines, thickness, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogDebug("OpenGeometry 2D thickness failed: {Error}", result.Error);
            return Editor2DGeometryKernelResult.Failure(result.Error ?? "OpenGeometry worker returned no 2D thickness result.");
        }

        var paths = ConvertOffsetRegionsToPaths(result.Regions);
        return paths.Count == 0
            ? Editor2DGeometryKernelResult.Failure("OpenGeometry returned no usable thickness outline regions.")
            : Editor2DGeometryKernelResult.Success(paths);
    }

    private static IReadOnlyList<Editor2DPreviewPath> ConvertOffsetRegionsToPaths(
        IReadOnlyList<OpenGeometryOffsetRegion> regions)
    {
        if (regions.Count == 0)
            return [];

        var paths = new List<Editor2DPreviewPath>();
        foreach (var region in regions)
        {
            AddOffsetLoop(paths, region.Outer, "opengeometry-thickness");
            foreach (var hole in region.Holes)
                AddOffsetLoop(paths, hole, "opengeometry-thickness-hole");
        }

        return paths;
    }

    private static void AddOffsetLoop(
        ICollection<Editor2DPreviewPath> paths,
        IReadOnlyList<OpenGeometryPoint> points,
        string idPrefix)
    {
        var loop = NormalizePoints(points, isClosed: true);
        if (loop.Count < 3)
            return;

        paths.Add(new Editor2DPreviewPath(
            Id: $"{idPrefix}-{Guid.NewGuid():N}",
            EntityType: "LWPOLYLINE",
            Points: loop,
            IsClosed: true,
            IsAxisAlignedRectangle: Editor2DGeometry.IsAxisAlignedRectangle(loop, isClosed: true)));
    }

    private static IReadOnlyList<Editor2DPoint> NormalizePoints(IReadOnlyList<OpenGeometryPoint> points, bool isClosed)
    {
        var loop = new List<Editor2DPoint>(points.Count);
        foreach (var point in points)
        {
            var next = new Editor2DPoint(point.X, point.Y);
            if (loop.Count == 0 || !AreSamePoint(loop[^1], next))
                loop.Add(next);
        }

        if (isClosed && loop.Count > 1 && AreSamePoint(loop[0], loop[^1]))
            loop.RemoveAt(loop.Count - 1);

        return loop;
    }

    private static double ResolveSignedOffsetDistance(
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed,
        double offsetDistance,
        bool offsetOutward)
    {
        var magnitude = Math.Max(Math.Abs(offsetDistance), PointTolerance);
        if (!isClosed)
            return offsetOutward ? magnitude : -magnitude;

        var signedArea = CalculateSignedArea(points);
        var outwardUsesLeftNormal = signedArea < 0.0;
        return offsetOutward == outwardUsesLeftNormal ? magnitude : -magnitude;
    }

    private static double CalculateSignedArea(IReadOnlyList<Editor2DPoint> points)
    {
        if (points.Count < 3)
            return 0.0;

        var area = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var nextIndex = (index + 1) % points.Count;
            area += (points[index].X * points[nextIndex].Y) - (points[nextIndex].X * points[index].Y);
        }

        return area / 2.0;
    }

    private static double NormalizeAngleSweepDegrees(double startAngleDegrees, double endAngleDegrees)
    {
        var sweep = NormalizeAngleDegrees(endAngleDegrees) - NormalizeAngleDegrees(startAngleDegrees);
        while (sweep <= 0.0)
            sweep += 360.0;

        return sweep;
    }

    private static double NormalizeAngleDegrees(double angleDegrees)
    {
        var normalized = angleDegrees % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static bool AreSamePoint(Editor2DPoint left, Editor2DPoint right)
        => Math.Abs(left.X - right.X) <= PointTolerance
           && Math.Abs(left.Y - right.Y) <= PointTolerance;
}
