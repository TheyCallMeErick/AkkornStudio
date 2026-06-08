using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using AkkornStudio.UI.Controls;
using Xunit;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class InfiniteCanvasCoreControlTests
{
    [Fact]
    public void PropertyForwarding_SynchronizesViewportAndSceneOverlayContent()
    {
        var core = new InfiniteCanvasCoreControl();
        var viewport = new TestViewportState();
        var scene = new Canvas();
        var overlay = new Canvas();

        core.Viewport = viewport;
        core.SceneContent = scene;
        core.OverlayContent = overlay;

        Assert.Same(viewport, core.ViewportSurface.Viewport);
        Assert.Same(scene, core.ViewportSurface.SceneContent);
        Assert.Same(overlay, core.ViewportSurface.OverlayContent);
    }

    [Fact]
    public void DataContextForwarding_SynchronizesInnerViewportSurface()
    {
        var core = new InfiniteCanvasCoreControl();
        var dataContext = new object();

        core.DataContext = dataContext;

        Assert.Same(dataContext, core.ViewportSurface.DataContext);
    }

    private sealed class TestViewportState : ICanvasViewportState, INotifyPropertyChanged
    {
        private double _zoom = 1;
        private Point _panOffset;

        public event PropertyChangedEventHandler? PropertyChanged;

        public double Zoom
        {
            get => _zoom;
            set
            {
                _zoom = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Zoom)));
            }
        }

        public Point PanOffset
        {
            get => _panOffset;
            set
            {
                _panOffset = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PanOffset)));
            }
        }

        public void SetViewportSize(double width, double height) { }

        public void ZoomToward(Point screen, double factor)
        {
            Zoom *= factor;
        }
    }
}
