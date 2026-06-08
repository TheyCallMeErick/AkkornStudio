using Avalonia;
using Avalonia.Controls;

namespace AkkornStudio.UI.Controls;

public sealed class InfiniteCanvasCoreControl : UserControl
{
    public static readonly StyledProperty<ICanvasViewportState?> ViewportProperty =
        AvaloniaProperty.Register<InfiniteCanvasCoreControl, ICanvasViewportState?>(nameof(Viewport));

    public static readonly StyledProperty<Control?> SceneContentProperty =
        AvaloniaProperty.Register<InfiniteCanvasCoreControl, Control?>(nameof(SceneContent));

    public static readonly StyledProperty<Control?> OverlayContentProperty =
        AvaloniaProperty.Register<InfiniteCanvasCoreControl, Control?>(nameof(OverlayContent));

    private readonly CanvasViewportSurface _surface = new();

    static InfiniteCanvasCoreControl()
    {
        ViewportProperty.Changed.AddClassHandler<InfiniteCanvasCoreControl>((control, _) => control.SyncViewportProperty());
        SceneContentProperty.Changed.AddClassHandler<InfiniteCanvasCoreControl>((control, _) => control.SyncSceneContentProperty());
        OverlayContentProperty.Changed.AddClassHandler<InfiniteCanvasCoreControl>((control, _) => control.SyncOverlayContentProperty());
        DataContextProperty.Changed.AddClassHandler<InfiniteCanvasCoreControl>((control, _) => control.SyncDataContextProperty());
    }

    public InfiniteCanvasCoreControl()
    {
        Background = Avalonia.Media.Brushes.Transparent;
        ClipToBounds = true;
        Content = _surface;
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

    public DotGridBackground GridBackground => _surface.GridBackground;

    public Canvas SceneCanvas => _surface.SceneCanvas;

    public Canvas OverlayCanvas => _surface.OverlayCanvas;

    public CanvasViewportSurface ViewportSurface => _surface;

    public void SyncViewport() => _surface.SyncViewport();

    private void SyncViewportProperty()
    {
        _surface.Viewport = Viewport;
    }

    private void SyncSceneContentProperty()
    {
        _surface.SceneContent = SceneContent;
    }

    private void SyncOverlayContentProperty()
    {
        _surface.OverlayContent = OverlayContent;
    }

    private void SyncDataContextProperty()
    {
        _surface.DataContext = DataContext;
    }
}
