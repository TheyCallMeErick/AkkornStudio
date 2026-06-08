using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace AkkornStudio.UI.Controls;

public enum CanvasViewportPointerReleaseKind
{
    None,
    PanEnded,
    MarqueeCompleted,
}

public sealed class CanvasViewportInteractionHost
{
    private readonly CanvasViewportController _viewportController = new();
    private readonly CanvasViewportGesturePolicy _gesturePolicy;
    private Point _marqueeStartCanvas;
    private Point _marqueeCurrentCanvas;
    private bool _isSpacePanArmed;

    public CanvasViewportInteractionHost(CanvasViewportGesturePolicy gesturePolicy)
    {
        _gesturePolicy = gesturePolicy;
    }

    public bool IsMarqueeSelecting { get; private set; }

    public Rect MarqueeCanvasRegion =>
        new(
            Math.Min(_marqueeStartCanvas.X, _marqueeCurrentCanvas.X),
            Math.Min(_marqueeStartCanvas.Y, _marqueeCurrentCanvas.Y),
            Math.Abs(_marqueeCurrentCanvas.X - _marqueeStartCanvas.X),
            Math.Abs(_marqueeCurrentCanvas.Y - _marqueeStartCanvas.Y));

    public bool HandlePointerWheel(ICanvasViewportState viewport, Control host, PointerWheelEventArgs e)
    {
        _viewportController.ZoomAtPointer(viewport, host, e);
        e.Handled = true;
        return true;
    }

    public bool HandlePointerPressed(ICanvasViewportState viewport, Control host, PointerPressedEventArgs e)
    {
        PointerPointProperties pointerProperties = e.GetCurrentPoint(host).Properties;
        bool isPanGesture = CanvasViewportGestureDecisions.IsPanGesture(
            _gesturePolicy,
            pointerProperties,
            e.KeyModifiers,
            _isSpacePanArmed);

        if (isPanGesture)
        {
            _viewportController.BeginPan(host, e.Pointer, e.GetPosition(host));
            e.Handled = true;
            return true;
        }

        if (!pointerProperties.IsLeftButtonPressed)
            return false;

        Point screen = e.GetPosition(host);
        _marqueeStartCanvas = _viewportController.ScreenToCanvas(viewport, screen);
        _marqueeCurrentCanvas = _marqueeStartCanvas;
        IsMarqueeSelecting = true;
        e.Pointer.Capture(host);
        e.Handled = true;
        return true;
    }

    public bool HandlePointerMoved(ICanvasViewportState viewport, Control host, PointerEventArgs e, out bool marqueeChanged)
    {
        marqueeChanged = false;

        if (_viewportController.TryPan(viewport, host, e))
            return true;

        if (!IsMarqueeSelecting)
            return false;

        _marqueeCurrentCanvas = _viewportController.ScreenToCanvas(viewport, e.GetPosition(host));
        marqueeChanged = true;
        return true;
    }

    public CanvasViewportPointerReleaseKind HandlePointerReleased(PointerReleasedEventArgs e)
    {
        if (_viewportController.EndPan(e))
            return CanvasViewportPointerReleaseKind.PanEnded;

        if (!IsMarqueeSelecting)
            return CanvasViewportPointerReleaseKind.None;

        IsMarqueeSelecting = false;
        e.Pointer.Capture(null);
        return CanvasViewportPointerReleaseKind.MarqueeCompleted;
    }

    public void CancelMarquee()
    {
        IsMarqueeSelecting = false;
    }

    public void SetSpacePanArmed(bool isArmed)
    {
        _isSpacePanArmed = isArmed;
    }
}
