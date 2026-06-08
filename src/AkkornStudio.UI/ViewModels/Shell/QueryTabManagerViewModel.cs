using System.Collections.ObjectModel;
using AkkornStudio.UI.Services.Explain;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// Holds query tab session state independent from the window code-behind.
/// </summary>
public sealed class QueryTabManagerViewModel : ViewModelBase
{
    private int _activeTabIndex;

    public ObservableCollection<QueryTabState> Tabs { get; } = [];

    public int ActiveTabIndex
    {
        get => _activeTabIndex;
        private set => Set(ref _activeTabIndex, value);
    }

    public bool IsRestoringTab { get; set; }

    public void Initialize(
        string firstSnapshotJson,
        string? currentFilePath,
        bool isDirty,
        IReadOnlyList<ExplainHistoryState>? explainHistory = null)
    {
        Tabs.Clear();
        Tabs.Add(new QueryTabState
        {
            FallbackTitle = "Consulta 1",
            SnapshotJson = firstSnapshotJson,
            CurrentFilePath = currentFilePath,
            IsDirty = isDirty,
            ExplainHistory = explainHistory?.ToList() ?? [],
        });

        ActiveTabIndex = 0;
    }

    public void CaptureActive(
        string snapshotJson,
        string? currentFilePath,
        bool isDirty,
        IReadOnlyList<ExplainHistoryState>? explainHistory = null)
    {
        if (!TryGetActive(out QueryTabState? active) || active is null)
            return;

        active.SnapshotJson = snapshotJson;
        active.CurrentFilePath = currentFilePath;
        active.IsDirty = isDirty;
        active.ExplainHistory = explainHistory?.ToList() ?? [];
    }

    public void SyncActiveMetadata(string? currentFilePath, bool isDirty)
    {
        if (!TryGetActive(out QueryTabState? active) || active is null)
            return;

        active.CurrentFilePath = currentFilePath;
        active.IsDirty = isDirty;
    }

    public QueryTabState? GetTab(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= Tabs.Count)
            return null;

        return Tabs[tabIndex];
    }

    public bool TryActivate(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= Tabs.Count || tabIndex == ActiveTabIndex)
            return false;

        ActiveTabIndex = tabIndex;
        return true;
    }

    public void ResetActive(string snapshotJson)
    {
        if (!TryGetActive(out QueryTabState? active) || active is null)
            return;

        active.SnapshotJson = snapshotJson;
        active.CurrentFilePath = null;
        active.IsDirty = false;
    }

    public void AddNewTab(string snapshotJson, IReadOnlyList<ExplainHistoryState>? explainHistory = null)
    {
        int tabNumber = Tabs.Count + 1;
        Tabs.Add(new QueryTabState
        {
            FallbackTitle = $"Consulta {tabNumber}",
            SnapshotJson = snapshotJson,
            CurrentFilePath = null,
            IsDirty = false,
            ExplainHistory = explainHistory?.ToList() ?? [],
        });

        ActiveTabIndex = Tabs.Count - 1;
    }

    /// <summary>
    /// Removes the tab at <paramref name="tabIndex"/> and returns the new active index.
    /// Does nothing and returns the current active index if only one tab remains.
    /// </summary>
    public int CloseTab(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= Tabs.Count || Tabs.Count <= 1)
            return ActiveTabIndex;

        Tabs.RemoveAt(tabIndex);

        int newActive = ActiveTabIndex;
        if (tabIndex < ActiveTabIndex)
            newActive = ActiveTabIndex - 1;
        else if (tabIndex == ActiveTabIndex)
            newActive = Math.Min(tabIndex, Tabs.Count - 1);

        ActiveTabIndex = newActive;
        return newActive;
    }

    private bool TryGetActive(out QueryTabState? tab)
    {
        QueryTabState? found = GetTab(ActiveTabIndex);
        if (found is null)
        {
            tab = null;
            return false;
        }

        tab = found;
        return true;
    }
}
