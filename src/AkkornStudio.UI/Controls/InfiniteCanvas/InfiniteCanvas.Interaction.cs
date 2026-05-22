using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Controls.DragDrop;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;
using Material.Icons;
using Material.Icons.Avalonia;
using System.Windows.Input;
using AkkornStudio.UI.Services.Theming;

namespace AkkornStudio.UI.Controls;

/// <summary>
/// Interaction responsibilities extracted from InfiniteCanvas:
/// pointer/keyboard gestures, context menu, rubber-band visuals and alignment guides.
/// </summary>
public sealed partial class InfiniteCanvas
{
    // Note: In Avalonia 11+, Panel.Render() is sealed and cannot be overridden.
    // Rubber band selection rectangle is drawn as a visual element instead.
    private CanvasMarqueeAdorner? _rubberBandRect;

    private const double GuideThreshold = 8.0; // canvas units — how close to snap a guide
    private const double DefaultNodeH = 130;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        _ = sender;
        if (ViewModel is null)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        bool canDropNode = TryGetDraggedNodeDefinition(e.Data, out _);
        bool canDropTable = TryGetDraggedSchemaTable(e.Data, out string? tableFullName)
            && !string.IsNullOrWhiteSpace(tableFullName);

        e.DragEffects = canDropNode || canDropTable ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _ = sender;
        if (ViewModel is null)
            return;

        Point canvasPoint = ScreenToCanvas(e.GetPosition(this));
        bool handled = false;

        if (TryGetDraggedNodeDefinition(e.Data, out NodeDefinition? definition) && definition is not null)
        {
            ViewModel.SpawnNode(definition, canvasPoint);
            handled = true;
        }
        else if (TryGetDraggedSchemaTable(e.Data, out string? tableFullName) && !string.IsNullOrWhiteSpace(tableFullName))
        {
            handled = ViewModel.TryInsertSchemaTableNode(tableFullName!, canvasPoint);
        }

        if (handled)
        {
            RequestWireSync();
            InvalidateVisual();
        }

        e.Handled = handled;
    }

    private static bool TryGetDraggedNodeDefinition(IDataObject data, out NodeDefinition? definition)
    {
        definition = null;
        if (!data.Contains(CanvasDragDropDataFormats.NodeType))
            return false;

        if (data.Get(CanvasDragDropDataFormats.NodeType) is not string nodeTypeText
            || !Enum.TryParse(nodeTypeText, true, out NodeType nodeType))
        {
            return false;
        }

        try
        {
            definition = NodeDefinitionRegistry.Get(nodeType);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDraggedSchemaTable(IDataObject data, out string? tableFullName)
    {
        tableFullName = null;
        if (!data.Contains(CanvasDragDropDataFormats.SchemaTableFullName))
            return false;

        tableFullName = data.Get(CanvasDragDropDataFormats.SchemaTableFullName) as string;
        return !string.IsNullOrWhiteSpace(tableFullName);
    }

    private bool TryDeleteWire(ConnectionViewModel? wire)
    {
        if (ViewModel is null || wire is null)
            return false;

        ClearHoverHighlights();
        _wires.AddRemovalFlash(wire);
        ViewModel.DeleteConnection(wire);
        RequestWireSync();
        _wires.InvalidateVisual();
        return true;
    }

    private void OnPinPressed(object? s, (PinViewModel Pin, Point Canvas) a)
    {
        _pinDrag?.BeginDrag(a.Pin, a.Pin.AbsolutePosition);
        _wires.PendingConnection = _pinDrag?.LiveWire;
        Log($">>> PIN DRAG STARTED: Pin='{a.Pin.Owner.Title}.{a.Pin.Name}'");
    }

    private void OnWheel(object? s, PointerWheelEventArgs e)
    {
        if (ViewModel is null)
            return;
        double oldZoom = _zoom;
        Point oldPan = _panOffset;

        Point screen = e.GetPosition(this);
        double factor = e.Delta.Y > 0 ? 1.10 : 0.91;

        // Nodify-style approach: update viewport from a single source of truth (ViewModel),
        // then sync once to the visual transform.
        _isApplyingViewportFromCanvas = true;
        try
        {
            _viewportController.ZoomAtPointer(ViewModel, this, e);
        }
        finally
        {
            _isApplyingViewportFromCanvas = false;
        }

        SyncTransform();
        SyncWires();
        e.Handled = true;
        Log($"    ZOOM: {oldZoom:F2} -> {_zoom:F2}, Pan: {oldPan} -> {_panOffset}, Delta={e.Delta.Y}");
    }

    private void OnPressed(object? s, PointerPressedEventArgs e)
    {
        Focus();
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        Point screen = e.GetPosition(this);

        if (_dragBreakpointWire is null
            && CanvasViewportGestureDecisions.IsPanGesture(
                _gesturePolicy,
                props,
                e.KeyModifiers,
                _isSpacePanArmed))
        {
            ClearHoverHighlights();
            Log($">>> PAN STARTED: Screen={screen}, PanOffset={_panOffset}, Zoom={_zoom}");
            _viewportController.BeginPan(this, e.Pointer, screen);
            _isPanning = true;
            _startPanOffset = _panOffset; // NodifyM pattern: capture pan offset at start
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
            return;
        }

        // Nodify-style usability: right-drag pans, right-click opens context menu.
        if (props.IsRightButtonPressed)
        {
            _contextMenuPending = true;
            _contextMenuPressScreen = screen;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            Point canvas = ScreenToCanvas(screen);
            _rubberStart = canvas;
            _rubberCurrent = canvas;

            if (_wires.TryHitToolbar(canvas, out BezierWireLayer.WireToolbarHit toolbarHit)
                && HandleToolbarHit(toolbarHit))
            {
                e.Handled = true;
                return;
            }

            // Fast delete gesture for wires: Ctrl+Click on a wire removes it.
            if (TryHandleCtrlClickWireDelete(
                e.KeyModifiers.HasFlag(KeyModifiers.Control),
                canvas,
                c => _wires.HitTestWire(c, tolerance: 8),
                TryDeleteWire))
            {
                e.Handled = true;
                return;
            }

            // If a NodeControl already fired PinPressed (which called BeginDrag via OnPinPressed),
            // the event bubbled here without e.Handled — just capture the pointer so that
            // move and release events are owned by the canvas for the full drag gesture.
            if (_pinDrag?.IsDragging == true)
            {
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            ConnectionViewModel? selectedWireCandidate = _wires.HitTestWire(canvas, tolerance: 8);
            if (selectedWireCandidate is not null)
            {
                if (selectedWireCandidate.RoutingMode == CanvasWireRoutingMode.Orthogonal)
                {
                    int breakpointIndex = BezierWireLayer.FindBreakpointAt(selectedWireCandidate, canvas, tolerance: 8);
                    if (TryGetBreakpointPosition(selectedWireCandidate, breakpointIndex, out Point breakpointPosition))
                    {
                        _dragBreakpointWire = selectedWireCandidate;
                        _dragBreakpointIndex = breakpointIndex;
                        _dragBreakpointInitialPosition = breakpointPosition;
                        ViewModel?.SelectConnection(selectedWireCandidate);
                        ViewModel?.SelectWireBreakpoint(selectedWireCandidate, breakpointIndex);
                        SyncWires();
                        e.Pointer.Capture(this);
                        e.Handled = true;
                        return;
                    }
                }

                ViewModel?.SelectConnection(selectedWireCandidate);
                SyncWires();
                e.Handled = true;
                return;
            }

            PinViewModel? pin = _pinDrag?.HitTestPin(canvas);
            if (pin is not null)
            {
                _pinDrag!.BeginDrag(pin, pin.AbsolutePosition);
                _wires.PendingConnection = _pinDrag.LiveWire;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            _isRubberBanding = RubberBandEnabled;
            UpdateRubberBandVisual();
            ViewModel?.DeselectAll();
            if (_isRubberBanding)
                e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
    }

    internal bool HandleToolbarHit(BezierWireLayer.WireToolbarHit toolbarHit, bool synchronizeCanvas = true)
    {
        if (ViewModel is null)
            return false;

        switch (toolbarHit.Action)
        {
            case BezierWireLayer.WireToolbarAction.Delete:
                return TryDeleteWire(toolbarHit.Wire);
            case BezierWireLayer.WireToolbarAction.SetBezier:
            case BezierWireLayer.WireToolbarAction.SetStraight:
            case BezierWireLayer.WireToolbarAction.SetOrthogonal:
            {
                bool changed = TryApplyToolbarRoutingAction(ViewModel, toolbarHit);
                if (changed && synchronizeCanvas)
                    SyncWires();
                return changed;
            }
            default:
                return false;
        }
    }

    internal static bool TryApplyToolbarRoutingAction(CanvasViewModel vm, BezierWireLayer.WireToolbarHit toolbarHit)
    {
        vm.SelectConnection(toolbarHit.Wire);

        return toolbarHit.Action switch
        {
            BezierWireLayer.WireToolbarAction.SetBezier =>
                vm.SetConnectionRoutingMode(toolbarHit.Wire, CanvasWireRoutingMode.Bezier),
            BezierWireLayer.WireToolbarAction.SetStraight =>
                vm.SetConnectionRoutingMode(toolbarHit.Wire, CanvasWireRoutingMode.Straight),
            BezierWireLayer.WireToolbarAction.SetOrthogonal =>
                vm.SetConnectionRoutingMode(toolbarHit.Wire, CanvasWireRoutingMode.Orthogonal),
            _ => false,
        };
    }

    internal static bool TryHandleWireDeleteShortcut(
        CanvasViewModel vm,
        ConnectionViewModel? hoveredWire,
        Func<ConnectionViewModel?, bool> tryDeleteWire,
        Action? onSelectedBreakpointDeleted = null)
    {
        if (vm.DeleteSelectedWireBreakpoint())
        {
            onSelectedBreakpointDeleted?.Invoke();
            return true;
        }

        ConnectionViewModel? selectedWire = vm.SelectedConnection;
        return tryDeleteWire(selectedWire ?? hoveredWire);
    }

    internal static bool TryHandleCtrlClickWireDelete(
        bool isControlModifierPressed,
        Point canvasPoint,
        Func<Point, ConnectionViewModel?> hitTestWire,
        Func<ConnectionViewModel?, bool> tryDeleteWire)
    {
        if (!isControlModifierPressed)
            return false;

        ConnectionViewModel? wireUnderPointer = hitTestWire(canvasPoint);
        return tryDeleteWire(wireUnderPointer);
    }

    internal enum WireContextBreakpointActionKind
    {
        None,
        Add,
        Remove,
    }

    internal readonly record struct WireContextBreakpointAction(
        WireContextBreakpointActionKind Kind,
        int Index,
        Point Position)
    {
        public static WireContextBreakpointAction NoneAction()
        {
            return new WireContextBreakpointAction(WireContextBreakpointActionKind.None, -1, default);
        }
    }

    internal static WireContextBreakpointAction ResolveWireContextBreakpointAction(
        ConnectionViewModel wire,
        Point canvasPoint,
        double breakpointTolerance = 8,
        double segmentTolerance = 12)
    {
        if (wire.RoutingMode != CanvasWireRoutingMode.Orthogonal)
            return WireContextBreakpointAction.NoneAction();

        int breakpointIndex = BezierWireLayer.FindBreakpointAt(wire, canvasPoint, breakpointTolerance);
        if (breakpointIndex >= 0 && breakpointIndex < wire.Breakpoints.Count)
        {
            return new WireContextBreakpointAction(
                WireContextBreakpointActionKind.Remove,
                breakpointIndex,
                wire.Breakpoints[breakpointIndex].Position);
        }

        if (BezierWireLayer.TryProjectToOrthogonalSegment(
            wire,
            canvasPoint,
            segmentTolerance,
            out Point projected,
            out int insertIndex,
            out _))
        {
            return new WireContextBreakpointAction(WireContextBreakpointActionKind.Add, insertIndex, projected);
        }

        return WireContextBreakpointAction.NoneAction();
    }

    internal static bool TryGetBreakpointPosition(
        ConnectionViewModel wire,
        int breakpointIndex,
        out Point breakpointPosition)
    {
        breakpointPosition = default;

        if (breakpointIndex < 0 || breakpointIndex >= wire.Breakpoints.Count)
            return false;

        breakpointPosition = wire.Breakpoints[breakpointIndex].Position;
        return true;
    }

    internal static IReadOnlyList<NodeDefinition> ResolveCompatibleWireInsertDefinitions(
        ConnectionViewModel wire,
        CanvasContext canvasContext,
        int limit = 20)
    {
        if (wire.ToPin is null || limit <= 0)
            return [];

        static bool IsAllowedInCanvasContext(NodeDefinition definition, CanvasContext context) =>
            context switch
            {
                CanvasContext.Ddl => definition.Category == NodeCategory.Ddl,
                CanvasContext.Query or CanvasContext.ViewSubcanvas => definition.Category != NodeCategory.Ddl,
                _ => true,
            };

        static bool IsCompatible(ConnectionViewModel connection, NodeDefinition definition)
        {
            var probe = new NodeViewModel(definition, new Point(0, 0));
            PinViewModel? input = probe.InputPins.FirstOrDefault(pin => pin.EvaluateConnection(connection.FromPin).IsAllowed);
            if (input is null)
                return false;

            return probe.OutputPins.Any(pin => connection.ToPin!.EvaluateConnection(pin).IsAllowed);
        }

        return NodeDefinitionRegistry.All
            .Where(definition => IsAllowedInCanvasContext(definition, canvasContext))
            .Where(definition => IsCompatible(wire, definition))
            .OrderBy(definition => definition.Category)
            .ThenBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    internal bool TryInsertNodeDefinitionOnWire(ConnectionViewModel wire, NodeDefinition definition)
    {
        if (ViewModel is null || wire.ToPin is null)
            return false;

        Point spawnPosition = new(
            (wire.FromPoint.X + wire.ToPoint.X) / 2d,
            (wire.FromPoint.Y + wire.ToPoint.Y) / 2d);

        using UndoRedoStack.UndoRedoTransaction tx = ViewModel.UndoRedo.BeginTransaction("Insert node on selected wire");

        NodeViewModel insertedNode = ViewModel.SpawnNode(definition, spawnPosition);
        PinViewModel? insertInput = insertedNode.InputPins.FirstOrDefault(pin => pin.EvaluateConnection(wire.FromPin).IsAllowed);
        PinViewModel? insertOutput = insertedNode.OutputPins.FirstOrDefault(pin => wire.ToPin.EvaluateConnection(pin).IsAllowed);
        if (insertInput is null || insertOutput is null)
            return false;

        ViewModel.DeleteConnection(wire);
        ViewModel.ConnectPins(wire.FromPin, insertInput);
        ViewModel.ConnectPins(insertOutput, wire.ToPin);

        bool insertedIncoming = ViewModel.Connections.Any(connection =>
            ReferenceEquals(connection.FromPin, wire.FromPin) && ReferenceEquals(connection.ToPin, insertInput));
        bool insertedOutgoing = ViewModel.Connections.Any(connection =>
            ReferenceEquals(connection.FromPin, insertOutput) && ReferenceEquals(connection.ToPin, wire.ToPin));
        if (!insertedIncoming || !insertedOutgoing)
            return false;

        ViewModel.DeselectAll();
        insertedNode.IsSelected = true;
        tx.Commit();
        return true;
    }

    internal static bool TryQuickAddTop100ForResultOutput(CanvasViewModel vm, NodeViewModel resultOutputNode)
    {
        if (resultOutputNode.Type != NodeType.ResultOutput)
            return false;

        PinViewModel? topInput = resultOutputNode.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "top", StringComparison.OrdinalIgnoreCase));
        if (topInput is null)
            return false;

        bool alreadyHasTop = vm.Connections.Any(connection =>
            connection.ToPin is not null
            && ReferenceEquals(connection.ToPin.Owner, resultOutputNode)
            && string.Equals(connection.ToPin.Name, topInput.Name, StringComparison.OrdinalIgnoreCase));
        if (alreadyHasTop)
            return false;

        NodeDefinition topDefinition = NodeDefinitionRegistry.Get(NodeType.Top);
        NodeDefinition numberDefinition = NodeDefinitionRegistry.Get(NodeType.ValueNumber);

        Point resultPosition = resultOutputNode.Position;
        Point topPosition = new(resultPosition.X - 250, resultPosition.Y + 20);
        Point numberPosition = new(topPosition.X - 230, topPosition.Y - 10);

        using UndoRedoStack.UndoRedoTransaction tx = vm.UndoRedo.BeginTransaction("Quick add TOP 100");

        NodeViewModel topNode = vm.SpawnNode(topDefinition, topPosition);
        NodeViewModel numberNode = vm.SpawnNode(numberDefinition, numberPosition);
        numberNode.Parameters["value"] = "100";

        PinViewModel? numberOutput = numberNode.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "result", StringComparison.OrdinalIgnoreCase));
        PinViewModel? topCount = topNode.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "count", StringComparison.OrdinalIgnoreCase));
        PinViewModel? topOutput = topNode.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "result", StringComparison.OrdinalIgnoreCase));
        if (numberOutput is null || topCount is null || topOutput is null)
            return false;

        vm.ConnectPins(numberOutput, topCount);
        vm.ConnectPins(topOutput, topInput);

        bool hasCountConnection = vm.Connections.Any(connection =>
            ReferenceEquals(connection.FromPin, numberOutput) && ReferenceEquals(connection.ToPin, topCount));
        bool hasTopConnection = vm.Connections.Any(connection =>
            ReferenceEquals(connection.FromPin, topOutput) && ReferenceEquals(connection.ToPin, topInput));
        if (!hasCountConnection || !hasTopConnection)
            return false;

        vm.DeselectAll();
        resultOutputNode.IsSelected = true;
        tx.Commit();
        return true;
    }

    private bool TrySelectWireEndpointNode(ConnectionViewModel wire, bool upstream, bool traverseChain = false)
    {
        if (ViewModel is null || wire.ToPin is null)
            return false;

        NodeViewModel endpoint = upstream ? wire.FromPin.Owner : wire.ToPin.Owner;
        ViewModel.DeselectAll();
        endpoint.IsSelected = true;

        if (traverseChain)
            _ = SelectLinkedNodes(traverseUpstream: upstream);

        return true;
    }

    private void OnMoved(object? s, PointerEventArgs e)
    {
        Point screen = e.GetPosition(this);
        Point canvas = ScreenToCanvas(screen);

        if (_contextMenuPending && !_isPanning && _dragBreakpointWire is null)
        {
            Vector d = screen - _contextMenuPressScreen;
            if (d.X * d.X + d.Y * d.Y >= ContextPanStartThreshold * ContextPanStartThreshold)
            {
                _contextMenuPending = false;
                _viewportController.BeginPan(this, e.Pointer, screen);
                _isPanning = true;
                _startPanOffset = _panOffset;
                Cursor = new Cursor(StandardCursorType.SizeAll);
                Log($">>> PAN STARTED (right-drag): Screen={screen}, PanOffset={_panOffset}, Zoom={_zoom}");
            }
            else
            {
                return;
            }
        }

        if (_isPanning)
        {
            ClearHoverHighlights();
            _viewportController.TryPan(ViewModel!, this, e);
            _panOffset = ViewModel!.PanOffset;

            Log($"    PAN MOVING: Screen={screen}, NewPanOffset={_panOffset}, Zoom={_zoom}");

            // Force immediate visual update during pan (don't wait for layout pass)
            _viewportController.SyncVisuals(ViewModel!, _scene, _grid, Bounds.Size);
            SyncWires();
            return;
        }
        if (_pinDrag?.IsDragging == true)
        {
            ClearHoverHighlights();
            _pinDrag.UpdateDrag(canvas);
            _wires.PendingConnection = _pinDrag.LiveWire;
            _wires.InvalidateVisual();
            return;
        }

        if (_dragBreakpointWire is not null && _dragBreakpointIndex >= 0)
        {
            List<WireBreakpoint> points = [.. _dragBreakpointWire.Breakpoints];
            if (_dragBreakpointIndex < points.Count)
            {
                points[_dragBreakpointIndex] = new WireBreakpoint(canvas);
                _dragBreakpointWire.SetBreakpoints(points);
                _wires.InvalidateVisual();
            }

            return;
        }

        if (RubberBandEnabled && _isRubberBanding)
        {
            _rubberCurrent = canvas;
            UpdateRubberBand();
            UpdateRubberBandVisual();

            // Ensure wires stay visible during rubber band drag
            _wires.InvalidateVisual();

            // Keep wires on top as nodes are selected
            EnsureWiresOnTop();
        }

        // Always ensure wires are on top after any movement
        if (!_isPanning && !(_pinDrag?.IsDragging == true))
        {
            EnsureWiresOnTop();
            UpdateHoverHighlights(canvas);
        }
    }

    private void OnReleased(object? s, PointerReleasedEventArgs e)
    {
        Point screen = e.GetPosition(this);
        Point canvas = ScreenToCanvas(e.GetPosition(this));

        if (_contextMenuPending)
        {
            _contextMenuPending = false;
            e.Pointer.Capture(null);
            ShowContextMenu(screen);
            return;
        }

        if (_isPanning)
        {
            Log($"<<< PAN COMPLETED: FinalPanOffset={_panOffset}, Zoom={_zoom}");
            // Final sync to ViewModel to ensure persistence
            if (ViewModel is not null)
            {
                ViewModel.PanOffset = _panOffset;
                Log($"    Synced to ViewModel: PanOffset={_panOffset}");
            }
            _isPanning = false;
            _viewportController.EndPan(e);
            Cursor = _isSpacePanArmed ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            return;
        }
        if (_pinDrag?.IsDragging == true)
        {
            _pinDrag.EndDrag(canvas);
            _wires.PendingConnection = null;
            _wires.InvalidateVisual();
            e.Pointer.Capture(null);
            return;
        }

        if (_dragBreakpointWire is not null && _dragBreakpointIndex >= 0)
        {
            Point finalPosition = TryGetBreakpointPosition(_dragBreakpointWire, _dragBreakpointIndex, out Point currentBreakpointPosition)
                ? currentBreakpointPosition
                : _dragBreakpointInitialPosition;

            if (ViewModel is not null)
            {
                _ = ViewModel.CommitWireBreakpointDrag(
                    _dragBreakpointWire,
                    _dragBreakpointIndex,
                    _dragBreakpointInitialPosition,
                    finalPosition);
                ViewModel.SelectWireBreakpoint(_dragBreakpointWire, _dragBreakpointIndex);
            }

            _dragBreakpointWire = null;
            _dragBreakpointIndex = -1;
            SyncWires();
            e.Pointer.Capture(null);
            return;
        }
        if (RubberBandEnabled && _isRubberBanding)
        {
            Log($"<<< RUBBER BAND COMPLETED: Canvas={canvas}");
            _isRubberBanding = false;
            if (ViewModel is ICanvasViewportSelectionState selectionState)
            {
                Rect region = new(
                    Math.Min(_rubberStart.X, _rubberCurrent.X),
                    Math.Min(_rubberStart.Y, _rubberCurrent.Y),
                    Math.Abs(_rubberCurrent.X - _rubberStart.X),
                    Math.Abs(_rubberCurrent.Y - _rubberStart.Y));
                _selectionAdornerController.CompleteMarqueeSelection(selectionState, region, _rubberBandRect);
            }
            else
            {
            }
            UpdateRubberBandVisual();
            e.Pointer.Capture(null);

            // Ensure wires are fully synced and visible after rubber band selection
            SyncWires();
            EnsureWiresOnTop();
        }
    }

    private void OnKey(object? s, KeyEventArgs e)
    {
        if (ViewModel is null)
            return;

        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _isWireInsertModifierPressed = true;
            if (_dragNode is not null)
                UpdateWireInsertPreview(_dragNode);
        }

        if (e.Key == Key.Space)
        {
            _isSpacePanArmed = true;
            if (!_isPanning)
                Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            List<NodeViewModel> selected = ViewModel.Nodes.Where(n => n.IsSelected).ToList();
            if (selected.Count > 0)
            {
                double step = ViewModel.SnapToGrid ? CanvasViewModel.GridSize : 1.0;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    step *= 10.0;

                double dx = 0, dy = 0;
                switch (e.Key)
                {
                    case Key.Left:
                        dx = -step;
                        break;
                    case Key.Right:
                        dx = step;
                        break;
                    case Key.Up:
                        dy = -step;
                        break;
                    case Key.Down:
                        dy = step;
                        break;
                }

                foreach (NodeViewModel n in selected)
                {
                    double nx = n.Position.X + dx;
                    double ny = n.Position.Y + dy;
                    if (ViewModel.SnapToGrid)
                    {
                        nx = CanvasViewModel.Snap(nx);
                        ny = CanvasViewModel.Snap(ny);
                    }
                    n.Position = new Point(nx, ny);
                }

                SyncWires();
                InvalidateArrange();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                if (FitSelectionInView())
                {
                    e.Handled = true;
                    return;
                }
            }

            if (CenterSelectionInView())
            {
                e.Handled = true;
                return;
            }
        }
        if (e.Key == Key.Escape)
        {
            _pinDrag?.CancelDrag();
            _wires.PendingConnection = null;
            _wires.InvalidateVisual();
            ClearHoverHighlights();
            ViewModel.DeselectAll();
            ViewModel.SearchMenu.Close();
            _isRubberBanding = false;
            UpdateRubberBandVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.X && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (TryBypassSelectedNode())
            {
                SyncWires();
                InvalidateArrange();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Q && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (SelectLinkedNodes(traverseUpstream: true))
            {
                SyncWires();
                InvalidateArrange();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (SelectLinkedNodes(traverseUpstream: false))
            {
                SyncWires();
                InvalidateArrange();
                e.Handled = true;
                return;
            }
        }

        if (e.Key is Key.Delete or Key.Back)
        {
            if (TryHandleWireDeleteShortcut(
                ViewModel,
                _hoveredWire,
                TryDeleteWire,
                SyncWires))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void OnKeyUp(object? s, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _isWireInsertModifierPressed = false;
            if (_dragNode is not null)
                UpdateWireInsertPreview(_dragNode);
        }

        if (e.Key != Key.Space)
            return;

        _isSpacePanArmed = false;
        if (!_isPanning)
            Cursor = Cursor.Default;
        e.Handled = true;
    }

    private void UpdateRubberBand()
    {
        if (ViewModel is not ICanvasViewportSelectionState selectionState)
            return;

        Rect region = new(
            Math.Min(_rubberStart.X, _rubberCurrent.X),
            Math.Min(_rubberStart.Y, _rubberCurrent.Y),
            Math.Abs(_rubberCurrent.X - _rubberStart.X),
            Math.Abs(_rubberCurrent.Y - _rubberStart.Y)
        );
        _ = selectionState.TrySelectInRegion(region);
    }

    private void UpdateRubberBandVisual()
    {
        if (!_isRubberBanding)
        {
            if (_rubberBandRect is not null)
            {
                _overlay.Children.Remove(_rubberBandRect);
                _rubberBandRect = null;

                // Ensure wires stay on top after rubber band is removed
                EnsureWiresOnTop();
            }
            return;
        }

        Rect region = new(
            Math.Min(_rubberStart.X, _rubberCurrent.X),
            Math.Min(_rubberStart.Y, _rubberCurrent.Y),
            Math.Abs(_rubberCurrent.X - _rubberStart.X),
            Math.Abs(_rubberCurrent.Y - _rubberStart.Y));

        if (_rubberBandRect is null)
        {
            _rubberBandRect = new CanvasMarqueeAdorner
            {
                Fill = ResourceBrush("SelectedOverlayBrush", UiColorConstants.C_7C96FF22),
                Stroke = ResourceBrush("BorderFocusBrush", UiColorConstants.C_6B8CFF),
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            // Add rubber band before wires so it renders behind
            _overlay.Children.Insert(0, _rubberBandRect);
        }

        _selectionAdornerController.SyncMarqueeAdorner(ViewModel!, region, _rubberBandRect);

        // Ensure wires remain visible during rubber band selection
        EnsureWiresOnTop();
        _wires.InvalidateVisual();
    }

    private void ShowContextMenu(Point screen)
    {
        if (ViewModel is null)
            return;

        Point canvasPoint = ScreenToCanvas(screen);
        ConnectionViewModel? wireUnderPointer = _wires.HitTestWire(canvasPoint, tolerance: 8);
        var sel = ViewModel.Nodes.Where(n => n.IsSelected).ToList();
        var menu = new ContextMenu();
        menu.Classes.Add("canvas-context-menu");

        MenuItem NewItem(string header, MaterialIconKind icon, ICommand command, bool isEnabled = true)
        {
            var item = new MenuItem
            {
                Header = BuildContextMenuHeader(header, icon),
                Command = command,
                IsEnabled = isEnabled,
            };
            item.Classes.Add("canvas-context-menu-item");
            return item;
        }

        MenuItem NewSubmenu(string header, MaterialIconKind icon)
        {
            var item = new MenuItem
            {
                Header = BuildContextMenuHeader(header, icon),
            };
            item.Classes.Add("canvas-context-menu-item");
            return item;
        }

        MenuItem NewDisabledInfoItem(string header, MaterialIconKind icon)
        {
            var item = new MenuItem
            {
                Header = BuildContextMenuHeader(header, icon),
                IsEnabled = false,
            };
            item.Classes.Add("canvas-context-menu-item");
            return item;
        }

        Separator NewSeparator()
        {
            var separator = new Separator();
            separator.Classes.Add("canvas-context-menu-sep");
            return separator;
        }
        if (sel.Count > 0)
        {
            menu.Items.Add(
                NewItem(
                    sel.Count == 1
                        ? string.Format(L("context.deleteSingle", "Delete {0}"), sel[0].Title)
                        : string.Format(L("context.deleteMultiple", "Delete {0} nodes"), sel.Count),
                    MaterialIconKind.DeleteOutline,
                    new RelayCommand(ViewModel.DeleteSelected))
            );

            if (sel.Count == 1)
            {
                NodeViewModel selectedNode = sel[0];
                bool canSetAsFrom = ViewModel.CanSetPrimaryFromSource(selectedNode);
                menu.Items.Add(
                    NewItem(
                        L("context.setAsFromSource", "Definir como FROM"),
                        MaterialIconKind.DatabaseArrowLeft,
                        new RelayCommand(() => ViewModel.TrySetPrimaryFromSourceNode(selectedNode)),
                        canSetAsFrom));
            }

            if (sel.Count == 1 && sel[0].Type == NodeType.CteDefinition)
            {
                NodeViewModel cteNode = sel[0];
                menu.Items.Add(
                    NewItem(
                        LocalizationService.Instance["context.editCte"],
                        MaterialIconKind.FileTree,
                        new RelayCommand(() => _ = ViewModel.EnterCteEditorAsync(cteNode)))
                );
            }

            if (sel.Count == 1 && sel[0].Type == NodeType.ViewDefinition)
            {
                NodeViewModel viewNode = sel[0];
                menu.Items.Add(
                    NewItem(
                        LocalizationService.Instance["context.editViewSubcanvas"],
                        MaterialIconKind.FileCodeOutline,
                        new RelayCommand(() => _ = ViewModel.EnterViewEditorAsync(viewNode)))
                );
            }

            if (sel.Count == 1 && sel[0].Type is NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition)
            {
                NodeViewModel subqueryNode = sel[0];
                menu.Items.Add(
                    NewItem(
                        L("context.editSubquerySubcanvas", "Edit subquery"),
                        MaterialIconKind.FileCodeOutline,
                        new RelayCommand(() => _ = ViewModel.EnterSubqueryEditorAsync(subqueryNode)))
                );
            }

            if (sel.Count == 1 && sel[0].Type == NodeType.ResultOutput)
            {
                NodeViewModel resultOutputNode = sel[0];
                bool hasTopConnection = ViewModel.Connections.Any(connection =>
                    connection.ToPin is not null
                    && ReferenceEquals(connection.ToPin.Owner, resultOutputNode)
                    && string.Equals(connection.ToPin.Name, "top", StringComparison.OrdinalIgnoreCase));

                if (!hasTopConnection)
                {
                    menu.Items.Add(
                        NewItem(
                            L("context.quickTop100", "Quick add TOP 100"),
                            MaterialIconKind.SortNumericAscending,
                            new RelayCommand(() =>
                            {
                                if (TryQuickAddTop100ForResultOutput(ViewModel, resultOutputNode))
                                    SyncWires();
                            }))
                    );
                }
            }

            menu.Items.Add(
                NewItem(L("context.bringForward", "Bring Forward (Ctrl+PgUp)"), MaterialIconKind.ArrangeBringForward, ViewModel.BringSelectionForwardCommand)
            );
            menu.Items.Add(
                NewItem(L("context.sendBackward", "Send Backward (Ctrl+PgDown)"), MaterialIconKind.ArrangeSendBackward, ViewModel.SendSelectionBackwardCommand)
            );
            menu.Items.Add(
                NewItem(L("context.bringToFront", "Bring to Front (Ctrl+Shift+PgUp)"), MaterialIconKind.ArrangeBringToFront, ViewModel.BringSelectionToFrontCommand)
            );
            menu.Items.Add(
                NewItem(L("context.sendToBack", "Send to Back (Ctrl+Shift+PgDown)"), MaterialIconKind.ArrangeSendToBack, ViewModel.SendSelectionToBackCommand)
            );
            menu.Items.Add(
                NewItem(L("context.normalizeLayers", "Normalize Layers"), MaterialIconKind.LayersTripleOutline, ViewModel.NormalizeLayersCommand)
            );

            menu.Items.Add(NewSeparator());
            menu.Items.Add(
                NewItem(
                    "Node Wrangler: Bypass selected (Ctrl+Shift+X)",
                    MaterialIconKind.TransitSkip,
                    new RelayCommand(() =>
                    {
                        if (TryBypassSelectedNode())
                        {
                            SyncWires();
                            InvalidateArrange();
                        }
                    }))
            );
            menu.Items.Add(
                NewItem(
                    "Node Wrangler: Select upstream (Alt+Q)",
                    MaterialIconKind.SourceCommitStartNextLocal,
                    new RelayCommand(() =>
                    {
                        if (SelectLinkedNodes(traverseUpstream: true))
                        {
                            SyncWires();
                            InvalidateArrange();
                        }
                    }))
            );
            menu.Items.Add(
                NewItem(
                    "Node Wrangler: Select downstream (Alt+E)",
                    MaterialIconKind.SourceCommitEndLocal,
                    new RelayCommand(() =>
                    {
                        if (SelectLinkedNodes(traverseUpstream: false))
                        {
                            SyncWires();
                            InvalidateArrange();
                        }
                    }))
            );
        }
        else if (wireUnderPointer is not null)
        {
            ConnectionViewModel wire = wireUnderPointer;

            menu.Items.Add(
                NewItem(
                    L("context.wireRoutingBezier", "Routing: Bezier"),
                    MaterialIconKind.ChartBellCurve,
                    new RelayCommand(() =>
                    {
                        if (ViewModel is null)
                            return;

                        ViewModel.SelectConnection(wire);
                        if (ViewModel.SetConnectionRoutingMode(wire, CanvasWireRoutingMode.Bezier))
                            SyncWires();
                    }))
            );
            menu.Items.Add(
                NewItem(
                    L("context.wireRoutingStraight", "Routing: Straight"),
                    MaterialIconKind.RayStartEnd,
                    new RelayCommand(() =>
                    {
                        if (ViewModel is null)
                            return;

                        ViewModel.SelectConnection(wire);
                        if (ViewModel.SetConnectionRoutingMode(wire, CanvasWireRoutingMode.Straight))
                            SyncWires();
                    }))
            );
            menu.Items.Add(
                NewItem(
                    L("context.wireRoutingOrthogonal", "Routing: Orthogonal"),
                    MaterialIconKind.VectorPolyline,
                    new RelayCommand(() =>
                    {
                        if (ViewModel is null)
                            return;

                        ViewModel.SelectConnection(wire);
                        if (ViewModel.SetConnectionRoutingMode(wire, CanvasWireRoutingMode.Orthogonal))
                            SyncWires();
                    }))
            );
            menu.Items.Add(NewSeparator());

            WireContextBreakpointAction breakpointAction = ResolveWireContextBreakpointAction(wire, canvasPoint);
            if (breakpointAction.Kind == WireContextBreakpointActionKind.Remove)
            {
                int removeIndex = breakpointAction.Index;
                menu.Items.Add(
                    NewItem(
                        L("context.removeBreakpoint", "Remove breakpoint"),
                        MaterialIconKind.MapMarkerRemoveOutline,
                        new RelayCommand(() =>
                        {
                            if (ViewModel is null)
                                return;

                            ViewModel.SelectConnection(wire);
                            ViewModel.SelectWireBreakpoint(wire, removeIndex);
                            if (ViewModel.RemoveWireBreakpoint(wire, removeIndex))
                                SyncWires();
                        }))
                );
            }
            else if (breakpointAction.Kind == WireContextBreakpointActionKind.Add)
            {
                int addIndex = breakpointAction.Index;
                Point addPosition = breakpointAction.Position;
                menu.Items.Add(
                    NewItem(
                        L("context.addBreakpoint", "Add breakpoint"),
                        MaterialIconKind.MapMarkerPlusOutline,
                        new RelayCommand(() =>
                        {
                            if (ViewModel?.InsertWireBreakpoint(wire, addIndex, addPosition) == true)
                                SyncWires();
                        }))
                );
            }

            menu.Items.Add(NewSeparator());
            menu.Items.Add(
                NewItem(
                    "Node Wrangler: Select upstream endpoint",
                    MaterialIconKind.SourceCommitStart,
                    new RelayCommand(() =>
                    {
                        if (TrySelectWireEndpointNode(wire, upstream: true))
                            SyncWires();
                    }))
            );
            menu.Items.Add(
                NewItem(
                    "Node Wrangler: Select downstream endpoint",
                    MaterialIconKind.SourceCommitEnd,
                    new RelayCommand(() =>
                    {
                        if (TrySelectWireEndpointNode(wire, upstream: false))
                            SyncWires();
                    }))
            );
            menu.Items.Add(
                NewItem(
                    "Node Wrangler: Select upstream chain (Alt+Q)",
                    MaterialIconKind.SourceCommitStartNextLocal,
                    new RelayCommand(() =>
                    {
                        if (TrySelectWireEndpointNode(wire, upstream: true, traverseChain: true))
                            SyncWires();
                    }))
            );
            menu.Items.Add(
                NewItem(
                    "Node Wrangler: Select downstream chain (Alt+E)",
                    MaterialIconKind.SourceCommitEndLocal,
                    new RelayCommand(() =>
                    {
                        if (TrySelectWireEndpointNode(wire, upstream: false, traverseChain: true))
                            SyncWires();
                    }))
            );

            CanvasContext canvasContext = ViewModel.SearchMenu.CanvasContext;
            IReadOnlyList<NodeDefinition> compatibleDefinitions = ResolveCompatibleWireInsertDefinitions(
                wire,
                canvasContext,
                limit: 18);

            var insertNodeMenu = NewSubmenu(L("context.insertNodeOnWire", "Insert compatible node on wire"), MaterialIconKind.PlaylistPlus);

            if (compatibleDefinitions.Count == 0)
            {
                insertNodeMenu.Items.Add(
                    NewDisabledInfoItem(L("context.insertNodeOnWireNone", "No compatible node available"), MaterialIconKind.CloseCircleOutline)
                );
            }
            else
            {
                foreach (NodeDefinition definition in compatibleDefinitions)
                {
                    NodeDefinition capturedDefinition = definition;
                    insertNodeMenu.Items.Add(
                        NewItem(
                            $"{capturedDefinition.DisplayName} ({capturedDefinition.Category})",
                            ResolveContextIconForNodeDefinition(capturedDefinition),
                            new RelayCommand(() =>
                            {
                                if (TryInsertNodeDefinitionOnWire(wire, capturedDefinition))
                                    SyncWires();
                            }))
                    );
                }
            }

            menu.Items.Add(insertNodeMenu);
            menu.Items.Add(
                NewItem(L("context.deleteWire", "Delete wire"), MaterialIconKind.DeleteOutline, new RelayCommand(() => TryDeleteWire(wire)))
            );
        }
        else
        {
            menu.Items.Add(
                NewItem(
                    L("context.addNode", "Add Node (Shift+A)"),
                    MaterialIconKind.ShapePlus,
                    new RelayCommand(() =>
                        ViewModel.SearchMenu.Open(ScreenToCanvas(screen))
                    ))
            );
        }

        menu.Items.Add(NewSeparator());
        menu.Items.Add(
            NewItem(
                string.Format(L("context.undoWithDescription", "Undo {0}"), ViewModel.UndoRedo.UndoDescription),
                MaterialIconKind.UndoVariant,
                ViewModel.UndoCommand)
        );
        menu.Items.Add(NewItem(L("context.redo", "Redo"), MaterialIconKind.RedoVariant, ViewModel.RedoCommand));
        menu.Open(this);
    }

    private static object BuildContextMenuHeader(string text, MaterialIconKind iconKind)
    {
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new MaterialIcon
                {
                    Kind = iconKind,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = text,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            },
        };
    }

    private static MaterialIconKind ResolveContextIconForNodeDefinition(NodeDefinition definition)
    {
        return definition.Category switch
        {
            NodeCategory.DataSource => MaterialIconKind.DatabaseOutline,
            NodeCategory.Comparison => MaterialIconKind.FilterOutline,
            NodeCategory.Aggregate => MaterialIconKind.FunctionVariant,
            NodeCategory.MathTransform => MaterialIconKind.CalculatorVariantOutline,
            NodeCategory.StringTransform => MaterialIconKind.FormatLetterCase,
            NodeCategory.LogicGate => MaterialIconKind.SourceBranch,
            NodeCategory.TypeCast => MaterialIconKind.SwapHorizontal,
            NodeCategory.Json => MaterialIconKind.CodeJson,
            NodeCategory.Conditional => MaterialIconKind.HelpNetworkOutline,
            NodeCategory.ResultModifier => MaterialIconKind.TuneVariant,
            NodeCategory.Output => MaterialIconKind.ExportVariant,
            NodeCategory.Literal => MaterialIconKind.FormatTextVariantOutline,
            NodeCategory.Ddl => MaterialIconKind.DatabaseEditOutline,
            _ => MaterialIconKind.CircleOutline,
        };
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private bool IsWireInsertModifierActive()
    {
        return _isWireInsertModifierPressed;
    }

    private void SetWireInsertPreviewPins(PinViewModel? inputPin, PinViewModel? outputPin)
    {
        if (_wireInsertPreviewInputPin is not null)
            _wireInsertPreviewInputPin.IsDropTarget = false;
        if (_wireInsertPreviewOutputPin is not null)
            _wireInsertPreviewOutputPin.IsDropTarget = false;

        _wireInsertPreviewInputPin = inputPin;
        _wireInsertPreviewOutputPin = outputPin;

        if (_wireInsertPreviewInputPin is not null)
            _wireInsertPreviewInputPin.IsDropTarget = true;
        if (_wireInsertPreviewOutputPin is not null)
            _wireInsertPreviewOutputPin.IsDropTarget = true;
    }

    private void SetWireInsertPreviewInvalidWire(ConnectionViewModel? wire)
    {
        _wireInsertPreviewInvalidWire = wire;
        _wires.InvalidPreviewConnection = wire;
    }

    private void UpdateWireInsertPreview(NodeViewModel node)
    {
        if (ViewModel is null)
            return;

        CanvasHoverHighlighter.ClearHover(ViewModel);
        SetWireInsertPreviewPins(null, null);
        SetWireInsertPreviewInvalidWire(null);

        if (!IsWireInsertModifierActive())
        {
            _hoveredPin = null;
            _hoveredWire = null;
            _wires.InvalidateVisual();
            return;
        }

        ConnectionViewModel? wire = FindWireUnderNode(node);
        if (wire?.ToPin is null)
        {
            _hoveredPin = null;
            _hoveredWire = null;
            _wires.InvalidateVisual();
            return;
        }

        PinViewModel? insertInput = node.InputPins.FirstOrDefault(p => p.EvaluateConnection(wire.FromPin).IsAllowed);
        PinViewModel? insertOutput = node.OutputPins.FirstOrDefault(p => wire.ToPin.EvaluateConnection(p).IsAllowed);

        if (insertInput is null || insertOutput is null)
        {
            wire.IsHighlighted = true;
            wire.FromPin.IsHovered = true;
            wire.ToPin.IsHovered = true;
            SetWireInsertPreviewInvalidWire(wire);
            _hoveredPin = null;
            _hoveredWire = wire;
            _wires.InvalidateVisual();
            return;
        }

        wire.IsHighlighted = true;
        wire.FromPin.IsHovered = true;
        wire.ToPin.IsHovered = true;
        insertInput.IsHovered = true;
        insertOutput.IsHovered = true;
        SetWireInsertPreviewPins(insertInput, insertOutput);
        SetWireInsertPreviewInvalidWire(null);

        _hoveredPin = insertInput;
        _hoveredWire = wire;
        _wires.InvalidateVisual();
    }

    private bool TryInsertNodeOnWire(NodeViewModel node)
    {
        if (ViewModel is null || !IsWireInsertModifierActive())
            return false;

        ConnectionViewModel? wire = FindWireUnderNode(node);
        if (wire?.ToPin is null)
            return false;

        if (ReferenceEquals(wire.FromPin.Owner, node) || ReferenceEquals(wire.ToPin.Owner, node))
            return false;

        PinViewModel? insertInput = node.InputPins.FirstOrDefault(p => p.EvaluateConnection(wire.FromPin).IsAllowed);
        if (insertInput is null)
            return false;

        PinViewModel? insertOutput = node.OutputPins.FirstOrDefault(p => wire.ToPin.EvaluateConnection(p).IsAllowed);
        if (insertOutput is null)
            return false;

        using UndoRedoStack.UndoRedoTransaction tx = ViewModel.UndoRedo.BeginTransaction("Insert node on wire");

        ViewModel.DeleteConnection(wire);
        ViewModel.ConnectPins(wire.FromPin, insertInput);
        ViewModel.ConnectPins(insertOutput, wire.ToPin);

        bool insertedIn = ViewModel.Connections.Any(c =>
            ReferenceEquals(c.FromPin, wire.FromPin) && ReferenceEquals(c.ToPin, insertInput));
        bool insertedOut = ViewModel.Connections.Any(c =>
            ReferenceEquals(c.FromPin, insertOutput) && ReferenceEquals(c.ToPin, wire.ToPin));

        if (!insertedIn || !insertedOut)
            return false;

        tx.Commit();
        return true;
    }

    private ConnectionViewModel? FindWireUnderNode(NodeViewModel node)
    {
        if (!_nodeControlCache.TryGetValue(node, out NodeControl? control))
            return null;

        Point center = new(
            node.Position.X + Math.Max(control.Bounds.Width, 1) / 2,
            node.Position.Y + Math.Max(control.Bounds.Height, 1) / 2
        );

        return _wires.HitTestWire(center, tolerance: 16);
    }

    private bool TryBypassSelectedNode()
    {
        if (ViewModel is null)
            return false;

        NodeViewModel? selected = ViewModel.Nodes.FirstOrDefault(n => n.IsSelected);
        if (selected is null)
            return false;

        List<ConnectionViewModel> incoming =
        [
            .. ViewModel.Connections.Where(c => c.ToPin is not null && ReferenceEquals(c.ToPin.Owner, selected)),
        ];
        List<ConnectionViewModel> outgoing =
        [
            .. ViewModel.Connections.Where(c => c.ToPin is not null && ReferenceEquals(c.FromPin.Owner, selected)),
        ];

        ConnectionViewModel? inConn = null;
        ConnectionViewModel? outConn = null;

        foreach (ConnectionViewModel candidateIn in incoming)
        {
            ConnectionViewModel? candidateOut = outgoing.FirstOrDefault(o => o.ToPin is not null && o.ToPin.EvaluateConnection(candidateIn.FromPin).IsAllowed);
            if (candidateOut is null)
                continue;

            inConn = candidateIn;
            outConn = candidateOut;
            break;
        }

        if (inConn is null || outConn?.ToPin is null)
            return false;

        using UndoRedoStack.UndoRedoTransaction tx = ViewModel.UndoRedo.BeginTransaction("Bypass selected node");
        ViewModel.ConnectPins(inConn.FromPin, outConn.ToPin);
        ViewModel.DeleteSelected();
        tx.Commit();
        return true;
    }

    private bool SelectLinkedNodes(bool traverseUpstream)
    {
        if (ViewModel is null)
            return false;

        List<NodeViewModel> selected = [.. ViewModel.Nodes.Where(n => n.IsSelected)];
        if (selected.Count == 0)
            return false;

        var visited = new HashSet<NodeViewModel>(selected);
        var queue = new Queue<NodeViewModel>(selected);

        while (queue.Count > 0)
        {
            NodeViewModel current = queue.Dequeue();

            IEnumerable<NodeViewModel> linked = traverseUpstream
                ? ViewModel.Connections
                    .Where(c => c.ToPin is not null && ReferenceEquals(c.ToPin.Owner, current))
                    .Select(c => c.FromPin.Owner)
                : ViewModel.Connections
                    .Where(c => c.ToPin is not null && ReferenceEquals(c.FromPin.Owner, current))
                    .Select(c => c.ToPin!.Owner);

            foreach (NodeViewModel neighbor in linked)
            {
                if (!visited.Add(neighbor))
                    continue;

                queue.Enqueue(neighbor);
            }
        }

        ViewModel.DeselectAll();
        foreach (NodeViewModel node in visited)
            node.IsSelected = true;

        return true;
    }

    /// <summary>
    /// Detects horizontal and vertical alignment between the dragged node and all
    /// other (non-selected) nodes. Guides appear for: left edge, right edge,
    /// vertical centre, top edge, bottom edge, horizontal centre.
    /// </summary>
    private void UpdateAlignGuides(NodeViewModel dragged)
    {
        if (ViewModel is null)
        {
            _guides.ClearGuides();
            return;
        }

        var others = ViewModel.Nodes.Where(n => n != dragged && !n.IsSelected).ToList();

        double dX = dragged.Position.X;
        double dW = dragged.Width > 0 ? dragged.Width : 230;
        double dCX = dX + dW / 2.0;
        double dRX = dX + dW;

        double dY = dragged.Position.Y;
        double dH = DefaultNodeH;
        double dCY = dY + dH / 2.0;
        double dBY = dY + dH;

        var hGuides = new List<double>(); // Y coordinates of horizontal guides
        var vGuides = new List<double>(); // X coordinates of vertical guides

        foreach (NodeViewModel n in others)
        {
            double oX = n.Position.X;
            double oW = n.Width > 0 ? n.Width : 230;
            double oCX = oX + oW / 2.0;
            double oRX = oX + oW;

            double oY = n.Position.Y;
            double oH = DefaultNodeH;
            double oCY = oY + oH / 2.0;
            double oBY = oY + oH;

            // Vertical guides (X alignment)
            if (Math.Abs(dX - oX) < GuideThreshold)
                vGuides.Add(oX);
            if (Math.Abs(dX - oRX) < GuideThreshold)
                vGuides.Add(oRX);
            if (Math.Abs(dCX - oCX) < GuideThreshold)
                vGuides.Add(oCX);
            if (Math.Abs(dRX - oX) < GuideThreshold)
                vGuides.Add(oX);
            if (Math.Abs(dRX - oRX) < GuideThreshold)
                vGuides.Add(oRX);

            // Horizontal guides (Y alignment)
            if (Math.Abs(dY - oY) < GuideThreshold)
                hGuides.Add(oY);
            if (Math.Abs(dY - oBY) < GuideThreshold)
                hGuides.Add(oBY);
            if (Math.Abs(dCY - oCY) < GuideThreshold)
                hGuides.Add(oCY);
            if (Math.Abs(dBY - oY) < GuideThreshold)
                hGuides.Add(oY);
            if (Math.Abs(dBY - oBY) < GuideThreshold)
                hGuides.Add(oBY);
        }

        _guides.SetGuides([.. hGuides.Distinct()], [.. vGuides.Distinct()]);
        if (hGuides.Count > 0 || vGuides.Count > 0)
            Log($"    Guides updated: {hGuides.Distinct().Count()} h, {vGuides.Distinct().Count()} v");
    }

    private static IBrush ResourceBrush(string key, string fallbackHex)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out object? resource) == true && resource is IBrush brush)
            return brush;

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }
}
