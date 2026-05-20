namespace AkkornStudio.UI.ViewModels.UndoRedo.Commands;

public sealed class DeleteNodeCommand(NodeViewModel node) : ICanvasCommand
{
    private readonly NodeViewModel _node = node;
    private readonly List<ConnectionViewModel> _removedConnections = [];
    public string Description => $"Delete {_node.Title}";

    public void Execute(CanvasViewModel canvas)
    {
        _removedConnections.Clear();
        List<ConnectionViewModel> wires = [];
        foreach (ConnectionViewModel connection in canvas.Connections)
        {
            if (connection.FromPin.Owner == _node || connection.ToPin?.Owner == _node)
                wires.Add(connection);
        }
        bool nodeWasPresent = canvas.Nodes.Contains(_node);
        try
        {
            foreach (ConnectionViewModel? w in wires)
            {
                try
                {
                    canvas.Connections.Remove(w);
                }
                finally
                {
                    if (!canvas.Connections.Contains(w))
                        _removedConnections.Add(w);
                }
            }

            if (nodeWasPresent)
                canvas.Nodes.Remove(_node);
        }
        catch
        {
            if (nodeWasPresent && !canvas.Nodes.Contains(_node))
                canvas.Nodes.Add(_node);

            foreach (ConnectionViewModel connection in _removedConnections)
            {
                if (!canvas.Connections.Contains(connection))
                    canvas.Connections.Add(connection);
            }

            throw;
        }
    }

    public void Undo(CanvasViewModel canvas)
    {
        canvas.Nodes.Add(_node);
        foreach (ConnectionViewModel w in _removedConnections)
            canvas.Connections.Add(w);
    }
}
