using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CanvasControl = Avalonia.Controls.Canvas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;

namespace AkkornStudio.UI.Controls;

public sealed partial class InfiniteCanvas : Panel
{
    private static readonly ILogger<InfiniteCanvas> _logger = NullLogger<InfiniteCanvas>.Instance;

    private static void Log(string message)
    {
        _logger.LogDebug("{Message}", message);
    }

    public static readonly StyledProperty<CanvasViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<InfiniteCanvas, CanvasViewModel?>(nameof(ViewModel));

    public CanvasViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static InfiniteCanvas()
    {
        ViewModelProperty.Changed.AddClassHandler<InfiniteCanvas>((c, _) => c.Rebuild());
        ClipToBoundsProperty.OverrideDefaultValue<InfiniteCanvas>(true);
    }

    private readonly InfiniteCanvasCoreControl _core = new();
    private readonly BezierWireLayer _wires = new();
    private readonly CanvasViewportController _viewportController = new();
    private readonly CanvasViewportSelectionAdornerController _selectionAdornerController = new();
    private readonly CanvasViewportSelectionNavigationController _selectionNavigationController = new();
    private readonly CanvasViewportGesturePolicy _gesturePolicy = CanvasViewportGesturePolicy.InfiniteCanvasDefault;
    private DotGridBackground _grid => _core.GridBackground;
    private CanvasControl _scene => _core.SceneCanvas;
    private CanvasControl _overlay => _core.OverlayCanvas;

    private PinDragInteraction? _pinDrag;
    private double _zoom = 1.0;
    private Point _panOffset;
    private bool _isPanning;
    private bool _isApplyingViewportFromCanvas;
    private bool _isSpacePanArmed;
    private bool _contextMenuPending;
    private Point _contextMenuPressScreen;
    private const double ContextPanStartThreshold = 4.0;

    internal bool IsSpacePanModeArmed => _isSpacePanArmed;

    // NodifyM pattern: Track pan offset at start of operations for compensation during node drag
    private Point _startPanOffset;
    private Point _startNodeDragCanvasPos;

    private bool _isRubberBanding;
    private Point _rubberStart,
        _rubberCurrent;
    private const bool RubberBandEnabled = true;
    private NodeViewModel? _dragNode;
    private Point _nodeDragStart,
        _nodePosStart;

    // Multi-node group drag: other selected nodes moving with _dragNode
    private List<(NodeViewModel Node, Point StartPos)>? _groupDragStarts;

    // Track whether we've initialized from ViewModel to prevent resetting pan offset during rebuild
    private bool _hasInitialized;

    // Alignment guides drawn during node drag
    private readonly AlignGuidesLayer _guides = new() { IsHitTestVisible = false };

    // O(1) lookup: NodeViewModel → its NodeControl (maintained in SyncNodes)
    private readonly Dictionary<NodeViewModel, NodeControl> _nodeControlCache = new();
    // Per-node PropertyChanged handlers so they can be unsubscribed on removal
    private readonly Dictionary<NodeViewModel, PropertyChangedEventHandler> _nodePositionHandlers = new();

    // Skip guide recalculation when the node hasn't moved more than this threshold (canvas units)
    private Point _lastGuideCheckPosition;
    private const double GuideRecheckThresholdSq = 4.0; // 2px²
    private bool _wireSyncQueued;
    private int _wireSyncRetryCount;
    private PinViewModel? _hoveredPin;
    private ConnectionViewModel? _hoveredWire;
    private bool _isWireInsertModifierPressed;
    private PinViewModel? _wireInsertPreviewInputPin;
    private PinViewModel? _wireInsertPreviewOutputPin;
    private ConnectionViewModel? _wireInsertPreviewInvalidWire;
    private ConnectionViewModel? _dragBreakpointWire;
    private int _dragBreakpointIndex = -1;
    private Point _dragBreakpointInitialPosition;

    public InfiniteCanvas()
    {
        // Transparent background makes the entire panel surface hit-testable,
        // so middle-mouse panning and rubber-band work even on empty canvas areas.
        Background = Brushes.Transparent;
        _wires.IsHitTestVisible = false;
        Children.Add(_core);
        _scene.Children.Add(_wires);
        _overlay.Children.Add(_guides);
        PointerWheelChanged += OnWheel;
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerExited += (_, _) => ClearHoverHighlights();
        AddHandler(Avalonia.Input.DragDrop.DragOverEvent, OnDragOver);
        AddHandler(Avalonia.Input.DragDrop.DropEvent, OnDrop);
        Avalonia.Input.DragDrop.SetAllowDrop(this, true);
        KeyDown += OnKey;
        KeyUp += OnKeyUp;
        Focusable = true;
        // Re-sync wire endpoints after every layout pass so that pin positions
        // are computed from the real nc.Bounds.Width, not the 220px fallback
        // that is used when NodeControls have not yet been measured/arranged.
        LayoutUpdated += (_, _) =>
        {
            if (ViewModel is not null)
                RequestWireSync();
        };
    }

    private void RequestWireSync()
    {
        if (ViewModel is null || _wireSyncQueued)
            return;

        _wireSyncQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                _wireSyncQueued = false;
                if (ViewModel is not null)
                    SyncWires();
            },
            Avalonia.Threading.DispatcherPriority.Render
        );
    }

    private void ClearHoverHighlights()
    {
        if (ViewModel is null)
            return;

        if (_hoveredPin is null && _hoveredWire is null)
            return;

        CanvasHoverHighlighter.ClearHover(ViewModel);

        _hoveredPin = null;
        _hoveredWire = null;
        _wires.InvalidateVisual();
    }

    private void UpdateHoverHighlights(Point canvasPoint)
    {
        if (ViewModel is null)
            return;

        (PinViewModel? pin, ConnectionViewModel? wire) =
            CanvasHoverHighlighter.ResolveHoverTarget(ViewModel, _wires, _pinDrag, canvasPoint);

        if (ReferenceEquals(_hoveredPin, pin) && ReferenceEquals(_hoveredWire, wire))
            return;

        CanvasHoverHighlighter.ApplyHover(ViewModel, pin, wire);

        _hoveredPin = pin;
        _hoveredWire = wire;
        _wires.InvalidateVisual();
    }

    protected override Size MeasureOverride(Size s)
    {
        _core.Measure(s);
        Log($"    MeasureOverride: Size={s}, PanOffset={_panOffset}");
        return s;
    }

    protected override Size ArrangeOverride(Size s)
    {
        _core.Arrange(new Rect(s));

        foreach (NodeControl nc in _scene.Children.OfType<NodeControl>())
            if (nc.DataContext is NodeViewModel vm)
            {
                Canvas.SetLeft(nc, vm.Position.X);
                Canvas.SetTop(nc, vm.Position.Y);
            }

        _wires.Arrange(new Rect(new Size(20000, 20000)));
        _guides.Width = s.Width;
        _guides.Height = s.Height;
        _guides.Zoom = _zoom;
        _guides.PanOffset = _panOffset;

        // Keep FitToScreen accurate by letting the layout manager know the viewport size.
        ViewModel?.SetViewportSize(s.Width, s.Height);

        // DETECT UNEXPECTED PAN RESETS
        if (_panOffset.X == 0 && _panOffset.Y == 0 && _zoom == 1 && !_isPanning && _dragNode is null)
        {
            Log($"    [ArrangeOverride] Current state: PanOffset={_panOffset}, Zoom={_zoom}, Panning={_isPanning}, Dragging={_dragNode is not null}");
        }

        return s;
    }

    private void Rebuild()
    {
        Log($">>> REBUILD started");
        foreach (NodeControl? nc in _scene.Children.OfType<NodeControl>().ToList())
            _scene.Children.Remove(nc);
        _nodeControlCache.Clear();
        _nodePositionHandlers.Clear();
        if (ViewModel is null)
        {
            Log($"    ViewModel is null, exiting rebuild");
            _core.Viewport = null;
            return;
        }
        _core.Viewport = ViewModel;
        _pinDrag = new PinDragInteraction(ViewModel, _scene);
        ViewModel.Nodes.CollectionChanged += (_, e) =>
        {
            Log($"    !!! ViewModel.Nodes.CollectionChanged: Action={e.Action}");
            SyncNodes();
            // Node list changes can race with connection sync during import/load;
            // schedule a wire sync after nodes are materialized.
            RequestWireSync();
        };
        ViewModel.Connections.CollectionChanged += (_, e) =>
        {
            Log($"    !!! ViewModel.Connections.CollectionChanged: Action={e.Action}");
            RequestWireSync();
        };
        // Keep canvas transform in sync with ViewModel for both Zoom and PanOffset.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (_isApplyingViewportFromCanvas)
                return;

            if (e.PropertyName == nameof(CanvasViewModel.Zoom))
            {
                Log($"    !!! ViewModel.Zoom changed, calling SyncTransform");
                SyncTransform();
            }
            else if (e.PropertyName == nameof(CanvasViewModel.PanOffset))
            {
                Log($"    !!! ViewModel.PanOffset changed, calling SyncTransform");
                SyncTransform();
            }
            else if (e.PropertyName == nameof(CanvasViewModel.WireCurveMode))
            {
                _wires.WireCurveMode = ViewModel.WireCurveMode;
                _wires.InvalidateVisual();
            }
        };
        // Initialize from ViewModel (load from persistence) - only on first load
        // After first initialization, preserve _panOffset even if Rebuild is called again
        if (!_hasInitialized)
        {
            _panOffset = ViewModel.PanOffset;
            _zoom = ViewModel.Zoom;
            Log($"    First initialization: PanOffset={_panOffset}, Zoom={_zoom}");
            _hasInitialized = true;
        }
        else
        {
            Log($"    Rebuild called again, preserving PanOffset={_panOffset}, Zoom={_zoom}");
        }

        SyncNodes();

        // Force layout update to ensure NodeControls have been measured/arranged
        InvalidateArrange();

        // Defer SyncWires until after the first layout/render pass so that
        // TranslatePoint returns valid coordinates.  Calling it immediately here
        // (before NodeControls are measured/arranged by the visual tree) leaves
        // every AbsolutePosition at (0,0), making all wires invisible until the
        // user moves a node and triggers a re-sync.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            SyncWires,
            Avalonia.Threading.DispatcherPriority.Render
        );

        // Schedule additional SyncWires calls to ensure wires are rendered even if layout isn't complete
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                if (ViewModel is not null)
                {
                    InvalidateArrange();
                    RequestWireSync();
                }
            },
            Avalonia.Threading.DispatcherPriority.Background
        );

        SyncTransform();
        Log($"<<< REBUILD completed");
    }

    private void SyncNodes()
    {
        if (ViewModel is null)
            return;

        Log($"    SyncNodes: Starting sync for {ViewModel.Nodes.Count} nodes");

        // Add controls for nodes not yet in the cache
        foreach (NodeViewModel vm in ViewModel.Nodes)
        {
            if (_nodeControlCache.ContainsKey(vm))
                continue;
            var nc = new NodeControl { DataContext = vm };
            Canvas.SetLeft(nc, vm.Position.X);
            Canvas.SetTop(nc, vm.Position.Y);
            nc.ZIndex = vm.ZOrder;
            nc.NodeDragStarted += OnNodeDragStarted;
            nc.NodeDragDelta += OnNodeDragDelta;
            nc.NodeDragCompleted += OnNodeDragCompleted;
            nc.NodeClicked += OnNodeClicked;
            nc.NodeDoubleClicked += OnNodeDoubleClicked;
            nc.PinPressed += OnPinPressed;

            // Track position handler so it can be unsubscribed when the node is removed
            NodeControl capturedNc = nc;
            NodeViewModel capturedVm = vm;
            PropertyChangedEventHandler posHandler = (_, e) =>
            {
                try
                {
                    if (e.PropertyName == nameof(NodeViewModel.Position))
                    {
                        Log($"    !!! NODE POSITION CHANGED: {capturedVm.Title}, New Position={capturedVm.Position}");
                        Canvas.SetLeft(capturedNc, capturedVm.Position.X);
                        Canvas.SetTop(capturedNc, capturedVm.Position.Y);
                        Log($"    Canvas position set for {capturedVm.Title}");
                        RequestWireSync();
                        Log($"    RequestWireSync queued for position change");
                        InvalidateArrange();
                        Log($"    InvalidateArrange called for position change");
                    }
                    else if (e.PropertyName == nameof(NodeViewModel.ZOrder))
                    {
                        capturedNc.ZIndex = capturedVm.ZOrder;
                        RequestWireSync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in node position property-changed handler");
                    // Don't rethrow - this could prevent the whole app from continuing
                }
            };
            vm.PropertyChanged += posHandler;
            _nodePositionHandlers[vm] = posHandler;
            _nodeControlCache[vm] = nc;
            _scene.Children.Add(nc);
            Log($"      Added node '{vm.Title}' at {vm.Position}");
        }

        // Remove controls for nodes no longer in ViewModel.Nodes
        var currentNodes = new HashSet<NodeViewModel>(ViewModel.Nodes);
        var toRemove = _nodeControlCache.Keys.Where(vm => !currentNodes.Contains(vm)).ToList();
        foreach (NodeViewModel vm in toRemove)
        {
            NodeControl nc = _nodeControlCache[vm];
            nc.NodeDragStarted -= OnNodeDragStarted;
            nc.NodeDragDelta -= OnNodeDragDelta;
            nc.NodeDragCompleted -= OnNodeDragCompleted;
            nc.NodeClicked -= OnNodeClicked;
            nc.NodeDoubleClicked -= OnNodeDoubleClicked;
            nc.PinPressed -= OnPinPressed;
            if (_nodePositionHandlers.TryGetValue(vm, out PropertyChangedEventHandler? posHandler))
            {
                vm.PropertyChanged -= posHandler;
                _nodePositionHandlers.Remove(vm);
            }
            _nodeControlCache.Remove(vm);
            _scene.Children.Remove(nc);
            Log($"      Removed node '{vm.Title}'");
        }

        NormalizeNodeZOrder();

        // Keep wiring layer policy consistent after topology changes.
        EnsureWiresOnTop();
        // Keep wire endpoints synchronized after any node materialization/removal.
        RequestWireSync();
        Log($"    SyncNodes: Completed. Cache: {_nodeControlCache.Count} nodes");
    }

    private void EnsureWiresOnTop()
    {
        // Keep wires behind node cards, including drag operations,
        // to avoid endpoint markers popping above node content.
        _wires.ZIndex = -10_000;
        if (_rubberBandRect is not null)
            _rubberBandRect.ZIndex = -9_999;
    }

    private void SyncWires()
    {
        if (ViewModel is null)
            return;

        // Remove stale/orphan connections that can leave detached wire fragments
        // on canvas after template or node structure changes.
        var nodeSet = ViewModel.Nodes.ToHashSet();
        ConnectionViewModel? liveWire = _pinDrag?.LiveWire;
        List<ConnectionViewModel> staleConnections =
        [
            .. ViewModel.Connections.Where(c =>
                // Incomplete connections are only valid for the active drag wire.
                (c.ToPin is null && !ReferenceEquals(c, liveWire))
                // Both endpoints must belong to live nodes on canvas.
                || !nodeSet.Contains(c.FromPin.Owner)
                || (c.ToPin is not null && !nodeSet.Contains(c.ToPin.Owner))
            ),
        ];

        foreach (ConnectionViewModel stale in staleConnections)
            ViewModel.Connections.Remove(stale);

        int updatedPins = UpdatePinPositions();
        int connectionCount = ViewModel.Connections.Count(c => c.ToPin is not null);
        bool hasUnpositionedConnectionPin = ViewModel.Connections.Any(c =>
            c.ToPin is not null && (!c.FromPin.HasAbsolutePosition || !c.ToPin.HasAbsolutePosition)
        );

        // During add/import/load, node visuals may exist but not be fully translatable yet.
        // Avoid overwriting existing wire endpoints with transient invalid pin coordinates;
        // keep retrying across subsequent frames until all connected endpoints are valid.
        if (connectionCount > 0 && (updatedPins == 0 || hasUnpositionedConnectionPin))
        {
            _wireSyncRetryCount++;
            Log(
                $"    SyncWires deferred: updatedPins={updatedPins}, hasUnpositionedConnectionPin={hasUnpositionedConnectionPin} with {connectionCount} connections (retry {_wireSyncRetryCount})"
            );
            RequestWireSync();
            Avalonia.Threading.Dispatcher.UIThread.Post(
                RequestWireSync,
                Avalonia.Threading.DispatcherPriority.Background
            );
            return;
        }

        _wireSyncRetryCount = 0;
        foreach (ConnectionViewModel c in ViewModel.Connections)
        {
            if (c.ToPin is not null && (!c.FromPin.HasAbsolutePosition || !c.ToPin.HasAbsolutePosition))
                continue;

            c.FromPoint = c.FromPin.AbsolutePosition;
            if (c.ToPin is not null)
                c.ToPoint = c.ToPin.AbsolutePosition;
        }
        _wires.WireCurveMode = ViewModel.WireCurveMode;
        _wires.Connections = ViewModel.Connections;
        _wires.PendingConnection = _pinDrag?.IsDragging == true ? _pinDrag.LiveWire : null;
        _wires.InvalidPreviewConnection = _wireInsertPreviewInvalidWire;
        _wires.SelectedBreakpointConnection = ViewModel.SelectedBreakpointConnection;
        _wires.SelectedBreakpointIndex = ViewModel.SelectedBreakpointIndex;
        _wires.InvalidateVisual();
        Log($"    SyncWires: {ViewModel.Connections.Count} connections synced");
    }

    public void InvalidateWires() => SyncWires();

    private void SyncTransform()
    {
        if (ViewModel is null)
            return;
        _zoom = ViewModel.Zoom;
        _panOffset = ViewModel.PanOffset;
        Log($"    SyncTransform: Zoom={_zoom}, PanOffset={_panOffset}");

        // Apply immediately (not only on next layout pass) to avoid transient
        // or missed viewport states during fast wheel interactions.
        InvalidateArrange();
        _core.SyncViewport();
        _wires.InvalidateVisual();
    }

    private int UpdatePinPositions()
    {
        if (ViewModel is null)
            return 0;

        foreach (NodeViewModel node in ViewModel.Nodes)
            foreach (PinViewModel pin in node.AllPins)
                pin.ResetAbsolutePosition();

        var updatedPins = new HashSet<PinViewModel>();
        foreach (NodeViewModel node in ViewModel.Nodes)
        {
            if (!_nodeControlCache.TryGetValue(node, out NodeControl? nc))
                continue;

            // Primary path: PinShapeControl is the active pin visual in current templates.
            foreach (PinShapeControl pinControl in nc.GetLogicalDescendants().OfType<PinShapeControl>())
            {
                if (pinControl.DataContext is not PinViewModel pvm)
                    continue;
                if (pinControl.Bounds.Width == 0 || pinControl.Bounds.Height == 0)
                    continue; // skip collapsed/hidden elements

                var center = new Point(pinControl.Bounds.Width / 2, pinControl.Bounds.Height / 2);
                Point? inScene = pinControl.TranslatePoint(center, _scene);
                if (inScene.HasValue
                    && !double.IsNaN(inScene.Value.X)
                    && !double.IsNaN(inScene.Value.Y)
                    && !double.IsInfinity(inScene.Value.X)
                    && !double.IsInfinity(inScene.Value.Y))
                {
                    pvm.AbsolutePosition = inScene.Value;
                    updatedPins.Add(pvm);
                }
            }

            // Backward-compatible fallback for legacy templates that still render pins as Border.
            foreach (Border b in nc.GetLogicalDescendants().OfType<Border>())
            {
                if (b.DataContext is not PinViewModel pvm)
                    continue;
                if (updatedPins.Contains(pvm))
                    continue;
                if (b.Bounds.Width == 0 || b.Bounds.Height == 0)
                    continue;

                var center = new Point(b.Bounds.Width / 2, b.Bounds.Height / 2);
                Point? inScene = b.TranslatePoint(center, _scene);
                if (inScene.HasValue
                    && !double.IsNaN(inScene.Value.X)
                    && !double.IsNaN(inScene.Value.Y)
                    && !double.IsInfinity(inScene.Value.X)
                    && !double.IsInfinity(inScene.Value.Y))
                {
                    pvm.AbsolutePosition = inScene.Value;
                    updatedPins.Add(pvm);
                }
            }
        }
        Log($"    UpdatePinPositions: Updated {updatedPins.Count} pins");
        return updatedPins.Count;
    }
}

