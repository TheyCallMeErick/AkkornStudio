using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AkkornStudio.UI.Controls;

public sealed class CanvasViewportSurface : Panel
{
    public static readonly StyledProperty<ICanvasViewportState?> ViewportProperty =
        AvaloniaProperty.Register<CanvasViewportSurface, ICanvasViewportState?>(nameof(Viewport));

    public static readonly StyledProperty<Control?> SceneContentProperty =
        AvaloniaProperty.Register<CanvasViewportSurface, Control?>(nameof(SceneContent));

    public static readonly StyledProperty<Control?> OverlayContentProperty =
        AvaloniaProperty.Register<CanvasViewportSurface, Control?>(nameof(OverlayContent));

    private readonly CanvasViewportController _controller = new();
    private INotifyPropertyChanged? _observedViewport;
    private Control? _attachedSceneContent;
    private Control? _attachedOverlayContent;

    static CanvasViewportSurface()
    {
        ViewportProperty.Changed.AddClassHandler<CanvasViewportSurface>((surface, _) => surface.OnViewportChanged());
        SceneContentProperty.Changed.AddClassHandler<CanvasViewportSurface>((surface, _) => surface.OnSceneContentChanged());
        OverlayContentProperty.Changed.AddClassHandler<CanvasViewportSurface>((surface, _) => surface.OnOverlayContentChanged());
        DataContextProperty.Changed.AddClassHandler<CanvasViewportSurface>((surface, _) => surface.SyncAttachedContentDataContext());
    }

    public CanvasViewportSurface()
    {
        GridBackground = new DotGridBackground { IsHitTestVisible = false };
        SceneCanvas = new Canvas
        {
            Width = 20000,
            Height = 20000,
            Background = Brushes.Transparent,
        };
        OverlayCanvas = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            ClipToBounds = true,
        };

        Children.Add(GridBackground);
        Children.Add(SceneCanvas);
        Children.Add(OverlayCanvas);
    }

    public ICanvasViewportState? Viewport
    {
        get => GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public Control? SceneContent
    {
        get => GetValue(SceneContentProperty);
        set => SetValue(SceneContentProperty, value);
    }

    public Control? OverlayContent
    {
        get => GetValue(OverlayContentProperty);
        set => SetValue(OverlayContentProperty, value);
    }

    public DotGridBackground GridBackground { get; }

    public Canvas SceneCanvas { get; }

    public Canvas OverlayCanvas { get; }

    public void SyncViewport()
    {
        if (Viewport is null)
            return;

        Viewport.SetViewportSize(Bounds.Width, Bounds.Height);
        _controller.SyncVisuals(Viewport, SceneCanvas, GridBackground, Bounds.Size);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        GridBackground.Measure(availableSize);
        SceneCanvas.Measure(Size.Infinity);
        OverlayCanvas.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        GridBackground.Arrange(new Rect(finalSize));
        SceneCanvas.Arrange(new Rect(new Point(0, 0), new Size(20000, 20000)));
        OverlayCanvas.Arrange(new Rect(finalSize));
        OverlayCanvas.Width = finalSize.Width;
        OverlayCanvas.Height = finalSize.Height;
        SyncChildCanvasSizes();
        SyncOverlayContentSize();
        SyncViewport();
        return finalSize;
    }

    private void OnViewportChanged()
    {
        if (_observedViewport is not null)
            _observedViewport.PropertyChanged -= OnViewportPropertyChanged;

        _observedViewport = Viewport as INotifyPropertyChanged;
        if (_observedViewport is not null)
            _observedViewport.PropertyChanged += OnViewportPropertyChanged;

        SyncViewport();
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            or nameof(ICanvasViewportState.Zoom)
            or nameof(ICanvasViewportState.PanOffset))
        {
            SyncViewport();
        }
    }

    private void OnSceneContentChanged()
    {
        if (_attachedSceneContent is not null)
            SceneCanvas.Children.Remove(_attachedSceneContent);

        _attachedSceneContent = SceneContent;
        if (_attachedSceneContent is not null && !SceneCanvas.Children.Contains(_attachedSceneContent))
        {
            SceneCanvas.Children.Add(_attachedSceneContent);
            SyncChildCanvasSizes();
            SyncAttachedContentDataContext();
        }
    }

    private void OnOverlayContentChanged()
    {
        if (_attachedOverlayContent is not null)
            OverlayCanvas.Children.Remove(_attachedOverlayContent);

        _attachedOverlayContent = OverlayContent;
        if (_attachedOverlayContent is not null && !OverlayCanvas.Children.Contains(_attachedOverlayContent))
        {
            OverlayCanvas.Children.Add(_attachedOverlayContent);
            SyncOverlayContentSize();
            SyncAttachedContentDataContext();
        }
    }

    private void SyncAttachedContentDataContext()
    {
        if (_attachedSceneContent is not null)
            _attachedSceneContent.DataContext = DataContext;

        if (_attachedOverlayContent is not null)
            _attachedOverlayContent.DataContext = DataContext;
    }

    private void SyncChildCanvasSizes()
    {
        if (_attachedSceneContent is Canvas sceneCanvas)
        {
            sceneCanvas.Width = SceneCanvas.Width;
            sceneCanvas.Height = SceneCanvas.Height;
        }
    }

    private void SyncOverlayContentSize()
    {
        if (_attachedOverlayContent is Canvas overlayCanvas)
        {
            overlayCanvas.Width = OverlayCanvas.Width;
            overlayCanvas.Height = OverlayCanvas.Height;
        }
    }
}
