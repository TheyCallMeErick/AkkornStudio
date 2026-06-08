using AkkornStudio.UI.ViewModels.Validation.Conventions;

namespace AkkornStudio.UI.ViewModels.UndoRedo.Commands;

public sealed class AutoFixNamingCommand : ICanvasCommand
{
    private readonly List<(NodeViewModel Node, string? OldAlias, string NewAlias)> _renames = [];

    public string Description =>
        _renames.Count == 1
            ? $"Fix alias '{_renames[0].OldAlias}' → '{_renames[0].NewAlias}'"
            : $"Fix {_renames.Count} alias name(s)";

    public AutoFixNamingCommand(
        IEnumerable<NodeViewModel> nodes,
        NamingConventionPolicy? policy = null,
        IAliasConventionRegistry? registry = null)
    {
        List<NodeViewModel> nodeList = nodes.ToList();
        Dictionary<string, int> beforeAliasCounts = CountAliases(nodeList.Select(n => n.Alias));
        var effectiveAliases = new Dictionary<NodeViewModel, string?>();
        foreach (NodeViewModel node in nodeList)
            effectiveAliases[node] = node.Alias;

        policy ??= NamingConventionPolicy.Default;
        registry ??= AliasConventionRegistry.CreateDefault();

        foreach (NodeViewModel node in nodeList)
        {
            if (string.IsNullOrWhiteSpace(node.Alias))
                continue;
            IReadOnlyList<AliasViolation> violations =
                NamingConventionValidator.CheckAlias(node.Alias!, policy, registry);
            if (violations.Count == 0)
                continue;
            string fixed_ = NamingConventionValidator.NormalizeAlias(node.Alias!, policy, registry);
            if (fixed_ != node.Alias)
            {
                _renames.Add((node, node.Alias, fixed_));
                effectiveAliases[node] = fixed_;
            }
        }

        Dictionary<string, int> afterAliasCounts = CountAliases(effectiveAliases.Values);
        foreach (KeyValuePair<string, int> afterAlias in afterAliasCounts)
        {
            int beforeCount = beforeAliasCounts.TryGetValue(afterAlias.Key, out int count) ? count : 0;
            if (afterAlias.Value > 1 && afterAlias.Value > beforeCount)
                throw new InvalidOperationException(
                    $"Auto-fix alias collision for '{afterAlias.Key}': multiple nodes would share this alias.");
        }
    }

    public bool HasChanges => _renames.Count > 0;

    public void Execute(CanvasViewModel canvas)
    {
        foreach ((NodeViewModel node, string? _, string newAlias) in _renames)
            node.Alias = newAlias;
    }

    public void Undo(CanvasViewModel canvas)
    {
        foreach ((NodeViewModel node, string? oldAlias, string _) in _renames)
            node.Alias = oldAlias;
    }

    private static Dictionary<string, int> CountAliases(IEnumerable<string?> aliases)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string? alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            if (!counts.TryAdd(alias, 1))
                counts[alias]++;
        }

        return counts;
    }
}
