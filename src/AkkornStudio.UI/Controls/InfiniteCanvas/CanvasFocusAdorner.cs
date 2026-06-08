using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AkkornStudio.UI.Controls;

public sealed class CanvasFocusAdorner : Control
{
    public static readonly StyledProperty<Rect> FocusRectProperty =
        AvaloniaProperty.Register<CanvasFocusAdorner, Rect>(nameof(FocusRect));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<CanvasFocusAdorner, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<CanvasFocusAdorner, double>(nameof(StrokeThickness), 2d);

    static CanvasFocusAdorner()
    {
        AffectsRender<CanvasFocusAdorner>(FocusRectProperty, StrokeProperty, StrokeThicknessProperty);
    }

    public Rect FocusRect
    {
        get => GetValue(FocusRectProperty);
        set => SetValue(FocusRectProperty, value);
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
        if (FocusRect.Width <= 0 || FocusRect.Height <= 0 || Stroke is null)
            return;

        var pen = new Pen(Stroke, StrokeThickness);
        context.DrawRectangle(null, pen, FocusRect);
    }
}
