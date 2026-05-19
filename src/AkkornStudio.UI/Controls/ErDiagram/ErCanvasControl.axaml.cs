using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using AkkornStudio.UI.ViewModels.ErDiagram;
using System.ComponentModel;
using AkkornStudio.UI.Controls;

namespace AkkornStudio.UI.Controls.ErDiagram;

public sealed partial class ErCanvasControl : UserControl
{
    private readonly CanvasViewportInteractionHost _interactionHost =
        new(CanvasViewportGesturePolicy.ErCanvasDefault);
    private readonly CanvasViewportSelectionAdornerController _selectionAdornerController = new();
    private readonly CanvasViewportSelectionNavigationController _selectionNavigationController = new();
    private readonly CanvasViewportGesturePolicy _gesturePolicy = CanvasViewportGesturePolicy.ErCanvasDefault;
    private ErCanvasViewModel? _observedCanvas;
    private bool _isSpacePanArmed;

    public bool IsSpacePanArmed => _isSpacePanArmed;

    public ErCanvasControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LayoutUpdated += (_, _) => UpdateViewportState();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        LostFocus += OnLostFocus;
        OnDataContextChanged(this, EventArgs.Empty);
    }

    private void Edge_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ErCanvasViewModel canvas)
            return;

        PointerPointProperties pointerProperties = e.GetCurrentPoint(this).Properties;
        bool isPanGesture = CanvasViewportGestureDecisions.IsPanGesture(
            _gesturePolicy,
            pointerProperties,
            e.KeyModifiers,
            _isSpacePanArmed);
        if (isPanGesture)
            return;

        if (sender is not Control control || control.DataContext is not ErRelationEdgeViewModel edge)
            return;

        canvas.SelectedEdge = edge;
        e.Handled = true;
    }

    private void Edge_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ErCanvasViewModel canvas)
            return;

        if (sender is not Control control || control.DataContext is not ErRelationEdgeViewModel edge)
            return;

        canvas.SetHoveredEdge(edge);
    }

    private void Edge_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ErCanvasViewModel canvas)
            return;

        canvas.SetHoveredEdge(null);
    }

    private void CanvasBackground_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Canvas)
            return;

        PointerPointProperties pointerProperties = e.GetCurrentPoint(this).Properties;
        bool isPanGesture = CanvasViewportGestureDecisions.IsPanGesture(
            _gesturePolicy,
            pointerProperties,
            e.KeyModifiers,
            _isSpacePanArmed);
        if (isPanGesture)
            return;

        // Deixa o evento subir para o ViewportSurface decidir entre pan/marquee.
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedCanvas is not null)
            _observedCanvas.PropertyChanged -= OnCanvasPropertyChanged;

        _observedCanvas = DataContext as ErCanvasViewModel;

        Canvas? sceneContentCanvas = this.FindControl<Canvas>("SceneContentCanvas");
        if (sceneContentCanvas is not null)
            sceneContentCanvas.DataContext = _observedCanvas;

        Canvas? overlayCanvas = this.FindControl<CanvasMarqueeAdorner>("SelectionMarquee")?.Parent as Canvas;
        if (overlayCanvas is not null)
            overlayCanvas.DataContext = _observedCanvas;

        ItemsControl? edgeItems = this.FindControl<ItemsControl>("EdgeItems");
        if (edgeItems is not null)
            edgeItems.ItemsSource = _observedCanvas?.Edges;

        ItemsControl? entityItems = this.FindControl<ItemsControl>("EntityItems");
        if (entityItems is not null)
            entityItems.ItemsSource = _observedCanvas?.Entities;

        if (_observedCanvas is not null)
            _observedCanvas.PropertyChanged += OnCanvasPropertyChanged;

        SyncTransform();
    }

    private void OnCanvasPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ErCanvasViewModel.FocusRequestVersion))
        {
            Dispatcher.UIThread.Post(CenterViewportOnFocusTarget, DispatcherPriority.Background);
            return;
        }

        if (e.PropertyName is nameof(ErCanvasViewModel.Zoom)
            or nameof(ErCanvasViewModel.ViewportX)
            or nameof(ErCanvasViewModel.ViewportY)
            or nameof(ErCanvasViewModel.SelectedEntity)
            or nameof(ErCanvasViewModel.SelectedEdge))
        {
            SyncTransform();
        }
    }

    private void CenterViewportOnFocusTarget()
    {
        if (_observedCanvas is null)
            return;

        UpdateViewportState();
        if (!_selectionNavigationController.TryCenterSelection(_observedCanvas, Bounds.Size))
            _observedCanvas.CenterViewportOnCanvasPoint(_observedCanvas.FocusTargetX, _observedCanvas.FocusTargetY);
        SyncTransform();
    }

    private void CenterSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (_observedCanvas is null)
            return;

        UpdateViewportState();
        if (_selectionNavigationController.TryCenterSelection(_observedCanvas, Bounds.Size))
            SyncTransform();
    }

    private void FitSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (_observedCanvas is null)
            return;

        UpdateViewportState();
        if (_selectionNavigationController.TryFitSelection(
            _observedCanvas,
            Bounds.Size,
            padding: 40,
            minZoom: 0.15,
            maxZoom: 2.0))
        {
            SyncTransform();
        }
    }

    private void Viewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_observedCanvas is null || sender is not InfiniteCanvasCoreControl viewportHost)
            return;

        _interactionHost.HandlePointerWheel(_observedCanvas, viewportHost.ViewportSurface, e);
        viewportHost.SyncViewport();
    }

    private void Viewport_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_observedCanvas is null || sender is not InfiniteCanvasCoreControl viewportHost)
            return;

        viewportHost.Focus();

        PointerPointProperties props = e.GetCurrentPoint(viewportHost.ViewportSurface).Properties;
        bool isPanGesture = CanvasViewportGestureDecisions.IsPanGesture(_gesturePolicy, props, e.KeyModifiers, _isSpacePanArmed);
        if (!isPanGesture && !props.IsLeftButtonPressed)
            return;

        if (_interactionHost.HandlePointerPressed(_observedCanvas, viewportHost.ViewportSurface, e))
            UpdateMarqueeVisual();
    }

    private void Viewport_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_observedCanvas is null || sender is not InfiniteCanvasCoreControl viewportHost)
            return;

        if (!_interactionHost.HandlePointerMoved(_observedCanvas, viewportHost.ViewportSurface, e, out bool marqueeChanged))
            return;

        if (marqueeChanged)
        {
            UpdateMarqueeVisual();
            return;
        }

        viewportHost.SyncViewport();
    }

    private void Viewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        CanvasViewportPointerReleaseKind releaseKind = _interactionHost.HandlePointerReleased(e);
        if (releaseKind == CanvasViewportPointerReleaseKind.PanEnded)
            return;

        if (releaseKind != CanvasViewportPointerReleaseKind.MarqueeCompleted
            || _observedCanvas is null
            || sender is not InfiniteCanvasCoreControl viewportHost)
            return;

        CanvasMarqueeAdorner? marquee = this.FindControl<CanvasMarqueeAdorner>("SelectionMarquee");
        _selectionAdornerController.CompleteMarqueeSelection(_observedCanvas, _interactionHost, marquee);
        viewportHost.SyncViewport();
    }

    private void UpdateViewportState()
    {
        if (_observedCanvas is null)
            return;

        InfiniteCanvasCoreControl? viewportHost = this.FindControl<InfiniteCanvasCoreControl>("ViewportSurface");
        if (viewportHost is null)
            return;

        _observedCanvas.SetViewportSize(viewportHost.Bounds.Width, viewportHost.Bounds.Height);
        viewportHost.SyncViewport();
    }

    private void SyncTransform()
    {
        this.FindControl<InfiniteCanvasCoreControl>("ViewportSurface")?.SyncViewport();
        UpdateFocusAdorner();
        UpdateMarqueeVisual();
    }

    public void RefreshViewportVisuals()
    {
        SyncTransform();
    }

    private void UpdateFocusAdorner()
    {
        if (_observedCanvas is null)
            return;

        CanvasFocusAdorner? adorner = this.FindControl<CanvasFocusAdorner>("FocusOverlay");
        _selectionAdornerController.SyncFocusAdorner(_observedCanvas, adorner, padding: 12);
    }

    private void UpdateMarqueeVisual()
    {
        CanvasMarqueeAdorner? marquee = this.FindControl<CanvasMarqueeAdorner>("SelectionMarquee");
        if (_observedCanvas is null)
        {
            if (marquee is not null)
                marquee.SelectionRect = default;
            return;
        }

        _selectionAdornerController.SyncMarqueeAdorner(_observedCanvas, _interactionHost, marquee);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !_isSpacePanArmed)
        {
            _isSpacePanArmed = true;
            _interactionHost.SetSpacePanArmed(true);
        }

        if (_observedCanvas is null)
            return;

        if ((e.Key == Key.Delete || e.Key == Key.Back) && e.KeyModifiers == KeyModifiers.None)
        {
            if (_observedCanvas.DeleteSelection())
            {
                e.Handled = true;
                SyncTransform();
            }
            return;
        }

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (_observedCanvas.Undo())
            {
                e.Handled = true;
                SyncTransform();
            }
            return;
        }

        if ((e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            if (_observedCanvas.Redo())
            {
                e.Handled = true;
                SyncTransform();
            }
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space)
            return;

        _isSpacePanArmed = false;
        _interactionHost.SetSpacePanArmed(false);
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        _isSpacePanArmed = false;
        _interactionHost.SetSpacePanArmed(false);
    }
}
