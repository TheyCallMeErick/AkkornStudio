using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AkkornStudio.UI.Controls;

public sealed class CanvasViewportController
{
    private const double MinZoomMagnitude = 1e-6;
    private Point _panStartScreen;

    public bool IsPanning { get; private set; }

    public Point ScreenToCanvas(ICanvasViewportState viewport, Point screen) =>
        new(
            (screen.X - viewport.PanOffset.X) / NormalizeZoom(viewport.Zoom),
            (screen.Y - viewport.PanOffset.Y) / NormalizeZoom(viewport.Zoom)
        );

    public void SyncVisuals(
        ICanvasViewportState viewport,
        Control scene,
        DotGridBackground grid,
        Size viewportSize)
    {
        scene.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        scene.RenderTransform = new TransformGroup
        {
            Children =
            [
                new ScaleTransform(viewport.Zoom, viewport.Zoom),
                new TranslateTransform(viewport.PanOffset.X, viewport.PanOffset.Y),
            ],
        };

        grid.Width = viewportSize.Width;
        grid.Height = viewportSize.Height;
        grid.Zoom = viewport.Zoom;
        grid.PanOffset = viewport.PanOffset;
        grid.InvalidateVisual();
    }

    public void ZoomAtPointer(ICanvasViewportState viewport, Control host, PointerWheelEventArgs e)
    {
        double factor = e.Delta.Y > 0 ? 1.10 : 0.91;
        viewport.ZoomToward(e.GetPosition(host), factor);
    }

    public void BeginPan(Control host, IPointer pointer, Point screen)
    {
        IsPanning = true;
        _panStartScreen = screen;
        pointer.Capture(host);
    }

    public bool TryPan(ICanvasViewportState viewport, Control host, PointerEventArgs e)
    {
        if (!IsPanning)
            return false;

        Point screen = e.GetPosition(host);
        Vector delta = screen - _panStartScreen;
        _panStartScreen = screen;
        viewport.PanOffset = viewport.PanOffset + delta;
        return true;
    }

    public bool EndPan(PointerReleasedEventArgs e)
    {
        if (!IsPanning)
            return false;

        IsPanning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        return true;
    }

    internal static double NormalizeZoom(double zoom) =>
        Math.Abs(zoom) < MinZoomMagnitude ? 1.0 : zoom;
}
