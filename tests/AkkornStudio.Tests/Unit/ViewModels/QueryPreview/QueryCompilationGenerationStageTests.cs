using AkkornStudio.Nodes;
using AkkornStudio.QueryEngine;
using AkkornStudio.UI.Services.QueryPreview;

namespace AkkornStudio.Tests.Unit.ViewModels.QueryPreview;

public class QueryCompilationGenerationStageTests
{
    [Fact]
    public void Execute_WhenGeneratorSucceeds_ReturnsInlinedSql()
    {
        var generator = new FakeGenerator(
            () => new GeneratedQuery("SELECT * FROM t", new Dictionary<string, object?>(), "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql + " --inlined",
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        var errors = new List<string>();
        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                new NodeGraph(),
                [],
                null,
                errors));

        Assert.Equal("SELECT * FROM t --inlined", result.Sql);
        Assert.Equal("SELECT * FROM t", result.ExecutionSqlTemplate);
        Assert.Empty(result.Bindings);
        Assert.Empty(result.ParameterContexts);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Execute_WhenGeneratorFails_ReturnsFallbackAndMappedErrors()
    {
        var generator = new FakeGenerator(() => throw new InvalidOperationException("boom"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback-sql");

        var errors = new List<string>();
        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                new NodeGraph(),
                [new JoinDefinition("t", "a", "b")],
                null,
                errors));

        Assert.Equal("fallback-sql", result.Sql);
        Assert.Null(result.ExecutionSqlTemplate);
        Assert.Empty(result.Bindings);
        Assert.Empty(result.ParameterContexts);
        Assert.Contains("mapped: boom", result.Errors);
    }

    [Fact]
    public void Execute_WhenGeneratorSucceeds_PreservesExistingErrorsAndSkipsFallback()
    {
        int fallbackCalls = 0;
        var generator = new FakeGenerator(
            () => new GeneratedQuery("SELECT 42", new Dictionary<string, object?>(), "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) =>
            {
                fallbackCalls++;
                return "fallback";
            });

        var errors = new List<string> { "seed-error" };
        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                new NodeGraph(),
                [],
                null,
                errors));

        Assert.Equal("SELECT 42", result.Sql);
        Assert.Equal("SELECT 42", result.ExecutionSqlTemplate);
        Assert.Empty(result.Bindings);
        Assert.Empty(result.ParameterContexts);
        Assert.Equal(0, fallbackCalls);
        Assert.Single(result.Errors);
        Assert.Contains("seed-error", result.Errors);
    }

    [Fact]
    public void Execute_WhenGeneratorFails_AccumulatesAllMappedErrors()
    {
        var generator = new FakeGenerator(() => throw new InvalidOperationException("boom"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped-a", "mapped-b"],
            (fromTable, joins) => "fallback-sql");

        var errors = new List<string> { "seed-error" };
        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                new NodeGraph(),
                [],
                null,
                errors));

        Assert.Equal("fallback-sql", result.Sql);
        Assert.Null(result.ExecutionSqlTemplate);
        Assert.Empty(result.Bindings);
        Assert.Empty(result.ParameterContexts);
        Assert.Contains("seed-error", result.Errors);
        Assert.Contains("mapped-a", result.Errors);
        Assert.Contains("mapped-b", result.Errors);
    }

    [Fact]
    public void Execute_WhenPredicateBindingsUseLiterals_ExtractsStructuralParameterContexts()
    {
        var graph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "orders",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    TableFullName: "public.orders"),
                new NodeInstance(
                    "cmp",
                    NodeType.Equals,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "literal",
                    NodeType.ValueNumber,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["value"] = "42" }),
            ],
            Connections =
            [
                new Connection("orders", "customer_id", "cmp", "left"),
                new Connection("literal", "result", "cmp", "right"),
            ],
            WhereConditions =
            [
                new WhereBinding("cmp", "result"),
            ],
        };

        var generator = new FakeGenerator(
            () => new GeneratedQuery(
                "SELECT * FROM public.orders WHERE customer_id = @p0",
                new Dictionary<string, object?> { ["p0"] = 42 },
                "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                graph,
                [],
                null,
                []));

        QueryExecutionParameterContext context = Assert.Single(result.ParameterContexts.Values);
        Assert.Equal("@p0", context.BindingLabel);
        Assert.Equal("public.orders.customer_id", context.SourceReference);
        Assert.Equal("public.orders", context.TableRef);
        Assert.Equal("customer_id", context.ColumnName);
    }

    [Fact]
    public void Execute_WhenPredicateUsesAliasWrapper_PreservesStructuralSource()
    {
        var graph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "orders",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    TableFullName: "public.orders"),
                new NodeInstance(
                    "alias",
                    NodeType.Alias,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["alias"] = "customer_alias" }),
                new NodeInstance(
                    "cmp",
                    NodeType.Equals,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "literal",
                    NodeType.ValueNumber,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["value"] = "42" }),
            ],
            Connections =
            [
                new Connection("orders", "customer_id", "alias", "expression"),
                new Connection("alias", "result", "cmp", "left"),
                new Connection("literal", "result", "cmp", "right"),
            ],
            WhereConditions =
            [
                new WhereBinding("cmp", "result"),
            ],
        };

        var generator = new FakeGenerator(
            () => new GeneratedQuery(
                "SELECT * FROM public.orders WHERE customer_alias = @p0",
                new Dictionary<string, object?> { ["p0"] = 42 },
                "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                graph,
                [],
                null,
                []));

        QueryExecutionParameterContext context = Assert.Single(result.ParameterContexts.Values);
        Assert.Equal("public.orders.customer_id", context.SourceReference);
    }

    [Fact]
    public void Execute_WhenPredicateUsesTransformWrapper_PreservesStructuralSource()
    {
        var graph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "orders",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    TableFullName: "public.orders"),
                new NodeInstance(
                    "upper",
                    NodeType.Upper,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "cmp",
                    NodeType.Equals,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "literal",
                    NodeType.ValueString,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["value"] = "ACTIVE" }),
            ],
            Connections =
            [
                new Connection("orders", "status", "upper", "text"),
                new Connection("upper", "result", "cmp", "left"),
                new Connection("literal", "result", "cmp", "right"),
            ],
            WhereConditions =
            [
                new WhereBinding("cmp", "result"),
            ],
        };

        var generator = new FakeGenerator(
            () => new GeneratedQuery(
                "SELECT * FROM public.orders WHERE UPPER(status) = @p0",
                new Dictionary<string, object?> { ["p0"] = "ACTIVE" },
                "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                graph,
                [],
                null,
                []));

        QueryExecutionParameterContext context = Assert.Single(result.ParameterContexts.Values);
        Assert.Equal("public.orders.status", context.SourceReference);
    }

    [Fact]
    public void Execute_WhenPredicateUsesConcatExpression_ProducesCompositeStructuralContext()
    {
        var graph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "orders",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    TableFullName: "public.orders"),
                new NodeInstance(
                    "concat",
                    NodeType.Concat,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "cmp",
                    NodeType.Equals,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "literal",
                    NodeType.ValueString,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["value"] = "John Doe" }),
            ],
            Connections =
            [
                new Connection("orders", "first_name", "concat", "a"),
                new Connection("orders", "last_name", "concat", "b"),
                new Connection("concat", "result", "cmp", "left"),
                new Connection("literal", "result", "cmp", "right"),
            ],
            WhereConditions =
            [
                new WhereBinding("cmp", "result"),
            ],
        };

        var generator = new FakeGenerator(
            () => new GeneratedQuery(
                "SELECT * FROM public.orders WHERE CONCAT(first_name, last_name) = @p0",
                new Dictionary<string, object?> { ["p0"] = "John Doe" },
                "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                graph,
                [],
                null,
                []));

        QueryExecutionParameterContext context = Assert.Single(result.ParameterContexts.Values);
        Assert.Contains("public.orders.first_name", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public.orders.last_name", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Null(context.TableRef);
        Assert.Null(context.ColumnName);
    }

    [Fact]
    public void Execute_WhenPredicateUsesArithmeticExpression_ProducesCompositeStructuralContext()
    {
        var graph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "orders",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    TableFullName: "public.orders"),
                new NodeInstance(
                    "sum",
                    NodeType.Add,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "cmp",
                    NodeType.GreaterThan,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "literal",
                    NodeType.ValueNumber,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["value"] = "100" }),
            ],
            Connections =
            [
                new Connection("orders", "subtotal", "sum", "a"),
                new Connection("orders", "shipping_total", "sum", "b"),
                new Connection("sum", "result", "cmp", "left"),
                new Connection("literal", "result", "cmp", "right"),
            ],
            WhereConditions =
            [
                new WhereBinding("cmp", "result"),
            ],
        };

        var generator = new FakeGenerator(
            () => new GeneratedQuery(
                "SELECT * FROM public.orders WHERE (subtotal + shipping_total) > @p0",
                new Dictionary<string, object?> { ["p0"] = 100 },
                "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                graph,
                [],
                null,
                []));

        QueryExecutionParameterContext context = Assert.Single(result.ParameterContexts.Values);
        Assert.Contains("public.orders.subtotal", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public.orders.shipping_total", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Null(context.TableRef);
        Assert.Null(context.ColumnName);
    }

    [Fact]
    public void Execute_WhenPredicateUsesNullFill_PreservesConditionalRoles()
    {
        var graph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "orders",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    TableFullName: "public.orders"),
                new NodeInstance(
                    "fill",
                    NodeType.NullFill,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "cmp",
                    NodeType.Equals,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "literal",
                    NodeType.ValueString,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["value"] = "guest" }),
            ],
            Connections =
            [
                new Connection("orders", "nickname", "fill", "value"),
                new Connection("orders", "display_name", "fill", "fallback"),
                new Connection("fill", "result", "cmp", "left"),
                new Connection("literal", "result", "cmp", "right"),
            ],
            WhereConditions =
            [
                new WhereBinding("cmp", "result"),
            ],
        };

        var generator = new FakeGenerator(
            () => new GeneratedQuery(
                "SELECT * FROM public.orders WHERE COALESCE(nickname, display_name) = @p0",
                new Dictionary<string, object?> { ["p0"] = "guest" },
                "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                graph,
                [],
                null,
                []));

        QueryExecutionParameterContext context = Assert.Single(result.ParameterContexts.Values);
        Assert.Equal("conditional", context.ExpressionKind);
        Assert.Contains("Condicional sobre public.orders.nickname", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fallback de public.orders.display_name", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WhenHavingUsesAggregate_PreservesAggregateStructuralContext()
    {
        var graph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "orders",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    TableFullName: "public.orders"),
                new NodeInstance(
                    "sum",
                    NodeType.Sum,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "cmp",
                    NodeType.GreaterThan,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "literal",
                    NodeType.ValueNumber,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["value"] = "500" }),
            ],
            Connections =
            [
                new Connection("orders", "total", "sum", "value"),
                new Connection("sum", "total", "cmp", "left"),
                new Connection("literal", "result", "cmp", "right"),
            ],
            Havings =
            [
                new HavingBinding("cmp", "result"),
            ],
        };

        var generator = new FakeGenerator(
            () => new GeneratedQuery(
                "SELECT customer_id, SUM(total) FROM public.orders GROUP BY customer_id HAVING SUM(total) > @p0",
                new Dictionary<string, object?> { ["p0"] = 500 },
                "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                graph,
                [],
                null,
                []));

        QueryExecutionParameterContext context = Assert.Single(result.ParameterContexts.Values);
        Assert.Equal("aggregate", context.ExpressionKind);
        Assert.Equal("public.orders.total", context.SourceReference);
        Assert.Contains("Agregado sobre public.orders.total", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WhenHavingUsesStringAgg_PreservesAggregateOrderingContext()
    {
        var graph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "orders",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    TableFullName: "public.orders"),
                new NodeInstance(
                    "agg",
                    NodeType.StringAgg,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "cmp",
                    NodeType.Equals,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "literal",
                    NodeType.ValueString,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["value"] = "paid,shipped" }),
            ],
            Connections =
            [
                new Connection("orders", "status", "agg", "value"),
                new Connection("orders", "created_at", "agg", "order_by"),
                new Connection("agg", "result", "cmp", "left"),
                new Connection("literal", "result", "cmp", "right"),
            ],
            Havings =
            [
                new HavingBinding("cmp", "result"),
            ],
        };

        var generator = new FakeGenerator(
            () => new GeneratedQuery(
                "SELECT customer_id, STRING_AGG(status, ',' ORDER BY created_at) FROM public.orders GROUP BY customer_id HAVING STRING_AGG(status, ',' ORDER BY created_at) = @p0",
                new Dictionary<string, object?> { ["p0"] = "paid,shipped" },
                "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                graph,
                [],
                null,
                []));

        QueryExecutionParameterContext context = Assert.Single(result.ParameterContexts.Values);
        Assert.Equal("aggregate-string", context.ExpressionKind);
        Assert.Contains("Agregacao textual sobre public.orders.status", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ordenada por public.orders.created_at", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WhenQualifyUsesWindowFunction_PreservesWindowStructuralContext()
    {
        var graph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "orders",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    TableFullName: "public.orders"),
                new NodeInstance(
                    "window",
                    NodeType.WindowFunction,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["function"] = "Lag" }),
                new NodeInstance(
                    "cmp",
                    NodeType.NotEquals,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new NodeInstance(
                    "literal",
                    NodeType.ValueString,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["value"] = "pending" }),
            ],
            Connections =
            [
                new Connection("orders", "status", "window", "value"),
                new Connection("orders", "customer_id", "window", "partition_1"),
                new Connection("orders", "created_at", "window", "order_1"),
                new Connection("window", "result", "cmp", "left"),
                new Connection("literal", "result", "cmp", "right"),
            ],
            Qualifies =
            [
                new QualifyBinding("cmp", "result"),
            ],
        };

        var generator = new FakeGenerator(
            () => new GeneratedQuery(
                "SELECT * FROM public.orders QUALIFY LAG(status) OVER (PARTITION BY customer_id ORDER BY created_at) <> @p0",
                new Dictionary<string, object?> { ["p0"] = "pending" },
                "debug"));

        var stage = new QueryCompilationGenerationStage(
            generator,
            (sql, bindings) => sql,
            new QueryExecutionParameterContextExtractor(),
            ex => ["mapped: " + ex.Message],
            (fromTable, joins) => "fallback");

        QueryCompilationGenerationStageResult result = stage.Execute(
            new QueryCompilationGenerationStageInput(
                "public.orders",
                graph,
                [],
                null,
                []));

        QueryExecutionParameterContext context = Assert.Single(result.ParameterContexts.Values);
        Assert.Equal("window", context.ExpressionKind);
        Assert.Contains("Janela sobre public.orders.status", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("particionada por public.orders.customer_id", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ordenada por public.orders.created_at", context.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeGenerator(Func<GeneratedQuery> behavior) : IQueryCompilationSqlGenerator
    {
        private readonly Func<GeneratedQuery> _behavior = behavior;

        public GeneratedQuery Generate(
            string fromTable,
            NodeGraph graph,
            IReadOnlyList<JoinDefinition> joins,
            SetOperationDefinition? setOperation) => _behavior();
    }
}
