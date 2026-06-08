namespace AkkornStudio.Nodes.LogicalPlan;

public sealed class AliasGenerator
{
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public string GenerateFor(string suggestion)
    {
        string seed = string.IsNullOrWhiteSpace(suggestion)
            ? "ds"
            : suggestion.Trim();

        if (_used.Add(seed))
            return seed;

        int index = 1;
        while (true)
        {
            string candidate = $"{seed}_{index}";
            if (_used.Add(candidate))
                return candidate;

            index++;
        }
    }

    public void Reset()
    {
        _used.Clear();
    }
}
