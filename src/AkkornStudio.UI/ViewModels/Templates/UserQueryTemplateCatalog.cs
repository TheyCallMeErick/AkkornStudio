using System.Text.Json;
using System.Text.Json.Serialization;
using AkkornStudio.UI.Serialization;

namespace AkkornStudio.UI.ViewModels;

public sealed record UserQueryTemplate(
    string Id,
    string Name,
    string Description,
    string Category,
    string Tags,
    string CanvasJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public interface IUserQueryTemplateStore
{
    IReadOnlyList<UserQueryTemplate> Load();

    void Save(UserQueryTemplate template);

    bool Delete(string id);
}

public sealed class FileUserQueryTemplateStore(string? storeFilePath = null) : IUserQueryTemplateStore
{
    private static readonly JsonSerializerOptions Options = new()
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
            return Path.Combine(dir, "user_query_templates.json");
        }
    }

    public IReadOnlyList<UserQueryTemplate> Load()
    {
        string path = StoreFilePath;
        if (!File.Exists(path))
            return [];

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<UserQueryTemplate>>(json, Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(UserQueryTemplate template)
    {
        List<UserQueryTemplate> templates = Load().ToList();
        int index = templates.FindIndex(t =>
            string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
            templates[index] = template;
        else
            templates.Insert(0, template);

        Directory.CreateDirectory(Path.GetDirectoryName(StoreFilePath)!);
        File.WriteAllText(StoreFilePath, JsonSerializer.Serialize(templates, Options));
    }

    public bool Delete(string id)
    {
        List<UserQueryTemplate> templates = Load().ToList();
        int removed = templates.RemoveAll(t =>
            string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(StoreFilePath)!);
        File.WriteAllText(StoreFilePath, JsonSerializer.Serialize(templates, Options));
        return true;
    }
}

public static class QueryTemplateCatalog
{
    public const string UserTemplateCategory = "User";

    public static IReadOnlyList<QueryTemplate> LoadAll(IUserQueryTemplateStore? store = null)
    {
        IReadOnlyList<UserQueryTemplate> userTemplates = (store ?? new FileUserQueryTemplateStore()).Load();
        return [.. QueryTemplateLibrary.All, .. userTemplates.Select(ToQueryTemplate)];
    }

    public static QueryTemplate? Find(string idOrName, IUserQueryTemplateStore? store = null) =>
        LoadAll(store).FirstOrDefault(template =>
            string.Equals(template.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(template.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    public static UserQueryTemplate CreateFromCanvas(
        CanvasViewModel canvas,
        string name,
        string? description = null,
        string? category = null,
        string? tags = null,
        DateTimeOffset? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Template name is required.", nameof(name));

        DateTimeOffset timestamp = nowUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        string normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? $"User-created template captured from {canvas.Nodes.Count} node(s)."
            : description.Trim();

        return new UserQueryTemplate(
            Id: Guid.NewGuid().ToString("N"),
            Name: name.Trim(),
            Description: normalizedDescription,
            Category: string.IsNullOrWhiteSpace(category) ? UserTemplateCategory : category.Trim(),
            Tags: string.IsNullOrWhiteSpace(tags) ? "user custom saved canvas" : tags.Trim(),
            CanvasJson: canvas.SerializeForPersistence(),
            CreatedAtUtc: timestamp,
            UpdatedAtUtc: timestamp);
    }

    public static QueryTemplate ToQueryTemplate(UserQueryTemplate template) =>
        new(
            Name: template.Name,
            Description: template.Description,
            Category: string.IsNullOrWhiteSpace(template.Category) ? UserTemplateCategory : template.Category,
            Tags: template.Tags,
            Build: canvas =>
            {
                CanvasLoadResult result = CanvasSerializer.Deserialize(template.CanvasJson, canvas);
                if (!result.Success)
                    throw new InvalidOperationException(result.Error ?? "Could not load user template canvas.");
            },
            IsUserCreated: true,
            Id: template.Id);

    public static UserQueryTemplate SaveFromCanvas(
        CanvasViewModel canvas,
        string name,
        IUserQueryTemplateStore? store = null,
        string? description = null,
        string? category = null,
        string? tags = null)
    {
        UserQueryTemplate template = CreateFromCanvas(canvas, name, description, category, tags);
        (store ?? new FileUserQueryTemplateStore()).Save(template);
        return template;
    }
}
