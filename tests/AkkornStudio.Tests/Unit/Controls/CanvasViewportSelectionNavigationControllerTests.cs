using System.ComponentModel;
using Avalonia;
using AkkornStudio.UI.Controls;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class CanvasViewportSelectionNavigationControllerTests
{
    [Fact]
    public void TryCenterSelection_UpdatesPanOffset_ToCenterSelection()
    {
        var controller = new CanvasViewportSelectionNavigationController();
        var state = new TestSelectionState
        {
            Zoom = 2,
            SelectionFrame = new Rect(100, 50, 40, 30),
            HasSelectionFrame = true,
        };

        bool centered = controller.TryCenterSelection(state, new Size(400, 300));

        Assert.True(centered);
        Assert.Equal(new Point(-40, 20), state.PanOffset);
    }

    [Fact]
    public void TryFitSelection_UpdatesZoomAndPan_ToKeepSelectionVisible()
    {
        var controller = new CanvasViewportSelectionNavigationController();
        var state = new TestSelectionState
        {
            SelectionFrame = new Rect(100, 50, 200, 100),
            HasSelectionFrame = true,
        };

        bool fitted = controller.TryFitSelection(state, new Size(800, 600), padding: 40, minZoom: 0.15, maxZoom: 4);

        Assert.True(fitted);
        Assert.InRange(state.Zoom, 0.15, 4);

        Rect projected = new(
            state.SelectionFrame.X * state.Zoom + state.PanOffset.X,
            state.SelectionFrame.Y * state.Zoom + state.PanOffset.Y,
            state.SelectionFrame.Width * state.Zoom,
            state.SelectionFrame.Height * state.Zoom);
        Assert.True(projected.X >= 0);
        Assert.True(projected.Y >= 0);
        Assert.True(projected.Right <= 800);
        Assert.True(projected.Bottom <= 600);
    }

    private sealed class TestSelectionState : ICanvasViewportSelectionState, INotifyPropertyChanged
    {
        private double _zoom = 1;
        private Point _panOffset;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Rect SelectionFrame { get; set; }

        public bool HasSelectionFrame { get; set; }

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

        public void ClearSelection() { }

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
