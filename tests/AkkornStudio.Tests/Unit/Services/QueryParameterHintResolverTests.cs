using AkkornStudio.UI.Services;
using AkkornStudio.Core;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Services;

public sealed class QueryParameterHintResolverTests
{
    [Fact]
    public void Resolve_InfersIntegerForIdParameter()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM users WHERE customer_id = @customer_id",
            new QueryParameterPlaceholder("@customer_id", QueryParameterPlaceholderKind.Named));

        Assert.Equal("integer", hint.TypeLabel);
        Assert.Equal("42", hint.ExampleValue);
    }

    [Fact]
    public void Resolve_InfersBooleanForActiveParameter()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM users WHERE is_active = @is_active",
            new QueryParameterPlaceholder("@is_active", QueryParameterPlaceholderKind.Named));

        Assert.Equal("boolean", hint.TypeLabel);
        Assert.Equal("true", hint.ExampleValue);
    }

    [Fact]
    public void Resolve_InfersDateForDateParameter()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM orders WHERE created_at >= :start_date",
            new QueryParameterPlaceholder(":start_date", QueryParameterPlaceholderKind.Named));

        Assert.Equal("date/time", hint.TypeLabel);
        Assert.Equal("2026-01-31", hint.ExampleValue);
    }

    [Fact]
    public void Resolve_InfersTextForLikeContext()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM users WHERE name LIKE ?",
            new QueryParameterPlaceholder("?", QueryParameterPlaceholderKind.Positional, 1));

        Assert.Equal("text", hint.TypeLabel);
        Assert.Equal("sample", hint.ExampleValue);
    }

    [Fact]
    public void Resolve_PrefersSuggestedIntegerBindingFromVisualPipeline()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM users WHERE customer_id = @customer_id",
            new QueryParameterPlaceholder("@customer_id", QueryParameterPlaceholderKind.Named),
            new QueryParameter("@customer_id", 108));

        Assert.Equal("integer", hint.TypeLabel);
        Assert.Equal("108", hint.ExampleValue);
        Assert.Contains("pipeline visual", hint.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@customer_id", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_PrefersSuggestedDateTimeBindingFromVisualPipeline()
    {
        DateTime value = new(2026, 4, 22, 13, 45, 0, DateTimeKind.Utc);
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM orders WHERE created_at >= :start_date",
            new QueryParameterPlaceholder(":start_date", QueryParameterPlaceholderKind.Named),
            new QueryParameter(":start_date", value));

        Assert.Equal("date/time", hint.TypeLabel);
        Assert.Equal("2026-04-22T13:45:00.0000000Z", hint.ExampleValue);
        Assert.Contains(":start_date", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ExtractsSourceReferenceFromSqlContext()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM orders o WHERE o.customer_id = @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named));

        Assert.Contains("o.customer_id", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_CombinesVisualBindingAndSqlSourceContext()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM orders o WHERE o.customer_id = @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named),
            new QueryParameter("@p0", 108));

        Assert.Contains("@p0", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("o.customer_id", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_UsesMetadataToInferColumnTypeAndFullOrigin()
    {
        DbMetadata metadata = BuildMetadata();

        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM public.orders o WHERE o.customer_id = @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named),
            suggestedParameter: null,
            structuralContext: null,
            metadata,
            DatabaseProvider.Postgres);

        Assert.Equal("integer", hint.TypeLabel);
        Assert.Contains("public.orders.customer_id", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("integer", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_UsesMetadataForBooleanColumnWhenNoSuggestedValueExists()
    {
        DbMetadata metadata = BuildMetadata();

        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM public.orders o WHERE o.is_active = @flag",
            new QueryParameterPlaceholder("@flag", QueryParameterPlaceholderKind.Named),
            suggestedParameter: null,
            structuralContext: null,
            metadata,
            DatabaseProvider.Postgres);

        Assert.Equal("boolean", hint.TypeLabel);
        Assert.Contains("public.orders.is_active", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_UsesColumnDefaultAsExampleValueWhenMetadataProvidesOne()
    {
        DbMetadata metadata = BuildMetadata();

        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM public.orders o WHERE o.status = @status",
            new QueryParameterPlaceholder("@status", QueryParameterPlaceholderKind.Named),
            suggestedParameter: null,
            structuralContext: null,
            metadata,
            DatabaseProvider.Postgres);

        Assert.Equal("text", hint.TypeLabel);
        Assert.Equal("pending", hint.ExampleValue);
        Assert.Contains("default", hint.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_IncludesColumnCommentInMetadataContext()
    {
        DbMetadata metadata = BuildMetadata();

        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM public.orders o WHERE o.status = @status",
            new QueryParameterPlaceholder("@status", QueryParameterPlaceholderKind.Named),
            suggestedParameter: null,
            structuralContext: null,
            metadata,
            DatabaseProvider.Postgres);

        Assert.Contains("Workflow status", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_PrefersStructuralMetadataWhenProvided()
    {
        DbMetadata metadata = BuildMetadata();

        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM something_else WHERE x = @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named),
            suggestedParameter: null,
            new QueryExecutionParameterContext(
                BindingLabel: "@p0",
                SourceReference: "public.orders.customer_id",
                TableRef: "public.orders",
                ColumnName: "customer_id",
                ContextLabel: "Origem estrutural: public.orders.customer_id"),
            metadata,
            DatabaseProvider.Postgres);

        Assert.Equal("integer", hint.TypeLabel);
        Assert.Contains("public.orders.customer_id", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_CombinesSuggestedValueWithStructuralContext()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM x WHERE y = @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named),
            new QueryParameter("@p0", 108),
            new QueryExecutionParameterContext(
                BindingLabel: "@p0",
                SourceReference: "public.orders.customer_id",
                TableRef: "public.orders",
                ColumnName: "customer_id",
                ContextLabel: "Origem estrutural: public.orders.customer_id"));

        Assert.Equal("integer", hint.TypeLabel);
        Assert.Equal("108", hint.ExampleValue);
        Assert.Contains("public.orders.customer_id", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_DescribesAggregateStructuralContext()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM x HAVING SUM(total) > @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named),
            new QueryParameter("@p0", 500),
            new QueryExecutionParameterContext(
                BindingLabel: "@p0",
                SourceReference: "public.orders.total",
                TableRef: "public.orders",
                ColumnName: "total",
                ContextLabel: "Origem estrutural: public.orders.total",
                ExpressionKind: "aggregate",
                SourceCount: 1,
                SourceReferences: ["public.orders.total"]));

        Assert.Contains("agregada", hint.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_DescribesWindowStructuralContext()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM x QUALIFY LAG(status) <> @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named),
            new QueryParameter("@p0", "pending"),
            new QueryExecutionParameterContext(
                BindingLabel: "@p0",
                SourceReference: "public.orders.status",
                TableRef: null,
                ColumnName: null,
                ContextLabel: "Origens estruturais: public.orders.status, public.orders.customer_id",
                ExpressionKind: "window",
                SourceCount: 2,
                SourceReferences: ["public.orders.status", "public.orders.customer_id"]));

        Assert.Contains("janela", hint.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("janela particionada", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_DescribesCompositeStructuralContext()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM x WHERE CONCAT(first_name, last_name) = @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named),
            new QueryParameter("@p0", "John Doe"),
            new QueryExecutionParameterContext(
                BindingLabel: "@p0",
                SourceReference: "public.orders.first_name",
                TableRef: null,
                ColumnName: null,
                ContextLabel: "Origens estruturais: public.orders.first_name, public.orders.last_name",
                ExpressionKind: "concat",
                SourceCount: 2,
                SourceReferences: ["public.orders.first_name", "public.orders.last_name"]));

        Assert.Contains("concatenacao", hint.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valor concatenado", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_DescribesAggregateStringStructuralContext()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM x HAVING STRING_AGG(status, ',') = @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named),
            new QueryParameter("@p0", "paid,shipped"),
            new QueryExecutionParameterContext(
                BindingLabel: "@p0",
                SourceReference: "public.orders.status",
                TableRef: null,
                ColumnName: null,
                ContextLabel: "Origens estruturais: public.orders.status, public.orders.created_at",
                ExpressionKind: "aggregate-string",
                SourceCount: 2,
                SourceReferences: ["public.orders.status", "public.orders.created_at"]));

        Assert.Contains("agregacao textual ordenada", hint.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agregacao textual ordenada", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public.orders.status", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_DescribesConditionalFallbackStructuralContext()
    {
        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            "SELECT * FROM x WHERE COALESCE(nickname, display_name) = @p0",
            new QueryParameterPlaceholder("@p0", QueryParameterPlaceholderKind.Named),
            new QueryParameter("@p0", "guest"),
            new QueryExecutionParameterContext(
                BindingLabel: "@p0",
                SourceReference: "public.orders.nickname",
                TableRef: null,
                ColumnName: null,
                ContextLabel: "Condicional sobre public.orders.nickname | fallback de public.orders.display_name",
                ExpressionKind: "conditional",
                SourceCount: 2,
                SourceReferences: ["public.orders.nickname", "public.orders.display_name"]));

        Assert.Contains("fallback", hint.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fallback de public.orders.display_name", hint.ContextLabel, StringComparison.OrdinalIgnoreCase);
    }

    private static DbMetadata BuildMetadata()
    {
        TableMetadata orders = new(
            "public",
            "orders",
            TableKind.Table,
            null,
            [
                new ColumnMetadata("customer_id", "integer", "int4", false, false, true, false, true, 1),
                new ColumnMetadata("is_active", "boolean", "bool", false, false, false, false, false, 2),
                new ColumnMetadata("status", "text", "text", false, false, false, false, false, 3, "'pending'", null, null, null, "Workflow status"),
            ],
            [],
            [],
            []);

        return new DbMetadata(
            "app",
            DatabaseProvider.Postgres,
            "16",
            DateTimeOffset.UtcNow,
            [new SchemaMetadata("public", [orders])],
            []);
    }
}
