using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using Xunit;

namespace AkkornStudio.Tests.Unit.Metadata;

public sealed class MetadataServiceCacheBehaviorTests
{
    [Fact]
    public void MetadataServiceOptions_UsesEnvironmentVariable_WhenNoExplicitValueProvided()
    {
        string? previous = Environment.GetEnvironmentVariable(MetadataServiceOptions.CacheTtlSecondsEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(MetadataServiceOptions.CacheTtlSecondsEnvVar, "42");

            var options = new MetadataServiceOptions();

            Assert.Equal(TimeSpan.FromSeconds(42), options.CacheTtl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MetadataServiceOptions.CacheTtlSecondsEnvVar, previous);
        }
    }

    [Fact]
    public async Task GetMetadataAsync_CacheFresh_DoesNotCallInspectorAgain()
    {
        var inspector = new CountingInspector(() => Task.FromResult(MetadataFixtures.EcommerceDb()));
        using var sut = new MetadataService(inspector);

        _ = await sut.GetMetadataAsync();
        _ = await sut.GetMetadataAsync();

        Assert.Equal(1, inspector.InspectCalls);
    }

    [Fact]
    public async Task GetMetadataAsync_CacheExpired_CallsInspectorAgain()
    {
        string? previous = Environment.GetEnvironmentVariable(MetadataServiceOptions.CacheTtlSecondsEnvVar);
        Environment.SetEnvironmentVariable(MetadataServiceOptions.CacheTtlSecondsEnvVar, "3600");

        var inspector = new CountingInspector(() => Task.FromResult(MetadataFixtures.EcommerceDb()));
        IOptions<MetadataServiceOptions> options = Options.Create(
            new MetadataServiceOptions
            {
                CacheTtl = TimeSpan.FromMilliseconds(1),
            }
        );

        try
        {
            using var sut = new MetadataService(inspector, options);
            _ = await sut.GetMetadataAsync();
            await Task.Delay(10);
            _ = await sut.GetMetadataAsync();

            Assert.Equal(2, inspector.InspectCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MetadataServiceOptions.CacheTtlSecondsEnvVar, previous);
        }
    }

    [Fact]
    public async Task AddMetadataIntelligence_RegistersJoinSuggestionEngine_AndMetadataServiceFactory()
    {
        var services = new ServiceCollection();
        services.AddAkkornStudio();
        services.AddMetadataIntelligence();

        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IJoinSuggestionEngine>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<MetadataService>());
    }

    [Fact]
    public async Task AddMetadataIntelligence_ConfigureCallback_OverridesOptionDefaults()
    {
        var services = new ServiceCollection();
        services.AddAkkornStudio();
        services.AddMetadataIntelligence(opts => opts.CacheTtl = TimeSpan.FromSeconds(9));

        await using ServiceProvider provider = services.BuildServiceProvider();
        IOptions<MetadataServiceOptions> options = provider.GetRequiredService<IOptions<MetadataServiceOptions>>();

        Assert.Equal(TimeSpan.FromSeconds(9), options.Value.CacheTtl);
    }

    [Fact]
    public async Task AddMetadataIntelligence_AllowsCustomJoinSuggestionEngineFactory()
    {
        var customEngine = new RecordingJoinSuggestionEngine();
        var services = new ServiceCollection();
        services.AddAkkornStudio();
        services.AddMetadataIntelligence(
            configure: null,
            configureIntelligence: intelligence =>
            {
                intelligence.JoinSuggestionEngineFactory = _ => customEngine;
            }
        );

        await using ServiceProvider provider = services.BuildServiceProvider();
        IJoinSuggestionEngine resolved = provider.GetRequiredService<IJoinSuggestionEngine>();

        Assert.Same(customEngine, resolved);
    }

    [Fact]
    public async Task GetMetadataAsync_ForceRefresh_BypassesCache()
    {
        var inspector = new CountingInspector(() => Task.FromResult(MetadataFixtures.EcommerceDb()));
        using var sut = new MetadataService(inspector);

        _ = await sut.GetMetadataAsync();
        _ = await sut.GetMetadataAsync(forceRefresh: true);

        Assert.Equal(2, inspector.InspectCalls);
    }

    [Fact]
    public async Task GetMetadataAsync_ConcurrentCalls_InspectorCalledOnce()
    {
        var inspector = new CountingInspector(async () =>
        {
            await Task.Delay(50);
            return MetadataFixtures.EcommerceDb();
        });
        using var sut = new MetadataService(inspector);

        Task<DbMetadata>[] calls = Enumerable.Range(0, 5).Select(_ => sut.GetMetadataAsync()).ToArray();
        _ = await Task.WhenAll(calls);

        Assert.Equal(1, inspector.InspectCalls);
    }

    [Fact]
    public async Task InvalidateCache_AfterPopulate_NextCallRefetches()
    {
        var inspector = new CountingInspector(() => Task.FromResult(MetadataFixtures.EcommerceDb()));
        using var sut = new MetadataService(inspector);

        _ = await sut.GetMetadataAsync();
        sut.InvalidateCache();
        _ = await sut.GetMetadataAsync();

        Assert.Equal(2, inspector.InspectCalls);
    }

    [Fact]
    public async Task GetMetadataAsync_ActiveConnectionChanged_RebindsInspectorAndInvalidatesCache()
    {
        ConnectionConfig activeConfig = BuildConnectionConfig("db_a");
        var factory = new SwitchingInspectorFactory();
        IDatabaseInspector initialInspector = factory.Create(activeConfig);

        using var sut = new MetadataService(
            initialInspector,
            inspectorFactory: factory,
            connectionConfigResolver: () => activeConfig,
            initialInspectorBindingConfig: activeConfig);

        DbMetadata first = await sut.GetMetadataAsync();
        Assert.Equal("db_a", first.DatabaseName);

        activeConfig = activeConfig with { Database = "db_b" };
        DbMetadata second = await sut.GetMetadataAsync();

        Assert.Equal("db_b", second.DatabaseName);
        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(2, factory.TotalInspectCalls);
    }

    [Fact]
    public async Task SuggestJoinsAsync_UsesInjectedJoinEngine_WithTrackedCanvasTables()
    {
        var metadata = MetadataFixtures.EcommerceDb();
        var joinEngine = new RecordingJoinSuggestionEngine();
        var tracker = new CanvasTableTracker();
        var cache = new StaticSnapshotCache(metadata);
        var inspector = new CountingInspector(() => Task.FromResult(metadata));

        tracker.Add("public.orders");

        using var sut = new MetadataService(
            inspector,
            options: null,
            logger: null,
            canvasTableTracker: tracker,
            joinSuggestionEngine: joinEngine,
            snapshotCache: cache
        );

        IReadOnlyList<JoinSuggestion> result = await sut.SuggestJoinsAsync("public.customers");

        Assert.Single(result);
        Assert.Equal("public.customers", joinEngine.CapturedNewTable);
        Assert.Contains("public.orders", joinEngine.CapturedCanvasTables, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, inspector.InspectCalls);
    }

    [Fact]
    public async Task RefreshTableAsync_DelegatesTableReplacementToSnapshotCache()
    {
        var metadata = MetadataFixtures.EcommerceDb();
        var cache = new TrackingSnapshotCache(metadata);
        var inspector = new FixedTableInspector(metadata);

        using var sut = new MetadataService(
            inspector,
            options: null,
            logger: null,
            canvasTableTracker: null,
            joinSuggestionEngine: null,
            snapshotCache: cache
        );

        _ = await sut.GetMetadataAsync();
        TableMetadata refreshed = await sut.RefreshTableAsync("public", "orders");

        Assert.NotNull(cache.ReplacedTable);
        Assert.Equal(refreshed.FullName, cache.ReplacedTable!.FullName);
    }

    private sealed class CountingInspector(Func<Task<DbMetadata>> inspectFactory) : IDatabaseInspector
    {
        private readonly Func<Task<DbMetadata>> _inspectFactory = inspectFactory;
        private readonly ConcurrentDictionary<string, TableMetadata> _tables = new(
            StringComparer.OrdinalIgnoreCase
        );
        private int _inspectCalls;

        public DatabaseProvider Provider => DatabaseProvider.Postgres;
        public int InspectCalls => _inspectCalls;

        public async Task<DbMetadata> InspectAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _inspectCalls);
            DbMetadata metadata = await _inspectFactory();

            foreach (TableMetadata table in metadata.AllTables)
                _tables[table.FullName] = table;

            return metadata;
        }

        public Task<TableMetadata> InspectTableAsync(
            string schema,
            string table,
            CancellationToken ct = default
        )
        {
            string fullName = $"{schema}.{table}";
            if (_tables.TryGetValue(fullName, out TableMetadata? found))
                return Task.FromResult(found);

            throw new InvalidOperationException($"Table '{fullName}' not found in test inspector.");
        }

        public Task<IReadOnlyList<ForeignKeyRelation>> GetForeignKeysAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ForeignKeyRelation>>([]);
    }

    private sealed class SwitchingInspectorFactory : IDatabaseInspectorFactory
    {
        private readonly List<SwitchingInspector> _created = [];

        public int CreateCalls => _created.Count;
        public int TotalInspectCalls => _created.Sum(inspector => inspector.InspectCalls);

        public IDatabaseInspector Create(ConnectionConfig config)
        {
            var inspector = new SwitchingInspector(config);
            _created.Add(inspector);
            return inspector;
        }
    }

    private sealed class SwitchingInspector(ConnectionConfig config) : IDatabaseInspector
    {
        private int _inspectCalls;
        public DatabaseProvider Provider => config.Provider;
        public int InspectCalls => _inspectCalls;

        public Task<DbMetadata> InspectAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _inspectCalls);
            DbMetadata template = MetadataFixtures.EcommerceDb();
            return Task.FromResult(template with
            {
                DatabaseName = config.Database,
                Provider = config.Provider,
                CapturedAt = DateTimeOffset.UtcNow,
            });
        }

        public Task<TableMetadata> InspectTableAsync(string schema, string table, CancellationToken ct = default)
        {
            TableMetadata fallback = MetadataFixtures.EcommerceDb().FindTable(schema, table)
                ?? new TableMetadata(schema, table, TableKind.Table, null, [], [], [], []);
            return Task.FromResult(fallback);
        }

        public Task<IReadOnlyList<ForeignKeyRelation>> GetForeignKeysAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ForeignKeyRelation>>([]);
    }

    private sealed class RecordingJoinSuggestionEngine : IJoinSuggestionEngine
    {
        public string CapturedNewTable { get; private set; } = string.Empty;
        public List<string> CapturedCanvasTables { get; } = [];

        public IReadOnlyList<JoinSuggestion> Suggest(
            DbMetadata metadata,
            string newTable,
            IEnumerable<string> canvasTables
        )
        {
            CapturedNewTable = newTable;
            CapturedCanvasTables.Clear();
            CapturedCanvasTables.AddRange(canvasTables);

            return
            [
                new JoinSuggestion(
                    ExistingTable: CapturedCanvasTables.FirstOrDefault() ?? "public.orders",
                    NewTable: newTable,
                    JoinType: "INNER",
                    LeftColumn: "public.orders.customer_id",
                    RightColumn: "public.customers.id",
                    OnClause: "public.orders.customer_id = public.customers.id",
                    Score: 0.9,
                    Confidence: JoinConfidence.HeuristicStrong,
                    Rationale: "test"
                ),
            ];
        }
    }

    private sealed class StaticSnapshotCache(DbMetadata metadata) : IMetadataSnapshotCache
    {
        private readonly DbMetadata _metadata = metadata;

        public Task<DbMetadata> GetOrLoadAsync(
            Func<CancellationToken, Task<DbMetadata>> loader,
            bool forceRefresh,
            CancellationToken ct = default
        ) => Task.FromResult(_metadata);

        public void ReplaceTable(TableMetadata fresh) { }

        public void Invalidate() { }

        public void Dispose() { }
    }

    private sealed class TrackingSnapshotCache(DbMetadata metadata) : IMetadataSnapshotCache
    {
        private readonly DbMetadata _metadata = metadata;
        public TableMetadata? ReplacedTable { get; private set; }

        public Task<DbMetadata> GetOrLoadAsync(
            Func<CancellationToken, Task<DbMetadata>> loader,
            bool forceRefresh,
            CancellationToken ct = default
        ) => Task.FromResult(_metadata);

        public void ReplaceTable(TableMetadata fresh) => ReplacedTable = fresh;

        public void Invalidate() { }

        public void Dispose() { }
    }

    private sealed class FixedTableInspector(DbMetadata metadata) : IDatabaseInspector
    {
        private readonly DbMetadata _metadata = metadata;

        public DatabaseProvider Provider => DatabaseProvider.Postgres;

        public Task<DbMetadata> InspectAsync(CancellationToken ct = default) =>
            Task.FromResult(_metadata);

        public Task<TableMetadata> InspectTableAsync(
            string schema,
            string table,
            CancellationToken ct = default
        )
        {
            TableMetadata found =
                _metadata.FindTable(schema, table)
                ?? throw new InvalidOperationException($"Table '{schema}.{table}' not found in metadata.");

            return Task.FromResult(found);
        }

        public Task<IReadOnlyList<ForeignKeyRelation>> GetForeignKeysAsync(CancellationToken ct = default) =>
            Task.FromResult(_metadata.AllForeignKeys);
    }

    private static ConnectionConfig BuildConnectionConfig(string database) =>
        new(
            Provider: DatabaseProvider.Postgres,
            Host: "localhost",
            Port: 5432,
            Database: database,
            Username: "tester",
            Password: "secret");
}
