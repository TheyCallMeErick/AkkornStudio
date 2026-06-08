using AkkornStudio.Core;

namespace AkkornStudio.UI.Services.SqlEditor;

public sealed record SqlEditorSessionDraftEntry
{
    public required string TabId { get; init; }
    public required string FallbackTitle { get; init; }
    public string SqlText { get; init; } = string.Empty;
    public string? FilePath { get; init; }
    public DatabaseProvider Provider { get; init; }
    public string? ConnectionProfileId { get; init; }
    public IReadOnlyDictionary<string, string>? QueryParameterLastValues { get; init; }
    public int TabOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset SavedAtUtc { get; init; }
}
