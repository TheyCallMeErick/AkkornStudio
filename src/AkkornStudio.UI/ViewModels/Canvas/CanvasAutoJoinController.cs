using System.Collections.ObjectModel;
using Avalonia;
using AkkornStudio.CanvasKit;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Canvas.AutoJoin;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// Encapsulates all auto-join detection, suggestion, and application logic,
/// removing it from CanvasViewModel (SRP).
/// </summary>
internal sealed class CanvasAutoJoinController : IDisposable
{
    private readonly ObservableCollection<NodeViewModel> _nodes;
    private readonly ObservableCollection<ConnectionViewModel> _connections;
    private readonly AutoJoinOverlayViewModel _autoJoin;
    private readonly ManualJoinDialogViewModel _manualJoinDialog;
    private readonly ICanvasAutoJoinApplicationService _applicationService;
    private readonly ICanvasAutoJoinSuggestionService _suggestionService;
    private readonly ICanvasAutoJoinNotifier _notifier;
    private readonly Func<NodeDefinition, Point, NodeViewModel> _spawnNode;
    private readonly Action<PinViewModel, PinViewModel> _connectPins;
    private readonly Action _notifyRunSelectedAutoJoinCanExecute;
    private readonly Dictionary<string, SuggestionNodeAnchor> _suggestionAnchorsByKey = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<JoinSuggestion> _lastSuggestions = [];

    public CanvasAutoJoinController(
        ObservableCollection<NodeViewModel> nodes,
        ObservableCollection<ConnectionViewModel> connections,
        AutoJoinOverlayViewModel autoJoin,
        ManualJoinDialogViewModel manualJoinDialog,
        ToastCenterViewModel toasts,
        ILocalizationService localizationService,
        ICanvasAutoJoinApplicationService? applicationService,
        ICanvasAutoJoinSuggestionService? suggestionService,
        ICanvasAutoJoinNotifier? notifier,
        Func<NodeDefinition, Point, NodeViewModel> spawnNode,
        Action<PinViewModel, PinViewModel> connectPins,
        Action notifyRunSelectedAutoJoinCanExecute)
    {
        _nodes = nodes;
        _connections = connections;
        _autoJoin = autoJoin;
        _manualJoinDialog = manualJoinDialog;
        _applicationService = applicationService ?? new CanvasAutoJoinApplicationService();
        _suggestionService = suggestionService ?? new CanvasAutoJoinSuggestionService();
        _notifier = notifier ?? new CanvasAutoJoinNotifier(
            toasts,
            new CanvasAutoJoinMessagePresenter(localizationService));
        _spawnNode = spawnNode;
        _connectPins = connectPins;
        _notifyRunSelectedAutoJoinCanExecute = notifyRunSelectedAutoJoinCanExecute;

        _autoJoin.JoinAccepted += OnJoinAccepted;
        _manualJoinDialog.Confirmed += OnManualJoinConfirmed;
    }

    public bool HasTwoSelectedTableSources => GetSelectedTableSources().Count == 2;
    public bool CanRunAutoJoin => HasTwoSelectedTableSources || _lastSuggestions.Count > 0;

    /// <summary>
    /// Analyses join opportunities involving <paramref name="newTableFullName"/>
    /// against all other table-source nodes currently on the canvas.
    /// </summary>
    public void TriggerAutoJoinAnalysis(string newTableFullName)
    {
        try
        {
            IReadOnlyList<JoinSuggestion> suggestions = _suggestionService.AnalyzeNewTable(newTableFullName, _nodes);
            if (suggestions.Count > 0)
                ShowAutoJoinSuggestionsMessage(suggestions);
        }
        catch
        {
            _notifier.ShowSuggestionsUnavailable();
        }
    }

    /// <summary>
    /// Analyses ALL table-source pairs currently on the canvas and shows
    /// the join suggestion overlay if any high-confidence suggestions are found.
    /// </summary>
    public void AnalyzeAllCanvasJoins()
    {
        try
        {
            IReadOnlyList<JoinSuggestion> allSuggestions = _suggestionService.AnalyzeAllTables(_nodes);
            if (allSuggestions.Count > 0)
                ShowAutoJoinSuggestionsMessage(allSuggestions);
        }
        catch
        {
            _notifier.ShowSuggestionsUnavailable();
        }
    }

    public void RunSelectedAutoJoin()
    {
        List<NodeViewModel> selected = GetSelectedTableSources();
        if (selected.Count != 2)
        {
            TryReopenLastSuggestions();
            return;
        }

        NodeViewModel left = selected[0];
        NodeViewModel right = selected[1];

        IReadOnlyList<JoinSuggestion> suggestions;
        try
        {
            suggestions = _suggestionService.AnalyzePair(left, right, _nodes);
        }
        catch
        {
            _manualJoinDialog.Open(left, right);
            _notifier.ShowSuggestionsUnavailable();
            return;
        }
        if (suggestions.Count == 0)
        {
            _manualJoinDialog.Open(left, right);
            _notifier.ShowNoSimilarityFound();
            return;
        }

        if (suggestions.Count > 1)
        {
            _manualJoinDialog.Open(left, right);
            PrefillManualDialogFromSuggestion(left, right, suggestions[0]);
            _notifier.ShowMultipleCandidatesFound(suggestions.Count);
            return;
        }

        if (!TryApplySuggestion(suggestions[0]))
        {
            _manualJoinDialog.Open(left, right);
            _notifier.ShowNoSimilarityFound();
            return;
        }

        _notifier.ShowAutoJoinApplied(suggestions[0].OnClause);
    }

    internal void ApplyJoinSuggestion(JoinSuggestion suggestion) =>
        OnJoinAccepted(this, suggestion);

    public void Dispose()
    {
        _autoJoin.JoinAccepted -= OnJoinAccepted;
        _manualJoinDialog.Confirmed -= OnManualJoinConfirmed;
    }

    private bool TryApplySuggestion(JoinSuggestion suggestion)
    {
        if (!IsSuggestionAnchorCurrent(suggestion))
            return false;

        return _applicationService.TryApplySuggestion(
            suggestion,
            _nodes,
            _connections,
            _spawnNode,
            _connectPins);
    }

    private List<NodeViewModel> GetSelectedTableSources() =>
        _nodes.Where(n => n.IsSelected && n.IsTableSource).ToList();

    private void PrefillManualDialogFromSuggestion(
        NodeViewModel leftTable,
        NodeViewModel rightTable,
        JoinSuggestion suggestion)
    {
        if (CanvasAutoJoinSemantics.TryParseQualifiedColumn(suggestion.LeftColumn, out string? leftSource, out string leftColumn)
            && CanvasAutoJoinSemantics.TryParseQualifiedColumn(suggestion.RightColumn, out string? rightSource, out string rightColumn))
        {
            NodeViewModel resolvedLeft = ResolveSourceNode(leftSource, leftTable, rightTable, leftColumn);
            NodeViewModel resolvedRight = ResolveSourceNode(rightSource, rightTable, leftTable, rightColumn);

            if (resolvedLeft == leftTable && resolvedRight == rightTable)
            {
                _manualJoinDialog.SelectedLeftColumn = _manualJoinDialog.LeftColumns
                    .FirstOrDefault(c => c.Name.Equals(leftColumn, StringComparison.OrdinalIgnoreCase))
                    ?? _manualJoinDialog.SelectedLeftColumn;
                _manualJoinDialog.SelectedRightColumn = _manualJoinDialog.RightColumns
                    .FirstOrDefault(c => c.Name.Equals(rightColumn, StringComparison.OrdinalIgnoreCase))
                    ?? _manualJoinDialog.SelectedRightColumn;
                _manualJoinDialog.SelectedJoinType = string.IsNullOrWhiteSpace(suggestion.JoinType)
                    ? "INNER"
                    : suggestion.JoinType.Trim().ToUpperInvariant();
            }
        }
    }

    private void ShowAutoJoinSuggestionsMessage(IReadOnlyList<JoinSuggestion> suggestions)
    {
        CacheSuggestions(suggestions);
        CacheSuggestionAnchors(suggestions);
        _notifier.ShowSuggestionsFound(
            suggestions.Count,
            onDetails: () => OpenManualDialogFromSuggestions(suggestions));
    }

    private void CacheSuggestions(IReadOnlyList<JoinSuggestion> suggestions)
    {
        _lastSuggestions = suggestions
            .Where(static s => s is not null)
            .ToArray();
        _notifyRunSelectedAutoJoinCanExecute();
    }

    private bool TryReopenLastSuggestions()
    {
        if (_lastSuggestions.Count == 0)
            return false;

        List<JoinSuggestion> currentSuggestions = _lastSuggestions
            .Where(IsSuggestionAnchorCurrent)
            .ToList();

        if (currentSuggestions.Count == 0)
        {
            _lastSuggestions = [];
            _notifyRunSelectedAutoJoinCanExecute();
            _notifier.ShowSuggestionsUnavailable();
            return false;
        }

        OpenManualDialogFromSuggestions(currentSuggestions);
        return true;
    }

    private void OpenManualDialogFromSuggestions(IReadOnlyList<JoinSuggestion> suggestions)
    {
        JoinSuggestion? best = suggestions.OrderByDescending(s => s.Score).FirstOrDefault();
        if (best is null)
            return;

        if (!IsSuggestionAnchorCurrent(best))
        {
            _notifier.ShowSuggestionsUnavailable();
            return;
        }

        NodeViewModel? left = FindTableSourceNode(best.ExistingTable);
        NodeViewModel? right = FindTableSourceNode(best.NewTable);
        if (left is null || right is null)
        {
            _notifier.ShowSuggestionsUnavailable();
            return;
        }

        _manualJoinDialog.Open(left, right);
        PrefillManualDialogFromSuggestion(left, right, best);
    }

    private static string GetTableIdentifier(NodeViewModel node)
    {
        if (!string.IsNullOrWhiteSpace(node.Subtitle))
            return node.Subtitle;

        return node.Title ?? string.Empty;
    }

    private void OnJoinAccepted(object? _, JoinSuggestion suggestion)
    {
        if (!TryApplySuggestion(suggestion))
            _notifier.ShowSuggestionsUnavailable();
    }

    private void OnManualJoinConfirmed(object? _, ManualJoinRequest request)
    {
        if (!_applicationService.TryCreateManualJoin(
                request.LeftTable,
                request.LeftColumn,
                request.RightTable,
                request.RightColumn,
                request.JoinType,
                _nodes,
                _connections,
                _spawnNode,
                _connectPins))
        {
            _notifier.ShowManualJoinFailed();
            return;
        }

        string leftRef = $"{GetTableIdentifier(request.LeftTable)}.{request.LeftColumn}";
        string rightRef = $"{GetTableIdentifier(request.RightTable)}.{request.RightColumn}";
        _notifier.ShowManualJoinCreated(leftRef, rightRef);
    }

    private NodeViewModel? FindTableSourceNode(string tableRef)
    {
        string full = tableRef.Trim();
        string shortName = full.Split('.').Last();

        return _nodes.FirstOrDefault(n =>
            n.IsTableSource
            && (
                n.Subtitle.Equals(full, StringComparison.OrdinalIgnoreCase)
                || n.Title.Equals(shortName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(n.Alias)
                    && n.Alias.Equals(shortName, StringComparison.OrdinalIgnoreCase))
            ));
    }

    private static NodeViewModel ResolveSourceNode(
        string? sourceRef,
        NodeViewModel preferred,
        NodeViewModel fallback,
        string expectedColumn)
    {
        if (!string.IsNullOrWhiteSpace(sourceRef) && MatchesSource(preferred, sourceRef))
            return preferred;

        if (!string.IsNullOrWhiteSpace(sourceRef) && MatchesSource(fallback, sourceRef))
            return fallback;

        if (preferred.OutputPins.Any(p => p.Name.Equals(expectedColumn, StringComparison.OrdinalIgnoreCase)))
            return preferred;

        return fallback;
    }

    private static bool MatchesSource(NodeViewModel node, string sourceRef) =>
        CanvasAutoJoinSemantics.MatchesSource(node.Subtitle, node.Title, node.Alias, sourceRef);

    private void CacheSuggestionAnchors(IReadOnlyList<JoinSuggestion> suggestions)
    {
        _suggestionAnchorsByKey.Clear();

        foreach (JoinSuggestion suggestion in suggestions)
        {
            NodeViewModel? existing = FindTableSourceNode(suggestion.ExistingTable);
            NodeViewModel? incoming = FindTableSourceNode(suggestion.NewTable);
            if (existing is null || incoming is null)
                continue;

            _suggestionAnchorsByKey[BuildSuggestionKey(suggestion)] = new SuggestionNodeAnchor(existing.Id, incoming.Id);
        }
    }

    private bool IsSuggestionAnchorCurrent(JoinSuggestion suggestion)
    {
        string key = BuildSuggestionKey(suggestion);
        if (!_suggestionAnchorsByKey.TryGetValue(key, out SuggestionNodeAnchor? anchor))
            return true;

        NodeViewModel? existing = FindTableSourceNode(suggestion.ExistingTable);
        NodeViewModel? incoming = FindTableSourceNode(suggestion.NewTable);
        if (existing is null || incoming is null)
            return false;

        return existing.Id == anchor.ExistingNodeId
            && incoming.Id == anchor.IncomingNodeId;
    }

    private static string BuildSuggestionKey(JoinSuggestion suggestion) =>
        CanvasAutoJoinSemantics.BuildSuggestionPairKey(
            suggestion.ExistingTable,
            suggestion.NewTable,
            suggestion.LeftColumn,
            suggestion.RightColumn);

    private sealed record SuggestionNodeAnchor(string ExistingNodeId, string IncomingNodeId);
}
