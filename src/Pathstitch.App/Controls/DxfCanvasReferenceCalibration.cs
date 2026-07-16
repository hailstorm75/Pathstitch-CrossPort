using System;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal static class DxfCanvasReferenceCalibration
{
    public static double Distance(Editor2DPoint start, Editor2DPoint end)
        => Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));

    public static Editor2DPoint Midpoint(Editor2DPoint start, Editor2DPoint end)
        => new((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
}
