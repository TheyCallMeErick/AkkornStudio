using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkkornStudio.UI.Services.SqlEditor.Results;

public sealed record SqlSavedQuerySnippet(
    string Id,
    string Name,
    string Description,
    string Tags,
    string SqlText,
    string? ConnectionId,
    string? DatabaseName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsFavorite);

public interface ISqlResultSnippetStore
{
    IReadOnlyList<SqlSavedQuerySnippet> Load();

    void Upsert(SqlSavedQuerySnippet snippet);

    bool Delete(string snippetId);
}

public sealed class FileSqlResultSnippetStore(string? storeFilePath = null) : ISqlResultSnippetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string StoreFilePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(storeFilePath))
                return storeFilePath;

            string dir = global::AkkornStudio.UI.AppConstants.AppDataDirectory;
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "sql-result-snippets.json");
        }
    }

    public IReadOnlyList<SqlSavedQuerySnippet> Load()
    {
        string path = StoreFilePath;
        if (!File.Exists(path))
            return [];

        try
        {
            string json = File.ReadAllText(path);
            List<SqlSavedQuerySnippet>? snippets = JsonSerializer.Deserialize<List<SqlSavedQuerySnippet>>(json, JsonOptions);
            return (snippets ?? [])
                .Where(snippet => !string.IsNullOrWhiteSpace(snippet.SqlText))
                .OrderByDescending(snippet => snippet.UpdatedAtUtc)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public void Upsert(SqlSavedQuerySnippet snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);

        List<SqlSavedQuerySnippet> snippets = Load().ToList();
        int index = snippets.FindIndex(existing =>
            string.Equals(existing.Id, snippet.Id, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
            snippets[index] = snippet;
        else
            snippets.Insert(0, snippet);

        Persist(snippets);
    }

    public bool Delete(string snippetId)
    {
        if (string.IsNullOrWhiteSpace(snippetId))
            return false;

        List<SqlSavedQuerySnippet> snippets = Load().ToList();
        int removed = snippets.RemoveAll(existing =>
            string.Equals(existing.Id, snippetId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return false;

        Persist(snippets);
        return true;
    }

    private void Persist(IReadOnlyList<SqlSavedQuerySnippet> snippets)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoreFilePath)!);
            string json = JsonSerializer.Serialize(snippets, JsonOptions);
            File.WriteAllText(StoreFilePath, json);
        }
        catch
        {
            // Best effort persistence.
        }
    }
}
