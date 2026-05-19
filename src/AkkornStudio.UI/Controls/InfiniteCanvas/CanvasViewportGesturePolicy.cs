using Avalonia.Input;

namespace AkkornStudio.UI.Controls;

public sealed record CanvasViewportGesturePolicy(
    bool PanWithMiddleButton = true,
    bool PanWithAltLeftButton = true,
    bool PanWithSpaceLeftButton = false,
    bool PanWithRightButton = false,
    bool PanWithPrimaryLeftButton = false)
{
    public static CanvasViewportGesturePolicy InfiniteCanvasDefault { get; } = new(
        PanWithMiddleButton: true,
        PanWithAltLeftButton: true,
        PanWithSpaceLeftButton: true,
        PanWithRightButton: false,
        PanWithPrimaryLeftButton: false);

    public static CanvasViewportGesturePolicy ErCanvasDefault { get; } = new(
        PanWithMiddleButton: true,
        PanWithAltLeftButton: true,
        PanWithSpaceLeftButton: true,
        PanWithRightButton: false,
        PanWithPrimaryLeftButton: false);
}

public static class CanvasViewportGestureDecisions
{
    public static bool IsPanGesture(
        CanvasViewportGesturePolicy policy,
        PointerPointProperties pointerProperties,
        KeyModifiers keyModifiers,
        bool isSpacePanArmed = false)
    {
        if (policy.PanWithMiddleButton && pointerProperties.IsMiddleButtonPressed)
            return true;

        if (policy.PanWithRightButton && pointerProperties.IsRightButtonPressed)
            return true;

        if (policy.PanWithAltLeftButton
            && pointerProperties.IsLeftButtonPressed
            && keyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return true;
        }

        if (policy.PanWithSpaceLeftButton
            && pointerProperties.IsLeftButtonPressed
            && isSpacePanArmed)
        {
            return true;
        }

        if (policy.PanWithPrimaryLeftButton
            && pointerProperties.IsLeftButtonPressed
            && !keyModifiers.HasFlag(KeyModifiers.Alt)
            && !keyModifiers.HasFlag(KeyModifiers.Control)
            && !keyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return true;
        }

        return false;
    }
}
