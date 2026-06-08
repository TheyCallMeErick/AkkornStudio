using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.Core;
using System.Data;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class ShellSqlEditorFactoryTests
{
    [Fact]
    public void Constructor_UsesInjectedSqlEditorFactory()
    {
        var expectedVm = new SqlEditorViewModel();
        var factory = new FakeSqlEditorViewModelFactory(expectedVm);

        var shell = new ShellViewModel(sqlEditorViewModelFactory: factory, connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        Assert.Same(expectedVm, shell.SqlEditor);
        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public async Task SqlEditorSelectExecution_NavigatesToSqlResultPage_AndBackToEditor()
    {
        ConnectionConfig config = BuildConnectionConfig();
        var factory = new RoutedSqlEditorViewModelFactory(config);
        var shell = new ShellViewModel(
            sqlEditorViewModelFactory: factory,
            connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);
        shell.SqlEditor.ActiveTab.SqlText = "SELECT 1;";

        SqlEditorResultSet result = await shell.SqlEditor.ExecuteSelectionOrCurrentAsync(0, 0, 0);

        Assert.True(result.Success);
        Assert.Equal(WorkspaceDocumentType.SqlResult, shell.ActiveWorkspaceDocumentType);
        SqlResultPageViewModel resultPage = Assert.IsType<SqlResultPageViewModel>(shell.ActiveSqlResultDocument);
        Assert.True(resultPage.HasSession);
        Assert.Equal("SELECT 1", resultPage.SqlText);

        resultPage.BackToEditorCommand.Execute(null);
        Assert.Equal(WorkspaceDocumentType.SqlEditor, shell.ActiveWorkspaceDocumentType);
    }

    [Fact]
    public async Task SqlEditorNonSelectExecution_KeepsSqlEditorAsActiveDocument()
    {
        ConnectionConfig config = BuildConnectionConfig();
        var factory = new RoutedSqlEditorViewModelFactory(config);
        var shell = new ShellViewModel(
            sqlEditorViewModelFactory: factory,
            connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);
        shell.SqlEditor.ActiveTab.SqlText = "UPDATE customers SET name = 'x' WHERE id = 1;";

        SqlEditorResultSet result = await shell.SqlEditor.ExecuteSelectionOrCurrentAsync(0, 0, 0);

        Assert.True(result.Success);
        Assert.Equal(WorkspaceDocumentType.SqlEditor, shell.ActiveWorkspaceDocumentType);
        Assert.Null(shell.ActiveSqlResultDocument);
    }

    private sealed class FakeSqlEditorViewModelFactory(SqlEditorViewModel vm) : ISqlEditorViewModelFactory
    {
        public int CreateCalls { get; private set; }

        public SqlEditorViewModel Create(SqlEditorViewModelFactoryContext context)
        {
            CreateCalls++;
            _ = context.ConnectionConfigResolver();
            _ = context.ConnectionConfigByProfileIdResolver(null);
            _ = context.ConnectionProfilesResolver();
            _ = context.MetadataResolver();
            return vm;
        }
    }

    private sealed class RoutedSqlEditorViewModelFactory(ConnectionConfig config) : ISqlEditorViewModelFactory
    {
        public SqlEditorViewModel Create(SqlEditorViewModelFactoryContext context)
        {
            _ = context.ConnectionProfilesResolver();
            _ = context.MetadataResolver();
            _ = context.SharedConnectionManagerResolver();

            return new SqlEditorViewModel(
                executionService: new SqlEditorExecutionService(new DeterministicOrchestratorFactory()),
                connectionConfigResolver: () => config,
                connectionConfigByProfileIdResolver: _ => config,
                metadataResolver: () => null,
                sharedConnectionManagerResolver: context.SharedConnectionManagerResolver);
        }
    }

    private sealed class DeterministicOrchestratorFactory : IDbOrchestratorFactory
    {
        public IDbOrchestrator Create(ConnectionConfig config) => new DeterministicOrchestrator(config);
        public Func<ConnectionConfig, IDbOrchestrator>? Register(DatabaseProvider provider, Func<ConnectionConfig, IDbOrchestrator> factory) => null;
        public bool IsRegistered(DatabaseProvider provider) => true;
    }

    private sealed class DeterministicOrchestrator(ConnectionConfig config) : IDbOrchestrator
    {
        public DatabaseProvider Provider => config.Provider;
        public ConnectionConfig Config => config;

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default) =>
            Task.FromResult(new ConnectionTestResult(true));

        public Task<DatabaseSchema> GetSchemaAsync(CancellationToken ct = default) =>
            Task.FromResult(new DatabaseSchema("db", config.Provider, []));

        public Task<PreviewResult> ExecutePreviewAsync(string sql, int maxRows = PreviewExecutionOptions.UseConfiguredDefault, CancellationToken ct = default)
        {
            string statement = sql?.Trim() ?? string.Empty;
            bool isSelect = statement.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);

            DataTable? table = null;
            if (isSelect)
            {
                table = new DataTable();
                table.Columns.Add("value", typeof(int));
                table.Rows.Add(1);
            }

            return Task.FromResult(new PreviewResult(
                Success: true,
                Data: table,
                ErrorMessage: null,
                ExecutionTime: TimeSpan.FromMilliseconds(3),
                RowsAffected: 1));
        }

        public Task<DdlExecutionResult> ExecuteDdlAsync(string sql, bool stopOnError = true, CancellationToken ct = default) =>
            Task.FromResult(new DdlExecutionResult(true, []));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static ConnectionConfig BuildConnectionConfig()
    {
        return new ConnectionConfig(
            Provider: DatabaseProvider.Postgres,
            Host: "localhost",
            Port: 5432,
            Database: "app_db",
            Username: "postgres",
            Password: "postgres");
    }
}
