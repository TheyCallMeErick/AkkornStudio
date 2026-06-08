using Avalonia;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Controls;

/// <summary>
/// Viewport and layer-order responsibilities extracted from InfiniteCanvas.
/// Keeps input/render pipeline lean while reusing shared math helpers.
/// </summary>
public sealed partial class InfiniteCanvas
{
    private Point ScreenToCanvas(Point s) =>
        ViewModel is null ? default : _viewportController.ScreenToCanvas(ViewModel, s);

    private bool CenterSelectionInView()
    {
        if (ViewModel is not ICanvasViewportSelectionState selectionState)
            return false;

        _isApplyingViewportFromCanvas = true;
        try
        {
            if (!_selectionNavigationController.TryCenterSelection(selectionState, Bounds.Size))
                return false;
        }
        finally
        {
            _isApplyingViewportFromCanvas = false;
        }

        SyncTransform();
        SyncWires();
        return true;
    }

    private bool FitSelectionInView()
    {
        if (ViewModel is not ICanvasViewportSelectionState selectionState)
            return false;

        _isApplyingViewportFromCanvas = true;
        try
        {
            if (!_selectionNavigationController.TryFitSelection(
                selectionState,
                Bounds.Size,
                padding: 40,
                minZoom: 0.15,
                maxZoom: 4.0))
            {
                return false;
            }
        }
        finally
        {
            _isApplyingViewportFromCanvas = false;
        }

        SyncTransform();
        SyncWires();
        return true;
    }

    private bool BringSelectionToFront()
    {
        if (ViewModel is null)
            return false;
        bool changed = ViewModel.BringSelectionToFront();
        if (changed)
        {
            SyncWires();
            InvalidateArrange();
        }
        return changed;
    }

    private bool SendSelectionToBack()
    {
        if (ViewModel is null)
            return false;
        bool changed = ViewModel.SendSelectionToBack();
        if (changed)
        {
            SyncWires();
            InvalidateArrange();
        }
        return changed;
    }

    private bool BringSelectionForward()
    {
        if (ViewModel is null)
            return false;
        bool changed = ViewModel.BringSelectionForward();
        if (changed)
        {
            SyncWires();
            InvalidateArrange();
        }
        return changed;
    }

    private bool SendSelectionBackward()
    {
        if (ViewModel is null)
            return false;
        bool changed = ViewModel.SendSelectionBackward();
        if (changed)
        {
            SyncWires();
            InvalidateArrange();
        }
        return changed;
    }

    private bool NormalizeLayers()
    {
        if (ViewModel is null)
            return false;
        bool changed = ViewModel.NormalizeLayers();
        if (!changed)
            return false;
        NormalizeNodeZOrder();
        SyncWires();
        InvalidateArrange();
        return true;
    }

    private void NormalizeNodeZOrder()
    {
        if (ViewModel is null)
            return;

        int z = 0;
        foreach (NodeViewModel n in ViewModel.Nodes.OrderBy(n => n.ZOrder))
            n.ZOrder = z++;
    }
}
