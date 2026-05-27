using System.Data;
using AkkornStudio;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.Preview;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class QuickDataPreviewModalViewModelHardeningTests
{
    [Fact]
    public async Task OpenSqlPreviewAsync_ConcurrentCalls_IgnoresStaleCompletion()
    {
        var orchestrator = new ScriptedOrchestrator(async (sql, _) =>
        {
            if (string.Equals(sql, "slow", StringComparison.Ordinal))
            {
                await Task.Delay(120);
                return Success(sql, "slow");
            }

            await Task.Delay(10);
            return Success(sql, "fast");
        });
        var vm = BuildViewModel(orchestrator);

        Task slowTask = vm.OpenSqlPreviewAsync(
            title: "Slow",
            subtitle: string.Empty,
            sql: "slow",
            connection: BuildConnection(),
            provider: DatabaseProvider.Postgres,
            metadata: null,
            focusTableFullName: null,
            sourceDocumentType: null);
        await Task.Delay(20);
        Task fastTask = vm.OpenSqlPreviewAsync(
            title: "Fast",
            subtitle: string.Empty,
            sql: "fast",
            connection: BuildConnection(),
            provider: DatabaseProvider.Postgres,
            metadata: null,
            focusTableFullName: null,
            sourceDocumentType: null);
        await Task.WhenAll(slowTask, fastTask);

        Assert.Equal("fast", vm.SqlText);
        Assert.Equal("fast", Assert.IsType<string>(vm.ResultData?.Rows[0]["value"]));
    }

    [Fact]
    public async Task OpenSqlPreviewAsync_WhenExecutionFails_DoesNotKeepRelationships()
    {
        var orchestrator = new ScriptedOrchestrator((sql, _) =>
            Task.FromResult(new PreviewResult(
                Success: false,
                Data: null,
                ErrorMessage: $"failure for {sql}",
                ExecutionTime: TimeSpan.FromMilliseconds(1))));
        var vm = BuildViewModel(orchestrator);
        DbMetadata metadata = BuildMetadataWithRelationship();

        await vm.OpenSqlPreviewAsync(
            title: "Fail",
            subtitle: string.Empty,
            sql: "broken_sql",
            connection: BuildConnection(),
            provider: DatabaseProvider.Postgres,
            metadata: metadata,
            focusTableFullName: "public.orders",
            sourceDocumentType: null);

        Assert.True(vm.HasError);
        Assert.Empty(vm.Relationships);
    }

    [Fact]
    public async Task ExecuteCurrentSqlAsync_WhenTablePaginationFails_LeavesTableModeAndPreventsFurtherPaging()
    {
        var orchestrator = new ScriptedOrchestrator((sql, _) =>
        {
            if (sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new PreviewResult(
                    Success: false,
                    Data: null,
                    ErrorMessage: "paging failed",
                    ExecutionTime: TimeSpan.FromMilliseconds(2)));
            }

            return Task.FromResult(Success(sql, "row-1"));
        });
        var vm = BuildViewModel(orchestrator);

        await vm.OpenTablePreviewAsync(
            tableFullName: "public.orders",
            connection: BuildConnection(),
            provider: DatabaseProvider.Postgres,
            metadata: null,
            sourceDocumentType: null,
            maxRows: 1);
        Assert.True(vm.IsTableMode);
        Assert.True(vm.CanGoNextPage);

        vm.NextPageCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsLoading, TimeSpan.FromSeconds(2));

        Assert.True(vm.HasError);
        Assert.False(vm.IsTableMode);
        Assert.False(vm.CanGoNextPage);

        int callsBeforeRetry = orchestrator.ExecutedSql.Count;
        vm.NextPageCommand.Execute(null);
        await Task.Delay(100);
        Assert.Equal(callsBeforeRetry, orchestrator.ExecutedSql.Count);
    }

    private static QuickDataPreviewModalViewModel BuildViewModel(ScriptedOrchestrator orchestrator)
    {
        var factory = new SingleOrchestratorFactory(orchestrator);
        var executionService = new SqlEditorExecutionService(orchestratorFactory: factory);
        var previewService = new QuickDataPreviewService(executionService);
        return new QuickDataPreviewModalViewModel(previewService);
    }

    private static ConnectionConfig BuildConnection()
    {
        return new ConnectionConfig(
            Provider: DatabaseProvider.Postgres,
            Host: "localhost",
            Port: 5432,
            Database: "akkorn",
            Username: "dev",
            Password: "dev");
    }

    private static DbMetadata BuildMetadataWithRelationship()
    {
        ForeignKeyRelation relation = new(
            ConstraintName: "fk_orders_customers",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "customer_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction,
            OrdinalPosition: 1);
        return new DbMetadata(
            DatabaseName: "akkorn",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [],
            AllForeignKeys: [relation]);
    }

    private static PreviewResult Success(string sql, string value)
    {
        var table = new DataTable();
        table.Columns.Add("value", typeof(string));
        table.Rows.Add(value);
        return new PreviewResult(
            Success: true,
            Data: table,
            ErrorMessage: null,
            ExecutionTime: TimeSpan.FromMilliseconds(2),
            RowsAffected: 1);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(20);
        }

        throw new TimeoutException("Timed out waiting for expected state.");
    }

    private sealed class SingleOrchestratorFactory : IDbOrchestratorFactory
    {
        private readonly IDbOrchestrator _orchestrator;

        public SingleOrchestratorFactory(IDbOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        public IDbOrchestrator Create(ConnectionConfig config) => _orchestrator;

        public Func<ConnectionConfig, IDbOrchestrator>? Register(
            DatabaseProvider provider,
            Func<ConnectionConfig, IDbOrchestrator> factory) => null;

        public bool IsRegistered(DatabaseProvider provider) => true;
    }

    private sealed class ScriptedOrchestrator : IDbOrchestrator
    {
        private readonly Func<string, CancellationToken, Task<PreviewResult>> _executePreview;

        public ScriptedOrchestrator(Func<string, CancellationToken, Task<PreviewResult>> executePreview)
        {
            _executePreview = executePreview;
        }

        public List<string> ExecutedSql { get; } = [];

        public DatabaseProvider Provider => DatabaseProvider.Postgres;

        public ConnectionConfig Config => BuildConnection();

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true, null, TimeSpan.FromMilliseconds(1)));

        public Task<DatabaseSchema> GetSchemaAsync(CancellationToken ct = default)
            => Task.FromResult(new DatabaseSchema("akkorn", DatabaseProvider.Postgres, []));

        public async Task<PreviewResult> ExecutePreviewAsync(
            string sql,
            int maxRows = PreviewExecutionOptions.UseConfiguredDefault,
            CancellationToken ct = default)
        {
            ExecutedSql.Add(sql);
            return await _executePreview(sql, ct);
        }

        public Task<DdlExecutionResult> ExecuteDdlAsync(
            string sql,
            bool stopOnError = true,
            CancellationToken ct = default)
            => Task.FromResult(new DdlExecutionResult(true, []));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
