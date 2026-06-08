using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Providers.Dialects;
using Xunit;

namespace AkkornStudio.Tests.Unit.Core;

public class BaseDbOrchestratorExceptionStrategyTests
{
    [Fact]
    public async Task TestConnectionAsync_WhenOpenConnectionFails_ReturnsFailureResult()
    {
        var sut = new ThrowingOrchestrator();

        ConnectionTestResult result = await sut.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task ExecutePreviewAsync_WhenOpenConnectionFails_ReturnsFailureResult()
    {
        var sut = new ThrowingOrchestrator();

        PreviewResult result = await sut.ExecutePreviewAsync("SELECT 1", 10);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetSchemaAsync_WhenProviderReturnsBlankTableName_ThrowsInvalidOperationException()
    {
        var sut = new SchemaStubOrchestrator([
            ("main", "   "),
        ]);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetSchemaAsync());

        Assert.Contains("invalid table name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSchemaAsync_WhenProviderReturnsNullTableName_ThrowsInvalidOperationException()
    {
        var sut = new SchemaStubOrchestrator([
            ("main", null!),
        ]);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetSchemaAsync());

        Assert.Contains("invalid table name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSchemaAsync_TrimsSchemaAndTableNames_BeforeFetchingColumns()
    {
        var sut = new SchemaStubOrchestrator([
            (" main ", " orders "),
        ]);

        DatabaseSchema schema = await sut.GetSchemaAsync();

        TableSchema table = Assert.Single(schema.Tables);
        Assert.Equal("main", table.Schema);
        Assert.Equal("orders", table.Name);

        (string schemaName, string tableName) = Assert.Single(sut.FetchColumnsRequests);
        Assert.Equal("main", schemaName);
        Assert.Equal("orders", tableName);
    }

    [Fact]
    public async Task ExecutePreviewAsync_WhenCellExceedsConfiguredLimit_ReturnsFailureResult()
    {
        var options = Options.Create(new PreviewExecutionOptions
        {
            MaxCellBytes = 16,
            MaxPayloadBytes = 1_000_000,
        });
        var sut = new SeededPreviewOrchestrator(
            options,
            "CREATE TABLE t(v TEXT);",
            "INSERT INTO t(v) VALUES ('abcdefghijklmnopqrstuvwxyz');");

        PreviewResult result = await sut.ExecutePreviewAsync("SELECT v FROM t", maxRows: 10);

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Contains("exceeded", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutePreviewAsync_WhenPayloadExceedsConfiguredLimit_ReturnsFailureResult()
    {
        var options = Options.Create(new PreviewExecutionOptions
        {
            MaxCellBytes = 1024,
            MaxPayloadBytes = 32,
        });
        var sut = new SeededPreviewOrchestrator(
            options,
            "CREATE TABLE t(v TEXT);",
            "INSERT INTO t(v) VALUES ('aaaaaaaaaaaaaaaaaaaa');",
            "INSERT INTO t(v) VALUES ('bbbbbbbbbbbbbbbbbbbb');");

        PreviewResult result = await sut.ExecutePreviewAsync("SELECT v FROM t", maxRows: 10);

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Contains("payload exceeded", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingOrchestrator()
        : BaseDbOrchestrator(new ConnectionConfig(DatabaseProvider.SQLite, "localhost", 0, "x.db", "", ""))
    {
        public override DatabaseProvider Provider => DatabaseProvider.SQLite;

        protected override Task<DbConnection> OpenConnectionAsync(CancellationToken ct) =>
            throw new InvalidOperationException("Simulated open failure");

        protected override ISqlDialect GetDialect() => new SqliteDialect();

        protected override IMetadataQueryProvider GetMetadataQueryProvider() => new SqliteMetadataQueries();
    }

    private sealed class SchemaStubOrchestrator(IReadOnlyList<(string Schema, string Table)> tables)
        : BaseDbOrchestrator(new ConnectionConfig(DatabaseProvider.SQLite, "localhost", 0, "x.db", "", ""))
    {
        private readonly IReadOnlyList<(string Schema, string Table)> _tables = tables;

        public List<(string Schema, string Table)> FetchColumnsRequests { get; } = [];

        public override DatabaseProvider Provider => DatabaseProvider.SQLite;

        protected override async Task<DbConnection> OpenConnectionAsync(CancellationToken ct)
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            await conn.OpenAsync(ct);
            return conn;
        }

        protected override ISqlDialect GetDialect() => new SqliteDialect();

        protected override IMetadataQueryProvider GetMetadataQueryProvider() => new SqliteMetadataQueries();

        protected override Task<IReadOnlyList<(string Schema, string Table)>> FetchTablesAsync(
            DbConnection conn,
            CancellationToken ct) =>
            Task.FromResult(_tables);

        protected override Task<IReadOnlyList<ColumnSchema>> FetchColumnsAsync(
            DbConnection conn,
            string schema,
            string table,
            CancellationToken ct)
        {
            FetchColumnsRequests.Add((schema, table));
            IReadOnlyList<ColumnSchema> columns =
            [
                new ColumnSchema("id", "INTEGER", IsNullable: false, IsPrimaryKey: true, IsForeignKey: false),
            ];

            return Task.FromResult(columns);
        }
    }

    private sealed class SeededPreviewOrchestrator(
        IOptions<PreviewExecutionOptions> options,
        params string[] seedStatements)
        : BaseDbOrchestrator(
            new ConnectionConfig(DatabaseProvider.SQLite, "localhost", 0, ":memory:", "", ""),
            previewOptions: options)
    {
        private readonly IReadOnlyList<string> _seedStatements = seedStatements;

        public override DatabaseProvider Provider => DatabaseProvider.SQLite;

        protected override async Task<DbConnection> OpenConnectionAsync(CancellationToken ct)
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            await conn.OpenAsync(ct);

            foreach (string sql in _seedStatements)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            return conn;
        }

        protected override ISqlDialect GetDialect() => new SqliteDialect();

        protected override IMetadataQueryProvider GetMetadataQueryProvider() => new SqliteMetadataQueries();
    }
}
