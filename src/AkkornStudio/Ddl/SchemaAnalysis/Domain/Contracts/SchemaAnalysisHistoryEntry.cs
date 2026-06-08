using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;

namespace AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;

public sealed record SchemaAnalysisHistoryEntry(
    string EntryId,
    string ProjectKey,
    string AnalysisId,
    SchemaAnalysisStatus Status,
    DatabaseProvider Provider,
    string DatabaseName,
    DateTimeOffset CompletedAtUtc,
    string MetadataFingerprint,
    string ProfileContentHash,
    int TotalIssues,
    int WarningCount,
    int CriticalCount,
    int QuickWinCount,
    double OverallScore
);
