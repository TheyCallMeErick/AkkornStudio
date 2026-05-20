namespace AkkornStudio.UI.ViewModels.ErDiagram;

public sealed class ErEditHistory
{
    private readonly Stack<ErCanvasMutation> _undo = [];
    private readonly Stack<ErCanvasMutation> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoDepth => _undo.Count;

    public int RedoDepth => _redo.Count;

    public void Execute(ErCanvasMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        mutation.Execute();
        _undo.Push(mutation);
        _redo.Clear();
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;

        ErCanvasMutation mutation = _undo.Pop();
        try
        {
            mutation.Undo();
            _redo.Push(mutation);
            return true;
        }
        catch
        {
            _undo.Push(mutation);
            throw;
        }
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;

        ErCanvasMutation mutation = _redo.Pop();
        try
        {
            mutation.Execute();
            _undo.Push(mutation);
            return true;
        }
        catch
        {
            _redo.Push(mutation);
            throw;
        }
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

public sealed record ErCanvasMutation(
    string Description,
    Action Execute,
    Action Undo);
