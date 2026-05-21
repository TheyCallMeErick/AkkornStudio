using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;

namespace AkkornStudio.Tests.Unit.Ddl.SchemaAnalysis;

public sealed class SchemaMissingRequiredCommentSqlCandidateFactoryTests
{
    private readonly SchemaMissingRequiredCommentSqlCandidateFactory _factory = new();

    [Fact]
    public void CreateCandidate_ReturnsPostgresTableComment()
    {
        SqlFixCandidate? candidate = _factory.CreateCandidate(CreateTableIssue(), DatabaseProvider.Postgres, "s-1");

        Assert.NotNull(candidate);
        Assert.Equal(SqlCandidateSafety.NonDestructive, candidate!.Safety);
        Assert.Equal(CandidateVisibility.VisibleActionable, candidate.Visibility);
        Assert.Equal("COMMENT ON TABLE \"public\".\"orders\" IS 'Technical description for table orders.';", candidate.Sql);
    }

    [Fact]
    public void CreateCandidate_ReturnsPostgresColumnComment()
    {
        SqlFixCandidate? candidate = _factory.CreateCandidate(CreateColumnIssue(), DatabaseProvider.Postgres, "s-1");

        Assert.NotNull(candidate);
        Assert.Equal("COMMENT ON COLUMN \"public\".\"orders\".\"customer_id\" IS 'Technical description for column orders.customer_id.';", candidate!.Sql);
    }

    [Fact]
    public void CreateCandidate_ReturnsSqlServerAddUpdateFlow()
    {
        SqlFixCandidate? candidate = _factory.CreateCandidate(CreateColumnIssue("dbo"), DatabaseProvider.SqlServer, "s-1");

        Assert.NotNull(candidate);
        Assert.Contains("sp_updateextendedproperty", candidate!.Sql);
        Assert.Contains("sp_addextendedproperty", candidate.Sql);
    }

    [Fact]
    public void CreateCandidate_ReturnsMySqlTableComment()
    {
        SqlFixCandidate? candidate = _factory.CreateCandidate(CreateTableIssue(schemaName: null), DatabaseProvider.MySql, "s-1");

        Assert.NotNull(candidate);
        Assert.Equal("ALTER TABLE `orders` COMMENT = 'Technical description for table orders.';", candidate!.Sql);
    }

    [Fact]
    public void CreateCandidate_ReturnsNull_ForMySqlColumnWithoutFullDefinition()
    {
        SqlFixCandidate? candidate = _factory.CreateCandidate(CreateColumnIssue(schemaName: null), DatabaseProvider.MySql, "s-1");

        Assert.Null(candidate);
    }

    [Fact]
    public void CreateCandidate_ReturnsNull_ForSqlite()
    {
        SqlFixCandidate? candidate = _factory.CreateCandidate(CreateTableIssue(), DatabaseProvider.SQLite, "s-1");

        Assert.Null(candidate);
    }

    private static SchemaIssue CreateTableIssue(string? schemaName = "public")
    {
        return SchemaAnalysisContractValidatorTestData.CreateIssue(
            issueId: "issue-comment-table",
            targetType: SchemaTargetType.Table,
            tableName: "orders",
            columnName: null,
            suggestions: []
        ) with
        {
            RuleCode = SchemaRuleCode.MISSING_REQUIRED_COMMENT,
            SchemaName = schemaName,
            Title = "Missing required comment",
            Message = "Comment required"
        };
    }

    private static SchemaIssue CreateColumnIssue(string? schemaName = "public")
    {
        return SchemaAnalysisContractValidatorTestData.CreateIssue(
            issueId: "issue-comment-column",
            targetType: SchemaTargetType.Column,
            tableName: "orders",
            columnName: "customer_id",
            suggestions: []
        ) with
        {
            RuleCode = SchemaRuleCode.MISSING_REQUIRED_COMMENT,
            SchemaName = schemaName,
            Title = "Missing required comment",
            Message = "Comment required"
        };
    }
}
