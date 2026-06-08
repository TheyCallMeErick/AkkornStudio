using Avalonia;

namespace AkkornStudio.UI.Controls;

public sealed class CanvasViewportSelectionNavigationController
{
    public bool TryCenterSelection(ICanvasViewportSelectionState selectionState, Size viewportSize)
    {
        if (!TryGetSelectionBounds(selectionState, out CanvasSelectionViewportMath.SelectionBounds bounds))
            return false;

        selectionState.PanOffset = CanvasSelectionViewportMath.ComputeCenterPan(bounds, viewportSize, selectionState.Zoom);
        return true;
    }

    public bool TryFitSelection(
        ICanvasViewportSelectionState selectionState,
        Size viewportSize,
        double padding,
        double minZoom = 0.15,
        double maxZoom = 4.0)
    {
        if (!TryGetSelectionBounds(selectionState, out CanvasSelectionViewportMath.SelectionBounds bounds))
            return false;

        (double zoom, Point pan) = CanvasSelectionViewportMath.ComputeFit(
            bounds,
            viewportSize,
            padding,
            minZoom,
            maxZoom);
        selectionState.Zoom = zoom;
        selectionState.PanOffset = pan;
        return true;
    }

    private static bool TryGetSelectionBounds(
        ICanvasViewportSelectionState selectionState,
        out CanvasSelectionViewportMath.SelectionBounds bounds)
    {
        if (!selectionState.TryGetSelectionFrame(0, out Rect frame)
            || frame.Width <= 0
            || frame.Height <= 0)
        {
            bounds = default;
            return false;
        }

        bounds = CanvasSelectionViewportMath.FromRect(frame);
        return true;
    }
}
