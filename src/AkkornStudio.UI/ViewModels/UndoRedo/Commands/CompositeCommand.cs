namespace AkkornStudio.UI.ViewModels.UndoRedo.Commands;

/// <summary>
/// Groups multiple <see cref="ICanvasCommand"/>s into a single undo / redo entry.
///
/// Used by the transaction mechanism (<see cref="UndoRedoStack.BeginTransaction"/> /
/// <see cref="UndoRedoStack.CommitTransaction"/>) and by multi-node drag to record
/// all node movements as one atomic history entry.
/// </summary>
public sealed class CompositeCommand : ICanvasCommand
{
    private readonly IReadOnlyList<ICanvasCommand> _commands;

    public string Description { get; }

    public CompositeCommand(string description, IEnumerable<ICanvasCommand> commands)
    {
        Description = description;
        _commands = [.. commands];
    }

    public void Execute(CanvasViewModel canvas)
    {
        List<ICanvasCommand> executed = [];
        try
        {
            foreach (ICanvasCommand cmd in _commands)
            {
                cmd.Execute(canvas);
                executed.Add(cmd);
            }
        }
        catch
        {
            // Best-effort compensation to preserve atomicity.
            foreach (ICanvasCommand cmd in executed.AsEnumerable().Reverse())
            {
                try
                {
                    cmd.Undo(canvas);
                }
                catch
                {
                    // Keep original exception as primary failure.
                }
            }

            throw;
        }
    }

    public void Undo(CanvasViewModel canvas)
    {
        List<ICanvasCommand> undone = [];
        try
        {
            foreach (ICanvasCommand cmd in _commands.AsEnumerable().Reverse())
            {
                cmd.Undo(canvas);
                undone.Add(cmd);
            }
        }
        catch
        {
            // Best-effort compensation to preserve atomicity.
            // Re-apply commands already undone in original forward order.
            foreach (ICanvasCommand cmd in undone.AsEnumerable().Reverse())
            {
                try
                {
                    cmd.Execute(canvas);
                }
                catch
                {
                    // Keep original exception as primary failure.
                }
            }

            throw;
        }
    }
}
