using Avalonia;

namespace AkkornStudio.UI.Controls;

public interface ICanvasViewportState
{
    double Zoom { get; set; }

    Point PanOffset { get; set; }

    void SetViewportSize(double width, double height);

    void ZoomToward(Point screen, double factor);
}
