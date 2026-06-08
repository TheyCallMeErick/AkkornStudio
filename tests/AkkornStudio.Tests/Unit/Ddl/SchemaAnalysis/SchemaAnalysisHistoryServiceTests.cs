using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Application.History;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;

namespace AkkornStudio.Tests.Unit.Ddl.SchemaAnalysis;

public sealed class SchemaAnalysisHistoryServiceTests
{
    [Fact]
    public void AddResult_CreatesProjectHistoryAndInitialBaseline()
    {
        SchemaAnalysisHistoryService service = new();

        SchemaAnalysisHistorySnapshot snapshot = service.AddResult("project-a", CreateResult("a1", totalIssues: 3, warnings: 2, critical: 1, score: 91));

        SchemaAnalysisHistoryEntry entry = Assert.Single(snapshot.Entries);
        Assert.Equal("project-a", snapshot.ProjectKey);
        Assert.Equal(entry, snapshot.Latest);
        Assert.Equal(entry, snapshot.Baseline);
        Assert.True(snapshot.DeltaFromBaseline.HasBaseline);
        Assert.Equal(0, snapshot.DeltaFromBaseline.TotalIssuesDelta);
    }

    [Fact]
    public void AddResult_ComputesLatestDeltaFromBaseline()
    {
        SchemaAnalysisHistoryService service = new();

        service.AddResult("project-a", CreateResult("baseline", totalIssues: 2, warnings: 1, critical: 0, quickWins: 1, score: 96));
        SchemaAnalysisHistorySnapshot snapshot = service.AddResult("project-a", CreateResult("latest", totalIssues: 5, warnings: 3, critical: 1, quickWins: 4, score: 88.5));

        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Equal("latest", snapshot.Latest!.AnalysisId);
        Assert.Equal("baseline", snapshot.Baseline!.AnalysisId);
        Assert.Equal(3, snapshot.DeltaFromBaseline.TotalIssuesDelta);
        Assert.Equal(2, snapshot.DeltaFromBaseline.WarningCountDelta);
        Assert.Equal(1, snapshot.DeltaFromBaseline.CriticalCountDelta);
        Assert.Equal(3, snapshot.DeltaFromBaseline.QuickWinCountDelta);
        Assert.Equal(-7.5, snapshot.DeltaFromBaseline.OverallScoreDelta);
    }

    [Fact]
    public void SetBaseline_UsesSelectedEntryForFutureDeltas()
    {
        SchemaAnalysisHistoryService service = new();

        service.AddResult("project-a", CreateResult("a1", totalIssues: 10, warnings: 8, critical: 2, score: 70));
        SchemaAnalysisHistorySnapshot afterSecond = service.AddResult("project-a", CreateResult("a2", totalIssues: 7, warnings: 6, critical: 1, score: 80));
        SchemaAnalysisHistoryEntry newBaseline = afterSecond.Latest!;

        SchemaAnalysisHistorySnapshot snapshot = service.SetBaseline("project-a", newBaseline.EntryId);

        Assert.Equal("a2", snapshot.Baseline!.AnalysisId);
        Assert.Equal(0, snapshot.DeltaFromBaseline.TotalIssuesDelta);
        Assert.Equal(0, snapshot.DeltaFromBaseline.OverallScoreDelta);
    }

    [Fact]
    public void AddResult_TrimsOldEntriesAndKeepsBaselineAvailable()
    {
        SchemaAnalysisHistoryService service = new();

        service.AddResult("project-a", CreateResult("a1", totalIssues: 1), maxEntriesPerProject: 2);
        service.AddResult("project-a", CreateResult("a2", totalIssues: 2), maxEntriesPerProject: 2);
        SchemaAnalysisHistorySnapshot snapshot = service.AddResult("project-a", CreateResult("a3", totalIssues: 3), maxEntriesPerProject: 2);

        Assert.Equal(["a3", "a2"], snapshot.Entries.Select(entry => entry.AnalysisId));
        Assert.NotNull(snapshot.Baseline);
        Assert.Contains(snapshot.Entries, entry => entry.EntryId == snapshot.Baseline!.EntryId);
    }

    [Fact]
    public void HistoriesAreScopedByProjectKey()
    {
        SchemaAnalysisHistoryService service = new();

        service.AddResult("project-a", CreateResult("a1", totalIssues: 1));
        service.AddResult("project-b", CreateResult("b1", totalIssues: 9));

        Assert.Equal("a1", service.GetSnapshot("project-a").Latest!.AnalysisId);
        Assert.Equal("b1", service.GetSnapshot("project-b").Latest!.AnalysisId);
    }

    private static SchemaAnalysisResult CreateResult(
        string analysisId,
        int totalIssues,
        int warnings = 0,
        int critical = 0,
        int quickWins = 0,
        double score = 100)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        return new SchemaAnalysisResult(
            AnalysisId: analysisId,
            Status: SchemaAnalysisStatus.Completed,
            Provider: DatabaseProvider.Postgres,
            DatabaseName: "db",
            StartedAtUtc: completedAt.AddMilliseconds(-10),
            CompletedAtUtc: completedAt,
            DurationMs: 10,
            MetadataFingerprint: $"metadata-{analysisId}",
            ProfileContentHash: "profile",
            ProfileVersion: 1,
            PartialState: new SchemaAnalysisPartialState(false, "NONE", 8, 8),
            Issues: [],
            Diagnostics: [],
            Summary: new SchemaAnalysisSummary(
                TotalIssues: totalIssues,
                InfoCount: Math.Max(0, totalIssues - warnings - critical),
                WarningCount: warnings,
                CriticalCount: critical,
                QuickWinCount: quickWins,
                OverallScore: score,
                PerRuleCount: new Dictionary<SchemaRuleCode, int>(),
                PerTableCount: new Dictionary<string, int>(),
                AreaScores: new Dictionary<string, double>(),
                ObservedPatterns: new SchemaObservedPatterns(NamingConvention.SnakeCase, null, null)));
    }
}
