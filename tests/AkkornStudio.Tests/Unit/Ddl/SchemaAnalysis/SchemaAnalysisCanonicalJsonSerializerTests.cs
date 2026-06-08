using System.Text.Json;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Ddl.SchemaAnalysis.Infrastructure.Serialization;

namespace AkkornStudio.Tests.Unit.Ddl.SchemaAnalysis;

public sealed class SchemaAnalysisCanonicalJsonSerializerTests
{
    [Fact]
    public void SerializeResultCanonical_IsDeterministic_ForSameLogicalInput()
    {
        SchemaAnalysisCanonicalJsonSerializer serializer = new();
        SchemaAnalysisResult result = CreateResult(
            CreateIssue(
                schemaName: " public ",
                tableName: " orders ",
                columnName: " customer_id ",
                suggestions: [CreateSuggestion("s-1")]
            )
        );

        string first = serializer.SerializeResultCanonical(result);
        string second = serializer.SerializeResultCanonical(result);

        Assert.Equal(first, second);
    }

    [Fact]
    public void SerializeProfileCanonical_SortsSetSemanticsCollections()
    {
        SchemaAnalysisCanonicalJsonSerializer serializer = new();
        SchemaAnalysisProfile profile = CreateProfile(
            requiredCommentTargets: ["ForeignKeyColumn", "Table", "PrimaryKeyColumn", "Table"],
            denylist: ["z", "tmp", "a"],
            allowlist: ["valor", "abc", "valor"],
            allowSemiStructuredPayloadList: ["b.col", "a.col", "b.col"],
            synonymGroups:
            [
                new List<string> { "cliente", "customer" },
                new List<string> { "person", "pessoa", "person" },
            ]
        );

        string json = serializer.SerializeProfileCanonical(profile);
        using JsonDocument document = JsonDocument.Parse(json);

        string[] requiredCommentTargets = document
            .RootElement.GetProperty("requiredCommentTargets")
            .EnumerateArray()
            .Select(x => x.GetString()!)
            .ToArray();
        string[] denylist = document
            .RootElement.GetProperty("lowQualityNameDenylist")
            .EnumerateArray()
            .Select(x => x.GetString()!)
            .ToArray();
        string[] allowlist = document
            .RootElement.GetProperty("nameAllowlist")
            .EnumerateArray()
            .Select(x => x.GetString()!)
            .ToArray();

        Assert.Equal(["ForeignKeyColumn", "PrimaryKeyColumn", "Table"], requiredCommentTargets);
        Assert.Equal(["a", "tmp", "z"], denylist);
        Assert.Equal(["abc", "valor"], allowlist);
    }

    [Fact]
    public void SerializeResultCanonical_UsesEnumStrings_AndNullForEmptyNames()
    {
        SchemaAnalysisCanonicalJsonSerializer serializer = new();
        SchemaAnalysisResult result = CreateResult(
            CreateIssue(
                targetType: SchemaTargetType.Constraint,
                schemaName: "   ",
                tableName: " ",
                columnName: "\t",
                constraintName: " fk_orders_customer "
            )
        );

        string json = serializer.SerializeResultCanonical(result);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement issue = root.GetProperty("issues")[0];

        Assert.Equal("Completed", root.GetProperty("status").GetString());
        Assert.Equal("Constraint", issue.GetProperty("targetType").GetString());
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("schemaName").ValueKind);
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("tableName").ValueKind);
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("columnName").ValueKind);
        Assert.Equal("fk_orders_customer", issue.GetProperty("constraintName").GetString());
    }

    private static SchemaAnalysisProfile CreateProfile(
        IReadOnlyList<string>? requiredCommentTargets = null,
        IReadOnlyList<string>? denylist = null,
        IReadOnlyList<string>? allowlist = null,
        IReadOnlyList<string>? allowSemiStructuredPayloadList = null,
        IReadOnlyList<IReadOnlyList<string>>? synonymGroups = null
    ) =>
        new(
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
            RequiredCommentTargets: requiredCommentTargets ?? ["Table", "PrimaryKeyColumn"],
            LowQualityNameDenylist: denylist ?? ["tmp"],
            NameAllowlist: allowlist ?? [],
            SynonymGroups: synonymGroups ?? [new List<string> { "person", "pessoa" }],
            SemiStructuredPayloadAllowlist: allowSemiStructuredPayloadList ?? [],
            DebugDiagnostics: false,
            RuleSettings: new Dictionary<SchemaRuleCode, SchemaRuleSetting>
            {
                [SchemaRuleCode.MISSING_FK] = new SchemaRuleSetting(true, 0.65, 1000),
                [SchemaRuleCode.NAMING_CONVENTION_VIOLATION] = new SchemaRuleSetting(true, 0.70, 1000),
            },
            CacheTtlSeconds: 300
        );

    private static SchemaAnalysisResult CreateResult(SchemaIssue issue)
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
            Issues: [issue],
            Diagnostics:
            [
                new SchemaRuleExecutionDiagnostic(
                    "ANL-METADATA-PARTIAL",
                    "Metadata parcial",
                    SchemaRuleCode.MISSING_FK,
                    RuleExecutionState.Completed,
                    IsFatal: false
                ),
            ],
            Summary: new SchemaAnalysisSummary(
                TotalIssues: 1,
                InfoCount: 0,
                WarningCount: 1,
                CriticalCount: 0,
                QuickWinCount: 0,
                OverallScore: 0d,
                PerRuleCount: new Dictionary<SchemaRuleCode, int>
                {
                  [SchemaRuleCode.MISSING_FK] = 1,
                  [SchemaRuleCode.NAMING_CONVENTION_VIOLATION] = 0,
                },
                PerTableCount: new Dictionary<string, int>
                {
                  ["public.orders"] = 1,
                  ["public.customers"] = 0,
                },
                AreaScores: new Dictionary<string, double>(),
                ObservedPatterns: new SchemaObservedPatterns(NamingConvention.MixedAllowed, null, null)
            )
        );
    }

    private static SchemaIssue CreateIssue(
        SchemaTargetType targetType = SchemaTargetType.Column,
        string? schemaName = "public",
        string? tableName = "orders",
        string? columnName = "customer_id",
        string? constraintName = null,
        IReadOnlyList<SchemaSuggestion>? suggestions = null
    ) =>
        new(
            IssueId: "issue-1",
            RuleCode: SchemaRuleCode.MISSING_FK,
            Severity: SchemaIssueSeverity.Warning,
            Confidence: 0.8500,
            TargetType: targetType,
            SchemaName: schemaName,
            TableName: tableName,
            ColumnName: columnName,
            ConstraintName: constraintName,
            Title: "Missing FK",
            Message: "FK inferred",
            Evidence: [new SchemaEvidence(EvidenceKind.MetadataFact, "key", "value", 1.0)],
            Suggestions: suggestions ?? [CreateSuggestion("s-1")],
            IsAmbiguous: false
        );

    private static SchemaSuggestion CreateSuggestion(string id) =>
        new(
            SuggestionId: id,
            Title: "Suggestion",
            Description: "Description",
            Confidence: 0.9000,
            SqlCandidates:
            [
                new SqlFixCandidate(
                    CandidateId: "cand-1",
                    Provider: DatabaseProvider.Postgres,
                    Title: "Candidate",
                    Sql: "SELECT 1;",
                    PreconditionsSql: ["SELECT 1"],
                    Safety: SqlCandidateSafety.NonDestructive,
                    Visibility: CandidateVisibility.VisibleActionable,
                    IsAutoApplicable: false,
                    Notes: ["note"]
                ),
            ]
        );
}
