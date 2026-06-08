using AkkornStudio.Core;
using AkkornStudio.Registry;

namespace AkkornStudio.Expressions;

/// <summary>
/// Passed through every expression during compilation.
/// Carries the provider dialect and the function registry so expressions
/// can produce correct SQL without knowing the database themselves.
/// </summary>
public sealed class EmitContext(DatabaseProvider provider, ISqlFunctionRegistry registry)
{
    private readonly Providers.Dialects.ISqlDialect _dialect =
        ResolveDialect(new ProviderRegistry(DefaultProviderRegistrations.CreateAll()), provider);

    public DatabaseProvider Provider { get; } = provider;
    public ISqlFunctionRegistry Registry { get; } = registry;

    public EmitContext(DatabaseProvider provider, ISqlFunctionRegistry registry, IProviderRegistry providerRegistry)
        : this(provider, registry)
    {
        ArgumentNullException.ThrowIfNull(providerRegistry);
        _dialect = ResolveDialect(providerRegistry, provider);
    }

    public string QuoteIdentifier(string id) => _dialect.QuoteIdentifier(id);

    public static string QuoteLiteral(string value) => SqlStringUtility.QuoteLiteral(value);

    private static Providers.Dialects.ISqlDialect ResolveDialect(IProviderRegistry providerRegistry, DatabaseProvider provider)
    {
        Providers.Dialects.ISqlDialect? dialect = providerRegistry.GetDialect(provider);
        if (dialect is null)
            throw new InvalidOperationException($"Provider registry returned null dialect for provider '{provider}'.");

        return dialect;
    }
}
