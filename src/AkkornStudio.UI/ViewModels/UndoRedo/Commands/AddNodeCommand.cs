using Avalonia;

namespace AkkornStudio.UI.ViewModels.UndoRedo.Commands;

public sealed class AddNodeCommand(NodeViewModel node) : ICanvasCommand
{
    private readonly NodeViewModel _node = node;
    public string Description => $"Add {_node.Title}";

    public void Execute(CanvasViewModel canvas)
    {
        foreach (NodeViewModel existing in canvas.Nodes)
        {
            if (ReferenceEquals(existing, _node))
                continue;
            if (existing.Id == _node.Id)
                throw new InvalidOperationException($"Node id '{_node.Id}' already exists in canvas.");
        }

        if (!canvas.Nodes.Contains(_node))
            canvas.Nodes.Add(_node);
    }

    public void Undo(CanvasViewModel canvas)
    {
        // Also remove any wires connected to this node
        List<ConnectionViewModel> wires = [];
        foreach (ConnectionViewModel connection in canvas.Connections)
        {
            bool fromThisNode = connection.FromPin.Owner == _node;
            bool toThisNode = connection.ToPin?.Owner == _node;
            if (fromThisNode || toThisNode)
                wires.Add(connection);
        }

        foreach (ConnectionViewModel? w in wires)
            canvas.Connections.Remove(w);
        canvas.Nodes.Remove(_node);
    }
}
