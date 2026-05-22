using System.Data;
using AkkornStudio;
using AkkornStudio.Core;
using AkkornStudio.Registry;

namespace AkkornStudio.Tests.Unit.Core;

public sealed class ActiveConnectionContextTests
{
    [Fact]
    public async Task SwitchAsync_WhenFirstValidationFailsTransiently_RetriesAndSucceeds()
    {
        var orchestrator = new SequencedConnectionTestOrchestrator(
            BuildConfig(DatabaseProvider.Postgres),
            [
                new ConnectionTestResult(false, "Network timeout while opening connection."),
                new ConnectionTestResult(true),
            ]
        );
        var factory = new StubOrchestratorFactory(orchestrator);
        var sut = new ActiveConnectionContext(
            ProviderRegistry.CreateDefault(),
            factory,
            retryDelayFactory: _ => TimeSpan.Zero
        );

        await sut.SwitchAsync(BuildConfig(DatabaseProvider.Postgres));

        Assert.Equal(2, orchestrator.TestConnectionCallCount);
        Assert.Equal(1, factory.CreateCallCount);
    }

    [Fact]
    public async Task SwitchAsync_WhenValidationFailsWithPermanentError_DoesNotRetry()
    {
        var orchestrator = new SequencedConnectionTestOrchestrator(
            BuildConfig(DatabaseProvider.Postgres),
            [
                new ConnectionTestResult(false, "Authentication failed for user."),
            ]
        );
        var factory = new StubOrchestratorFactory(orchestrator);
        var sut = new ActiveConnectionContext(
            ProviderRegistry.CreateDefault(),
            factory,
            retryDelayFactory: _ => TimeSpan.Zero
        );

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SwitchAsync(BuildConfig(DatabaseProvider.Postgres))
        );

        Assert.Contains("Connection failed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, orchestrator.TestConnectionCallCount);
    }

    [Fact]
    public async Task SwitchAsync_WhenTransientFailurePersists_StopsAfterMaxAttempts()
    {
        var orchestrator = new SequencedConnectionTestOrchestrator(
            BuildConfig(DatabaseProvider.Postgres),
            [
                new ConnectionTestResult(false, "Connection timeout."),
                new ConnectionTestResult(false, "Connection timeout."),
                new ConnectionTestResult(false, "Connection timeout."),
                new ConnectionTestResult(true),
            ]
        );
        var factory = new StubOrchestratorFactory(orchestrator);
        var sut = new ActiveConnectionContext(
            ProviderRegistry.CreateDefault(),
            factory,
            retryDelayFactory: _ => TimeSpan.Zero
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SwitchAsync(BuildConfig(DatabaseProvider.Postgres))
        );

        Assert.Equal(3, orchestrator.TestConnectionCallCount);
    }

    private static ConnectionConfig BuildConfig(DatabaseProvider provider) =>
        new(
            provider,
            Host: "localhost",
            Port: provider == DatabaseProvider.SQLite ? 0 : 5432,
            Database: provider == DatabaseProvider.SQLite ? "test.db" : "db",
            Username: "user",
            Password: "pass"
        );

    private sealed class StubOrchestratorFactory(IDbOrchestrator orchestrator) : IDbOrchestratorFactory
    {
        public int CreateCallCount { get; private set; }

        public IDbOrchestrator Create(ConnectionConfig config)
        {
            CreateCallCount++;
            return orchestrator;
        }

        public Func<ConnectionConfig, IDbOrchestrator>? Register(
            DatabaseProvider provider,
            Func<ConnectionConfig, IDbOrchestrator> factory
        ) => null;

        public bool IsRegistered(DatabaseProvider provider) => true;
    }

    private sealed class SequencedConnectionTestOrchestrator(
        ConnectionConfig config,
        IReadOnlyList<ConnectionTestResult> results
    ) : IDbOrchestrator
    {
        private int _testIndex;
        private readonly IReadOnlyList<ConnectionTestResult> _results =
            results.Count > 0
                ? results
                : throw new ArgumentException("At least one test result is required.", nameof(results));

        public int TestConnectionCallCount { get; private set; }

        public DatabaseProvider Provider => config.Provider;
        public ConnectionConfig Config => config;

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
        {
            TestConnectionCallCount++;
            int index = Math.Min(_testIndex, _results.Count - 1);
            _testIndex++;
            return Task.FromResult(_results[index]);
        }

        public Task<DatabaseSchema> GetSchemaAsync(CancellationToken ct = default) =>
            Task.FromResult(new DatabaseSchema("stub", Provider, []));

        public Task<PreviewResult> ExecutePreviewAsync(
            string sql,
            int maxRows = PreviewExecutionOptions.UseConfiguredDefault,
            CancellationToken ct = default
        ) => Task.FromResult(new PreviewResult(true, Data: new DataTable()));

        public Task<DdlExecutionResult> ExecuteDdlAsync(
            string sql,
            bool stopOnError = true,
            CancellationToken ct = default
        ) => Task.FromResult(new DdlExecutionResult(true, []));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
