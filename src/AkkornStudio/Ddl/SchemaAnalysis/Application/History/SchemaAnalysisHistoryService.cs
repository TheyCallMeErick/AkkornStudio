using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;

namespace AkkornStudio.Ddl.SchemaAnalysis.Application.History;

public sealed class SchemaAnalysisHistoryService
{
    public const int DefaultMaxEntriesPerProject = 20;

    private readonly Dictionary<string, List<SchemaAnalysisHistoryEntry>> _entriesByProject =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _baselineEntryIdByProject =
        new(StringComparer.OrdinalIgnoreCase);

    public SchemaAnalysisHistorySnapshot AddResult(
        string projectKey,
        SchemaAnalysisResult result,
        int maxEntriesPerProject = DefaultMaxEntriesPerProject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentNullException.ThrowIfNull(result);

        int effectiveLimit = Math.Max(1, maxEntriesPerProject);
        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        SchemaAnalysisHistoryEntry entry = CreateEntry(normalizedProjectKey, result);

        if (!_entriesByProject.TryGetValue(normalizedProjectKey, out List<SchemaAnalysisHistoryEntry>? entries))
        {
            entries = [];
            _entriesByProject[normalizedProjectKey] = entries;
        }

        entries.RemoveAll(existing => string.Equals(existing.AnalysisId, result.AnalysisId, StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, entry);
        if (entries.Count > effectiveLimit)
            entries.RemoveRange(effectiveLimit, entries.Count - effectiveLimit);

        if (!_baselineEntryIdByProject.ContainsKey(normalizedProjectKey))
            _baselineEntryIdByProject[normalizedProjectKey] = entry.EntryId;

        if (_baselineEntryIdByProject.TryGetValue(normalizedProjectKey, out string? baselineId)
            && entries.All(existing => !string.Equals(existing.EntryId, baselineId, StringComparison.OrdinalIgnoreCase)))
        {
            _baselineEntryIdByProject[normalizedProjectKey] = entries[^1].EntryId;
        }

        return GetSnapshot(normalizedProjectKey);
    }

    public SchemaAnalysisHistorySnapshot SetBaseline(string projectKey, string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        if (!_entriesByProject.TryGetValue(normalizedProjectKey, out List<SchemaAnalysisHistoryEntry>? entries)
            || entries.All(entry => !string.Equals(entry.EntryId, entryId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"History entry '{entryId}' was not found for project '{normalizedProjectKey}'.");
        }

        _baselineEntryIdByProject[normalizedProjectKey] = entryId;
        return GetSnapshot(normalizedProjectKey);
    }

    public SchemaAnalysisHistorySnapshot GetSnapshot(string projectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);

        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        IReadOnlyList<SchemaAnalysisHistoryEntry> entries = _entriesByProject.TryGetValue(normalizedProjectKey, out List<SchemaAnalysisHistoryEntry>? existing)
            ? existing.ToList()
            : [];

        SchemaAnalysisHistoryEntry? baseline = null;
        if (_baselineEntryIdByProject.TryGetValue(normalizedProjectKey, out string? baselineId))
            baseline = entries.FirstOrDefault(entry => string.Equals(entry.EntryId, baselineId, StringComparison.OrdinalIgnoreCase));

        SchemaAnalysisHistoryEntry? latest = entries.FirstOrDefault();
        return new SchemaAnalysisHistorySnapshot(
            normalizedProjectKey,
            entries,
            baseline,
            latest,
            BuildDelta(baseline, latest));
    }

    public void ClearProject(string projectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);

        string normalizedProjectKey = NormalizeProjectKey(projectKey);
        _entriesByProject.Remove(normalizedProjectKey);
        _baselineEntryIdByProject.Remove(normalizedProjectKey);
    }

    private static SchemaAnalysisHistoryEntry CreateEntry(string projectKey, SchemaAnalysisResult result)
    {
        string entryId = string.Join(
            ":",
            projectKey,
            result.AnalysisId,
            result.CompletedAtUtc.UtcTicks);

        return new SchemaAnalysisHistoryEntry(
            EntryId: entryId,
            ProjectKey: projectKey,
            AnalysisId: result.AnalysisId,
            Status: result.Status,
            Provider: result.Provider,
            DatabaseName: result.DatabaseName,
            CompletedAtUtc: result.CompletedAtUtc,
            MetadataFingerprint: result.MetadataFingerprint,
            ProfileContentHash: result.ProfileContentHash,
            TotalIssues: result.Summary.TotalIssues,
            WarningCount: result.Summary.WarningCount,
            CriticalCount: result.Summary.CriticalCount,
            QuickWinCount: result.Summary.QuickWinCount,
            OverallScore: result.Summary.OverallScore);
    }

    private static SchemaAnalysisHistoryDelta BuildDelta(
        SchemaAnalysisHistoryEntry? baseline,
        SchemaAnalysisHistoryEntry? latest)
    {
        if (baseline is null || latest is null)
            return new SchemaAnalysisHistoryDelta(false, 0, 0, 0, 0, 0);

        return new SchemaAnalysisHistoryDelta(
            true,
            latest.TotalIssues - baseline.TotalIssues,
            latest.WarningCount - baseline.WarningCount,
            latest.CriticalCount - baseline.CriticalCount,
            latest.QuickWinCount - baseline.QuickWinCount,
            Math.Round(latest.OverallScore - baseline.OverallScore, 4, MidpointRounding.ToEven));
    }

    private static string NormalizeProjectKey(string projectKey) =>
        string.Join(" ", projectKey.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
