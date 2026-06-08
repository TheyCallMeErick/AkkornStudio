using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AkkornStudio.UI.Controls;

public sealed class CanvasMarqueeAdorner : Control
{
    public static readonly StyledProperty<Rect> SelectionRectProperty =
        AvaloniaProperty.Register<CanvasMarqueeAdorner, Rect>(nameof(SelectionRect));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<CanvasMarqueeAdorner, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<CanvasMarqueeAdorner, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<CanvasMarqueeAdorner, double>(nameof(StrokeThickness), 1d);

    static CanvasMarqueeAdorner()
    {
        AffectsRender<CanvasMarqueeAdorner>(SelectionRectProperty, FillProperty, StrokeProperty, StrokeThicknessProperty);
    }

    public Rect SelectionRect
    {
        get => GetValue(SelectionRectProperty);
        set => SetValue(SelectionRectProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (SelectionRect.Width <= 0 || SelectionRect.Height <= 0)
            return;

        Pen? pen = Stroke is null ? null : new Pen(Stroke, StrokeThickness);
        context.DrawRectangle(Fill, pen, SelectionRect);
    }
}
