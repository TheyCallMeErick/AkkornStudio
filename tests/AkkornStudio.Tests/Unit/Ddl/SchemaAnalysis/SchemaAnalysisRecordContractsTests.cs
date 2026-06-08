using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;

namespace AkkornStudio.Tests.Unit.Ddl.SchemaAnalysis;

public sealed class SchemaAnalysisRecordContractsTests
{
    [Fact]
    public void Records_CanBeInstantiated_WithMinimalValidValues()
    {
        SchemaEvidence evidence = new(
            EvidenceKind.MetadataFact,
            "key",
            "value",
            1.0,
            SourcePath: null
        );

        SqlFixCandidate candidate = new(
            "cand-1",
            DatabaseProvider.Postgres,
            "Candidate",
            "SELECT 1;",
            ["SELECT 1"],
            SqlCandidateSafety.NonDestructive,
            CandidateVisibility.VisibleActionable,
            IsAutoApplicable: false,
            Notes: ["note"]
        );

        SchemaSuggestion suggestion = new("sug-1", "Suggestion", "Description", 0.9, [candidate]);

        SchemaIssue issue = new(
            "iss-1",
            SchemaRuleCode.MISSING_FK,
            SchemaIssueSeverity.Warning,
            0.85,
            SchemaTargetType.Column,
            "public",
            "orders",
            "customer_id",
            ConstraintName: null,
            "Missing FK",
            "FK inferred",
            [evidence],
            [suggestion],
            IsAmbiguous: false
        );

        SchemaRuleExecutionDiagnostic diagnostic = new(
            "ANL-CACHE-BYPASSED",
            "Cache bypassed",
            RuleCode: null,
            RuleExecutionState.Completed,
            IsFatal: false
        );

        SchemaAnalysisSummary summary = new(
            TotalIssues: 1,
            InfoCount: 0,
            WarningCount: 1,
            CriticalCount: 0,
            QuickWinCount: 0,
            OverallScore: 0d,
            PerRuleCount: new Dictionary<SchemaRuleCode, int> { [SchemaRuleCode.MISSING_FK] = 1 },
            PerTableCount: new Dictionary<string, int> { ["public.orders"] = 1 },
            AreaScores: new Dictionary<string, double>(),
            ObservedPatterns: new SchemaObservedPatterns(NamingConvention.MixedAllowed, null, null)
        );

        SchemaAnalysisPartialState partialState = new(
            IsPartial: false,
            ReasonCode: "NONE",
            CompletedRules: 8,
            TotalRules: 8
        );

        SchemaRuleSetting ruleSetting = new(Enabled: true, MinConfidence: 0.65, MaxIssues: 1000);

        SchemaAnalysisProfile profile = new(
            Version: 1,
            Enabled: true,
            MinConfidenceGlobal: 0.55,
            TimeoutMs: 15000,
            AllowPartialOnTimeout: true,
            AllowPartialOnRuleFailure: true,
            EnableParallelRules: true,
            MaxDegreeOfParallelism: 4,
            MaxIssues: 5000,
            MaxSuggestionsPerIssue: 3,
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
                [SchemaRuleCode.MISSING_FK] = ruleSetting,
            },
            CacheTtlSeconds: 300
        );

        SchemaAnalysisResult result = new(
            "analysis-1",
            SchemaAnalysisStatus.Completed,
            DatabaseProvider.Postgres,
            "db",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DurationMs: 10,
            MetadataFingerprint: "fingerprint",
            ProfileContentHash: "profile",
            ProfileVersion: profile.Version,
            PartialState: partialState,
            Issues: [issue],
            Diagnostics: [diagnostic],
            Summary: summary
        );

        Assert.NotNull(evidence);
        Assert.NotNull(candidate);
        Assert.NotNull(suggestion);
        Assert.NotNull(issue);
        Assert.NotNull(diagnostic);
        Assert.NotNull(summary);
        Assert.NotNull(partialState);
        Assert.NotNull(profile);
        Assert.NotNull(result);
    }

    [Fact]
    public void Records_PreserveStructuralEquality_AndWithSemantics()
    {
        SchemaRuleSetting original = new(Enabled: true, MinConfidence: 0.65, MaxIssues: 1000);
        SchemaRuleSetting same = new(Enabled: true, MinConfidence: 0.65, MaxIssues: 1000);
        SchemaRuleSetting changed = original with { MaxIssues = 50 };

        Assert.Equal(original, same);
        Assert.NotEqual(original, changed);
        Assert.Equal(50, changed.MaxIssues);
        Assert.Equal(1000, original.MaxIssues);
    }
}
