using AkkornStudio.SqlImport.Contracts;
using AkkornStudio.SqlImport.IR;
using AkkornStudio.SqlImport.IR.Expressions;
using AkkornStudio.SqlImport.IR.Metadata;
using AkkornStudio.SqlImport.IR.Sources;
using AkkornStudio.SqlImport.Tracing;
using SqlImportLiteralExpr = AkkornStudio.SqlImport.IR.Expressions.LiteralExpr;

namespace AkkornStudio.Tests.Unit.Services.SqlImport.Ir;

public sealed class InExprTests
{
    [Fact]
    public void Constructor_Throws_WhenValuesAndSubqueryAreBothProvided()
    {
        SqlExpression scalar = CreateLiteral("v");
        IReadOnlyList<SqlExpression> values = [CreateLiteral("1")];
        QueryExpr subquery = CreateSubquery();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            new InExpr(
                "expr",
                null,
                SqlImportSemanticType.Boolean,
                SqlResolutionStatus.Resolved,
                CreateTrace(),
                CreateNodeMetadata(),
                scalar,
                values,
                subquery,
                IsNegated: false));

        Assert.Equal("IN expression must have either value list or subquery, but not both.", ex.Message);
    }

    [Fact]
    public void Constructor_Throws_WhenValuesAndSubqueryAreBothMissing()
    {
        SqlExpression scalar = CreateLiteral("v");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            new InExpr(
                "expr",
                null,
                SqlImportSemanticType.Boolean,
                SqlResolutionStatus.Resolved,
                CreateTrace(),
                CreateNodeMetadata(),
                scalar,
                [],
                Subquery: null,
                IsNegated: false));

        Assert.Equal("IN expression must have either value list or subquery, but not both.", ex.Message);
    }

    [Fact]
    public void Constructor_AllowsValuesVariant()
    {
        SqlExpression scalar = CreateLiteral("v");
        IReadOnlyList<SqlExpression> values = [CreateLiteral("1"), CreateLiteral("2")];

        var expr = new InExpr(
            "expr",
            null,
            SqlImportSemanticType.Boolean,
            SqlResolutionStatus.Resolved,
            CreateTrace(),
            CreateNodeMetadata(),
            scalar,
            values,
            Subquery: null,
            IsNegated: true);

        Assert.Same(scalar, expr.Value);
        Assert.Equal(values, expr.Values);
        Assert.Null(expr.Subquery);
        Assert.True(expr.IsNegated);
    }

    [Fact]
    public void Constructor_AllowsSubqueryVariant()
    {
        SqlExpression scalar = CreateLiteral("v");
        QueryExpr subquery = CreateSubquery();

        var expr = new InExpr(
            "expr",
            null,
            SqlImportSemanticType.Boolean,
            SqlResolutionStatus.Resolved,
            CreateTrace(),
            CreateNodeMetadata(),
            scalar,
            [],
            subquery,
            IsNegated: false);

        Assert.Same(scalar, expr.Value);
        Assert.Empty(expr.Values);
        Assert.Same(subquery, expr.Subquery);
        Assert.False(expr.IsNegated);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNull_WhenValueIsNull()
    {
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new InExpr(
                "expr",
                null,
                SqlImportSemanticType.Boolean,
                SqlResolutionStatus.Resolved,
                CreateTrace(),
                CreateNodeMetadata(),
                Value: null!,
                Values: [CreateLiteral("1")],
                Subquery: null,
                IsNegated: false));

        Assert.Equal("Value", ex.ParamName);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNull_WhenValuesIsNull()
    {
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new InExpr(
                "expr",
                null,
                SqlImportSemanticType.Boolean,
                SqlResolutionStatus.Resolved,
                CreateTrace(),
                CreateNodeMetadata(),
                Value: CreateLiteral("v"),
                Values: null!,
                Subquery: null,
                IsNegated: false));

        Assert.Equal("Values", ex.ParamName);
    }

    private static SqlImportLiteralExpr CreateLiteral(string raw) =>
        new(
            ExprId: $"lit-{raw}",
            SourceSpan: null,
            SemanticType: SqlImportSemanticType.Text,
            ResolutionStatus: SqlResolutionStatus.Resolved,
            TraceMeta: CreateTrace(),
            NodeMetadata: CreateNodeMetadata(),
            Raw: raw,
            Normalized: raw,
            IsNullLiteral: false);

    private static QueryExpr CreateSubquery()
    {
        SourceExpr from = new TableRefSourceExpr(
            SourceId: "src",
            Database: null,
            Schema: "public",
            Table: "orders",
            Alias: "o",
            ResolutionStatus: SqlResolutionStatus.Resolved,
            NodeMetadata: CreateNodeMetadata());

        return new QueryExpr(
            SelectItems: [],
            FromSource: from,
            Joins: [],
            WhereExpr: null,
            GroupBy: [],
            HavingExpr: null,
            OrderBy: [],
            LimitOrTop: null,
            SetOperations: []);
    }

    private static TraceMeta CreateTrace() => new("q1", null, null, null);

    private static SqlIrNodeMetadata CreateNodeMetadata() => new(false, null, [], []);
}
