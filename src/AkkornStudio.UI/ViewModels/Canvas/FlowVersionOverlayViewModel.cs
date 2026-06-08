using System.Collections.ObjectModel;
using System.Text.Json;
using AkkornStudio.UI.Serialization;

namespace AkkornStudio.UI.ViewModels.Canvas;

// ═════════════════════════════════════════════════════════════════════════════
// OVERLAY VM
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Drives the Flow Version History overlay (Tarefa 21).
///
/// Responsibilities:
///   • Create named checkpoints (snapshots) of the current canvas.
///   • List all saved checkpoints with metadata.
///   • Compute a human-readable diff between any two selected versions.
///   • Restore a previous version (with backup of current state).
/// </summary>
public sealed class FlowVersionOverlayViewModel : ViewModelBase
{
    private readonly CanvasViewModel _canvas;

    private bool _isVisible;
    private bool _isDiffMode;
    private string _newLabel = string.Empty;
    private FlowVersionRowViewModel? _selectedVersion;
    private FlowVersionRowViewModel? _diffBaseVersion;
    private string ProjectKey => ResolveProjectKey(_canvas);

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<FlowVersionRowViewModel> Versions { get; } = [];
    public ObservableCollection<DiffItemViewModel> DiffItems { get; } = [];

    // ── Properties ────────────────────────────────────────────────────────────

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    /// <summary>Label typed by the user for the new checkpoint.</summary>
    public string NewLabel
    {
        get => _newLabel;
        set => Set(ref _newLabel, value);
    }

    /// <summary>Currently highlighted version row.</summary>
    public FlowVersionRowViewModel? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            Set(ref _selectedVersion, value);
            RaisePropertyChanged(nameof(HasSelection));
            RaisePropertyChanged(nameof(CanDiff));
            if (_isDiffMode && _diffBaseVersion is not null && value is not null)
                ComputeDiff(_diffBaseVersion.Version, value.Version);
        }
    }

    /// <summary>The version used as the "from" side of a diff comparison.</summary>
    public FlowVersionRowViewModel? DiffBaseVersion
    {
        get => _diffBaseVersion;
        set
        {
            Set(ref _diffBaseVersion, value);
            RaisePropertyChanged(nameof(CanDiff));
        }
    }

    public bool IsDiffMode
    {
        get => _isDiffMode;
        set
        {
            Set(ref _isDiffMode, value);
            if (!value)
            {
                DiffItems.Clear();
                DiffBaseVersion = null;
            }
        }
    }

    public bool HasSelection => SelectedVersion is not null;
    public bool CanDiff => DiffBaseVersion is not null && SelectedVersion is not null
                           && DiffBaseVersion.Id != SelectedVersion?.Id;
    public bool HasVersions => Versions.Count > 0;
    public bool HasDiffItems => DiffItems.Count > 0;

    public string DiffSummary =>
        DiffItems.Count == 0
            ? "No differences"
            : $"{DiffItems.Count(i => i.Kind == DiffKind.Added)} added · "
            + $"{DiffItems.Count(i => i.Kind == DiffKind.Removed)} removed · "
            + $"{DiffItems.Count(i => i.Kind == DiffKind.Modified)} modified";

    // ── Constructor ───────────────────────────────────────────────────────────

    public FlowVersionOverlayViewModel(CanvasViewModel canvas)
    {
        _canvas = canvas;
    }

    // ── Open / Close ──────────────────────────────────────────────────────────

    public void Open()
    {
        Reload();
        IsVisible = true;
    }

    public void Close()
    {
        IsVisible = false;
        IsDiffMode = false;
    }

    // ── Checkpoint creation ───────────────────────────────────────────────────

    /// <summary>
    /// Snapshots the current canvas state and saves it to the version store.
    /// If <paramref name="label"/> is empty, a timestamp label is generated.
    /// </summary>
    public void CreateCheckpoint(string? label = null)
    {
        string resolvedLabel = string.IsNullOrWhiteSpace(label)
            ? $"Checkpoint {DateTimeOffset.Now:yyyy-MM-dd HH:mm}"
            : label.Trim();

        string json = _canvas.SerializeForPersistence();
        var version = new FlowVersion(
            Id: Guid.NewGuid().ToString(),
            Label: resolvedLabel,
            CreatedAt: DateTimeOffset.UtcNow.ToString("o"),
            NodeCount: _canvas.Nodes.Count,
            ConnectionCount: _canvas.Connections.Count,
            CanvasJson: json,
            ProjectKey: ProjectKey
        );

        FlowVersionStore.Add(version, ProjectKey);
        NewLabel = string.Empty;
        Reload();
    }

    // ── Restore ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores the canvas to <paramref name="version"/>.
    /// The current state is automatically backed up as "Auto-backup before restore".
    /// </summary>
    public void Restore(FlowVersion version)
    {
        // Back up current state first
        CreateCheckpoint("Auto-backup before restore");

        CanvasSerializer.Deserialize(version.CanvasJson, _canvas);

        Close();
    }

    // ── Delete version ────────────────────────────────────────────────────────

    public void DeleteVersion(string id)
    {
        FlowVersionStore.Remove(id, ProjectKey);
        Reload();
    }

    // ── Diff ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the diff between <paramref name="baseVer"/> and <paramref name="headVer"/>
    /// and populates <see cref="DiffItems"/>.
    /// </summary>
    public void ComputeDiff(FlowVersion baseVer, FlowVersion headVer)
    {
        DiffItems.Clear();

        SavedCanvas? from = TryDeserializeCanvas(baseVer.CanvasJson);
        SavedCanvas? to   = TryDeserializeCanvas(headVer.CanvasJson);

        if (from is null || to is null)
        {
            DiffItems.Add(new DiffItemViewModel(DiffKind.Modified, "Could not parse one or both versions"));
            return;
        }

        // ── Nodes ─────────────────────────────────────────────────────────────
        // Use GroupBy + FirstOrDefault to handle duplicate NodeIds gracefully
        // (take the first occurrence, ignore duplicates from serialization bugs)

        var fromNodeGroups = from.Nodes.GroupBy(n => n.NodeId).ToList();
        var toNodeGroups   = to.Nodes.GroupBy(n => n.NodeId).ToList();

        int fromNodeDupCount = fromNodeGroups.Sum(g => Math.Max(0, g.Count() - 1));
        int toNodeDupCount   = toNodeGroups.Sum(g => Math.Max(0, g.Count() - 1));
        if (fromNodeDupCount > 0 || toNodeDupCount > 0)
            DiffItems.Add(new DiffItemViewModel(
                DiffKind.Modified,
                $"Duplicate NodeId entries detected (base={fromNodeDupCount}, head={toNodeDupCount}) — using first occurrence per NodeId"
            ));

        var fromNodes = fromNodeGroups.ToDictionary(g => g.Key, g => g.First());
        var toNodes   = toNodeGroups.ToDictionary(g => g.Key, g => g.First());

        foreach (var n in toNodes.Values.Where(n => !fromNodes.ContainsKey(n.NodeId)))
            DiffItems.Add(new DiffItemViewModel(DiffKind.Added, $"Node added: {n.NodeType}{(n.Alias is not null ? $" \"{n.Alias}\"" : "")}{(n.TableFullName is not null ? $" ({n.TableFullName})" : "")}"));

        foreach (var n in fromNodes.Values.Where(n => !toNodes.ContainsKey(n.NodeId)))
            DiffItems.Add(new DiffItemViewModel(DiffKind.Removed, $"Node removed: {n.NodeType}{(n.Alias is not null ? $" \"{n.Alias}\"" : "")}{(n.TableFullName is not null ? $" ({n.TableFullName})" : "")}"));

        foreach (var (id, toNode) in toNodes)
        {
            if (!fromNodes.TryGetValue(id, out var fromNode))
                continue;

            // Position change
            if (Math.Abs(toNode.X - fromNode.X) > 1 || Math.Abs(toNode.Y - fromNode.Y) > 1)
                DiffItems.Add(new DiffItemViewModel(DiffKind.Modified, $"Node moved: {toNode.NodeType} \"{toNode.Alias ?? id[..8]}\""));

            // Alias change
            if (toNode.Alias != fromNode.Alias)
                DiffItems.Add(new DiffItemViewModel(DiffKind.Modified, $"Alias changed: \"{fromNode.Alias}\" → \"{toNode.Alias}\""));

            // Parameter changes
            foreach (var (key, toVal) in toNode.Parameters)
            {
                if (fromNode.Parameters.TryGetValue(key, out var fromVal) && fromVal != toVal)
                    DiffItems.Add(new DiffItemViewModel(DiffKind.Modified, $"Param \"{key}\" changed: \"{Truncate(fromVal)}\" → \"{Truncate(toVal)}\""));
                else if (!fromNode.Parameters.ContainsKey(key))
                    DiffItems.Add(new DiffItemViewModel(DiffKind.Added, $"Param \"{key}\" added: \"{Truncate(toVal)}\""));
            }
            foreach (var key in fromNode.Parameters.Keys.Where(k => !toNode.Parameters.ContainsKey(k)))
                DiffItems.Add(new DiffItemViewModel(DiffKind.Removed, $"Param \"{key}\" removed"));
        }

        // ── Connections ───────────────────────────────────────────────────────
        // Use GroupBy + FirstOrDefault to handle duplicate connection keys gracefully

        string ConnKey(SavedConnection c) => $"{c.FromNodeId}.{c.FromPinName}→{c.ToNodeId}.{c.ToPinName}";

        var fromConnGroups = from.Connections.GroupBy(ConnKey).ToList();
        var toConnGroups   = to.Connections.GroupBy(ConnKey).ToList();

        int fromConnDupCount = fromConnGroups.Sum(g => Math.Max(0, g.Count() - 1));
        int toConnDupCount   = toConnGroups.Sum(g => Math.Max(0, g.Count() - 1));
        if (fromConnDupCount > 0 || toConnDupCount > 0)
            DiffItems.Add(new DiffItemViewModel(
                DiffKind.Modified,
                $"Duplicate connection keys detected (base={fromConnDupCount}, head={toConnDupCount}) — using first occurrence per key"
            ));

        var fromConns = fromConnGroups.ToDictionary(g => g.Key, g => g.First());
        var toConns   = toConnGroups.ToDictionary(g => g.Key, g => g.First());

        foreach (var c in toConns.Values.Where(c => !fromConns.ContainsKey(ConnKey(c))))
            DiffItems.Add(new DiffItemViewModel(DiffKind.Added, $"Connection added: {c.FromPinName} → {c.ToPinName}"));

        foreach (var c in fromConns.Values.Where(c => !toConns.ContainsKey(ConnKey(c))))
            DiffItems.Add(new DiffItemViewModel(DiffKind.Removed, $"Connection removed: {c.FromPinName} → {c.ToPinName}"));

        // ── Zoom / pan ────────────────────────────────────────────────────────

        if (Math.Abs(to.Zoom - from.Zoom) > 0.01)
            DiffItems.Add(new DiffItemViewModel(DiffKind.Modified, $"Zoom: {from.Zoom:F2} → {to.Zoom:F2}"));

        if (DiffItems.Count == 0)
            DiffItems.Add(new DiffItemViewModel(DiffKind.Modified, "No structural differences"));

        RaisePropertyChanged(nameof(HasDiffItems));
        RaisePropertyChanged(nameof(DiffSummary));
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void Reload()
    {
        Versions.Clear();
        foreach (FlowVersion v in FlowVersionStore.Load(ProjectKey))
            Versions.Add(new FlowVersionRowViewModel(v));

        SelectedVersion = null;
        DiffItems.Clear();
        RaisePropertyChanged(nameof(HasVersions));
        RaisePropertyChanged(nameof(HasDiffItems));
        RaisePropertyChanged(nameof(DiffSummary));
    }

    private static SavedCanvas? TryDeserializeCanvas(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SavedCanvas>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveProjectKey(CanvasViewModel canvas)
    {
        if (!string.IsNullOrWhiteSpace(canvas.CurrentFilePath))
            return Path.GetFullPath(canvas.CurrentFilePath);

        return FlowVersionStore.DefaultProjectKey;
    }

    private static string Truncate(string s, int max = 40) =>
        s.Length <= max ? s : s[..max] + "…";
}
