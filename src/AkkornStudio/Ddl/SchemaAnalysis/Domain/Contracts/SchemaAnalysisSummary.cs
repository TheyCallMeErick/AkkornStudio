using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;

namespace AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;

public sealed record SchemaAnalysisSummary(
    int TotalIssues,
    int InfoCount,
    int WarningCount,
    int CriticalCount,
    int QuickWinCount,
    double OverallScore,
    IReadOnlyDictionary<SchemaRuleCode, int> PerRuleCount,
    IReadOnlyDictionary<string, int> PerTableCount,
    IReadOnlyDictionary<string, double> AreaScores,
    SchemaObservedPatterns ObservedPatterns
);
