using Microsoft.Extensions.DependencyInjection;
using AkkornStudio.Metadata;
using AkkornStudio.QueryEngine;
using AkkornStudio.Registry;

namespace AkkornStudio;

public sealed class AkkornStudioServiceOptions
{
    public IEnumerable<IProviderRegistration>? ProviderRegistrations { get; set; }
    public IEnumerable<OrchestratorRegistration>? OrchestratorRegistrations { get; set; }
    public IEnumerable<InspectorRegistration>? InspectorRegistrations { get; set; }
    public Func<ICanvasTableTracker>? CanvasTableTrackerFactory { get; set; }
}

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all AkkornStudio services.
    /// Call this in Avalonia's App.axaml.cs or the composition root.
    ///
    /// <code>
    /// services.AddAkkornStudio();
    /// </code>
    /// </summary>
    public static IServiceCollection AddAkkornStudio(this IServiceCollection services) =>
        AddAkkornStudio(services, configure: null);

    /// <summary>
    /// Registers all AkkornStudio services with optional overrides for default registrations.
    /// </summary>
    public static IServiceCollection AddAkkornStudio(
        this IServiceCollection services,
        Action<AkkornStudioServiceOptions>? configure
    )
    {
        var options = new AkkornStudioServiceOptions();
        configure?.Invoke(options);

        IReadOnlyList<IProviderRegistration> providerRegistrations = ResolveRegistrations(
            options.ProviderRegistrations,
            DefaultProviderRegistrations.CreateAll,
            nameof(AkkornStudioServiceOptions.ProviderRegistrations)
        );
        IReadOnlyList<OrchestratorRegistration> orchestratorRegistrations = ResolveRegistrations(
            options.OrchestratorRegistrations,
            DbOrchestratorFactory.CreateDefaultRegistrations,
            nameof(AkkornStudioServiceOptions.OrchestratorRegistrations)
        );
        IReadOnlyList<InspectorRegistration> inspectorRegistrations = ResolveRegistrations(
            options.InspectorRegistrations,
            DatabaseInspectorFactory.CreateDefaultRegistrations,
            nameof(AkkornStudioServiceOptions.InspectorRegistrations)
        );
        Func<ICanvasTableTracker> canvasTableTrackerFactory =
            options.CanvasTableTrackerFactory ?? (() => new CanvasTableTracker());

        services.AddSingleton<IProviderRegistry>(
            _ => new ProviderRegistry(providerRegistrations)
        );
        services.AddSingleton<IDatabaseInspectorFactory>(_ => new DatabaseInspectorFactory(inspectorRegistrations));
        services.AddSingleton<ICanvasTableTracker>(_ => canvasTableTrackerFactory());
        services.AddSingleton<IDbOrchestratorFactory>(_ => new DbOrchestratorFactory(orchestratorRegistrations));

        // ActiveConnectionContext is a singleton so the canvas always shares
        // the same live connection across all view-models.
        services.AddSingleton<ActiveConnectionContext>();

        // Use a proxy registry that always delegates to the current
        // ActiveConnectionContext registry so provider switches do not stale
        // long-lived references resolved from DI.
        services.AddSingleton<ISqlFunctionRegistry, ActiveConnectionSqlFunctionRegistryProxy>();

        services.AddTransient<QueryBuilderService>(sp =>
            sp.GetRequiredService<ActiveConnectionContext>().QueryBuilder
        );

        return services;
    }

    private static IReadOnlyList<TRegistration> ResolveRegistrations<TRegistration>(
        IEnumerable<TRegistration>? configuredRegistrations,
        Func<IReadOnlyList<TRegistration>> defaultFactory,
        string optionName
    )
    {
        IReadOnlyList<TRegistration> registrations = configuredRegistrations?.ToArray() ?? defaultFactory();
        if (registrations.Count == 0)
            throw new InvalidOperationException(
                $"AkkornStudio service option '{optionName}' must contain at least one registration."
            );

        return registrations;
    }

    private sealed class ActiveConnectionSqlFunctionRegistryProxy(
        ActiveConnectionContext connectionContext
    ) : ISqlFunctionRegistry
    {
        public string GetFunction(string functionName, params string[] args) =>
            connectionContext.FunctionRegistry.GetFunction(functionName, args);

        public bool IsSupported(string functionName) =>
            connectionContext.FunctionRegistry.IsSupported(functionName);

        public IReadOnlyList<PortabilityWarning> CheckPortability(IEnumerable<string> functionNames) =>
            connectionContext.FunctionRegistry.CheckPortability(functionNames);
    }
}
