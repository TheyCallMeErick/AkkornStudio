using System.Text.Json;

namespace AkkornStudio.UI.Services;

public sealed record RecentFileEntry(string FilePath, DateTime LastOpenedUtc);

/// <summary>
/// Persists a lightweight MRU list for Start Menu recent files.
/// </summary>
public static class RecentFilesStore
{
    private const int MaxEntries = 20;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    public static event Action<string>? WarningRaised;

    private static string AppDataDir =>
        global::AkkornStudio.UI.AppConstants.AppDataDirectory;

    private static string RecentFilePath => Path.Combine(AppDataDir, "recent-files.json");

    public static IReadOnlyList<RecentFileEntry> GetRecent(int max = 6)
    {
        try
        {
            if (!File.Exists(RecentFilePath))
                return [];

            var json = File.ReadAllText(RecentFilePath);
            var all = JsonSerializer.Deserialize<List<RecentFileEntry>>(json, JsonOpts) ?? [];

            return all
                .Where(x => !string.IsNullOrWhiteSpace(x.FilePath) && File.Exists(x.FilePath))
                .OrderByDescending(x => x.LastOpenedUtc)
                .Take(Math.Max(1, max))
                .ToList();
        }
        catch (JsonException ex)
        {
            RaiseWarning($"Could not parse recent files store '{RecentFilePath}': {ex.Message}");
            return [];
        }
        catch (IOException ex)
        {
            RaiseWarning($"Could not read recent files store '{RecentFilePath}': {ex.Message}");
            return [];
        }
        catch
        {
            RaiseWarning($"Could not load recent files store '{RecentFilePath}'.");
            return [];
        }
    }

    public static void Touch(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            Directory.CreateDirectory(AppDataDir);

            List<RecentFileEntry> items = GetRecent(MaxEntries).ToList();
            items.RemoveAll(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, new RecentFileEntry(filePath, DateTime.UtcNow));

            items = items
                .OrderByDescending(x => x.LastOpenedUtc)
                .Take(MaxEntries)
                .ToList();

            WriteRecentFileAtomically(JsonSerializer.Serialize(items, JsonOpts));
        }
        catch (IOException ex)
        {
            RaiseWarning($"Could not persist recent files store '{RecentFilePath}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            RaiseWarning($"Access denied while writing recent files store '{RecentFilePath}': {ex.Message}");
        }
        catch
        {
            RaiseWarning($"Could not persist recent files store '{RecentFilePath}'.");
        }
    }

    private static void WriteRecentFileAtomically(string json)
    {
        string tempPath = $"{RecentFilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, RecentFilePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup; root operation already succeeded/failed.
            }
        }
    }

    private static void RaiseWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        WarningRaised?.Invoke(message);
    }
}
