namespace AkkornStudio.UI.ViewModels.UndoRedo.Commands;

public sealed class AddConnectionCommand(
    ConnectionViewModel connection,
    ConnectionViewModel? displaced = null
) : ICanvasCommand
{
    private readonly ConnectionViewModel _connection = connection;
    private readonly ConnectionViewModel? _displaced = displaced;
    public string Description => $"Connect {_connection.FromPin.Name} → {_connection.ToPin?.Name}";

    public void Execute(CanvasViewModel canvas)
    {
        bool displacedRemoved = false;
        bool createdAdded = false;

        try
        {
            if (_displaced is not null && canvas.Connections.Contains(_displaced))
            {
                canvas.Connections.Remove(_displaced);
                displacedRemoved = !canvas.Connections.Contains(_displaced);
            }

            if (!canvas.Connections.Contains(_connection))
            {
                canvas.Connections.Add(_connection);
                createdAdded = canvas.Connections.Contains(_connection);
            }

            _connection.FromPin.IsConnected = true;
            if (_connection.ToPin is not null)
                _connection.ToPin.IsConnected = true;
        }
        catch
        {
            if (canvas.Connections.Contains(_connection))
            {
                try
                {
                    canvas.Connections.Remove(_connection);
                }
                catch
                {
                    // keep original exception as primary failure
                }
            }

            if (displacedRemoved && _displaced is not null && !canvas.Connections.Contains(_displaced))
            {
                try
                {
                    canvas.Connections.Add(_displaced);
                }
                catch
                {
                    // keep original exception as primary failure
                }
            }

            _connection.FromPin.IsConnected = canvas.Connections.Any(c => c.FromPin == _connection.FromPin);
            if (_connection.ToPin is not null)
                _connection.ToPin.IsConnected = canvas.Connections.Any(c => c.ToPin == _connection.ToPin);

            if (_displaced is not null)
            {
                _displaced.FromPin.IsConnected = canvas.Connections.Any(c => c.FromPin == _displaced.FromPin);
                if (_displaced.ToPin is not null)
                    _displaced.ToPin.IsConnected = canvas.Connections.Any(c => c.ToPin == _displaced.ToPin);
            }

            throw;
        }
    }

    public void Undo(CanvasViewModel canvas)
    {
        canvas.Connections.Remove(_connection);

        if (_displaced is not null)
            canvas.Connections.Add(_displaced);

        _connection.FromPin.IsConnected = canvas.Connections.Any(c =>
            c.FromPin == _connection.FromPin
        );

        if (_connection.ToPin is not null)
        {
            _connection.ToPin.IsConnected = canvas.Connections.Any(c =>
                c.ToPin == _connection.ToPin
            );
        }

        if (_displaced is not null)
        {
            _displaced.FromPin.IsConnected = canvas.Connections.Any(c =>
                c.FromPin == _displaced.FromPin
            );
            if (_displaced.ToPin is not null)
            {
                _displaced.ToPin.IsConnected = canvas.Connections.Any(c =>
                    c.ToPin == _displaced.ToPin
                );
            }
        }
    }
}
