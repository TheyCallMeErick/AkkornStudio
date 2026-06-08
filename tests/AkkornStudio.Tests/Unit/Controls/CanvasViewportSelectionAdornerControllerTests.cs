using System.ComponentModel;
using Avalonia;
using AkkornStudio.UI.Controls;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class CanvasViewportSelectionAdornerControllerTests
{
    [Fact]
    public void SyncFocusAdorner_ProjectsCanvasSelectionFrameIntoViewportSpace()
    {
        var controller = new CanvasViewportSelectionAdornerController();
        var adorner = new CanvasFocusAdorner();
        var state = new TestSelectionState
        {
            Zoom = 2,
            PanOffset = new Point(10, 20),
            SelectionFrame = new Rect(100, 50, 40, 30),
            HasSelectionFrame = true,
        };

        controller.SyncFocusAdorner(state, adorner, padding: 0);

        Assert.Equal(new Rect(210, 120, 80, 60), adorner.FocusRect);
    }

    [Fact]
    public void CompleteMarqueeSelection_ClearsSelection_ForSmallRegions()
    {
        var controller = new CanvasViewportSelectionAdornerController();
        var adorner = new CanvasMarqueeAdorner { SelectionRect = new Rect(1, 1, 10, 10) };
        var state = new TestSelectionState();

        bool selected = controller.CompleteMarqueeSelection(
            state,
            new Rect(10, 10, 4, 4),
            adorner,
            minimumSize: 8);

        Assert.False(selected);
        Assert.True(state.ClearSelectionCalled);
        Assert.Equal(default, adorner.SelectionRect);
    }

    private sealed class TestSelectionState : ICanvasViewportSelectionState, INotifyPropertyChanged
    {
        private double _zoom = 1;
        private Point _panOffset;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Rect SelectionFrame { get; set; }

        public bool HasSelectionFrame { get; set; }

        public bool ClearSelectionCalled { get; private set; }

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

        public void ClearSelection()
        {
            ClearSelectionCalled = true;
        }

        public void SetViewportSize(double width, double height) { }

        public bool TryGetSelectionFrame(double padding, out Rect frame)
        {
            frame = SelectionFrame;
            return HasSelectionFrame;
        }

        public bool TrySelectInRegion(Rect region) => true;

        public void ZoomToward(Point screen, double factor)
        {
            Zoom *= factor;
        }
    }
}
