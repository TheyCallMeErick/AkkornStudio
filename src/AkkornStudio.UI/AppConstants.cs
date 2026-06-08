namespace AkkornStudio.UI;

/// <summary>
/// Application-wide constants and default values.
/// Centralises configuration that was previously spread across multiple view models and services.
/// </summary>
public static class AppConstants
{
    private static readonly Lazy<string> AppDataDirectoryLazy = new(ResolveAppDataDirectory, LazyThreadSafetyMode.ExecutionAndPublication);

    // ── App ──────────────────────────────────────────────────────────────────

    /// <summary>Internal app name used in storage paths.</summary>
    public const string AppName = "AkkornStudio";

    /// <summary>Display name shown in window titles and UI labels.</summary>
    public const string AppDisplayName = "AkkornStudio";

    /// <summary>Base directory under AppData for local app persistence.</summary>
    public static string AppDataDirectory => AppDataDirectoryLazy.Value;

    /// <summary>Application version embedded in saved canvas files.</summary>
    public const string AppVersion = "1.0.0";

    // ── Connection defaults ───────────────────────────────────────────────────

    /// <summary>Default hostname shown in new and reset connection profiles.</summary>
    public const string DefaultHost = "localhost";

    /// <summary>Seconds between background connection health-check pings.</summary>
    public const int HealthCheckIntervalSeconds = 60;

    // ── SQL import ────────────────────────────────────────────────────────────

    /// <summary>Maximum characters accepted in the SQL import text box.</summary>
    public const int DefaultMaxSqlInputLength = 50_000;

    /// <summary>Default timeout for a single SQL import operation.</summary>
    public static readonly TimeSpan DefaultImportTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Milliseconds to yield to the UI thread before starting heavy SQL import work,
    /// allowing the "Importing…" indicator to render before the CPU-bound parse begins.
    /// </summary>
    public const int DefaultImportStartDelayMs = 80;

    // ── UI timing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Milliseconds to wait after the last query-graph change before triggering a
    /// live SQL preview refresh (debounce window).
    /// </summary>
    public const int PreviewDebounceMs = 500;

    /// <summary>Milliseconds debounce before re-running canvas validation.</summary>
    public const int ValidationDebounceMs = 200;

    private static string ResolveAppDataDirectory()
    {
        if (IsRunningUnderTestHost())
        {
            string testPath = Path.Combine(Path.GetTempPath(), AppName, "testhost");
            if (TryEnsureWritableDirectory(testPath, out string writableTestPath))
                return writableTestPath;
        }

        string? overridePath = Environment.GetEnvironmentVariable("AKKORN_APPDATA_DIR");
        if (TryEnsureWritableDirectory(overridePath, out string writableOverride))
            return writableOverride;

        string local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppName);
        if (TryEnsureWritableDirectory(local, out string writableLocal))
            return writableLocal;

        string roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppName);
        if (TryEnsureWritableDirectory(roaming, out string writableRoaming))
            return writableRoaming;

        string temp = Path.Combine(Path.GetTempPath(), AppName);
        if (TryEnsureWritableDirectory(temp, out string writableTemp))
            return writableTemp;

        string localFallback = Path.Combine(AppContext.BaseDirectory, ".appdata");
        _ = TryEnsureWritableDirectory(localFallback, out string writableLocalFallback);
        return writableLocalFallback;
    }

    private static bool IsRunningUnderTestHost()
    {
        string processName = Environment.ProcessPath ?? string.Empty;
        if (processName.Contains("testhost", StringComparison.OrdinalIgnoreCase))
            return true;

        string friendlyName = AppDomain.CurrentDomain.FriendlyName;
        return friendlyName.Contains("testhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryEnsureWritableDirectory(string? path, out string writablePath)
    {
        writablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            Directory.CreateDirectory(path);
            string probeFile = Path.Combine(path, $".writable-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, "ok");
            File.Delete(probeFile);
            writablePath = path;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
