using Avalonia;
using AkkornStudio.UI.Controls;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class CanvasViewportControllerHardeningTests
{
    [Fact]
    public void NormalizeZoom_WhenZoomIsZero_ReturnsSafeFallback()
    {
        double normalized = CanvasViewportController.NormalizeZoom(0);

        Assert.Equal(1.0, normalized);
    }

    [Fact]
    public void ScreenToCanvas_WhenZoomIsZero_DoesNotDivideByZero()
    {
        var controller = new CanvasViewportController();
        var viewport = new TestViewportState
        {
            Zoom = 0,
            PanOffset = new Point(10, 20),
        };

        Point canvas = controller.ScreenToCanvas(viewport, new Point(110, 220));

        Assert.True(double.IsFinite(canvas.X));
        Assert.True(double.IsFinite(canvas.Y));
        Assert.Equal(100, canvas.X, 6);
        Assert.Equal(200, canvas.Y, 6);
    }

    private sealed class TestViewportState : ICanvasViewportState
    {
        public double Zoom { get; set; } = 1;
        public Point PanOffset { get; set; }

        public void SetViewportSize(double width, double height) { }

        public void ZoomToward(Point screen, double factor) => Zoom *= factor;
    }
}
