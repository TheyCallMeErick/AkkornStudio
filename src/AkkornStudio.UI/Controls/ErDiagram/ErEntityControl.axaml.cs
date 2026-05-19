using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;
using AkkornStudio.UI.ViewModels.ErDiagram;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AkkornStudio.UI.Controls.ErDiagram;

public sealed partial class ErEntityControl : UserControl
{
    private static readonly CanvasViewportGesturePolicy GesturePolicy = CanvasViewportGesturePolicy.ErCanvasDefault;
    private const int HoverFocusDelayMs = 180;
    private bool _isDragging;
    private bool _isPointerInside;
    private bool _isDoubleClick;
    private bool _didDrag;
    private Point _dragStartScreen;
    private readonly Dictionary<ErEntityNodeViewModel, Point> _dragStartPositions = [];
    private IReadOnlyList<ErEntityNodeViewModel> _dragEntities = [];
    private ErEntityNodeViewModel? _dragAnchorEntity;
    private ErCanvasViewModel? _dragCanvas;
    private CancellationTokenSource? _hoverDelayCts;

    public ErEntityControl()
    {
        InitializeComponent();
    }

    private void Root_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ErEntityNodeViewModel entity)
            return;

        _isPointerInside = true;
        entity.IsHovered = true;
        StartHoverFocusActivation(entity);
    }

    private void Root_PointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerInside = false;
        CancelHoverFocusActivation(clearFocus: true);
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

        CancelHoverFocusActivation(clearFocus: false);

        if (pointerProperties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _didDrag = false;
            _isDoubleClick = e.ClickCount >= 2;
            _dragStartScreen = e.GetPosition(canvasControl);
            _dragAnchorEntity = entity;
            _dragCanvas = canvas;
            _dragEntities = canvas.SelectedEntities.Contains(entity)
                ? [.. canvas.SelectedEntities]
                : [entity];
            _dragStartPositions.Clear();
            foreach (ErEntityNodeViewModel candidate in _dragEntities)
                _dragStartPositions[candidate] = new Point(candidate.X, candidate.Y);
            if (sender is IInputElement captureTarget)
                e.Pointer.Capture(captureTarget);
            else
                e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    private void Root_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _dragAnchorEntity is null || _dragCanvas is null || _dragEntities.Count == 0)
            return;

        ErCanvasControl? canvasControl = this.FindAncestorOfType<ErCanvasControl>();
        if (canvasControl is null)
            return;

        Point current = e.GetPosition(canvasControl);
        Vector delta = current - _dragStartScreen;
        if (!_didDrag && (Math.Abs(delta.X) > 2 || Math.Abs(delta.Y) > 2))
            _didDrag = true;
        double scaledDeltaX = delta.X / Math.Max(0.001, _dragCanvas.Zoom);
        double scaledDeltaY = delta.Y / Math.Max(0.001, _dragCanvas.Zoom);
        foreach (ErEntityNodeViewModel moved in _dragEntities)
        {
            if (!_dragStartPositions.TryGetValue(moved, out Point origin))
                continue;

            moved.X = origin.X + scaledDeltaX;
            moved.Y = origin.Y + scaledDeltaY;
        }
        _dragCanvas.RecomputeEdgeGeometry();
        canvasControl.RefreshViewportVisuals();
        e.Handled = true;
    }

    private void Root_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging || _dragCanvas is null || _dragEntities.Count == 0)
            return;

        ErCanvasViewModel canvas = _dragCanvas;
        Dictionary<ErEntityNodeViewModel, Point> oldPositions = _dragStartPositions
            .ToDictionary(static item => item.Key, static item => item.Value);
        bool didDrag = _didDrag;
        KeyModifiers keyModifiers = e.KeyModifiers;
        ErEntityNodeViewModel? clickedEntity = DataContext as ErEntityNodeViewModel;
        EndDrag();
        e.Pointer.Capture(null);
        canvas.RecordEntityMoves(oldPositions);
        if (!didDrag && _isDoubleClick && clickedEntity is not null)
        {
            bool additiveSelection = keyModifiers.HasFlag(KeyModifiers.Control)
                || keyModifiers.HasFlag(KeyModifiers.Shift);
            canvas.SelectEntity(clickedEntity, additiveSelection);
        }
        this.FindAncestorOfType<ErCanvasControl>()?.RefreshViewportVisuals();
        e.Handled = true;
    }

    private void Root_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndDrag();
        CancelHoverFocusActivation(clearFocus: false);
    }

    private void EndDrag()
    {
        _isDragging = false;
        _dragAnchorEntity = null;
        _dragEntities = [];
        _dragStartPositions.Clear();
        _dragCanvas = null;
        _isDoubleClick = false;
        _didDrag = false;
    }

    private void StartHoverFocusActivation(ErEntityNodeViewModel entity)
    {
        CancelHoverFocusActivation(clearFocus: false);
        ErCanvasViewModel? canvas = this.FindAncestorOfType<ErCanvasControl>()?.DataContext as ErCanvasViewModel;
        if (canvas is null)
            return;

        entity.IsHoverFocusPending = true;
        Cursor = new Cursor(StandardCursorType.Wait);
        _hoverDelayCts = new CancellationTokenSource();
        CancellationToken token = _hoverDelayCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(HoverFocusDelayMs, token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || !_isPointerInside || DataContext is not ErEntityNodeViewModel current || !ReferenceEquals(current, entity))
                        return;

                    current.IsHoverFocusPending = false;
                    Cursor = Cursor.Default;
                    canvas.SetHoveredEntity(current);
                });
            }
            catch (TaskCanceledException)
            {
                // noop
            }
        }, token);
    }

    private void CancelHoverFocusActivation(bool clearFocus)
    {
        _hoverDelayCts?.Cancel();
        _hoverDelayCts?.Dispose();
        _hoverDelayCts = null;
        Cursor = Cursor.Default;

        if (DataContext is ErEntityNodeViewModel entity)
            entity.IsHoverFocusPending = false;

        if (!clearFocus)
            return;

        ErCanvasViewModel? canvas = this.FindAncestorOfType<ErCanvasControl>()?.DataContext as ErCanvasViewModel;
        canvas?.SetHoveredEntity(null);
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
