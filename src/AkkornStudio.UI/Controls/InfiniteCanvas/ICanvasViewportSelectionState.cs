using Avalonia;

namespace AkkornStudio.UI.Controls;

public interface ICanvasViewportSelectionState : ICanvasViewportState
{
    void ClearSelection();

    bool TrySelectInRegion(Rect region);

    bool TryGetSelectionFrame(double padding, out Rect frame);
}
