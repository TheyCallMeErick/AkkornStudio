namespace AkkornStudio.Metadata;

public interface ICanvasTableTracker
{
    void Add(string fullTableName);
    bool Remove(string fullTableName);
    bool Contains(string fullTableName);
    IReadOnlyList<string> Snapshot();
    int Count { get; }
}

public sealed class CanvasTableTracker : ICanvasTableTracker
{
    private readonly HashSet<string> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public void Add(string fullTableName)
    {
        string normalized = Normalize(fullTableName);
        lock (_gate)
            _tables.Add(normalized);
    }

    public bool Remove(string fullTableName)
    {
        string normalized = Normalize(fullTableName);
        lock (_gate)
        {
            if (_tables.Remove(normalized))
                return true;

            if (normalized.Contains('.', StringComparison.Ordinal))
                return false;

            string suffix = "." + normalized;
            string[] matches = _tables
                .Where(table => table.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length != 1)
                return false;

            return _tables.Remove(matches[0]);
        }
    }

    public bool Contains(string fullTableName)
    {
        string normalized = Normalize(fullTableName);
        lock (_gate)
        {
            if (_tables.Contains(normalized))
                return true;

            if (normalized.Contains('.', StringComparison.Ordinal))
                return false;

            string suffix = "." + normalized;
            return _tables.Any(table => table.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
            return [.. _tables];
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _tables.Count;
        }
    }

    private static string Normalize(string fullTableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullTableName);
        return fullTableName.Trim();
    }
}
