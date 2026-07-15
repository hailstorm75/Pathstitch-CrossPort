using Domain.App.Models;

namespace Pathstitch.App.Controls;

internal static class DxfCanvasMeasurementEditing
{
    public static Editor2DMeasurement MoveEndpoint(
        Editor2DMeasurement measurement,
        Editor2DPoint point,
        bool start)
        => start
            ? measurement with { Start = point }
            : measurement with { End = point };
}
