using Microsoft.Data.Sqlite;
using AkkornStudio.Core;
using AkkornStudio.UI.Services;
using Xunit;

namespace AkkornStudio.Tests.Integration;

public class SqliteOrchestratorSmokeIntegrationTests
{
    [Fact]
    public async Task QueryExecutorService_RealSqliteFile_ExecutesSmokeQuery()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "vsa-smoke-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            using (var conn = new SqliteConnection($"Data Source={dbPath};"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
INSERT INTO users (name) VALUES ('Alice'), ('Bob'), ('Carol');";
                await cmd.ExecuteNonQueryAsync();
            }

            var config = new ConnectionConfig(
                DatabaseProvider.SQLite,
                Host: "localhost",
                Port: 0,
                Database: dbPath,
                Username: string.Empty,
                Password: string.Empty,
                TimeoutSeconds: 30
            );

            var svc = new QueryExecutorService();
            var dt = await svc.ExecuteQueryAsync(config, "SELECT id, name FROM users ORDER BY id", maxRows: 2);

            Assert.NotNull(dt);
            Assert.Equal(2, dt.Rows.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup for transient SQLite file handles in CI/test runners.
                }
            }
        }
    }

    [Fact]
    public async Task QueryExecutorService_RealSqliteFile_ExecutesParameterizedPreviewQuery()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "vsa-smoke-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            using (var conn = new SqliteConnection($"Data Source={dbPath};"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
INSERT INTO users (name) VALUES ('Alice'), ('Bob'), ('Carol');";
                await cmd.ExecuteNonQueryAsync();
            }

            var config = new ConnectionConfig(
                DatabaseProvider.SQLite,
                Host: "localhost",
                Port: 0,
                Database: dbPath,
                Username: string.Empty,
                Password: string.Empty,
                TimeoutSeconds: 30
            );

            var svc = new QueryExecutorService();
            var dt = await svc.ExecuteQueryAsync(
                config,
                "SELECT id, name FROM users WHERE id >= @minId ORDER BY id",
                [new QueryParameter("@minId", 2)],
                maxRows: 10);

            Assert.NotNull(dt);
            Assert.Equal(2, dt.Rows.Count);
            Assert.Equal("Bob", dt.Rows[0]["name"]);
            Assert.Equal("Carol", dt.Rows[1]["name"]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup for transient SQLite file handles in CI/test runners.
                }
            }
        }
    }

    [Fact]
    public async Task QueryExecutorService_RealSqliteFile_ExecutesMultiParameterPreviewQuery()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "vsa-smoke-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            using (var conn = new SqliteConnection($"Data Source={dbPath};"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE orders (id INTEGER PRIMARY KEY, status TEXT NOT NULL, total REAL NOT NULL);
INSERT INTO orders (status, total) VALUES ('OPEN', 10.5), ('OPEN', 25.0), ('CLOSED', 30.0);";
                await cmd.ExecuteNonQueryAsync();
            }

            var config = new ConnectionConfig(
                DatabaseProvider.SQLite,
                Host: "localhost",
                Port: 0,
                Database: dbPath,
                Username: string.Empty,
                Password: string.Empty,
                TimeoutSeconds: 30
            );

            var svc = new QueryExecutorService();
            var dt = await svc.ExecuteQueryAsync(
                config,
                "SELECT id, status, total FROM orders WHERE status = @status AND total >= @minTotal ORDER BY id",
                [
                    new QueryParameter("@status", "OPEN"),
                    new QueryParameter("@minTotal", 20.0),
                ],
                maxRows: 10);

            Assert.NotNull(dt);
            Assert.Single(dt.Rows);
            Assert.Equal(2L, Convert.ToInt64(dt.Rows[0]["id"]));
            Assert.Equal("OPEN", dt.Rows[0]["status"]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup for transient SQLite file handles in CI/test runners.
                }
            }
        }
    }

    [Fact]
    public async Task QueryExecutorService_RealSqliteFile_ExecutesNullParameterPreviewQuery()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "vsa-smoke-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            using (var conn = new SqliteConnection($"Data Source={dbPath};"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE orders (id INTEGER PRIMARY KEY, status TEXT NULL);
INSERT INTO orders (status) VALUES ('OPEN'), (NULL), ('CLOSED');";
                await cmd.ExecuteNonQueryAsync();
            }

            var config = new ConnectionConfig(
                DatabaseProvider.SQLite,
                Host: "localhost",
                Port: 0,
                Database: dbPath,
                Username: string.Empty,
                Password: string.Empty,
                TimeoutSeconds: 30
            );

            var svc = new QueryExecutorService();
            var dt = await svc.ExecuteQueryAsync(
                config,
                "SELECT id FROM orders WHERE status IS @status ORDER BY id",
                [new QueryParameter("@status", null)],
                maxRows: 10);

            Assert.NotNull(dt);
            Assert.Single(dt.Rows);
            Assert.Equal(2L, Convert.ToInt64(dt.Rows[0]["id"]));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup for transient SQLite file handles in CI/test runners.
                }
            }
        }
    }

    [Fact]
    public async Task QueryExecutorService_RealSqliteFile_ExpandsNamedListParameterPreviewQuery()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "vsa-smoke-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            using (var conn = new SqliteConnection($"Data Source={dbPath};"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE orders (id INTEGER PRIMARY KEY, status TEXT NOT NULL);
INSERT INTO orders (status) VALUES ('OPEN'), ('CLOSED'), ('PENDING');";
                await cmd.ExecuteNonQueryAsync();
            }

            var config = new ConnectionConfig(
                DatabaseProvider.SQLite,
                Host: "localhost",
                Port: 0,
                Database: dbPath,
                Username: string.Empty,
                Password: string.Empty,
                TimeoutSeconds: 30
            );

            var svc = new QueryExecutorService();
            var dt = await svc.ExecuteQueryAsync(
                config,
                "SELECT id FROM orders WHERE id IN (@ids) ORDER BY id",
                [new QueryParameter("@ids", new[] { 1, 3 })],
                maxRows: 10);

            Assert.NotNull(dt);
            Assert.Equal(2, dt.Rows.Count);
            Assert.Equal(1L, Convert.ToInt64(dt.Rows[0]["id"]));
            Assert.Equal(3L, Convert.ToInt64(dt.Rows[1]["id"]));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup for transient SQLite file handles in CI/test runners.
                }
            }
        }
    }
}
