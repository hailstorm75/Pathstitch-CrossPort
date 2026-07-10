using Avalonia;

namespace Pathstitch.App.Controls;

internal sealed class DxfCanvasInteractionSession
{
    internal bool IsPanning;
    internal bool IsMarqueeSelecting;
    internal Point LastPointerPosition;
    internal Point PointerPressPosition;
    internal string? HoveredPathId;
    internal string? PressedPathId;
    internal Point? MarqueeStartPoint;
    internal Point? MarqueeCurrentPoint;
    internal Point HoverPointerPosition;
    internal bool HasHoverPointerPosition;
    internal bool CancelInteractionOnPointerRelease;

    public bool HasActivePointerGesture => IsPanning || IsMarqueeSelecting;

    public void ResetPointerGesture()
    {
        IsPanning = false;
        IsMarqueeSelecting = false;
        PressedPathId = null;
        MarqueeStartPoint = null;
        MarqueeCurrentPoint = null;
        CancelInteractionOnPointerRelease = false;
    }
}
