using System;
using Avalonia;

namespace Pathstitch.App.Controls;

internal static class DxfCanvasRotationInteraction
{
    internal const double HandleDistancePixels = 100.0;

    public static Point GetHandlePosition(Point pivot, double angleDegrees = 0.0)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(
            pivot.X + (Math.Sin(radians) * HandleDistancePixels),
            pivot.Y - (Math.Cos(radians) * HandleDistancePixels));
    }

    public static double GetScreenCardinalAngle(Point pivot, Point pointer)
    {
        var angle = Math.Atan2(pointer.X - pivot.X, pivot.Y - pointer.Y) * 180.0 / Math.PI;
        return WrapDegrees(angle);
    }

    public static double GetGrabRelativeDelta(Point pivot, Point grabPoint, Point pointer)
        => WrapSignedDegrees(
            GetScreenCardinalAngle(pivot, pointer)
            - GetScreenCardinalAngle(pivot, grabPoint));

    public static bool IsHandleHit(Point pointer, Point handlePosition, double tolerancePixels)
    {
        if (!double.IsFinite(tolerancePixels) || tolerancePixels < 0.0)
            return false;

        var deltaX = pointer.X - handlePosition.X;
        var deltaY = pointer.Y - handlePosition.Y;
        return (deltaX * deltaX) + (deltaY * deltaY) <= tolerancePixels * tolerancePixels;
    }

    private static double WrapDegrees(double angleDegrees)
    {
        var wrapped = angleDegrees % 360.0;
        return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
    }

    private static double WrapSignedDegrees(double angleDegrees)
    {
        var wrapped = WrapDegrees(angleDegrees + 180.0) - 180.0;
        return wrapped == -180.0 && angleDegrees > 0.0 ? 180.0 : wrapped;
    }
}
