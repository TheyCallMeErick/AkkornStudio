using AkkornStudio.Metadata;
using AkkornStudio.Providers.Dialects;
using AkkornStudio.QueryEngine;
using AkkornStudio.Registry;
using AdvancedEmitContext = AkkornStudio.Expressions.EmitContext;

namespace AkkornStudio.Tests.Unit.Expressions;

public sealed class EmitContextTests
{
    [Fact]
    public void Constructor_DefaultRegistry_QuotesIdentifierForProvider()
    {
        var registry = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        var ctx = new AdvancedEmitContext(DatabaseProvider.Postgres, registry);

        string quoted = ctx.QuoteIdentifier("users");

        Assert.Equal("\"users\"", quoted);
        Assert.Equal(DatabaseProvider.Postgres, ctx.Provider);
        Assert.Same(registry, ctx.Registry);
    }

    [Fact]
    public void Constructor_CustomRegistry_UsesProvidedDialect()
    {
        var registry = new SqlFunctionRegistry(DatabaseProvider.SQLite);
        var ctx = new AdvancedEmitContext(
            DatabaseProvider.SQLite,
            registry,
            new StubProviderRegistry(new StubDialect("<", ">")));

        string quoted = ctx.QuoteIdentifier("table_name");

        Assert.Equal("<table_name>", quoted);
    }

    [Fact]
    public void Constructor_CustomRegistry_Throws_WhenDialectIsNull()
    {
        var registry = new SqlFunctionRegistry(DatabaseProvider.SqlServer);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            _ = new AdvancedEmitContext(
                DatabaseProvider.SqlServer,
                registry,
                new StubProviderRegistry(null)));

        Assert.Equal("Provider registry returned null dialect for provider 'SqlServer'.", ex.Message);
    }

    [Fact]
    public void QuoteLiteral_EscapesSingleQuotes()
    {
        string literal = AdvancedEmitContext.QuoteLiteral("O'Brien");

        Assert.Equal("'O''Brien'", literal);
    }

    private sealed class StubProviderRegistry(ISqlDialect? dialect) : IProviderRegistry
    {
        public void RegisterProvider(DatabaseProvider provider, ISqlDialect dialect, IMetadataQueryProvider metadataProvider, IFunctionFragmentProvider functionFragments)
        {
            _ = provider;
            _ = dialect;
            _ = metadataProvider;
            _ = functionFragments;
        }

        public ISqlFunctionRegistry CreateFunctionRegistry(DatabaseProvider provider) => new SqlFunctionRegistry(provider);

        public QueryBuilderService CreateQueryBuilder(DatabaseProvider provider, string fromTable) =>
            throw new NotSupportedException();

        public ISqlDialect GetDialect(DatabaseProvider provider)
        {
            _ = provider;
            return dialect!;
        }

        public IMetadataQueryProvider GetMetadataProvider(DatabaseProvider provider) => throw new NotSupportedException();

        public IFunctionFragmentProvider GetFunctionFragments(DatabaseProvider provider) => throw new NotSupportedException();

        public bool IsProviderRegistered(DatabaseProvider provider)
        {
            _ = provider;
            return dialect is not null;
        }

        public IReadOnlyList<DatabaseProvider> GetRegisteredProviders() =>
            dialect is null ? [] : [DatabaseProvider.Postgres];
    }

    private sealed class StubDialect(string open, string close) : ISqlDialect
    {
        public string QuoteIdentifier(string identifier) => $"{open}{identifier}{close}";
        public string GetTablesQuery() => "SELECT 1";
        public string GetColumnsQuery() => "SELECT 1";
        public string GetPrimaryKeysQuery() => "SELECT 1";
        public string GetForeignKeysQuery() => "SELECT 1";
        public string WrapWithPreviewLimit(string baseQuery, int maxRows) => baseQuery;
        public string FormatPagination(int? limit, int? offset) => "LIMIT 1";
        public string ApplyQueryHints(string sql, string? queryHints) => sql;
    }
}
