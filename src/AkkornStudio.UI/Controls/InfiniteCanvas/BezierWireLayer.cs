using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using System.Linq;
using System.Globalization;
using AkkornStudio.CanvasKit;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.Services.Theming;

namespace AkkornStudio.UI.Controls;

/// <summary>
/// Transparent overlay canvas that draws all Bézier connection wires.
/// Sits behind all node controls but above the grid background.
///
/// Performance optimizations applied (Tarefa 18):
///   • Geometry cache: <see cref="PathGeometry"/> is only rebuilt when endpoints change.
///   • Brush/pen cache: pens and brushes are reused across frames, keyed by color+thickness.
///   • Viewport culling: wires whose AABB lies entirely outside <see cref="Bounds"/> are skipped.
///   • Static shared brushes for constant-color elements (endpoint background, grid bg).
/// </summary>
public sealed class BezierWireLayer : Control
{
    public enum WireToolbarAction
    {
        SetBezier,
        SetStraight,
        SetOrthogonal,
        Delete,
    }

    public readonly record struct WireToolbarHit(ConnectionViewModel Wire, WireToolbarAction Action);
    internal readonly record struct WireToolbarLayout(
        Rect Toolbar,
        Rect Bezier,
        Rect Straight,
        Rect Orthogonal,
        Rect Delete);

    private static readonly Typeface OverlayTypeface = new(new FontFamily("Segoe UI"));
    private sealed record RemovalFlash(
        Point From,
        Point To,
        CanvasWireRoutingMode RoutingMode,
        Color Color,
        double Thickness,
        CanvasWireDashKind DashKind,
        long StartedAtMs
    );

    private readonly List<RemovalFlash> _removalFlashes = [];
    private const long RemovalFlashDurationMs = 180;

    // ── Avalonia Properties ───────────────────────────────────────────────────

    public static readonly StyledProperty<IReadOnlyList<ConnectionViewModel>> ConnectionsProperty =
        AvaloniaProperty.Register<BezierWireLayer, IReadOnlyList<ConnectionViewModel>>(
            nameof(Connections),
            defaultValue: []
        );

    public static readonly StyledProperty<ConnectionViewModel?> PendingConnectionProperty =
        AvaloniaProperty.Register<BezierWireLayer, ConnectionViewModel?>(nameof(PendingConnection));

    public static readonly StyledProperty<ConnectionViewModel?> InvalidPreviewConnectionProperty =
        AvaloniaProperty.Register<BezierWireLayer, ConnectionViewModel?>(
            nameof(InvalidPreviewConnection)
        );

    public static readonly StyledProperty<CanvasWireCurveMode> WireCurveModeProperty =
        AvaloniaProperty.Register<BezierWireLayer, CanvasWireCurveMode>(
            nameof(WireCurveMode),
            defaultValue: CanvasWireCurveMode.Bezier
        );

    public static readonly StyledProperty<ConnectionViewModel?> SelectedBreakpointConnectionProperty =
        AvaloniaProperty.Register<BezierWireLayer, ConnectionViewModel?>(
            nameof(SelectedBreakpointConnection));

    public static readonly StyledProperty<int> SelectedBreakpointIndexProperty =
        AvaloniaProperty.Register<BezierWireLayer, int>(
            nameof(SelectedBreakpointIndex),
            defaultValue: -1);

    public IReadOnlyList<ConnectionViewModel> Connections
    {
        get => GetValue(ConnectionsProperty);
        set => SetValue(ConnectionsProperty, value);
    }

    public ConnectionViewModel? PendingConnection
    {
        get => GetValue(PendingConnectionProperty);
        set => SetValue(PendingConnectionProperty, value);
    }

    public ConnectionViewModel? InvalidPreviewConnection
    {
        get => GetValue(InvalidPreviewConnectionProperty);
        set => SetValue(InvalidPreviewConnectionProperty, value);
    }

    public CanvasWireCurveMode WireCurveMode
    {
        get => GetValue(WireCurveModeProperty);
        set => SetValue(WireCurveModeProperty, value);
    }

    public ConnectionViewModel? SelectedBreakpointConnection
    {
        get => GetValue(SelectedBreakpointConnectionProperty);
        set => SetValue(SelectedBreakpointConnectionProperty, value);
    }

    public int SelectedBreakpointIndex
    {
        get => GetValue(SelectedBreakpointIndexProperty);
        set => SetValue(SelectedBreakpointIndexProperty, value);
    }

    static BezierWireLayer()
    {
        ConnectionsProperty.Changed.AddClassHandler<BezierWireLayer>((c, _) =>
        {
            c.PruneGeomCache();
            c.InvalidateVisual();
        });
        PendingConnectionProperty.Changed.AddClassHandler<BezierWireLayer>(
            (c, _) => c.InvalidateVisual()
        );
        InvalidPreviewConnectionProperty.Changed.AddClassHandler<BezierWireLayer>(
            (c, _) => c.InvalidateVisual()
        );
        SelectedBreakpointConnectionProperty.Changed.AddClassHandler<BezierWireLayer>(
            (c, _) => c.InvalidateVisual()
        );
        SelectedBreakpointIndexProperty.Changed.AddClassHandler<BezierWireLayer>(
            (c, _) => c.InvalidateVisual()
        );
        WireCurveModeProperty.Changed.AddClassHandler<BezierWireLayer>((c, _) =>
        {
            c._geomCache.Clear();
            c.InvalidateVisual();
        });
        AffectsRender<BezierWireLayer>(
            ConnectionsProperty,
            PendingConnectionProperty,
            InvalidPreviewConnectionProperty,
            SelectedBreakpointConnectionProperty,
            SelectedBreakpointIndexProperty,
            WireCurveModeProperty
        );
    }

    // ── Geometry cache ────────────────────────────────────────────────────────

    private readonly record struct WireCacheKey(
        Point From,
        Point To,
        CanvasWireRoutingMode RoutingMode,
        int BreakpointHash,
        CanvasWireCurveMode PendingCurveMode
    );

    /// <summary>Cached geometry per connection — rebuilt only when endpoints change.</summary>
    private readonly Dictionary<ConnectionViewModel, (PathGeometry Geom, WireCacheKey Key)>
        _geomCache = new();

    /// <summary>
    /// Removes geometry cache entries for connections that are no longer in the list.
    /// Called whenever the Connections property changes to prevent unbounded cache growth.
    /// Avoids intermediate allocations when the cache is already fully valid.
    /// </summary>
    private void PruneGeomCache()
    {
        if (_geomCache.Count == 0)
            return;

        var current = new HashSet<ConnectionViewModel>(Connections);

        // Collect stale keys in a separate pass to avoid "modified during enumeration".
        // Only allocates when there are actually stale entries.
        List<ConnectionViewModel>? stale = null;
        foreach (ConnectionViewModel key in _geomCache.Keys)
        {
            if (!current.Contains(key))
                (stale ??= []).Add(key);
        }

        if (stale is not null)
            foreach (ConnectionViewModel key in stale)
                _geomCache.Remove(key);
    }

    // ── Brush / Pen cache ─────────────────────────────────────────────────────

    private readonly record struct PenKey(
        Color Color,
        double Thickness,
        bool IsGlow,
        CanvasWireDashKind DashKind
    );

    private readonly Dictionary<PenKey, Pen> _penCache = new();
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = new();
    private readonly Dictionary<Color, Pen> _dragPenCache = new();
    private static readonly DashStyle DragDashStyle = new([6, 4], 0);
    private static readonly DashStyle ShortDashStyle = new([4, 4], 0);
    private static readonly DashStyle MediumDashStyle = new([6, 3], 0);
    private static readonly DashStyle ColumnSetDashStyle = new([8, 3], 0);
    private static readonly DashStyle RowSetDashStyle = new([12, 4], 0);
    private static readonly DashStyle ExpressionDashStyle = new([2, 4], 0);
    private WireToolbarLayout _toolbarLayout = default;
    private ConnectionViewModel? _toolbarWire;
    private readonly Dictionary<string, Color> _colorResourceCache = new();
    private readonly Dictionary<string, double> _doubleResourceCache = new();

    private Color ResourceColor(string key, string fallbackHex)
    {
        if (_colorResourceCache.TryGetValue(key, out Color cached))
            return cached;

        if (Application.Current?.TryFindResource(key, out object? resource) == true)
        {
            if (resource is ISolidColorBrush brush)
            {
                _colorResourceCache[key] = brush.Color;
                return brush.Color;
            }

            if (resource is Color color)
            {
                _colorResourceCache[key] = color;
                return color;
            }
        }

        Color fallback = Color.Parse(fallbackHex);
        _colorResourceCache[key] = fallback;
        return fallback;
    }

    private double ResourceDouble(string key, double fallback)
    {
        if (_doubleResourceCache.TryGetValue(key, out double cached))
            return cached;

        if (Application.Current?.TryFindResource(key, out object? resource) == true)
        {
            if (resource is double d)
            {
                _doubleResourceCache[key] = d;
                return d;
            }

            if (resource is float f)
            {
                _doubleResourceCache[key] = f;
                return f;
            }

            if (resource is int i)
            {
                _doubleResourceCache[key] = i;
                return i;
            }
        }

        _doubleResourceCache[key] = fallback;
        return fallback;
    }

    private Pen GetPen(
        Color color,
        double thickness,
        CanvasWireDashKind dashKind = CanvasWireDashKind.Solid,
        bool isGlow = false
    )
    {
        var cacheDashKind = isGlow ? CanvasWireDashKind.Solid : dashKind;
        var key = new PenKey(color, thickness, isGlow, cacheDashKind);
        if (!_penCache.TryGetValue(key, out Pen? pen))
        {
            SolidColorBrush brush = GetSolidBrush(color);
            pen = isGlow
                ? new Pen(brush, thickness)
                : new Pen(brush, thickness) { LineCap = PenLineCap.Round };

            if (!isGlow)
            {
                pen.DashStyle = dashKind switch
                {
                    CanvasWireDashKind.ShortDash => ShortDashStyle,
                    CanvasWireDashKind.MediumDash => MediumDashStyle,
                    CanvasWireDashKind.LongDash => ColumnSetDashStyle,
                    CanvasWireDashKind.WideDash => RowSetDashStyle,
                    CanvasWireDashKind.Dotted => ExpressionDashStyle,
                    _ => null,
                };
            }

            _penCache[key] = pen;
        }
        return pen;
    }

    private SolidColorBrush GetSolidBrush(Color color)
    {
        if (!_brushCache.TryGetValue(color, out SolidColorBrush? brush))
        {
            brush = new SolidColorBrush(color);
            _brushCache[color] = brush;
        }

        return brush;
    }

    private Pen GetDragPen(Color color)
    {
        if (!_dragPenCache.TryGetValue(color, out Pen? pen))
        {
            pen = new Pen(GetSolidBrush(color), 2)
            {
                DashStyle = DragDashStyle,
                LineCap = PenLineCap.Round,
            };
            _dragPenCache[color] = pen;
        }

        return pen;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private static bool HasValidViewportDimensions(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 1 && height > 1;

    public override void Render(DrawingContext dc)
    {
        // Bounds can be transiently invalid right after topology/materialization updates.
        // In that case, disable aggressive culling by using a safe virtual viewport.
        Rect viewport = HasValidViewportDimensions(Bounds.Width, Bounds.Height)
            ? new Rect(Bounds.Size)
            : new Rect(0, 0, 20000, 20000);
        _toolbarLayout = default;
        _toolbarWire = null;

        foreach (ConnectionViewModel conn in Connections
                     .Where(c => c.ToPin is not null)
                     .OrderBy(GetConnectionDepth)
                     .ThenBy(GetEmphasisLevel))
            DrawWire(dc, conn, viewport);

        DrawWireAffordances(dc, viewport);
        DrawRemovalFlashes(dc, viewport);

        if (PendingConnection is not null && PendingConnection.ToPin is null)
            DrawWireDragging(dc, PendingConnection);
    }

    public bool TryHitToolbar(Point point, out WireToolbarHit hit)
    {
        hit = default;
        if (_toolbarWire is null)
            return false;

        if (TryResolveToolbarAction(point, _toolbarLayout, out WireToolbarAction action))
        {
            hit = new WireToolbarHit(_toolbarWire, action);
            return true;
        }

        return false;
    }

    public bool TryHitToolbarDelete(Point point, out ConnectionViewModel? wire)
    {
        wire = null;
        if (!TryHitToolbar(point, out WireToolbarHit hit))
            return false;

        if (hit.Action != WireToolbarAction.Delete)
            return false;

        wire = hit.Wire;
        return wire is not null;
    }

    internal static bool TryResolveToolbarAction(
        Point point,
        WireToolbarLayout layout,
        out WireToolbarAction action)
    {
        action = default;
        if (layout.Delete.Width > 0 && layout.Delete.Height > 0 && layout.Delete.Contains(point))
        {
            action = WireToolbarAction.Delete;
            return true;
        }

        if (layout.Bezier.Width > 0 && layout.Bezier.Height > 0 && layout.Bezier.Contains(point))
        {
            action = WireToolbarAction.SetBezier;
            return true;
        }

        if (layout.Straight.Width > 0 && layout.Straight.Height > 0 && layout.Straight.Contains(point))
        {
            action = WireToolbarAction.SetStraight;
            return true;
        }

        if (layout.Orthogonal.Width > 0 && layout.Orthogonal.Height > 0 && layout.Orthogonal.Contains(point))
        {
            action = WireToolbarAction.SetOrthogonal;
            return true;
        }

        return false;
    }

    internal static WireToolbarLayout BuildToolbarLayout(Point anchor)
    {
        Rect toolbarRect = new(anchor.X - 96, anchor.Y - 38, 192, 22);
        Rect bezierRect = new(toolbarRect.X + 6, toolbarRect.Y + 3, 48, 16);
        Rect straightRect = new(bezierRect.Right + 4, toolbarRect.Y + 3, 48, 16);
        Rect orthogonalRect = new(straightRect.Right + 4, toolbarRect.Y + 3, 48, 16);
        Rect deleteRect = new(toolbarRect.Right - 24, toolbarRect.Y + 3, 18, 16);
        return new WireToolbarLayout(toolbarRect, bezierRect, straightRect, orthogonalRect, deleteRect);
    }

    /// <summary>
    /// Adds a short-lived visual flash for a connection being removed,
    /// providing immediate deletion feedback to the user.
    /// </summary>
    public void AddRemovalFlash(ConnectionViewModel conn)
    {
        if (conn.ToPin is null)
            return;

        _removalFlashes.Add(
            new RemovalFlash(
                conn.FromPoint,
                conn.ToPoint,
                conn.RoutingMode,
                conn.WireColor,
                conn.WireThickness,
                conn.CanvasDashKind,
                Environment.TickCount64
            )
        );

        InvalidateVisual();
    }

    /// <summary>
    /// Returns the top-most wire near <paramref name="point"/>, or null when none is within tolerance.
    /// Mirrors the same depth/emphasis ordering used in render so hover feels consistent.
    /// </summary>
    public ConnectionViewModel? HitTestWire(Point point, double tolerance = 8)
    {
        ConnectionViewModel? best = null;
        int bestDepth = int.MinValue;
        int bestEmphasis = int.MinValue;

        foreach (ConnectionViewModel conn in Connections.Where(c => c.ToPin is not null))
        {
            if (!IsPointNearWire(point, conn.FromPoint, conn.ToPoint, conn.Breakpoints, conn.RoutingMode, tolerance))
                continue;

            int depth = GetConnectionDepth(conn);
            int emphasis = GetEmphasisLevel(conn);
            if (best is null
                || depth > bestDepth
                || (depth == bestDepth && emphasis > bestEmphasis))
            {
                best = conn;
                bestDepth = depth;
                bestEmphasis = emphasis;
            }
        }

        return best;
    }

    private static int GetConnectionDepth(ConnectionViewModel conn)
    {
        int fromDepth = conn.FromPin.Owner.ZOrder;
        int toDepth = conn.ToPin?.Owner.ZOrder ?? fromDepth;
        return Math.Min(fromDepth, toDepth);
    }

    private static bool IsConnectionEmphasized(ConnectionViewModel conn) =>
        GetEmphasisLevel(conn) > 0;

    private static int GetEmphasisLevel(ConnectionViewModel conn)
    {
        if (conn.IsSelected)
            return 4;

        if (conn.IsHighlighted)
            return 3;

        bool fromSelected = conn.FromPin.Owner.IsSelected;
        bool toSelected = conn.ToPin?.Owner.IsSelected ?? false;

        if (fromSelected && toSelected)
            return 2;
        if (fromSelected || toSelected)
            return 1;
        return 0;
    }

    private static Color BoostForEmphasis(Color baseColor, int emphasisLevel)
    {
        if (emphasisLevel <= 0)
            return baseColor;

        double t = emphasisLevel >= 3 ? 0.32 : emphasisLevel == 2 ? 0.22 : 0.12;
        byte r = (byte)Math.Clamp(baseColor.R + (255 - baseColor.R) * t, 0, 255);
        byte g = (byte)Math.Clamp(baseColor.G + (255 - baseColor.G) * t, 0, 255);
        byte b = (byte)Math.Clamp(baseColor.B + (255 - baseColor.B) * t, 0, 255);
        return Color.FromArgb(baseColor.A, r, g, b);
    }

    private void DrawWireAffordances(DrawingContext dc, Rect viewport)
    {
        ConnectionViewModel? selected = Connections.FirstOrDefault(c => c.ToPin is not null && c.IsSelected);
        ConnectionViewModel? hovered = selected is null
            ? Connections.FirstOrDefault(c => c.ToPin is not null && c.IsHighlighted)
            : null;
        ConnectionViewModel? affordanceWire = selected ?? hovered;
        if (affordanceWire is null)
            return;

        Point anchor = BuildAnchorPoint(affordanceWire);
        if (anchor.X < -20 || anchor.Y < -20 || anchor.X > viewport.Width + 20 || anchor.Y > viewport.Height + 20)
            return;

        DrawMiniToolbar(dc, affordanceWire, anchor);

        if (selected is not null && selected.IsTooltipVisible)
            DrawSelectedTooltip(dc, selected, anchor);
    }

    private void DrawMiniToolbar(DrawingContext dc, ConnectionViewModel wire, Point anchor)
    {
        WireToolbarLayout layout = BuildToolbarLayout(anchor);
        Rect toolbarRect = layout.Toolbar;
        var bg = GetSolidBrush(ResourceColor("Bg1", UiColorConstants.C_0F1220));
        var stroke = GetSolidBrush(ResourceColor("Border", UiColorConstants.C_334164));
        dc.DrawRectangle(bg, new Pen(stroke, 1), toolbarRect, 6, 6);

        var deleteBg = GetSolidBrush(ResourceColor("BtnDangerBg", UiColorConstants.C_41202A));
        var deleteStroke = GetSolidBrush(ResourceColor("StatusError", UiColorConstants.C_E16174));

        DrawRoutingModeButton(dc, layout.Bezier, "Bezier", wire.RoutingMode == CanvasWireRoutingMode.Bezier);
        DrawRoutingModeButton(dc, layout.Straight, "Straight", wire.RoutingMode == CanvasWireRoutingMode.Straight);
        DrawRoutingModeButton(dc, layout.Orthogonal, "Orthogonal", wire.RoutingMode == CanvasWireRoutingMode.Orthogonal);
        dc.DrawRectangle(deleteBg, new Pen(deleteStroke, 1), layout.Delete, 4, 4);

        DrawText(
            dc,
            "x",
            new Point(layout.Delete.X + 6, layout.Delete.Y + 1),
            11,
            GetSolidBrush(ResourceColor("BtnDangerFg", UiColorConstants.C_FFD2DB))
        );

        _toolbarLayout = layout;
        _toolbarWire = wire;
    }

    private void DrawRoutingModeButton(DrawingContext dc, Rect bounds, string label, bool selected)
    {
        var fill = GetSolidBrush(
            selected
                ? ResourceColor("TabActiveBg", UiColorConstants.C_263556)
                : ResourceColor("Bg2", UiColorConstants.C_151A2C)
        );
        var stroke = GetSolidBrush(
            selected
                ? ResourceColor("BorderFocus", UiColorConstants.C_6B8CFF)
                : ResourceColor("BorderSubtle", UiColorConstants.C_2A3554)
        );
        var text = GetSolidBrush(
            selected
                ? ResourceColor("TextPrimary", UiColorConstants.C_E7ECFF)
                : ResourceColor("TextSecondary", UiColorConstants.C_AEB9D9)
        );
        dc.DrawRectangle(fill, new Pen(stroke, selected ? 1.2 : 1), bounds, 4, 4);
        DrawText(dc, label, new Point(bounds.X + 4, bounds.Y + 1), 9, text);
    }

    private void DrawSelectedTooltip(DrawingContext dc, ConnectionViewModel wire, Point anchor)
    {
        string fromNode = wire.FromPin.Owner.Title;
        string fromPin = wire.FromPin.Name;
        string toNode = wire.ToPin?.Owner.Title ?? "?";
        string toPin = wire.ToPin?.Name ?? "?";
        string routing = wire.RoutingMode.ToString();

        string line1 = $"{fromNode}.{fromPin} -> {toNode}.{toPin}";
        string line2 = $"Routing: {routing}";
        string line3 = $"Type: {wire.FromPin.EffectiveDataType}";

        double width = Math.Max(220, Math.Max(MeasureTextWidth(line1, 11), Math.Max(MeasureTextWidth(line2, 10), MeasureTextWidth(line3, 10))) + 20);
        Rect tooltipRect = new(anchor.X - width / 2, anchor.Y + 14, width, 52);
        var bg = GetSolidBrush(ResourceColor("Bg0", UiColorConstants.C_090B14));
        var stroke = GetSolidBrush(ResourceColor("BorderSubtle", UiColorConstants.C_2A3554));
        dc.DrawRectangle(bg, new Pen(stroke, 1), tooltipRect, 8, 8);

        DrawText(dc, line1, new Point(tooltipRect.X + 10, tooltipRect.Y + 8), 11, GetSolidBrush(ResourceColor("TextPrimary", UiColorConstants.C_E7ECFF)));
        DrawText(dc, line2, new Point(tooltipRect.X + 10, tooltipRect.Y + 24), 10, GetSolidBrush(ResourceColor("StatusInfo", UiColorConstants.C_4D9BFF)));
        DrawText(dc, line3, new Point(tooltipRect.X + 10, tooltipRect.Y + 38), 10, GetSolidBrush(ResourceColor("TextMuted", UiColorConstants.C_7F8AAE)));
    }

    private static void DrawText(DrawingContext dc, string text, Point origin, double size, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            OverlayTypeface,
            size,
            brush);
        dc.DrawText(formatted, origin);
    }

    private static double MeasureTextWidth(string text, double size)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            OverlayTypeface,
            size,
            new SolidColorBrush(Color.Parse(UiColorConstants.C_E7ECFF)));
        return formatted.Width;
    }

    private static Point BuildAnchorPoint(ConnectionViewModel wire)
    {
        if (wire.RoutingMode == CanvasWireRoutingMode.Orthogonal)
        {
            IReadOnlyList<Point> points = BuildOrthogonalPolyline(wire.FromPoint, wire.ToPoint, wire.Breakpoints);
            return InterpolatePolylineMidPoint(points);
        }

        return new Point(
            (wire.FromPoint.X + wire.ToPoint.X) * 0.5,
            (wire.FromPoint.Y + wire.ToPoint.Y) * 0.5);
    }

    private static Point InterpolatePolylineMidPoint(IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
            return points.Count == 1 ? points[0] : default;

        double total = 0;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector segment = points[i + 1] - points[i];
            total += Math.Sqrt((segment.X * segment.X) + (segment.Y * segment.Y));
        }

        if (total <= double.Epsilon)
            return points[0];

        double target = total * 0.5;
        double walked = 0;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector segment = points[i + 1] - points[i];
            double len = Math.Sqrt((segment.X * segment.X) + (segment.Y * segment.Y));
            if (walked + len < target)
            {
                walked += len;
                continue;
            }

            double localT = (target - walked) / Math.Max(len, double.Epsilon);
            return points[i] + (segment * localT);
        }

        return points[^1];
    }

    private void DrawWire(DrawingContext dc, ConnectionViewModel conn, Rect viewport)
    {
        Point from = conn.FromPoint;
        Point to   = conn.ToPoint;
        int emphasisLevel = GetEmphasisLevel(conn);
        bool emphasized = emphasisLevel > 0;

        // ── Viewport culling ──────────────────────────────────────────────────
        (double minX, double minY, double maxX, double maxY) = GetWireBounds(from, to, conn.RoutingMode, conn.Breakpoints);
        if (maxX < 0 || minX > viewport.Width || maxY < 0 || minY > viewport.Height)
            return; // entirely outside viewport

        // ── Geometry cache ────────────────────────────────────────────────────
        PathGeometry geometry = GetOrBuildGeometry(conn, from, to);

        Color color = conn.WireColor;
        double thickness = conn.WireThickness;
        bool isSelected = conn.IsSelected;
        bool isHovered = conn.IsHighlighted && !isSelected;
        double normalOpacity = ResourceDouble("WireOpacityNormal", 0.85);
        double hoverOpacity = ResourceDouble("WireOpacityHover", 1.0);
        double selectedOpacity = ResourceDouble("WireOpacitySelected", 1.0);
        double invalidOpacity = ResourceDouble("WireOpacityInvalid", 0.95);
        double hoverThicknessDelta = ResourceDouble("WireThicknessHoverDelta", 0.4);
        double selectedThicknessDelta = ResourceDouble("WireThicknessSelectedDelta", 0.8);
        double invalidThicknessDelta = ResourceDouble("WireThicknessInvalidDelta", 0.4);
        bool invalidPreview = ReferenceEquals(conn, InvalidPreviewConnection);
        if (invalidPreview)
        {
            color = ResourceColor("StatusError", UiColorConstants.C_E16174);
            thickness += invalidThicknessDelta;
        }

        thickness += isSelected ? selectedThicknessDelta : isHovered ? hoverThicknessDelta : 0;
        double baseOpacity = invalidPreview
            ? invalidOpacity
            : isSelected ? selectedOpacity : isHovered ? hoverOpacity : normalOpacity;

        double glowThickness = emphasisLevel >= 3 ? thickness + 10 : emphasisLevel == 2 ? thickness + 9 : emphasized ? thickness + 8 : thickness + 6;
        double mainThickness = emphasisLevel >= 2 ? thickness + 1.2 : emphasized ? thickness + 1.0 : thickness;
        double glowOpacity = invalidPreview
            ? 0.72
            : isSelected ? 0.55 : isHovered ? 0.42 : emphasisLevel >= 3 ? 0.52 : emphasisLevel == 2 ? 0.46 : emphasized ? 0.4 : 0.3;
        double endpointRadius = emphasisLevel >= 2 ? 4.6 : emphasized ? 4.2 : 3.5;

        // Glow pass (thick, low-alpha)
        Color glowSource = invalidPreview
            ? ResourceColor("WireErrorGlowBrush", UiColorConstants.C_E1617440)
            : isSelected ? ResourceColor("WireSelectedGlowBrush", UiColorConstants.C_8FA7FF55) : color;
        var glowColor = Color.FromArgb(25, glowSource.R, glowSource.G, glowSource.B);
        Pen glowPen = GetPen(glowColor, glowThickness, isGlow: true);
        using (dc.PushOpacity(glowOpacity))
            dc.DrawGeometry(null, glowPen, geometry);

        // Main wire
        Color boosted = BoostForEmphasis(color, emphasisLevel);
        byte mainAlpha = (byte)Math.Clamp(baseOpacity * 255, 0, 255);
        var mainColor  = Color.FromArgb(mainAlpha, boosted.R, boosted.G, boosted.B);
        Pen mainPen    = GetPen(
            mainColor,
            mainThickness,
            invalidPreview ? CanvasWireDashKind.ShortDash : conn.CanvasDashKind
        );
        dc.DrawGeometry(null, mainPen, geometry);

        // Endpoint dots
        DrawEndpointDot(dc, from, boosted, radius: endpointRadius);
        DrawEndpointDot(dc, to,   boosted, radius: endpointRadius);

        if (conn.RoutingMode == CanvasWireRoutingMode.Orthogonal && conn.IsSelected)
        {
            int selectedBreakpointIndex = ReferenceEquals(conn, SelectedBreakpointConnection)
                ? SelectedBreakpointIndex
                : -1;
            DrawBreakpointHandles(dc, conn.Breakpoints, selectedBreakpointIndex);
        }
    }

    private PathGeometry GetOrBuildGeometry(
        ConnectionViewModel conn,
        Point from,
        Point to
    )
    {
        var key = new WireCacheKey(
            from,
            to,
            conn.RoutingMode,
            ComputeBreakpointHash(conn.Breakpoints),
            WireCurveMode);

        if (_geomCache.TryGetValue(conn, out var cached) && cached.Key == key)
            return cached.Geom; // endpoints unchanged — reuse

        PathGeometry geom = BuildGeometry(from, to, conn.RoutingMode, conn.Breakpoints);
        _geomCache[conn] = (geom, key);
        return geom;
    }

    private static int ComputeBreakpointHash(IReadOnlyList<WireBreakpoint> breakpoints)
    {
        var hash = new HashCode();
        hash.Add(breakpoints.Count);

        foreach (WireBreakpoint breakpoint in breakpoints)
        {
            hash.Add(breakpoint.Position.X);
            hash.Add(breakpoint.Position.Y);
        }

        return hash.ToHashCode();
    }

    private void DrawWireDragging(DrawingContext dc, ConnectionViewModel conn)
    {
        Color color = conn.WireColor;
        Point from = conn.FromPoint;
        Point to   = conn.ToPoint;
        PathGeometry geometry = BuildGeometry(from, to, WireCurveMode.ToRoutingMode(), []);

        // Dashed animated-looking wire for pending connections.
        using (dc.PushOpacity(ResourceDouble("WireOpacityPreview", 0.7)))
            dc.DrawGeometry(null, GetDragPen(color), geometry);

        DrawEndpointDot(dc, from, color, radius: 4);
    }

    private void DrawRemovalFlashes(DrawingContext dc, Rect viewport)
    {
        if (_removalFlashes.Count == 0)
            return;

        long now = Environment.TickCount64;
        bool hasActive = false;

        for (int i = _removalFlashes.Count - 1; i >= 0; i--)
        {
            RemovalFlash flash = _removalFlashes[i];
            double t = (now - flash.StartedAtMs) / (double)RemovalFlashDurationMs;
            if (t >= 1)
            {
                _removalFlashes.RemoveAt(i);
                continue;
            }

            hasActive = true;

            Point from = flash.From;
            Point to = flash.To;
            (double minX, double minY, double maxX, double maxY) = GetWireBounds(from, to, flash.RoutingMode, []);
            if (maxX < 0 || minX > viewport.Width || maxY < 0 || minY > viewport.Height)
                continue;

            PathGeometry geometry = BuildGeometry(from, to, flash.RoutingMode, []);
            double ease = (1 - t) * (1 - t);
            byte alpha = (byte)(220 * ease);
            Color color = Color.FromArgb(alpha, flash.Color.R, flash.Color.G, flash.Color.B);
            Pen pen = GetPen(color, flash.Thickness + 2.2, flash.DashKind);

            using (dc.PushOpacity(0.95))
                dc.DrawGeometry(null, pen, geometry);
        }

        if (hasActive)
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private (double minX, double minY, double maxX, double maxY) GetWireBounds(
        Point from,
        Point to,
        CanvasWireRoutingMode routingMode,
        IReadOnlyList<WireBreakpoint> breakpoints
    )
    {
        if (routingMode == CanvasWireRoutingMode.Straight)
        {
            double minX = Math.Min(from.X, to.X);
            double minY = Math.Min(from.Y, to.Y);
            double maxX = Math.Max(from.X, to.X);
            double maxY = Math.Max(from.Y, to.Y);
            return (minX, minY, maxX, maxY);
        }

        if (routingMode == CanvasWireRoutingMode.Orthogonal)
        {
            IReadOnlyList<Point> points = BuildOrthogonalPolyline(from, to, breakpoints);
            double minX = points.Min(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxX = points.Max(p => p.X);
            double maxY = points.Max(p => p.Y);
            return (minX, minY, maxX, maxY);
        }

        (Point c1, Point c2) = BezierControlPoints(from, to);
        double bezMinX = Math.Min(Math.Min(from.X, to.X), Math.Min(c1.X, c2.X));
        double bezMinY = Math.Min(Math.Min(from.Y, to.Y), Math.Min(c1.Y, c2.Y));
        double bezMaxX = Math.Max(Math.Max(from.X, to.X), Math.Max(c1.X, c2.X));
        double bezMaxY = Math.Max(Math.Max(from.Y, to.Y), Math.Max(c1.Y, c2.Y));
        return (bezMinX, bezMinY, bezMaxX, bezMaxY);
    }

    private PathGeometry BuildGeometry(
        Point from,
        Point to,
        CanvasWireRoutingMode routingMode,
        IReadOnlyList<WireBreakpoint> breakpoints
    )
    {
        if (routingMode == CanvasWireRoutingMode.Straight)
            return BuildStraight(from, to);

        if (routingMode == CanvasWireRoutingMode.Orthogonal)
            return BuildOrthogonal(from, to, breakpoints);

        (Point c1, Point c2) = BezierControlPoints(from, to);
        return BuildBezier(from, c1, c2, to);
    }

    private static (Point c1, Point c2) BezierControlPoints(Point from, Point to)
    {
        double dx = Math.Abs(to.X - from.X);
        double offset = Math.Max(60, dx * 0.5);
        return (new Point(from.X + offset, from.Y), new Point(to.X - offset, to.Y));
    }

    private static PathGeometry BuildBezier(Point from, Point c1, Point c2, Point to)
    {
        var seg = new BezierSegment
        {
            Point1 = c1,
            Point2 = c2,
            Point3 = to,
        };

        var figure = new PathFigure
        {
            StartPoint = from,
            IsClosed   = false,
            IsFilled   = false,
            Segments   = [seg],
        };

        return new PathGeometry { Figures = [figure] };
    }

    private static PathGeometry BuildStraight(Point from, Point to)
    {
        var seg = new LineSegment
        {
            Point = to,
        };

        var figure = new PathFigure
        {
            StartPoint = from,
            IsClosed = false,
            IsFilled = false,
            Segments = [seg],
        };

        return new PathGeometry { Figures = [figure] };
    }

    private static PathGeometry BuildOrthogonal(
        Point from,
        Point to,
        IReadOnlyList<WireBreakpoint> breakpoints
    )
    {
        IReadOnlyList<Point> points = BuildOrthogonalPolyline(from, to, breakpoints);
        var segments = new PathSegments();
        for (int index = 1; index < points.Count; index++)
            segments.Add(new LineSegment { Point = points[index] });

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false,
            Segments = segments,
        };

        return new PathGeometry { Figures = [figure] };
    }

    internal static IReadOnlyList<Point> BuildOrthogonalPolyline(
        Point from,
        Point to,
        IReadOnlyList<WireBreakpoint> breakpoints
    )
    {
        if (breakpoints.Count > 0)
            return [from, .. breakpoints.Select(b => b.Position), to];

        if (Math.Abs(from.X - to.X) < 0.1 || Math.Abs(from.Y - to.Y) < 0.1)
            return [from, to];

        double midX = (from.X + to.X) * 0.5;
        return
        [
            from,
            new Point(midX, from.Y),
            new Point(midX, to.Y),
            to,
        ];
    }

    private bool IsPointNearWire(
        Point p,
        Point from,
        Point to,
        IReadOnlyList<WireBreakpoint> breakpoints,
        CanvasWireRoutingMode routingMode,
        double tolerance
    )
    {
        if (routingMode == CanvasWireRoutingMode.Straight)
            return DistanceSqPointToSegment(p, from, to) <= (tolerance * tolerance);

        if (routingMode == CanvasWireRoutingMode.Orthogonal)
        {
            IReadOnlyList<Point> points = BuildOrthogonalPolyline(from, to, breakpoints);
            double tolSq = tolerance * tolerance;
            for (int index = 0; index < points.Count - 1; index++)
            {
                if (DistanceSqPointToSegment(p, points[index], points[index + 1]) <= tolSq)
                    return true;
            }

            return false;
        }

        return IsPointNearBezier(p, from, to, tolerance);
    }

    private static bool IsPointNearBezier(Point p, Point from, Point to, double tolerance)
    {
        (Point c1, Point c2) = BezierControlPoints(from, to);
        double tolSq = tolerance * tolerance;

        Point prev = from;
        const int segments = 24;
        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)segments;
            Point cur = EvaluateCubicBezier(from, c1, c2, to, t);
            if (DistanceSqPointToSegment(p, prev, cur) <= tolSq)
                return true;
            prev = cur;
        }

        return false;
    }

    private static Point EvaluateCubicBezier(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double u = 1 - t;
        double tt = t * t;
        double uu = u * u;
        double uuu = uu * u;
        double ttt = tt * t;

        double x = (uuu * p0.X) + (3 * uu * t * p1.X) + (3 * u * tt * p2.X) + (ttt * p3.X);
        double y = (uuu * p0.Y) + (3 * uu * t * p1.Y) + (3 * u * tt * p2.Y) + (ttt * p3.Y);
        return new Point(x, y);
    }

    private static double DistanceSqPointToSegment(Point p, Point a, Point b)
    {
        double vx = b.X - a.X;
        double vy = b.Y - a.Y;
        double wx = p.X - a.X;
        double wy = p.Y - a.Y;

        double lenSq = (vx * vx) + (vy * vy);
        if (lenSq <= double.Epsilon)
            return (wx * wx) + (wy * wy);

        double t = ((wx * vx) + (wy * vy)) / lenSq;
        t = Math.Clamp(t, 0, 1);

        double px = a.X + (t * vx);
        double py = a.Y + (t * vy);
        double dx = p.X - px;
        double dy = p.Y - py;
        return (dx * dx) + (dy * dy);
    }

    private void DrawEndpointDot(DrawingContext dc, Point center, Color color, double radius)
    {
        Color dotBg = ResourceColor("Bg2", UiColorConstants.C_151A2C);
        dc.DrawEllipse(GetSolidBrush(dotBg), null, center, radius + 1.5, radius + 1.5);
        dc.DrawEllipse(GetSolidBrush(color), null, center, radius, radius);
    }

    private void DrawBreakpointHandles(
        DrawingContext dc,
        IReadOnlyList<WireBreakpoint> breakpoints,
        int selectedBreakpointIndex)
    {
        if (breakpoints.Count == 0)
            return;

        SolidColorBrush fill = GetSolidBrush(ResourceColor("Bg1", UiColorConstants.C_0F1220));
        SolidColorBrush stroke = GetSolidBrush(ResourceColor("BorderFocus", UiColorConstants.C_6B8CFF));
        SolidColorBrush selectedFill = GetSolidBrush(ResourceColor("AccentPrimary", UiColorConstants.C_5B7CFA));
        SolidColorBrush selectedStroke = GetSolidBrush(ResourceColor("TextPrimary", UiColorConstants.C_E7ECFF));
        Pen normalPen = new(stroke, 1.4);
        Pen selectedPen = new(selectedStroke, 2.0);

        for (int index = 0; index < breakpoints.Count; index++)
        {
            WireBreakpoint breakpoint = breakpoints[index];
            bool isSelected = index == selectedBreakpointIndex;
            dc.DrawRectangle(
                isSelected ? selectedFill : fill,
                isSelected ? selectedPen : normalPen,
                new Rect(
                    breakpoint.Position.X - 4,
                    breakpoint.Position.Y - 4,
                    8,
                    8));
        }
    }

    internal static bool TryProjectToOrthogonalSegment(
        ConnectionViewModel wire,
        Point point,
        double tolerance,
        out Point projected,
        out int insertIndex,
        out int segmentStartIndex
    )
    {
        projected = default;
        insertIndex = -1;
        segmentStartIndex = -1;

        if (wire.RoutingMode != CanvasWireRoutingMode.Orthogonal)
            return false;

        IReadOnlyList<Point> points = BuildOrthogonalPolyline(wire.FromPoint, wire.ToPoint, wire.Breakpoints);
        if (points.Count < 2)
            return false;

        double tolSq = tolerance * tolerance;
        double bestDistance = double.MaxValue;
        Point bestProjected = default;
        int bestSegment = -1;

        for (int index = 0; index < points.Count - 1; index++)
        {
            Point start = points[index];
            Point end = points[index + 1];
            Point candidate = ProjectPointToSegment(point, start, end);
            double distance = DistanceSq(point, candidate);
            if (distance > tolSq || distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestProjected = candidate;
            bestSegment = index;
        }

        if (bestSegment < 0)
            return false;

        projected = bestProjected;
        segmentStartIndex = bestSegment;
        insertIndex = bestSegment;
        return true;
    }

    internal static int FindBreakpointAt(ConnectionViewModel wire, Point point, double tolerance)
    {
        if (wire.Breakpoints.Count == 0)
            return -1;

        double tolSq = tolerance * tolerance;
        for (int index = 0; index < wire.Breakpoints.Count; index++)
        {
            if (DistanceSq(point, wire.Breakpoints[index].Position) <= tolSq)
                return index;
        }

        return -1;
    }

    private static Point ProjectPointToSegment(Point point, Point start, Point end)
    {
        double vx = end.X - start.X;
        double vy = end.Y - start.Y;
        double lenSq = (vx * vx) + (vy * vy);
        if (lenSq <= double.Epsilon)
            return start;

        double wx = point.X - start.X;
        double wy = point.Y - start.Y;
        double t = ((wx * vx) + (wy * vy)) / lenSq;
        t = Math.Clamp(t, 0, 1);
        return new Point(start.X + t * vx, start.Y + t * vy);
    }

    private static double DistanceSq(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// DOT-GRID BACKGROUND
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Renders the infinite dot-grid background that pans and zooms with the canvas.
///
/// Performance: brushes and pens are static — allocated once for the process lifetime.
/// </summary>
public sealed class DotGridBackground : Control
{
    private const int MaxDotsPerFrame = 14000;

    public static readonly StyledProperty<double> ZoomProperty = AvaloniaProperty.Register<
        DotGridBackground,
        double
    >(nameof(Zoom), 1.0);

    public static readonly StyledProperty<Point> PanOffsetProperty = AvaloniaProperty.Register<
        DotGridBackground,
        Point
    >(nameof(PanOffset));

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public Point PanOffset
    {
        get => GetValue(PanOffsetProperty);
        set => SetValue(PanOffsetProperty, value);
    }

    static DotGridBackground()
    {
        AffectsRender<DotGridBackground>(ZoomProperty, PanOffsetProperty);
    }

    // ── Cached instance resources (no allocations per render) ─────────────────

    private readonly SolidColorBrush _bgBrush;
    private readonly SolidColorBrush _dotBrush;
    private readonly SolidColorBrush _majorBrush;
    private readonly Pen _majorPen;

    public DotGridBackground()
    {
        _bgBrush = ResolveBrush("CanvasBgBrush", UiColorConstants.C_090B14);
        _dotBrush = ResolveBrush("CanvasDotBrush", UiColorConstants.C_1D2740);
        _majorBrush = ResolveBrush("CanvasGridBrush", UiColorConstants.C_2A3554);
        _majorPen = new Pen(_majorBrush, 0.5);
    }

    private static SolidColorBrush ResolveBrush(string key, string fallbackHex)
    {
        if (Application.Current?.TryFindResource(key, out object? resource) == true)
        {
            if (resource is ISolidColorBrush solid)
                return new SolidColorBrush(solid.Color) { Opacity = solid.Opacity };

            if (resource is Color color)
                return new SolidColorBrush(color);
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    public override void Render(DrawingContext dc)
    {
        // Base background fill
        dc.FillRectangle(_bgBrush, new Rect(Bounds.Size));

        const double baseSpacing = 28;
        double spacing = baseSpacing * Zoom;
        if (spacing < 6)
            return; // skip dots when too small

        double dotRadius = Math.Max(0.8, 1.2 * Zoom);

        int dotStride = ComputeDotStride(Bounds.Width, Bounds.Height, spacing, MaxDotsPerFrame);
        double effectiveSpacing = spacing * dotStride;

        // Offset so dots pan with the canvas
        double offsetX = PanOffset.X % effectiveSpacing;
        double offsetY = PanOffset.Y % effectiveSpacing;

        for (double x = offsetX; x < Bounds.Width + effectiveSpacing; x += effectiveSpacing)
        for (double y = offsetY; y < Bounds.Height + effectiveSpacing; y += effectiveSpacing)
            dc.FillRectangle(
                _dotBrush,
                new Rect(x - dotRadius, y - dotRadius, dotRadius * 2, dotRadius * 2)
            );

        // Subtle major grid lines every 5 cells
        double majorSpacing = spacing * 5;

        double majorOffX = PanOffset.X % majorSpacing;
        double majorOffY = PanOffset.Y % majorSpacing;

        for (double x = majorOffX; x < Bounds.Width + majorSpacing; x += majorSpacing)
            dc.DrawLine(_majorPen, new Point(x, 0), new Point(x, Bounds.Height));
        for (double y = majorOffY; y < Bounds.Height + majorSpacing; y += majorSpacing)
            dc.DrawLine(_majorPen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private static int ComputeDotStride(double width, double height, double spacing, int maxDots)
    {
        if (spacing <= 0 || width <= 0 || height <= 0 || maxDots <= 0)
            return 1;

        double projected = ((width / spacing) + 1) * ((height / spacing) + 1);
        if (projected <= maxDots)
            return 1;

        return Math.Max(1, (int)Math.Ceiling(Math.Sqrt(projected / maxDots)));
    }
}
