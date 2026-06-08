using System.Collections.ObjectModel;
using System.ComponentModel;
using AkkornStudio.Core;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Services.ConnectionManager.Models;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels.ErDiagram;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// Top-level tab host. Each <see cref="WorkspaceTabViewModel"/> groups the documents of one tab
/// (one per visited mode) and is bound to a connection profile. Documents are scoped to a tab via
/// <see cref="_documentTab"/>; switching modes inside a tab reuses the existing per-type document
/// (preserving state) and only creates one lazily on first visit.
/// </summary>
public sealed partial class ShellViewModel
{
    private readonly Dictionary<Guid, Guid> _documentTab = new();
    private Guid _activeTabId;
    private WorkspaceTabViewModel? _activeTab;

    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = [];

    public NewTabModeModalViewModel NewTabModal { get; private set; } = null!;

    public RelayCommand NewTabCommand { get; private set; } = null!;

    public RelayCommand<WorkspaceTabViewModel> ActivateTabCommand { get; private set; } = null!;

    public RelayCommand<WorkspaceTabViewModel> CloseTabCommand { get; private set; } = null!;

    public RelayCommand<WorkspaceDocumentType> SwitchModeCommand { get; private set; } = null!;

    public WorkspaceTabViewModel? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (Set(ref _activeTab, value))
                RaisePropertyChanged(nameof(CanCloseActiveTab));
        }
    }

    public bool CanCloseActiveTab => Tabs.Count > 1;

    public bool IsNewTabModalVisible => NewTabModal?.IsVisible == true;

    private void InitializeTabs()
    {
        NewTabModal = new NewTabModeModalViewModel();
        NewTabModal.ModeChosen += OnNewTabModeChosen;
        NewTabModal.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NewTabModeModalViewModel.IsVisible))
                RaisePropertyChanged(nameof(IsNewTabModalVisible));
        };

        NewTabCommand = new RelayCommand(() => NewTabModal.Show());
        ActivateTabCommand = new RelayCommand<WorkspaceTabViewModel>(ActivateTab);
        CloseTabCommand = new RelayCommand<WorkspaceTabViewModel>(CloseTab);
        SwitchModeCommand = new RelayCommand<WorkspaceDocumentType>(mode => ActivateDocument(mode));

        // Switching the connection inside a SQL tab writes the choice back onto the tab.
        _sqlEditorConnectionManager.PropertyChanged += OnSqlEditorConnectionChanged;

        // First tab adopts whatever document the shell opened during construction (Query).
        _activeTabId = Guid.NewGuid();
        var firstTab = new WorkspaceTabViewModel(
            _activeTabId,
            ActiveWorkspaceDocumentType ?? WorkspaceDocumentType.QueryCanvas,
            ResolveActiveProfileId(),
            ResolveActiveConnectionTitle(),
            "Aba 1");

        Tabs.Add(firstTab);
        ActiveTab = firstTab;
        firstTab.IsActive = true;

        RetagAllDocumentsToActiveTab();
    }

    // ── per-tab document scoping ──────────────────────────────────────────────

    /// <summary>
    /// The active tab's document of <paramref name="documentType"/>, if it has one. Documents
    /// created outside <see cref="OpenNewDocument"/> (e.g. via <c>EnsureCanvas</c>) start untagged;
    /// the first lookup that needs one adopts it into the active tab so it stays put afterwards.
    /// </summary>
    private OpenWorkspaceDocument? FindActiveTabDocument(WorkspaceDocumentType documentType)
    {
        OpenWorkspaceDocument? strict = _workspaceRouter.OpenDocuments.LastOrDefault(document =>
            document.Descriptor.DocumentType == documentType
            && _documentTab.TryGetValue(document.Descriptor.DocumentId, out Guid tabId)
            && tabId == _activeTabId);
        if (strict is not null)
            return strict;

        OpenWorkspaceDocument? untagged = _workspaceRouter.OpenDocuments.LastOrDefault(document =>
            document.Descriptor.DocumentType == documentType
            && !_documentTab.ContainsKey(document.Descriptor.DocumentId));
        if (untagged is not null)
            _documentTab[untagged.Descriptor.DocumentId] = _activeTabId;

        return untagged;
    }

    private void TagActiveTabDocument(Guid documentId) => _documentTab[documentId] = _activeTabId;

    private void RetagAllDocumentsToActiveTab()
    {
        foreach (OpenWorkspaceDocument document in _workspaceRouter.OpenDocuments)
            _documentTab[document.Descriptor.DocumentId] = _activeTabId;
    }

    /// <summary>Keeps the active tab's <c>CurrentMode</c> in sync with the active document type.</summary>
    private void SyncActiveTabModeFromActiveDocument()
    {
        if (_activeTab is null)
            return;

        if (ActiveWorkspaceDocumentType is WorkspaceDocumentType type)
            _activeTab.CurrentMode = type;
    }

    // ── tab operations ────────────────────────────────────────────────────────

    /// <summary>Snapshot of the tab axis (document→tab map + tabs + active tab) for session persistence.</summary>
    public WorkspaceTabsSaveSnapshot BuildTabsSaveSnapshot()
    {
        var tabs = Tabs
            .Select(tab => new SavedWorkspaceTab(
                tab.TabId,
                tab.Title,
                tab.CurrentMode.ToString(),
                tab.ProfileId,
                tab.ConnectionTitle))
            .ToList();

        return new WorkspaceTabsSaveSnapshot(
            new Dictionary<Guid, Guid>(_documentTab),
            tabs,
            _activeTab?.TabId);
    }

    /// <summary>Rebuilds the tab axis from a restored session snapshot (or a single tab for old saves).</summary>
    private void RestoreTabsFromSnapshot(
        SavedWorkspaceDocumentsCanvas workspace,
        IReadOnlyList<(Guid DocumentId, Guid TabId)> documentTabPairs)
    {
        Tabs.Clear();
        _documentTab.Clear();

        if (workspace.Tabs is { Count: > 0 })
        {
            foreach (SavedWorkspaceTab savedTab in workspace.Tabs)
            {
                WorkspaceDocumentType mode = Enum.TryParse(savedTab.CurrentMode, out WorkspaceDocumentType parsed)
                    ? parsed
                    : WorkspaceDocumentType.QueryCanvas;

                Tabs.Add(new WorkspaceTabViewModel(
                    savedTab.TabId,
                    mode,
                    savedTab.ProfileId,
                    string.IsNullOrWhiteSpace(savedTab.ConnectionTitle) ? "Sem conexao" : savedTab.ConnectionTitle!,
                    string.IsNullOrWhiteSpace(savedTab.Title) ? "Aba" : savedTab.Title));
            }

            Guid fallbackTabId = Tabs[0].TabId;
            HashSet<Guid> knownTabs = Tabs.Select(static t => t.TabId).ToHashSet();
            foreach ((Guid documentId, Guid tabId) in documentTabPairs)
                _documentTab[documentId] = tabId != Guid.Empty && knownTabs.Contains(tabId) ? tabId : fallbackTabId;
        }
        else
        {
            // Old / tab-less saves: wrap every restored document into a single tab.
            var singleTabId = Guid.NewGuid();
            WorkspaceDocumentType mode = ActiveWorkspaceDocumentType ?? WorkspaceDocumentType.QueryCanvas;
            Tabs.Add(new WorkspaceTabViewModel(singleTabId, mode, ResolveActiveProfileId(), ResolveActiveConnectionTitle(), "Aba 1"));
            foreach ((Guid documentId, Guid _) in documentTabPairs)
                _documentTab[documentId] = singleTabId;
        }

        // Keep the active tab consistent with the active document the router just selected.
        WorkspaceTabViewModel? activeTab = null;
        if (_workspaceRouter.ActiveDocumentId is Guid activeDocId
            && _documentTab.TryGetValue(activeDocId, out Guid ownerTabId))
        {
            activeTab = Tabs.FirstOrDefault(t => t.TabId == ownerTabId);
        }

        activeTab ??= workspace.ActiveTabId is Guid persistedActive
            ? Tabs.FirstOrDefault(t => t.TabId == persistedActive)
            : null;
        activeTab ??= Tabs[0];

        _activeTabId = activeTab.TabId;
        SetActiveTab(activeTab);
        RaisePropertyChanged(nameof(CanCloseActiveTab));
    }

    private void OnSqlEditorConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ConnectionManagerViewModel.SelectedProfile)
            or nameof(ConnectionManagerViewModel.ActiveProfileId)))
            return;

        if (_activeTab is null || _activeTab.CurrentMode != WorkspaceDocumentType.SqlEditor)
            return;

        ConnectionProfile? profile = _sqlEditorConnectionManager.SelectedProfile;
        if (profile is null)
            return;

        _activeTab.ProfileId = profile.Id;
        _activeTab.ConnectionTitle = profile.Name;
    }

    private void OnNewTabModeChosen(WorkspaceDocumentType mode) => CreateTab(mode);

    public WorkspaceTabViewModel CreateTab(WorkspaceDocumentType initialMode)
    {
        int ordinal = Tabs.Count + 1;
        _activeTabId = Guid.NewGuid();
        var tab = new WorkspaceTabViewModel(
            _activeTabId,
            initialMode,
            ResolveActiveProfileId(),
            ResolveActiveConnectionTitle(),
            $"Aba {ordinal}");

        Tabs.Add(tab);
        SetActiveTab(tab);

        // Lookups are now scoped to the new (empty) tab, so this always creates a fresh document.
        Guid documentId = OpenNewDocument(initialMode);
        _workspaceRouter.TryActivate(documentId);

        AfterActiveTabChanged();
        return tab;
    }

    public void ActivateTab(WorkspaceTabViewModel? tab)
    {
        if (tab is null || ReferenceEquals(tab, ActiveTab))
            return;

        _activeTabId = tab.TabId;
        SetActiveTab(tab);

        OpenWorkspaceDocument? target = FindActiveTabDocument(tab.CurrentMode);
        if (target is not null)
            _workspaceRouter.TryActivate(target.Descriptor.DocumentId);
        else
            _workspaceRouter.TryActivate(OpenNewDocument(tab.CurrentMode));

        AfterActiveTabChanged();
    }

    public void CloseTab(WorkspaceTabViewModel? tab)
    {
        if (tab is null || Tabs.Count <= 1)
            return;

        Guid[] documentIds = _documentTab
            .Where(pair => pair.Value == tab.TabId)
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (Guid documentId in documentIds)
        {
            _workspaceRouter.TryClose(documentId);
            _documentTab.Remove(documentId);
        }

        int index = Tabs.IndexOf(tab);
        bool wasActive = ReferenceEquals(tab, ActiveTab);
        Tabs.Remove(tab);
        RaisePropertyChanged(nameof(CanCloseActiveTab));

        if (!wasActive)
            return;

        WorkspaceTabViewModel neighbor = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        ActiveTab = null; // force ActivateTab to run even if reference matches stale state
        ActivateTab(neighbor);
    }

    private void SetActiveTab(WorkspaceTabViewModel tab)
    {
        foreach (WorkspaceTabViewModel candidate in Tabs)
            candidate.IsActive = ReferenceEquals(candidate, tab);

        ActiveTab = tab;
    }

    private void AfterActiveTabChanged()
    {
        RebindActiveTabDocuments();
        RepointConnectionForActiveTab();
        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
    }

    /// <summary>Points the shell's singleton document fields at the active tab's documents.</summary>
    private void RebindActiveTabDocuments()
    {
        _queryDocumentId = FindActiveTabDocument(WorkspaceDocumentType.QueryCanvas)?.Descriptor.DocumentId;
        _ddlDocumentId = FindActiveTabDocument(WorkspaceDocumentType.DdlCanvas)?.Descriptor.DocumentId;
        _erDiagramDocumentId = FindActiveTabDocument(WorkspaceDocumentType.ErDiagram)?.Descriptor.DocumentId;
        _sqlResultDocumentId = FindActiveTabDocument(WorkspaceDocumentType.SqlResult)?.Descriptor.DocumentId;
        _ddlSchemaCompareDocumentId = FindActiveTabDocument(WorkspaceDocumentType.DdlSchemaCompare)?.Descriptor.DocumentId;
        _ddlSchemaAnalysisDocumentId = FindActiveTabDocument(WorkspaceDocumentType.DdlSchemaAnalysis)?.Descriptor.DocumentId;

        Canvas = FindActiveTabDocument(WorkspaceDocumentType.QueryCanvas)?.DocumentViewModel as CanvasViewModel;
        DdlCanvas = FindActiveTabDocument(WorkspaceDocumentType.DdlCanvas)?.DocumentViewModel as CanvasViewModel;
        _erCanvas = FindActiveTabDocument(WorkspaceDocumentType.ErDiagram)?.DocumentViewModel as ErCanvasViewModel;
        _sqlResultPage = FindActiveTabDocument(WorkspaceDocumentType.SqlResult)?.DocumentViewModel as SqlResultPageViewModel;
    }

    // ── connection per tab ──────────────────────────────────────────────────────

    private string? ResolveActiveProfileId() =>
        ActiveConnectionManager?.SelectedProfile?.Id
        ?? _sqlEditorConnectionManager.ActiveProfileId
        ?? _sqlEditorConnectionManager.SelectedProfile?.Id;

    private string ResolveActiveConnectionTitle()
    {
        ConnectionProfile? profile =
            ActiveConnectionManager?.SelectedProfile
            ?? _sqlEditorConnectionManager.SelectedProfile;
        return profile?.Name ?? "Sem conexao";
    }

    private void RepointConnectionForActiveTab()
    {
        if (_activeTab is null)
            return;

        // Default the tab's connection to the current active profile the first time it is unset.
        _activeTab.ProfileId ??= ResolveActiveProfileId();

        ConnectionProfile? profile = string.IsNullOrWhiteSpace(_activeTab.ProfileId)
            ? null
            : _sqlEditorConnectionManager.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, _activeTab.ProfileId, StringComparison.OrdinalIgnoreCase));

        _activeTab.ConnectionTitle = profile?.Name ?? ResolveActiveConnectionTitle();

        // SQL tabs default to the tab's connection but can still be switched in the editor.
        if (profile is not null && ActiveWorkspaceDocumentType == WorkspaceDocumentType.SqlEditor)
        {
            _sqlEditorConnectionManager.SelectedProfile = profile;
            _sqlEditorConnectionManager.ActiveProfileId = profile.Id;
        }
    }
}
