using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;

namespace AkkornStudio.Tests.Unit.Ddl.SchemaAnalysis;

internal static class SchemaAnalysisContractValidatorTestData
{
    public static SchemaAnalysisResult CreateValidResult(SchemaIssue issue) => CreateValidResult([issue]);

    public static SchemaAnalysisResult CreateValidResult(IReadOnlyList<SchemaIssue> issues)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new SchemaAnalysisResult(
            AnalysisId: "analysis-1",
            Status: SchemaAnalysisStatus.Completed,
            Provider: DatabaseProvider.Postgres,
            DatabaseName: "db",
            StartedAtUtc: now,
            CompletedAtUtc: now,
            DurationMs: 1,
            MetadataFingerprint: "fingerprint",
            ProfileContentHash: "profile",
            ProfileVersion: 1,
            PartialState: new SchemaAnalysisPartialState(false, "NONE", 8, 8),
            Issues: issues,
            Diagnostics: [],
            Summary: new SchemaAnalysisSummary(
                TotalIssues: issues.Count,
                InfoCount: 0,
                WarningCount: issues.Count,
                CriticalCount: 0,
                QuickWinCount: 0,
                OverallScore: 0d,
                PerRuleCount: new Dictionary<SchemaRuleCode, int> { [SchemaRuleCode.MISSING_FK] = issues.Count },
                PerTableCount: new Dictionary<string, int> { ["public.orders"] = issues.Count },
                AreaScores: new Dictionary<string, double>(),
                ObservedPatterns: new SchemaObservedPatterns(NamingConvention.MixedAllowed, null, null)
            )
        );
    }

    public static SchemaAnalysisProfile CreateProfile(int maxIssues = 5000, int maxSuggestionsPerIssue = 3) =>
        new(
            Version: 1,
            Enabled: true,
            MinConfidenceGlobal: 0.55,
            TimeoutMs: 15000,
            AllowPartialOnTimeout: true,
            AllowPartialOnRuleFailure: true,
            EnableParallelRules: true,
            MaxDegreeOfParallelism: 4,
            MaxIssues: maxIssues,
            MaxSuggestionsPerIssue: maxSuggestionsPerIssue,
            NamingConvention: NamingConvention.SnakeCase,
            NormalizationStrictness: NormalizationStrictness.Balanced,
            RequiredCommentTargets: ["Table"],
            LowQualityNameDenylist: ["tmp"],
            NameAllowlist: [],
            SynonymGroups: [new List<string> { "person", "pessoa" }],
            SemiStructuredPayloadAllowlist: [],
            DebugDiagnostics: false,
            RuleSettings: new Dictionary<SchemaRuleCode, SchemaRuleSetting>
            {
                [SchemaRuleCode.MISSING_FK] = new SchemaRuleSetting(true, 0.65, 1000),
            },
            CacheTtlSeconds: 300
        );

    public static SchemaIssue CreateIssue(
        string issueId = "issue-1",
        double confidence = 0.8500,
        SchemaTargetType targetType = SchemaTargetType.Column,
        string? tableName = "orders",
        string? columnName = "customer_id",
        string? constraintName = null,
        IReadOnlyList<SchemaEvidence>? evidence = null,
        IReadOnlyList<SchemaSuggestion>? suggestions = null
    ) =>
        new(
            IssueId: issueId,
            RuleCode: SchemaRuleCode.MISSING_FK,
            Severity: SchemaIssueSeverity.Warning,
            Confidence: confidence,
            TargetType: targetType,
            SchemaName: "public",
            TableName: tableName,
            ColumnName: columnName,
            ConstraintName: constraintName,
            Title: "Missing FK",
            Message: "FK inferred",
            Evidence: evidence ?? [SchemaEvidenceFactory.MetadataFact("key", "value", 1.0)],
            Suggestions: suggestions ?? [CreateSuggestion("s-1")],
            IsAmbiguous: false
        );

    public static SchemaSuggestion CreateSuggestion(string suggestionId, IReadOnlyList<SqlFixCandidate>? candidates = null) =>
        new(
            SuggestionId: suggestionId,
            Title: "Suggestion",
            Description: "Description",
            Confidence: 0.9000,
            SqlCandidates: candidates ?? [CreateCandidate()]
        );

    public static SqlFixCandidate CreateCandidate(
        SqlCandidateSafety safety = SqlCandidateSafety.NonDestructive,
        CandidateVisibility visibility = CandidateVisibility.VisibleActionable,
        bool isAutoApplicable = false,
        IReadOnlyList<string>? preconditionsSql = null
    ) =>
        new(
            CandidateId: "cand-1",
            Provider: DatabaseProvider.Postgres,
            Title: "Candidate",
            Sql: "SELECT 1;",
            PreconditionsSql: preconditionsSql ?? ["SELECT 1"],
            Safety: safety,
            Visibility: visibility,
            IsAutoApplicable: isAutoApplicable,
            Notes: ["note"]
        );
}
