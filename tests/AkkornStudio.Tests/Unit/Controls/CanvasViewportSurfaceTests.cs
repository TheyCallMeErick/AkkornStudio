using Avalonia;
using Avalonia.Controls;
using AkkornStudio.UI.Controls;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class CanvasViewportSurfaceTests
{
    [Fact]
    public void ArrangeOverride_SynchronizesSceneAndOverlayCanvasContentSizes()
    {
        var surface = new CanvasViewportSurface();
        var sceneContent = new Canvas();
        var overlayContent = new Canvas();

        surface.SceneContent = sceneContent;
        surface.OverlayContent = overlayContent;
        surface.Measure(new Size(1280, 720));
        surface.Arrange(new Rect(0, 0, 1280, 720));

        Assert.Equal(20000, sceneContent.Width);
        Assert.Equal(20000, sceneContent.Height);
        Assert.Equal(1280, overlayContent.Width);
        Assert.Equal(720, overlayContent.Height);
    }
}
