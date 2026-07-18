using System;
using System.Collections.Generic;
using System.Linq;
using Domain.App.Models;

namespace Pathstitch.App.Services;

internal static class SvgCoordinateSystem
{
    public static double WorldToSvgY(double worldY) => -worldY;

    public static double WorldBoundsToSvgMinY(Editor2DBounds bounds) => -bounds.MaxY;

    public static IReadOnlyList<DxfPreviewPath> SvgPathsToPositiveWorld(
        IReadOnlyList<DxfPreviewPath> paths)
    {
        var points = paths.SelectMany(static path => path.Points).ToArray();
        if (points.Length == 0)
            return paths;

        const double targetMinimum = 10.0;
        var minX = points.Min(static point => point.X);
        var maxSvgY = points.Max(static point => point.Y);

        DxfPoint Transform(DxfPoint point)
            => new(point.X - minX + targetMinimum, maxSvgY - point.Y + targetMinimum);

        return paths.Select(path => path with
        {
            Points = path.Points.Select(Transform).ToArray(),
            FillLoops = path.FillLoops?.Select(loop => (IReadOnlyList<DxfPoint>)loop.Select(Transform).ToArray()).ToArray(),
            Start = path.Start is { } start ? Transform(start) : null,
            Center = path.Center is { } center ? Transform(center) : null,
            RotationDegrees = path.RotationDegrees is { } rotation ? -rotation : null,
        }).ToArray();
    }
}