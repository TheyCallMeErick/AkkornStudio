using Avalonia;

namespace AkkornStudio.UI.Controls;

public sealed class CanvasViewportSelectionAdornerController
{
    public void SyncFocusAdorner(
        ICanvasViewportSelectionState selectionState,
        CanvasFocusAdorner? focusAdorner,
        double padding = 12d)
    {
        if (focusAdorner is null)
            return;

        if (!selectionState.TryGetSelectionFrame(padding, out Rect frame))
        {
            focusAdorner.FocusRect = default;
            return;
        }

        focusAdorner.FocusRect = ProjectCanvasRect(selectionState, frame);
    }

    public void SyncMarqueeAdorner(
        ICanvasViewportState viewport,
        CanvasViewportInteractionHost interactionHost,
        CanvasMarqueeAdorner? marqueeAdorner)
    {
        if (marqueeAdorner is null)
            return;

        if (!interactionHost.IsMarqueeSelecting)
        {
            marqueeAdorner.SelectionRect = default;
            return;
        }

        SyncMarqueeAdorner(viewport, interactionHost.MarqueeCanvasRegion, marqueeAdorner);
    }

    public bool CompleteMarqueeSelection(
        ICanvasViewportSelectionState selectionState,
        CanvasViewportInteractionHost interactionHost,
        CanvasMarqueeAdorner? marqueeAdorner,
        double minimumSize = 8d)
    {
        if (marqueeAdorner is not null)
            marqueeAdorner.SelectionRect = default;

        return CompleteMarqueeSelection(selectionState, interactionHost.MarqueeCanvasRegion, marqueeAdorner, minimumSize);
    }

    public void SyncMarqueeAdorner(
        ICanvasViewportState viewport,
        Rect marqueeCanvasRegion,
        CanvasMarqueeAdorner? marqueeAdorner)
    {
        if (marqueeAdorner is null)
            return;

        if (marqueeCanvasRegion.Width <= 0 || marqueeCanvasRegion.Height <= 0)
        {
            marqueeAdorner.SelectionRect = default;
            return;
        }

        marqueeAdorner.SelectionRect = ProjectCanvasRect(viewport, marqueeCanvasRegion);
    }

    public bool CompleteMarqueeSelection(
        ICanvasViewportSelectionState selectionState,
        Rect region,
        CanvasMarqueeAdorner? marqueeAdorner,
        double minimumSize = 8d)
    {
        if (marqueeAdorner is not null)
            marqueeAdorner.SelectionRect = default;

        if (region.Width < minimumSize || region.Height < minimumSize)
        {
            selectionState.ClearSelection();
            return false;
        }

        return selectionState.TrySelectInRegion(region);
    }

    private static Rect ProjectCanvasRect(ICanvasViewportState viewport, Rect canvasRect)
    {
        return new Rect(
            canvasRect.X * viewport.Zoom + viewport.PanOffset.X,
            canvasRect.Y * viewport.Zoom + viewport.PanOffset.Y,
            canvasRect.Width * viewport.Zoom,
            canvasRect.Height * viewport.Zoom);
    }
}
