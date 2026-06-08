namespace AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;

public sealed record SchemaAnalysisHistorySnapshot(
    string ProjectKey,
    IReadOnlyList<SchemaAnalysisHistoryEntry> Entries,
    SchemaAnalysisHistoryEntry? Baseline,
    SchemaAnalysisHistoryEntry? Latest,
    SchemaAnalysisHistoryDelta DeltaFromBaseline
);
