using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AkkornStudio.UI.Services.Localization;

namespace AkkornStudio.UI.Serialization;

// ═════════════════════════════════════════════════════════════════════════════
// MODEL
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// A single versioned snapshot of the canvas state.
/// </summary>
public record FlowVersion(
    string Id,
    string Label,
    string CreatedAt,
    int NodeCount,
    int ConnectionCount,
    /// <summary>Full serialized canvas JSON (SavedCanvas).</summary>
    string CanvasJson,
    string ProjectKey = FlowVersionStore.DefaultProjectKey
);

// ═════════════════════════════════════════════════════════════════════════════
// STORE
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Persists flow version checkpoints to
/// ~/.config/AkkornStudio/flow_versions.json (Linux) or
/// %APPDATA%\AkkornStudio\flow_versions.json (Windows).
///
/// Automatically caps history at <see cref="MaxVersions"/> entries to avoid
/// unbounded growth — oldest entries are pruned when the cap is reached.
/// </summary>
public static class FlowVersionStore
{
    public const int MaxVersions = 50;
    public const string DefaultProjectKey = "global";

    public static event Action<string>? WarningRaised;
    private static readonly ILogger _logger = NullLogger.Instance;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string StorePath()
    {
        string dir = global::AkkornStudio.UI.AppConstants.AppDataDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "flow_versions.json");
    }

    /// <summary>Loads all versions from disk (newest first). Returns empty list on first run.</summary>
    public static List<FlowVersion> Load()
    {
        string path = StorePath();
        if (!File.Exists(path))
            return [];
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<FlowVersion>>(json, _opts) ?? [];
        }
        catch (Exception ex)
        {
            RaiseWarning(
                string.Format(
                    L("flowVersionStore.warning.loadFailed", "Failed to load flow versions from '{0}': {1}"),
                    path,
                    ex.Message
                )
            );
            return [];
        }
    }

    public static List<FlowVersion> Load(string projectKey)
    {
        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        return Load()
            .Where(version => string.Equals(NormalizeProjectKey(version.ProjectKey), normalizedProjectKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Overwrites the store with the given list.</summary>
    public static void Save(List<FlowVersion> versions)
    {
        try
        {
            File.WriteAllText(StorePath(), JsonSerializer.Serialize(versions, _opts));
        }
        catch (Exception ex)
        {
            RaiseWarning(
                string.Format(
                    L("flowVersionStore.warning.saveFailed", "Failed to save flow versions: {0}"),
                    ex.Message
                )
            );
        }
    }

    private static void RaiseWarning(string message)
    {
        _logger.LogWarning("[FlowVersionStore] {Message}", message);
        WarningRaised?.Invoke(message);
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    /// <summary>
    /// Prepends a new version to the history and persists.
    /// Prunes oldest entries beyond <see cref="MaxVersions"/>.
    /// </summary>
    public static void Add(FlowVersion version)
    {
        Add(version, version.ProjectKey);
    }

    public static void Add(FlowVersion version, string projectKey)
    {
        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        List<FlowVersion> all = Load();
        FlowVersion scopedVersion = version with { ProjectKey = normalizedProjectKey };
        all.RemoveAll(v => v.Id == scopedVersion.Id);
        all.Insert(0, scopedVersion);

        HashSet<string> pruneIds = all
            .Where(v => string.Equals(NormalizeProjectKey(v.ProjectKey), normalizedProjectKey, StringComparison.OrdinalIgnoreCase))
            .Skip(MaxVersions)
            .Select(v => v.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pruneIds.Count > 0)
            all.RemoveAll(v => pruneIds.Contains(v.Id));

        Save(all);
    }

    /// <summary>Removes a version by ID.</summary>
    public static void Remove(string id)
    {
        List<FlowVersion> all = Load();
        all.RemoveAll(v => v.Id == id);
        Save(all);
    }

    public static void Remove(string id, string projectKey)
    {
        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        List<FlowVersion> all = Load();
        all.RemoveAll(v =>
            v.Id == id
            && string.Equals(NormalizeProjectKey(v.ProjectKey), normalizedProjectKey, StringComparison.OrdinalIgnoreCase));
        Save(all);
    }

    public static string NormalizeProjectKey(string? projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            return DefaultProjectKey;

        return string.Join(" ", projectKey.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
