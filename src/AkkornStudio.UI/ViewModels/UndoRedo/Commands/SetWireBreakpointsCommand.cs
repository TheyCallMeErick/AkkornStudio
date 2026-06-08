namespace AkkornStudio.UI.ViewModels.UndoRedo.Commands;

public sealed class SetWireBreakpointsCommand(
    ConnectionViewModel wire,
    IReadOnlyList<WireBreakpoint> before,
    IReadOnlyList<WireBreakpoint> after,
    string description
) : ICanvasCommand
{
    private readonly ConnectionViewModel _wire = wire ?? throw new ArgumentNullException(nameof(wire));
    private readonly IReadOnlyList<WireBreakpoint> _before =
        before ?? throw new ArgumentNullException(nameof(before));
    private readonly IReadOnlyList<WireBreakpoint> _after =
        after ?? throw new ArgumentNullException(nameof(after));
    public string Description { get; } = description ?? throw new ArgumentNullException(nameof(description));

    public void Execute(CanvasViewModel canvas)
    {
        ApplyBreakpoints(canvas, _after);
    }

    public void Undo(CanvasViewModel canvas)
    {
        ApplyBreakpoints(canvas, _before);
    }

    private void ApplyBreakpoints(CanvasViewModel canvas, IReadOnlyList<WireBreakpoint> target)
    {
        if (!canvas.Connections.Contains(_wire))
            return;

        List<WireBreakpoint> previous = [.. _wire.Breakpoints];
        try
        {
            _wire.SetBreakpoints(target);
            canvas.IsDirty = true;
        }
        catch
        {
            try
            {
                _wire.SetBreakpoints(previous);
            }
            catch
            {
                // keep original exception as primary failure
            }

            throw;
        }
    }
}
