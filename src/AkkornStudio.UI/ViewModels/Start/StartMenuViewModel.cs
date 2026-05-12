using System.Collections.ObjectModel;
using System.Text.Json;
using AkkornStudio.UI.Services.Search;
using AkkornStudio.UI.Services;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// ViewModel for the startup home screen. Exposes lightweight data and intent commands.
/// </summary>
public sealed class StartMenuViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly List<StartRecentProjectItem> _allRecentProjects = [];
    private readonly HashSet<string> _favoriteTemplates;
    private readonly TextSearchService _textSearch = new();
    private string _recentSearchQuery = string.Empty;

    public StartMenuViewModel()
    {
        _favoriteTemplates = TemplateFavoritesStore.Load();

        CreateNewDiagramCommand = new RelayCommand(() => CreateNewDiagramRequested?.Invoke());
        OpenConnectionsCommand = new RelayCommand(() => OpenConnectionsRequested?.Invoke());
        OpenFromDiskCommand = new RelayCommand(() => OpenFromDiskRequested?.Invoke());
        OpenSchemaCompareCommand = new RelayCommand(() => OpenSchemaCompareRequested?.Invoke());
        OpenSqlEditorCommand = new RelayCommand(() => OpenSqlEditorRequested?.Invoke());
        OpenSavedConnectionCommand = new RelayCommand<StartSavedConnectionItem>(item =>
        {
            if (item is null)
                return;

            OpenSavedConnectionRequested?.Invoke(item);
        });

        OpenRecentProjectCommand = new RelayCommand<StartRecentProjectItem>(item =>
        {
            if (item is null)
                return;

            OpenRecentProjectRequested?.Invoke(item);
        });

        OpenTemplateCommand = new RelayCommand<StartTemplateItem>(item =>
        {
            if (item is null)
                return;

            OpenTemplateRequested?.Invoke(item);
        });

        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke());

        ToggleTemplateFavoriteCommand = new RelayCommand<StartTemplateItem>(item =>
        {
            if (item is null)
                return;

            item.IsFavorite = !item.IsFavorite;

            if (item.IsFavorite)
                _favoriteTemplates.Add(item.Name);
            else
                _favoriteTemplates.Remove(item.Name);

            TemplateFavoritesStore.Save(_favoriteTemplates);
            ReorderTemplates();
        });

        RecentProjects = [];
        SavedConnections = [];

        TemplateCatalog = QueryTemplateCatalog.LoadAll()
            .Select(t => new StartTemplateItem(t.Name, t.Category, t.Description, t.Id))
            .ToObservableCollection();

        foreach (StartTemplateItem template in TemplateCatalog)
            template.IsFavorite = _favoriteTemplates.Contains(template.Name);

        ReorderTemplates();

        RefreshData();
    }

    public string RecentSearchQuery
    {
        get => _recentSearchQuery;
        set
        {
            if (!Set(ref _recentSearchQuery, value))
                return;

            ApplyRecentFilter();
        }
    }

    public ObservableCollection<StartRecentProjectItem> RecentProjects { get; }

    public bool HasRecentProjects => RecentProjects.Count > 0;

    public ObservableCollection<StartSavedConnectionItem> SavedConnections { get; }

    public bool HasSavedConnections => SavedConnections.Count > 0;

    public ObservableCollection<StartTemplateItem> TemplateCatalog { get; }

    public RelayCommand CreateNewDiagramCommand { get; }

    public RelayCommand OpenConnectionsCommand { get; }

    public RelayCommand OpenFromDiskCommand { get; }

    public RelayCommand OpenSchemaCompareCommand { get; }

    public RelayCommand OpenSqlEditorCommand { get; }

    public RelayCommand<StartSavedConnectionItem> OpenSavedConnectionCommand { get; }

    public RelayCommand<StartRecentProjectItem> OpenRecentProjectCommand { get; }

    public RelayCommand<StartTemplateItem> OpenTemplateCommand { get; }

    public RelayCommand<StartTemplateItem> ToggleTemplateFavoriteCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public event Action? CreateNewDiagramRequested;

    public event Action? OpenConnectionsRequested;

    public event Action? OpenFromDiskRequested;

    public event Action? OpenSchemaCompareRequested;

    public event Action? OpenSqlEditorRequested;

    public event Action<StartSavedConnectionItem>? OpenSavedConnectionRequested;

    public event Action<StartRecentProjectItem>? OpenRecentProjectRequested;

    public event Action<StartTemplateItem>? OpenTemplateRequested;

    public event Action? OpenSettingsRequested;

    public void RefreshData(
        IEnumerable<ConnectionProfile>? runtimeProfiles = null,
        string? activeProfileId = null
    )
    {
        LoadRecentProjects();
        LoadSavedConnections(runtimeProfiles, activeProfileId);
    }

    private void LoadRecentProjects()
    {
        _allRecentProjects.Clear();
        RecentProjects.Clear();

        foreach (RecentFileEntry item in RecentFilesStore.GetRecent(6))
        {
            string displayName = Path.GetFileName(item.FilePath);
            string label = FormatRelativeTime(item.LastOpenedUtc);
            string summary = BuildSnapshotSummary(item.FilePath);
            _allRecentProjects.Add(
                new StartRecentProjectItem(displayName, "Canvas", label, item.FilePath, summary)
            );
        }

        ApplyRecentFilter();
        RaisePropertyChanged(nameof(HasRecentProjects));
    }

    private void ApplyRecentFilter()
    {
        RecentProjects.Clear();

        IEnumerable<StartRecentProjectItem> filtered = _allRecentProjects;
        if (!string.IsNullOrWhiteSpace(RecentSearchQuery))
        {
            filtered = filtered.Where(x =>
                _textSearch.Matches(RecentSearchQuery, x.DisplayName, x.FilePath)
            );
        }

        foreach (StartRecentProjectItem item in filtered)
            RecentProjects.Add(item);

        RaisePropertyChanged(nameof(HasRecentProjects));
    }

    private void LoadSavedConnections(
        IEnumerable<ConnectionProfile>? runtimeProfiles,
        string? activeProfileId
    )
    {
        SavedConnections.Clear();

        IEnumerable<ConnectionProfile> source = runtimeProfiles ?? LoadPersistedProfiles();
        foreach (ConnectionProfile profile in source.Take(6))
        {
            bool isConnected = !string.IsNullOrWhiteSpace(activeProfileId)
                && string.Equals(profile.Id, activeProfileId, StringComparison.OrdinalIgnoreCase);

            SavedConnections.Add(
                new StartSavedConnectionItem(
                    profile.Id,
                    profile.Name,
                    profile.Provider.ToString(),
                    isConnected ? "Conectado" : "Salva",
                    isConnected
                )
            );
        }

        RaisePropertyChanged(nameof(HasSavedConnections));
    }

    private static IReadOnlyList<ConnectionProfile> LoadPersistedProfiles()
    {
        try
        {
            if (!File.Exists(ProfilesFilePath))
                return [];

            string json = File.ReadAllText(ProfilesFilePath);
            return JsonSerializer.Deserialize<List<ConnectionProfile>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string ProfilesFilePath =>
        Path.Combine(
            global::AkkornStudio.UI.AppConstants.AppDataDirectory,
            "connections.json"
        );

    private static string FormatRelativeTime(DateTime utc)
    {
        TimeSpan delta = DateTime.UtcNow - utc;

        if (delta.TotalMinutes < 1)
            return "agora";
        if (delta.TotalHours < 1)
            return $"há {(int)delta.TotalMinutes} min";
        if (delta.TotalDays < 1)
            return $"há {(int)delta.TotalHours} hora(s)";
        if (delta.TotalDays < 7)
            return $"há {(int)delta.TotalDays} dia(s)";

        return utc.ToLocalTime().ToString("dd/MM/yyyy");
    }

    private static string BuildSnapshotSummary(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            int nodes = TryGetArrayCount(root, "Nodes", "nodes");
            int conns = TryGetArrayCount(root, "Connections", "connections");

            if (nodes >= 0 && conns >= 0)
                return $"{nodes} nós • {conns} conexões";
            if (nodes >= 0)
                return $"{nodes} nós";
            if (conns >= 0)
                return $"{conns} conexões";
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Keep fallback below.
        }

        return "Snapshot indisponível";
    }

    private static int TryGetArrayCount(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                continue;

            if (value.ValueKind == JsonValueKind.Array)
                return value.GetArrayLength();
        }

        return -1;
    }

    private void ReorderTemplates()
    {
        var ordered = TemplateCatalog
            .OrderByDescending(t => t.IsFavorite)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TemplateCatalog.Clear();
        foreach (StartTemplateItem template in ordered)
            TemplateCatalog.Add(template);
    }
}

internal static class StartMenuCollectionExtensions
{
    public static ObservableCollection<T> ToObservableCollection<T>(this IEnumerable<T> source) =>
        new(source);
}
