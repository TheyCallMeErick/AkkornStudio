using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;

namespace AkkornStudio.Tests.Unit.Ddl.SchemaAnalysis;

public sealed class SchemaSuggestionFactoryTests
{
    private readonly SchemaSuggestionFactory _factory = new();

    [Fact]
    public void CreateSuggestions_ReturnsSingleSuggestion_ForSupportedIssue()
    {
        SchemaIssue issue = SchemaAnalysisContractValidatorTestData.CreateIssue(
            issueId: "issue-comment",
            targetType: SchemaTargetType.Table,
            tableName: "orders",
            columnName: null,
            suggestions: []
        ) with
        {
            RuleCode = SchemaRuleCode.MISSING_REQUIRED_COMMENT,
            Title = "Missing required comment",
            Message = "Comment required"
        };

        IReadOnlyList<SchemaSuggestion> suggestions = _factory.CreateSuggestions(
            issue,
            DatabaseProvider.Postgres,
            SchemaAnalysisContractValidatorTestData.CreateProfile()
        );

        SchemaSuggestion suggestion = Assert.Single(suggestions);
        Assert.Equal("Add technical comment", suggestion.Title);
        Assert.NotEmpty(suggestion.SqlCandidates);
    }

    [Fact]
    public void CreateSuggestions_Truncates_ByConfidenceThenTitleThenSuggestionId()
    {
        SchemaIssue issue = SchemaAnalysisContractValidatorTestData.CreateIssue(
            issueId: "issue-fk",
            evidence:
            [
                SchemaEvidenceFactory.MetadataFact("targetSchema", "public", 0.9),
                SchemaEvidenceFactory.MetadataFact("targetTable", "customers", 0.9),
                SchemaEvidenceFactory.MetadataFact("targetColumn", "id", 0.9),
            ],
            suggestions: []
        ) with
        {
            RuleCode = SchemaRuleCode.MISSING_FK,
            Title = "Missing FK",
            Message = "FK inferred"
        };
        SchemaAnalysisProfile profile = SchemaAnalysisContractValidatorTestData.CreateProfile(maxSuggestionsPerIssue: 1);

        IReadOnlyList<SchemaSuggestion> suggestions = _factory.CreateSuggestions(issue, DatabaseProvider.Postgres, profile);

        SchemaSuggestion suggestion = Assert.Single(suggestions);
        Assert.Equal("Review inferred foreign key", suggestion.Title);
    }

    [Fact]
    public void CreateSuggestions_AttachesSqlCandidate_ForNonAmbiguousMissingFk()
    {
        SchemaIssue issue = SchemaAnalysisContractValidatorTestData.CreateIssue(
            issueId: "issue-fk",
            tableName: "orders",
            columnName: "customer_id",
            evidence:
            [
                SchemaEvidenceFactory.MetadataFact("targetSchema", "public", 0.9),
                SchemaEvidenceFactory.MetadataFact("targetTable", "customers", 0.9),
                SchemaEvidenceFactory.MetadataFact("targetColumn", "id", 0.9),
            ],
            suggestions: []
        ) with
        {
            RuleCode = SchemaRuleCode.MISSING_FK,
            Title = "Missing FK",
            Message = "FK inferred",
            IsAmbiguous = false,
        };

        IReadOnlyList<SchemaSuggestion> suggestions = _factory.CreateSuggestions(
            issue,
            DatabaseProvider.Postgres,
            SchemaAnalysisContractValidatorTestData.CreateProfile()
        );

        Assert.NotEmpty(suggestions[0].SqlCandidates);
    }

    [Fact]
    public void CreateSuggestions_AttachesSqlCandidate_ForNf1SplitColumnWhenPkEvidenceExists()
    {
        SchemaIssue issue = SchemaAnalysisContractValidatorTestData.CreateIssue(
            issueId: "issue-nf1",
            targetType: SchemaTargetType.Column,
            tableName: "orders",
            columnName: "tags",
            evidence:
            [
                SchemaEvidenceFactory.MetadataFact("primaryKeyColumn", "id", 0.85),
                SchemaEvidenceFactory.MetadataFact("primaryKeyNativeType", "integer", 0.85),
                SchemaEvidenceFactory.MetadataFact("columnNativeType", "text", 0.85),
            ],
            suggestions: []
        ) with
        {
            RuleCode = SchemaRuleCode.NF1_HINT_MULTI_VALUED,
            Title = "1NF hint multi-valued",
            Message = "Column may be multi-valued",
            IsAmbiguous = false,
        };

        IReadOnlyList<SchemaSuggestion> suggestions = _factory.CreateSuggestions(
            issue,
            DatabaseProvider.Postgres,
            SchemaAnalysisContractValidatorTestData.CreateProfile()
        );

        SchemaSuggestion suggestion = Assert.Single(suggestions);
        SqlFixCandidate candidate = Assert.Single(suggestion.SqlCandidates);
        Assert.Equal("Create normalized child table", candidate.Title);
        Assert.Equal(SqlCandidateSafety.NonDestructive, candidate.Safety);
        Assert.Equal(CandidateVisibility.VisibleActionable, candidate.Visibility);
        Assert.False(candidate.IsAutoApplicable);
        Assert.Contains("CREATE TABLE \"public\".\"orders_tags\"", candidate.Sql);
        Assert.Contains("FOREIGN KEY (\"id\") REFERENCES \"public\".\"orders\" (\"id\")", candidate.Sql);
        Assert.NotEmpty(candidate.PreconditionsSql);
    }

    [Fact]
    public void CreateSuggestions_SkipsNf1SqlCandidate_WhenPrimaryKeyEvidenceIsMissing()
    {
        SchemaIssue issue = SchemaAnalysisContractValidatorTestData.CreateIssue(
            issueId: "issue-nf1-no-pk",
            targetType: SchemaTargetType.Column,
            tableName: "orders",
            columnName: "tags",
            suggestions: []
        ) with
        {
            RuleCode = SchemaRuleCode.NF1_HINT_MULTI_VALUED,
            Title = "1NF hint multi-valued",
            Message = "Column may be multi-valued",
        };

        IReadOnlyList<SchemaSuggestion> suggestions = _factory.CreateSuggestions(
            issue,
            DatabaseProvider.Postgres,
            SchemaAnalysisContractValidatorTestData.CreateProfile()
        );

        SchemaSuggestion suggestion = Assert.Single(suggestions);
        Assert.Empty(suggestion.SqlCandidates);
    }

    [Fact]
    public void CreateSuggestions_ReturnsEmpty_WhenIssueHasNoSuggestionMapping()
    {
        SchemaIssue issue = SchemaAnalysisContractValidatorTestData.CreateIssue(
            issueId: "issue-fkcat",
            targetType: SchemaTargetType.Constraint,
            constraintName: "fk_orders_customer",
            suggestions: []
        ) with
        {
            RuleCode = SchemaRuleCode.FK_CATALOG_INCONSISTENT,
            Title = "FK catalog inconsistent",
            Message = "Constraint inconsistent"
        };

        IReadOnlyList<SchemaSuggestion> suggestions = _factory.CreateSuggestions(
            issue,
            DatabaseProvider.Postgres,
            SchemaAnalysisContractValidatorTestData.CreateProfile()
        );

        Assert.Empty(suggestions);
    }
}
