using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.Settings;

public sealed class AppSettings
{
    public string ThemeVariant { get; set; } = "Dark";
    public bool SqlEditorTop1000WithoutWhereEnabled { get; set; } = true;
    public bool SqlEditorProtectMutationWithoutWhereEnabled { get; set; } = true;
    public ShortcutSettingsSection Shortcuts { get; set; } = new();
    public double SqlEditorResultsSheetHeight { get; set; } = 260;
    public Dictionary<string, string> SqlEditorResultFiltersByTab { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SqlEditorSessionDraftEntry> SqlEditorSessionDrafts { get; set; } = [];
    public Dictionary<string, Dictionary<string, int>> SqlEditorCompletionFrequencyByProfile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<SqlEditorHistoryEntry>> SqlEditorHistoryByProfile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ProjectConventionSettings ProjectConventions { get; set; } = new();
}

public sealed class ProjectConventionSettings
{
    public string NamingConvention { get; set; } = "snake_case";
    public bool EnforceAliasNaming { get; set; } = true;
    public bool WarnOnReservedKeywords { get; set; } = true;
    public int MaxAliasLength { get; set; } = 64;
    public string DefaultWireCurveMode { get; set; } = "Bezier";
}

public static class AppSettingsStore
{
    private static readonly ILogger _logger = NullLogger.Instance;
    private static readonly AsyncLocal<string?> SettingsPathOverrideAsyncLocal = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string? SettingsPathOverride
    {
        get => SettingsPathOverrideAsyncLocal.Value;
        set => SettingsPathOverrideAsyncLocal.Value = value;
    }

    private static string SettingsPath => SettingsPathOverride ?? Path.Combine(
        global::AkkornStudio.UI.AppConstants.AppDataDirectory,
        "app.settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            string json = File.ReadAllText(SettingsPath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            settings.Shortcuts ??= new ShortcutSettingsSection();
            settings.Shortcuts.Overrides ??= [];
            if (settings.Shortcuts.Version <= 0)
                settings.Shortcuts.Version = 1;
            settings.SqlEditorResultFiltersByTab ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            settings.SqlEditorSessionDrafts ??= [];
            settings.SqlEditorCompletionFrequencyByProfile ??= new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            settings.SqlEditorHistoryByProfile ??= new Dictionary<string, List<SqlEditorHistoryEntry>>(StringComparer.OrdinalIgnoreCase);
            settings.ProjectConventions ??= new ProjectConventionSettings();
            settings.ProjectConventions.NamingConvention = NormalizeNamingConvention(settings.ProjectConventions.NamingConvention);
            settings.ProjectConventions.MaxAliasLength = Math.Max(0, settings.ProjectConventions.MaxAliasLength);
            settings.ProjectConventions.DefaultWireCurveMode = NormalizeWireCurveMode(settings.ProjectConventions.DefaultWireCurveMode);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to load app settings. Falling back to defaults.");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        _ = TrySave(settings);
    }

    public static bool TrySave(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            string json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(SettingsPath, json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to persist app settings (best effort).");
            return false;
        }
    }

    public static void SaveThemeVariant(string variant)
    {
        AppSettings settings = Load();
        settings.ThemeVariant = string.IsNullOrWhiteSpace(variant) ? "Dark" : variant;
        Save(settings);
    }

    public static (bool Top1000WithoutWhereEnabled, bool ProtectMutationWithoutWhereEnabled) LoadSqlEditorSafetySettings()
    {
        AppSettings settings = Load();
        return (
            settings.SqlEditorTop1000WithoutWhereEnabled,
            settings.SqlEditorProtectMutationWithoutWhereEnabled
        );
    }

    public static void SaveSqlEditorSafetySettings(bool top1000WithoutWhereEnabled, bool protectMutationWithoutWhereEnabled)
    {
        AppSettings settings = Load();
        settings.SqlEditorTop1000WithoutWhereEnabled = top1000WithoutWhereEnabled;
        settings.SqlEditorProtectMutationWithoutWhereEnabled = protectMutationWithoutWhereEnabled;
        Save(settings);
    }

    public static double LoadSqlEditorResultsSheetHeight(double fallback = 260)
    {
        AppSettings settings = Load();
        if (settings.SqlEditorResultsSheetHeight <= 0)
            return fallback;

        return settings.SqlEditorResultsSheetHeight;
    }

    public static void SaveSqlEditorResultsSheetHeight(double height)
    {
        if (height <= 0)
            return;

        AppSettings settings = Load();
        settings.SqlEditorResultsSheetHeight = height;
        Save(settings);
    }

    public static string LoadSqlEditorResultFilter(string tabKey)
    {
        if (string.IsNullOrWhiteSpace(tabKey))
            return string.Empty;

        AppSettings settings = Load();
        return settings.SqlEditorResultFiltersByTab.TryGetValue(tabKey, out string? filter)
            ? filter ?? string.Empty
            : string.Empty;
    }

    public static void SaveSqlEditorResultFilter(string tabKey, string filter)
    {
        if (string.IsNullOrWhiteSpace(tabKey))
            return;

        AppSettings settings = Load();
        settings.SqlEditorResultFiltersByTab ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.SqlEditorResultFiltersByTab[tabKey] = filter ?? string.Empty;
        Save(settings);
    }

    public static IReadOnlyList<SqlEditorSessionDraftEntry> LoadSqlEditorSessionDrafts()
    {
        AppSettings settings = Load();
        return settings.SqlEditorSessionDrafts ?? [];
    }

    public static void SaveSqlEditorSessionDrafts(IReadOnlyList<SqlEditorSessionDraftEntry> drafts)
    {
        AppSettings settings = Load();
        settings.SqlEditorSessionDrafts = drafts?.ToList() ?? [];
        Save(settings);
    }

    public static void ClearSqlEditorSessionDrafts()
    {
        AppSettings settings = Load();
        settings.SqlEditorSessionDrafts = [];
        Save(settings);
    }

    public static IReadOnlyDictionary<string, int> LoadSqlEditorCompletionFrequency(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        AppSettings settings = Load();
        if (!settings.SqlEditorCompletionFrequencyByProfile.TryGetValue(profileId, out Dictionary<string, int>? values)
            || values is null)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, int>(values, StringComparer.OrdinalIgnoreCase);
    }

    public static void SaveSqlEditorCompletionFrequency(string profileId, IReadOnlyDictionary<string, int> frequencies)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        AppSettings settings = Load();
        settings.SqlEditorCompletionFrequencyByProfile ??= new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        settings.SqlEditorCompletionFrequencyByProfile[profileId] = frequencies?
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Save(settings);
    }

    public static IReadOnlyList<SqlEditorHistoryEntry> LoadSqlEditorExecutionHistory(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return [];

        AppSettings settings = Load();
        if (!settings.SqlEditorHistoryByProfile.TryGetValue(profileId, out List<SqlEditorHistoryEntry>? entries)
            || entries is null)
        {
            return [];
        }

        return entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Sql))
            .OrderByDescending(static entry => entry.ExecutedAt)
            .ToList();
    }

    public static void SaveSqlEditorExecutionHistory(string profileId, IReadOnlyList<SqlEditorHistoryEntry> historyEntries)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        AppSettings settings = Load();
        settings.SqlEditorHistoryByProfile ??= new Dictionary<string, List<SqlEditorHistoryEntry>>(StringComparer.OrdinalIgnoreCase);
        settings.SqlEditorHistoryByProfile[profileId] = historyEntries?
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Sql))
            .OrderByDescending(static entry => entry.ExecutedAt)
            .Take(500)
            .ToList()
            ?? [];
        Save(settings);
    }

    public static void ClearSqlEditorExecutionHistory(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        AppSettings settings = Load();
        if (settings.SqlEditorHistoryByProfile.Remove(profileId))
            Save(settings);
    }

    public static ProjectConventionSettings LoadProjectConventionSettings()
    {
        AppSettings settings = Load();
        ProjectConventionSettings source = settings.ProjectConventions ?? new ProjectConventionSettings();
        return new ProjectConventionSettings
        {
            NamingConvention = NormalizeNamingConvention(source.NamingConvention),
            EnforceAliasNaming = source.EnforceAliasNaming,
            WarnOnReservedKeywords = source.WarnOnReservedKeywords,
            MaxAliasLength = Math.Max(0, source.MaxAliasLength),
            DefaultWireCurveMode = NormalizeWireCurveMode(source.DefaultWireCurveMode),
        };
    }

    public static void SaveProjectConventionSettings(ProjectConventionSettings projectSettings)
    {
        ArgumentNullException.ThrowIfNull(projectSettings);
        AppSettings settings = Load();
        settings.ProjectConventions = new ProjectConventionSettings
        {
            NamingConvention = NormalizeNamingConvention(projectSettings.NamingConvention),
            EnforceAliasNaming = projectSettings.EnforceAliasNaming,
            WarnOnReservedKeywords = projectSettings.WarnOnReservedKeywords,
            MaxAliasLength = Math.Max(0, projectSettings.MaxAliasLength),
            DefaultWireCurveMode = NormalizeWireCurveMode(projectSettings.DefaultWireCurveMode),
        };
        Save(settings);
    }

    private static string NormalizeNamingConvention(string? value)
    {
        return value switch
        {
            "snake_case" => "snake_case",
            "camelCase" => "camelCase",
            "PascalCase" => "PascalCase",
            "SCREAMING_SNAKE_CASE" => "SCREAMING_SNAKE_CASE",
            _ => "snake_case",
        };
    }

    private static string NormalizeWireCurveMode(string? value)
    {
        return value switch
        {
            "Bezier" => "Bezier",
            "Straight" => "Straight",
            "Orthogonal" => "Orthogonal",
            _ => "Bezier",
        };
    }
}
