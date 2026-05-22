using Avalonia;
using Avalonia.Media;
using AkkornStudio.CanvasKit;
using AkkornStudio.Nodes;
using AkkornStudio.Nodes.PinTypes;
using System.Collections.ObjectModel;

namespace AkkornStudio.UI.ViewModels;

public enum WireInteractionState
{
    Idle,
    Hover,
    Selected,
}

/// <summary>
/// Represents a connection (wire) entre two pins in the canvas.
/// Manages the visual and data flow representation of the connection.
/// </summary>
public sealed class ConnectionViewModel : ViewModelBase
{
    // Backward compatibility for existing tests and call sites.
    public enum EWireDashKind
    {
        Solid,
        ShortDash,
        MediumDash,
        LongDash,
        WideDash,
        Dotted,
    }

    private Point _fromPoint;
    private Point _toPoint;
    private bool _isHighlighted;
    private bool _isSelected;
    private CanvasWireRoutingMode _routingMode = CanvasWireRoutingMode.Bezier;
    private List<WireBreakpoint> _breakpoints = [];
    private ReadOnlyCollection<WireBreakpoint> _breakpointsView = null!;

    /// <summary>Unique identifier for this connection.</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>The pin data flows FROM (must be Output).</summary>
    public PinViewModel FromPin { get; }

    public ConnectionViewModel(PinViewModel fromPin, Point fromPoint, Point toPoint)
    {
        FromPin = fromPin;
        _fromPoint = fromPoint;
        _toPoint = toPoint;
        _breakpointsView = _breakpoints.AsReadOnly();
    }

    /// <summary>The pin data flows TO (must be Input). Can be null while dragging.</summary>
    public PinViewModel? ToPin { get; set; }

    /// <summary>Screen position of the source pin.</summary>
    public Point FromPoint
    {
        get => _fromPoint;
        set
        {
            Set(ref _fromPoint, value);
            RaisePropertyChanged(nameof(BezierPath));
        }
    }

    /// <summary>Screen position of the target pin.</summary>
    public Point ToPoint
    {
        get => _toPoint;
        set
        {
            Set(ref _toPoint, value);
            RaisePropertyChanged(nameof(BezierPath));
        }
    }

    /// <summary>True if this connection is currently highlighted (hovered).</summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (!Set(ref _isHighlighted, value))
                return;

            RaisePropertyChanged(nameof(WireOpacity));
            RaisePropertyChanged(nameof(WireThickness));
            RaisePropertyChanged(nameof(InteractionState));
            RaisePropertyChanged(nameof(IsToolbarVisible));
        }
    }

    /// <summary>True if this connection is the currently selected wire.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value))
                return;

            RaisePropertyChanged(nameof(WireOpacity));
            RaisePropertyChanged(nameof(WireThickness));
            RaisePropertyChanged(nameof(InteractionState));
            RaisePropertyChanged(nameof(IsTooltipVisible));
            RaisePropertyChanged(nameof(IsToolbarVisible));
        }
    }

    public WireInteractionState InteractionState => IsSelected
        ? WireInteractionState.Selected
        : IsHighlighted ? WireInteractionState.Hover : WireInteractionState.Idle;

    public bool IsTooltipVisible => IsSelected;

    public bool IsToolbarVisible => IsSelected || IsHighlighted;

    public CanvasWireRoutingMode RoutingMode
    {
        get => _routingMode;
        set
        {
            if (!Set(ref _routingMode, value))
                return;

            RaisePropertyChanged(nameof(BezierPath));
        }
    }

    public IReadOnlyList<WireBreakpoint> Breakpoints => _breakpointsView;

    internal void SetBreakpoints(IReadOnlyList<WireBreakpoint> breakpoints)
    {
        _breakpoints = [.. breakpoints];
        _breakpointsView = _breakpoints.AsReadOnly();
        RaisePropertyChanged(nameof(Breakpoints));
        RaisePropertyChanged(nameof(BezierPath));
    }

    /// <summary>Wire color matches the source pin's data type color.</summary>
    public Color WireColor => FromPin.PinColor;

    /// <summary>Opacity increases when highlighted.</summary>
    public double WireOpacity => IsSelected || IsHighlighted ? 1.0 : 0.75;

    public CanvasWireDashKind CanvasDashKind => PinTypeRegistry.GetType(FromPin.EffectiveDataType).WireDashKind switch
    {
        PinWireDashKind.ShortDash => CanvasWireDashKind.ShortDash,
        PinWireDashKind.MediumDash => CanvasWireDashKind.MediumDash,
        PinWireDashKind.LongDash => CanvasWireDashKind.LongDash,
        PinWireDashKind.WideDash => CanvasWireDashKind.WideDash,
        PinWireDashKind.Dotted => CanvasWireDashKind.Dotted,
        _ => CanvasWireDashKind.Solid,
    };

    public EWireDashKind DashKind => CanvasDashKind switch
    {
        CanvasWireDashKind.ShortDash => EWireDashKind.ShortDash,
        CanvasWireDashKind.MediumDash => EWireDashKind.MediumDash,
        CanvasWireDashKind.LongDash => EWireDashKind.LongDash,
        CanvasWireDashKind.WideDash => EWireDashKind.WideDash,
        CanvasWireDashKind.Dotted => EWireDashKind.Dotted,
        _ => EWireDashKind.Solid,
    };

    /// <summary>Thickness varies by pin family and increases when highlighted.</summary>
    public double WireThickness
    {
        get
        {
            double baseThickness = PinTypeRegistry.GetType(FromPin.EffectiveDataType).WireThickness;
            return CanvasWireStylePolicy.ResolveThickness(baseThickness, IsHighlighted || IsSelected);
        }
    }

    /// <summary>
    /// Generates a smooth cubic Bezier curve from FromPoint to ToPoint.
    /// Control points create smooth, readable connections.
    /// </summary>
    public string BezierPath
    {
        get => CanvasWireGeometry.BuildBezierPath(FromPoint.X, FromPoint.Y, ToPoint.X, ToPoint.Y);
    }
}
