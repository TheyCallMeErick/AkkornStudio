using AkkornStudio.Core;
using AkkornStudio.SqlImport.Contracts;
using AkkornStudio.SqlImport.Diagnostics;
using AkkornStudio.SqlImport.IR;
using AkkornStudio.SqlImport.IR.Expressions;
using AkkornStudio.SqlImport.IR.Sources;
using AkkornStudio.UI.Services.SqlImport.Execution.Parsing;
using AkkornStudio.UI.Services.SqlImport.Mapping;

namespace AkkornStudio.Tests.Unit.Services.SqlImport;

public sealed class SqlImportAstToIrMapperTests
{
    [Fact]
    public void MapSelectFrom_WithSimpleProjection_BuildsRootIrContracts()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", "order_id")],
            fromParts: [new SqlImportSourcePart("public.orders", "o", null, null)]
        );

        var ir = mapper.MapSelectFrom(parsed, "SELECT o.id AS order_id FROM public.orders o", DatabaseProvider.Postgres);

        Assert.Equal("1.0.0", ir.IrVersion);
        Assert.Single(ir.Query.SelectItems);
        Assert.IsType<TableRefSourceExpr>(ir.Query.FromSource);
        Assert.IsType<ColumnRefExpr>(ir.Query.SelectItems[0].Expression);
        Assert.Equal(SqlResolutionStatus.Resolved, ir.Query.SelectItems[0].Expression.ResolutionStatus);
    }

    [Fact]
    public void MapSelectFrom_WithSameInput_GeneratesDeterministicQueryId()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("id", null)],
            fromParts: [new SqlImportSourcePart("orders", null, null, null)]
        );

        var first = mapper.MapSelectFrom(parsed, "SELECT id FROM orders", DatabaseProvider.SqlServer);
        var second = mapper.MapSelectFrom(parsed, "SELECT id FROM orders", DatabaseProvider.SqlServer);

        Assert.Equal(first.QueryId, second.QueryId);
        Assert.Equal(first.SourceHash, second.SourceHash);
    }

    [Fact]
    public void MapSelectFrom_WithJoinAndWhere_MapsJoinAndWhereExpressions()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts:
            [
                new SqlImportSourcePart("orders", "o", null, null),
                new SqlImportSourcePart("customers", "c", "INNER JOIN", "o.customer_id = c.id"),
            ],
            whereClause: "o.id > 10"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o INNER JOIN customers c ON o.customer_id = c.id WHERE o.id > 10",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.Joins);
        Assert.NotNull(ir.Query.WhereExpr);
        Assert.IsType<AkkornStudio.SqlImport.IR.Expressions.ComparisonExpr>(ir.Query.Joins[0].OnExpr);
        Assert.IsType<AkkornStudio.SqlImport.IR.Expressions.ComparisonExpr>(ir.Query.WhereExpr);
    }

    [Fact]
    public void MapSelectFrom_WithUnqualifiedWhereAcrossMultipleSources_EmitsAmbiguousDiagnostic()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts:
            [
                new SqlImportSourcePart("orders", "o", null, null),
                new SqlImportSourcePart("customers", "c", "INNER JOIN", "o.customer_id = c.id"),
            ],
            whereClause: "id > 10"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o INNER JOIN customers c ON o.customer_id = c.id WHERE id > 10",
            DatabaseProvider.Postgres
        );

        Assert.Contains(ir.Diagnostics, diagnostic => diagnostic.Code == "SQLIMP_0201_COLUMN_AMBIGUOUS");
        Assert.NotNull(ir.Query.WhereExpr);
        Assert.Equal(SqlResolutionStatus.Ambiguous, ir.Query.WhereExpr!.ResolutionStatus);
    }

    [Fact]
    public void MapSelectFrom_WithGroupByAndHaving_MapsClauseExpressions()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns:
            [
                new SqlImportSelectedColumn("o.customer_id", "customer_id"),
                new SqlImportSelectedColumn("COUNT(*)", "total_orders"),
            ],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            groupBy: "o.customer_id",
            havingClause: "COUNT(*) > 1"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.customer_id, COUNT(*) AS total_orders FROM orders o GROUP BY o.customer_id HAVING COUNT(*) > 1",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.GroupBy);
        Assert.NotNull(ir.Query.HavingExpr);
        Assert.IsType<ColumnRefExpr>(ir.Query.GroupBy[0]);
        Assert.IsType<AkkornStudio.SqlImport.IR.Expressions.ComparisonExpr>(ir.Query.HavingExpr);
    }

    [Fact]
    public void MapSelectFrom_WithOrderBy_MapsDirectionAndExpression()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", "order_id")],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            orderBy: "o.id DESC"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id AS order_id FROM orders o ORDER BY o.id DESC",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.OrderBy);
        Assert.True(ir.Query.OrderBy[0].Descending);
        Assert.IsType<ColumnRefExpr>(ir.Query.OrderBy[0].Expression);
    }

    [Fact]
    public void MapSelectFrom_WithUnknownOrderByAlias_EmitsUnresolvedDiagnostic()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", "order_id")],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            orderBy: "x.id"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id AS order_id FROM orders o ORDER BY x.id",
            DatabaseProvider.Postgres
        );

        Assert.Contains(ir.Diagnostics, diagnostic => diagnostic.Code == "SQLIMP_0202_COLUMN_UNRESOLVED");
        Assert.Single(ir.Query.OrderBy);
        Assert.Equal(SqlResolutionStatus.Unresolved, ir.Query.OrderBy[0].ResolutionStatus);
    }

    [Fact]
    public void MapSelectFrom_WithTopSyntax_PreservesTopSemanticsInLimitExpr()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            limit: 10
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT TOP 10 o.id FROM orders o",
            DatabaseProvider.SqlServer
        );

        Assert.NotNull(ir.Query.LimitOrTop);
        Assert.Equal(10, ir.Query.LimitOrTop!.Value);
        Assert.True(ir.Query.LimitOrTop.IsTopSyntax);
    }

    [Fact]
    public void MapSelectFrom_WithUnion_MapsRightQueryAsSetOperation()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o UNION SELECT a.id FROM archived_orders a",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.SetOperations);
        Assert.Equal("UNION", ir.Query.SetOperations[0].Kind);
        Assert.False(ir.Query.SetOperations[0].IsAll);
        Assert.Single(ir.Query.SetOperations[0].RightQuery.SelectItems);
    }

    [Fact]
    public void MapSelectFrom_WithUnionAll_MapsSetOperationAsAll()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o UNION ALL SELECT a.id FROM archived_orders a",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.SetOperations);
        Assert.Equal("UNION", ir.Query.SetOperations[0].Kind);
        Assert.True(ir.Query.SetOperations[0].IsAll);
    }

    [Fact]
    public void MapSelectFrom_WithUnionChain_MapsMultipleSetOperations()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o UNION SELECT a.id FROM archived_orders a UNION ALL SELECT h.id FROM history_orders h",
            DatabaseProvider.Postgres
        );

        Assert.Equal(2, ir.Query.SetOperations.Count);
        Assert.All(ir.Query.SetOperations, setOp => Assert.Equal("UNION", setOp.Kind));
        Assert.False(ir.Query.SetOperations[0].IsAll);
        Assert.True(ir.Query.SetOperations[1].IsAll);
    }

    [Fact]
    public void MapSelectFrom_WithParenthesizedUnionOperands_MapsSetOperations()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o UNION (SELECT a.id FROM archived_orders a) UNION ALL (SELECT h.id FROM history_orders h)",
            DatabaseProvider.Postgres
        );

        Assert.Equal(2, ir.Query.SetOperations.Count);
        Assert.False(ir.Query.SetOperations[0].IsAll);
        Assert.True(ir.Query.SetOperations[1].IsAll);
    }

    [Fact]
    public void MapSelectFrom_WithParenthesizedUnionOperandAndSemicolon_MapsSetOperation()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o UNION (SELECT a.id FROM archived_orders a);",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.SetOperations);
        Assert.Equal("UNION", ir.Query.SetOperations[0].Kind);
    }

    [Fact]
    public void MapSelectFrom_WithUnparenthesizedUnionOperandOrderBy_EmitsPrecedenceDiagnostic()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o UNION SELECT a.id FROM archived_orders a ORDER BY a.id",
            DatabaseProvider.Postgres
        );

        Assert.Empty(ir.Query.SetOperations);
        Assert.Contains(ir.Diagnostics, diagnostic =>
            diagnostic.Code == "SQLIMP_0301_SET_OPERAND_PRECEDENCE_AMBIGUOUS");
    }

    [Fact]
    public void MapSelectFrom_WithParenthesizedUnionOperandOrderBy_MapsSetOperation()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o UNION (SELECT a.id FROM archived_orders a ORDER BY a.id)",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.SetOperations);
        Assert.DoesNotContain(ir.Diagnostics, diagnostic =>
            diagnostic.Code == "SQLIMP_0301_SET_OPERAND_PRECEDENCE_AMBIGUOUS");
    }

    [Fact]
    public void MapSelectFrom_WithUnionThenIntersect_MapsBothSetOperations()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o UNION SELECT a.id FROM archived_orders a INTERSECT SELECT h.id FROM history_orders h",
            DatabaseProvider.Postgres
        );

        Assert.Equal(2, ir.Query.SetOperations.Count);
        Assert.Equal("UNION", ir.Query.SetOperations[0].Kind);
        Assert.Equal("INTERSECT", ir.Query.SetOperations[1].Kind);
    }

    [Fact]
    public void MapSelectFrom_WithIntersect_MapsSetOperation()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o INTERSECT SELECT a.id FROM archived_orders a",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.SetOperations);
        Assert.Equal("INTERSECT", ir.Query.SetOperations[0].Kind);
    }

    [Fact]
    public void MapSelectFrom_WithExcept_MapsSetOperation()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o EXCEPT SELECT a.id FROM archived_orders a",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.SetOperations);
        Assert.Equal("EXCEPT", ir.Query.SetOperations[0].Kind);
    }

    [Fact]
    public void MapSelectFrom_WithSetOperationProjectionArityMismatch_EmitsDiagnostic()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o UNION SELECT a.id, a.name FROM archived_orders a",
            DatabaseProvider.Postgres
        );

        Assert.Empty(ir.Query.SetOperations);
        Assert.Contains(ir.Diagnostics, diagnostic =>
            diagnostic.Code == "SQLIMP_0302_SET_OPERAND_ARITY_MISMATCH");
    }

    [Fact]
    public void MapSelectFrom_WithSetOperationSemanticMismatch_EmitsNonBlockingDiagnostic()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("'alpha'", "v")],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT 'alpha' AS v FROM orders o UNION SELECT 1 AS v FROM archived_orders a",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.SetOperations);
        Assert.Contains(ir.Diagnostics, diagnostic =>
            diagnostic.Code == "SQLIMP_0303_SET_OPERAND_SEMANTIC_MISMATCH");
    }

    [Fact]
    public void MapSelectFrom_WithNumericSetOperationCompatibility_DoesNotEmitSemanticMismatch()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("1", "v")],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)]
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT 1 AS v FROM orders o UNION SELECT 2.5 AS v FROM archived_orders a",
            DatabaseProvider.Postgres
        );

        Assert.Single(ir.Query.SetOperations);
        Assert.DoesNotContain(ir.Diagnostics, diagnostic =>
            diagnostic.Code == "SQLIMP_0303_SET_OPERAND_SEMANTIC_MISMATCH");
    }

    [Fact]
    public void MapSelectFrom_WithQualifiedStarUnresolvedAlias_EmitsStarAliasUnresolvedDiagnostic()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = new(
            IsDistinct: false,
            IsStar: true,
            StarQualifier: "x",
            SelectedColumns: [],
            FromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            WhereClause: null,
            OrderBy: null,
            GroupBy: null,
            HavingClause: null,
            Limit: null,
            OuterAliases: [],
            SetOperations: []
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT x.* FROM orders o",
            DatabaseProvider.Postgres
        );

        Assert.Contains(ir.Diagnostics, diagnostic =>
            diagnostic.Code == SqlImportDiagnosticCodes.StarAliasUnresolved);
        Assert.Single(ir.Query.SelectItems);
        Assert.Equal(SqlResolutionStatus.Unresolved, ir.Query.SelectItems[0].Expression.ResolutionStatus);
    }

    [Fact]
    public void MapSelectFrom_WithGenericFunctionInWhere_EmitsForbiddenContextDiagnostic()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            whereClause: "MY_FUNC(o.id) = 1"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o WHERE MY_FUNC(o.id) = 1",
            DatabaseProvider.Postgres
        );

        Assert.Contains(ir.Diagnostics, diagnostic =>
            diagnostic.Code == SqlImportDiagnosticCodes.FunctionGenericForbiddenContext);
    }

    [Fact]
    public void MapSelectFrom_WithUnsupportedFunctionInWhere_EmitsFunctionUnsupportedDiagnostic()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            whereClause: "ROW_NUMBER(o.id) = 1"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o WHERE ROW_NUMBER(o.id) = 1",
            DatabaseProvider.Postgres
        );

        Assert.Contains(ir.Diagnostics, diagnostic =>
            diagnostic.Code == SqlImportDiagnosticCodes.FunctionUnsupported);
    }

    [Fact]
    public void MapSelectFrom_WithFunctionArgumentsInWhere_PreservesFunctionArgumentsInIr()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            whereClause: "MY_FUNC(o.id, 10) = 1"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o WHERE MY_FUNC(o.id, 10) = 1",
            DatabaseProvider.Postgres
        );

        var comparison = Assert.IsType<AkkornStudio.SqlImport.IR.Expressions.ComparisonExpr>(ir.Query.WhereExpr);
        var function = Assert.IsType<FunctionExpr>(comparison.Left);

        Assert.Equal("MY_FUNC", function.Name);
        Assert.Equal(2, function.Arguments.Count);
        Assert.IsType<ColumnRefExpr>(function.Arguments[0]);
        Assert.IsType<AkkornStudio.SqlImport.IR.Expressions.LiteralExpr>(function.Arguments[1]);
    }

    [Fact]
    public void MapSelectFrom_WithCountDistinctInWhere_PreservesDistinctArgumentNode()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            whereClause: "COUNT(DISTINCT o.customer_id) > 1"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o WHERE COUNT(DISTINCT o.customer_id) > 1",
            DatabaseProvider.Postgres
        );

        var comparison = Assert.IsType<AkkornStudio.SqlImport.IR.Expressions.ComparisonExpr>(ir.Query.WhereExpr);
        var outerFunction = Assert.IsType<FunctionExpr>(comparison.Left);
        Assert.Equal("COUNT", outerFunction.Name);
        Assert.Single(outerFunction.Arguments);

        var distinctArgument = Assert.IsType<FunctionExpr>(outerFunction.Arguments[0]);
        Assert.Equal("DISTINCT", distinctArgument.Name);
        Assert.Single(distinctArgument.Arguments);
        Assert.IsType<ColumnRefExpr>(distinctArgument.Arguments[0]);
    }

    [Fact]
    public void MapSelectFrom_WithCountStarInWhere_PreservesStarArgument()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            whereClause: "COUNT(*) > 1"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o WHERE COUNT(*) > 1",
            DatabaseProvider.Postgres
        );

        var comparison = Assert.IsType<AkkornStudio.SqlImport.IR.Expressions.ComparisonExpr>(ir.Query.WhereExpr);
        var function = Assert.IsType<FunctionExpr>(comparison.Left);
        Assert.Equal("COUNT", function.Name);
        Assert.Single(function.Arguments);

        var star = Assert.IsType<AkkornStudio.SqlImport.IR.Expressions.LiteralExpr>(function.Arguments[0]);
        Assert.Equal("*", star.Raw);
    }

    [Fact]
    public void MapSelectFrom_WithUntypedScalarFallback_EmitsTypeInferenceFallbackDiagnostic()
    {
        var mapper = new SqlImportAstToIrMapper();
        SqlImportParsedQuery parsed = CreateQuery(
            selectedColumns: [new SqlImportSelectedColumn("o.id", null)],
            fromParts: [new SqlImportSourcePart("orders", "o", null, null)],
            whereClause: "o.id + 1 > 10"
        );

        SqlToNodeIR ir = mapper.MapSelectFrom(
            parsed,
            "SELECT o.id FROM orders o WHERE o.id + 1 > 10",
            DatabaseProvider.Postgres
        );

        Assert.Contains(ir.Diagnostics, diagnostic =>
            diagnostic.Code == SqlImportDiagnosticCodes.TypeInferenceFallback);
    }

    private static SqlImportParsedQuery CreateQuery(
        IReadOnlyList<SqlImportSelectedColumn> selectedColumns,
        IReadOnlyList<SqlImportSourcePart> fromParts,
        string? whereClause = null,
        string? orderBy = null,
        string? groupBy = null,
        string? havingClause = null,
        int? limit = null
    )
    {
        return new SqlImportParsedQuery(
            IsDistinct: false,
            IsStar: false,
            StarQualifier: null,
            SelectedColumns: selectedColumns,
            FromParts: fromParts,
            WhereClause: whereClause,
            OrderBy: orderBy,
            GroupBy: groupBy,
            HavingClause: havingClause,
            Limit: limit,
            OuterAliases: [],
            SetOperations: []
        );
    }
}
