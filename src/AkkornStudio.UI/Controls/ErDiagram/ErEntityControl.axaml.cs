using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia;
using AkkornStudio.UI.ViewModels.ErDiagram;

namespace AkkornStudio.UI.Controls.ErDiagram;

public sealed partial class ErEntityControl : UserControl
{
    private static readonly CanvasViewportGesturePolicy GesturePolicy = CanvasViewportGesturePolicy.ErCanvasDefault;
    private bool _isDragging;
    private Point _dragStartScreen;
    private Point _entityStartPosition;
    private ErEntityNodeViewModel? _dragEntity;
    private ErCanvasViewModel? _dragCanvas;

    public ErEntityControl()
    {
        InitializeComponent();
    }

    private void Root_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is ErEntityNodeViewModel entity)
            entity.IsHovered = true;
    }

    private void Root_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is ErEntityNodeViewModel entity)
            entity.IsHovered = false;
    }

    private void Root_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ErCanvasControl? canvasControl = this.FindAncestorOfType<ErCanvasControl>();
        if (canvasControl is null)
            return;

        PointerPointProperties pointerProperties = e.GetCurrentPoint(this).Properties;
        bool isPanGesture = CanvasViewportGestureDecisions.IsPanGesture(
            GesturePolicy,
            pointerProperties,
            e.KeyModifiers,
            canvasControl.IsSpacePanArmed);
        if (isPanGesture)
            return;

        if (DataContext is not ErEntityNodeViewModel entity)
            return;

        if (canvasControl.DataContext is not ErCanvasViewModel canvas)
            return;

        canvas.SelectedEntity = entity;
        if (pointerProperties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStartScreen = e.GetPosition(canvasControl);
            _entityStartPosition = new Point(entity.X, entity.Y);
            _dragEntity = entity;
            _dragCanvas = canvas;
            if (sender is IInputElement captureTarget)
                e.Pointer.Capture(captureTarget);
            else
                e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    private void Root_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _dragEntity is null || _dragCanvas is null)
            return;

        ErCanvasControl? canvasControl = this.FindAncestorOfType<ErCanvasControl>();
        if (canvasControl is null)
            return;

        Point current = e.GetPosition(canvasControl);
        Vector delta = current - _dragStartScreen;
        _dragEntity.X = _entityStartPosition.X + (delta.X / Math.Max(0.001, _dragCanvas.Zoom));
        _dragEntity.Y = _entityStartPosition.Y + (delta.Y / Math.Max(0.001, _dragCanvas.Zoom));
        _dragCanvas.RecomputeEdgeGeometry();
        canvasControl.RefreshViewportVisuals();
        e.Handled = true;
    }

    private void Root_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging || _dragEntity is null || _dragCanvas is null)
            return;

        ErEntityNodeViewModel movedEntity = _dragEntity;
        ErCanvasViewModel canvas = _dragCanvas;
        Point oldPosition = _entityStartPosition;
        Point newPosition = new(_dragEntity.X, _dragEntity.Y);
        EndDrag();
        e.Pointer.Capture(null);
        canvas.RecordEntityMove(movedEntity, oldPosition, newPosition);
        this.FindAncestorOfType<ErCanvasControl>()?.RefreshViewportVisuals();
        e.Handled = true;
    }

    private void Root_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndDrag();
    }

    private void EndDrag()
    {
        _isDragging = false;
        _dragEntity = null;
        _dragCanvas = null;
    }

    private void ColumnRow_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control rowControl || rowControl.DataContext is not ErColumnRowViewModel column || !column.IsForeignKey)
            return;

        if (DataContext is not ErEntityNodeViewModel entity)
            return;

        ErCanvasViewModel? canvas = this.FindAncestorOfType<ErCanvasControl>()?.DataContext as ErCanvasViewModel;
        canvas?.SetHoveredRelationByColumn(entity.Id, column.ColumnName);
    }

    private void ColumnRow_PointerExited(object? sender, PointerEventArgs e)
    {
        ErCanvasViewModel? canvas = this.FindAncestorOfType<ErCanvasControl>()?.DataContext as ErCanvasViewModel;
        canvas?.SetHoveredEdge(null);
    }
}
