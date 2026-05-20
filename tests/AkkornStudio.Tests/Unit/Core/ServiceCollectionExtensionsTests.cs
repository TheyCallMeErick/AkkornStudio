using Microsoft.Extensions.DependencyInjection;
using AkkornStudio;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.QueryEngine;
using AkkornStudio.Registry;
using Xunit;

namespace AkkornStudio.Tests.Unit.Core;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddAkkornStudio_Default_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddAkkornStudio();

        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IProviderRegistry>());
        Assert.NotNull(provider.GetService<IDbOrchestratorFactory>());
        Assert.NotNull(provider.GetService<IDatabaseInspectorFactory>());
        Assert.NotNull(provider.GetService<ICanvasTableTracker>());
        Assert.NotNull(provider.GetService<ActiveConnectionContext>());
        Assert.NotNull(provider.GetService<ISqlFunctionRegistry>());
        Assert.NotNull(provider.GetService<QueryBuilderService>());
    }

    [Fact]
    public async Task AddAkkornStudio_AllowsOverridingCanvasTableTrackerFactory()
    {
        var services = new ServiceCollection();
        services.AddAkkornStudio(options =>
        {
            options.CanvasTableTrackerFactory = () => new StubCanvasTableTracker();
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        ICanvasTableTracker tracker = provider.GetRequiredService<ICanvasTableTracker>();

        Assert.IsType<StubCanvasTableTracker>(tracker);
    }

    [Fact]
    public async Task AddAkkornStudio_AllowsOverridingInspectorRegistrations()
    {
        var services = new ServiceCollection();
        services.AddAkkornStudio(options =>
        {
            options.InspectorRegistrations =
            [
                new InspectorRegistration(
                    DatabaseProvider.Postgres,
                    cfg => new StubInspector(cfg.Provider)
                ),
            ];
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IDatabaseInspectorFactory factory = provider.GetRequiredService<IDatabaseInspectorFactory>();

        IDatabaseInspector inspector = factory.Create(BuildConfig(DatabaseProvider.Postgres));
        Assert.IsType<StubInspector>(inspector);
    }

    [Fact]
    public void AddAkkornStudio_WithEmptyProviderRegistrations_Throws()
    {
        var services = new ServiceCollection();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddAkkornStudio(options =>
            {
                options.ProviderRegistrations = [];
            })
        );

        Assert.Contains(nameof(AkkornStudioServiceOptions.ProviderRegistrations), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAkkornStudio_WithEmptyOrchestratorRegistrations_Throws()
    {
        var services = new ServiceCollection();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddAkkornStudio(options =>
            {
                options.OrchestratorRegistrations = [];
            })
        );

        Assert.Contains(nameof(AkkornStudioServiceOptions.OrchestratorRegistrations), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAkkornStudio_WithEmptyInspectorRegistrations_Throws()
    {
        var services = new ServiceCollection();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddAkkornStudio(options =>
            {
                options.InspectorRegistrations = [];
            })
        );

        Assert.Contains(nameof(AkkornStudioServiceOptions.InspectorRegistrations), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAkkornStudio_FunctionRegistryBinding_TracksProviderSwitchWithoutStaleInstance()
    {
        var services = new ServiceCollection();
        services.AddAkkornStudio(options =>
        {
            options.OrchestratorRegistrations =
            [
                new OrchestratorRegistration(
                    DatabaseProvider.SQLite,
                    config => new StubOrchestrator(config)
                ),
            ];
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        ActiveConnectionContext context = provider.GetRequiredService<ActiveConnectionContext>();
        ISqlFunctionRegistry registry = provider.GetRequiredService<ISqlFunctionRegistry>();

        Assert.True(registry.IsSupported(SqlFn.CurrentDate));
        Assert.Equal("CURRENT_DATE", registry.GetFunction(SqlFn.CurrentDate));
        Assert.Empty(registry.CheckPortability([SqlFn.StringAgg]));

        await context.SwitchAsync(BuildConfig(DatabaseProvider.SQLite));

        Assert.True(registry.IsSupported(SqlFn.CurrentDate));
        Assert.Equal("DATE('now')", registry.GetFunction(SqlFn.CurrentDate));
        IReadOnlyList<PortabilityWarning> warnings = registry.CheckPortability([SqlFn.StringAgg]);
        Assert.Single(warnings);
        Assert.Equal(SqlFn.StringAgg, warnings[0].FunctionName);
    }

    private static ConnectionConfig BuildConfig(DatabaseProvider provider) =>
        new(
            provider,
            Host: "localhost",
            Port: 5432,
            Database: "db",
            Username: "user",
            Password: "pwd"
        );

    private sealed class StubCanvasTableTracker : ICanvasTableTracker
    {
        private readonly List<string> _tables = [];

        public void Add(string fullTableName) => _tables.Add(fullTableName);
        public bool Remove(string fullTableName) => _tables.Remove(fullTableName);
        public bool Contains(string fullTableName) => _tables.Contains(fullTableName, StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> Snapshot() => _tables.ToList();
        public int Count => _tables.Count;
    }

    private sealed class StubInspector(DatabaseProvider provider) : IDatabaseInspector
    {
        public DatabaseProvider Provider => provider;

        public Task<DbMetadata> InspectAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TableMetadata> InspectTableAsync(string schema, string table, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ForeignKeyRelation>> GetForeignKeysAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubOrchestrator(ConnectionConfig config) : IDbOrchestrator
    {
        public DatabaseProvider Provider => config.Provider;
        public ConnectionConfig Config => config;

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default) =>
            Task.FromResult(new ConnectionTestResult(true));

        public Task<DatabaseSchema> GetSchemaAsync(CancellationToken ct = default) =>
            Task.FromResult(new DatabaseSchema("stub", config.Provider, []));

        public Task<PreviewResult> ExecutePreviewAsync(string sql, int maxRows = PreviewExecutionOptions.UseConfiguredDefault, CancellationToken ct = default) =>
            Task.FromResult(new PreviewResult(true, Data: new DataTable()));

        public Task<DdlExecutionResult> ExecuteDdlAsync(string sql, bool stopOnError = true, CancellationToken ct = default) =>
            Task.FromResult(new DdlExecutionResult(true, []));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
