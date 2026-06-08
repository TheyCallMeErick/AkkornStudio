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

            string[] matches = ResolveEquivalentMatches(normalized);
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

            return ResolveEquivalentMatches(normalized).Length > 0;
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

    private string[] ResolveEquivalentMatches(string normalized)
    {
        if (_tables.Count == 0)
            return [];

        bool isQualified = normalized.Contains('.', StringComparison.Ordinal);
        string tableOnly = ExtractTableName(normalized);

        if (!isQualified)
        {
            string suffix = "." + normalized;
            return _tables
                .Where(table =>
                    table.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                    || table.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return _tables
            .Where(table =>
                table.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || (!table.Contains('.', StringComparison.Ordinal)
                    && table.Equals(tableOnly, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ExtractTableName(string fullTableName)
    {
        int lastDot = fullTableName.LastIndexOf('.');
        return lastDot < 0 ? fullTableName : fullTableName[(lastDot + 1)..];
    }
}
