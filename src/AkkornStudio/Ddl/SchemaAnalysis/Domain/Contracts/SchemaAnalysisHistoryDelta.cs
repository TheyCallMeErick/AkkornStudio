namespace AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;

public sealed record SchemaAnalysisHistoryDelta(
    bool HasBaseline,
    int TotalIssuesDelta,
    int WarningCountDelta,
    int CriticalCountDelta,
    int QuickWinCountDelta,
    double OverallScoreDelta
);
