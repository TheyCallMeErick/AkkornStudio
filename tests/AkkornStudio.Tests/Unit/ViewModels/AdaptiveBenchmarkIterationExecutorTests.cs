using AkkornStudio.Core;
using AkkornStudio.UI.Services.Benchmark;
using Microsoft.Data.Sqlite;

namespace AkkornStudio.Tests.Unit.ViewModels;

public sealed class AdaptiveBenchmarkIterationExecutorTests
{
    private static void DeleteFileWithRetry(string path, int attempts = 5, int delayMs = 50)
    {
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch (IOException)
            {
                if (attempt < attempts)
                    Thread.Sleep(delayMs);
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt < attempts)
                    Thread.Sleep(delayMs);
            }
        }
        // Best-effort cleanup only.
    }

    [Fact]
    public async Task ExecuteIterationAsync_UsesFallback_WhenConnectionIsUnavailable()
    {
        var fallback = new CountingFallbackExecutor(latencyMs: 42);
        var sut = new AdaptiveBenchmarkIterationExecutor(
            connectionResolver: () => null,
            sqlResolver: () => "SELECT 1",
            fallbackExecutor: fallback);

        double latency = await sut.ExecuteIterationAsync(CancellationToken.None);

        Assert.Equal(42, latency);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task ExecuteIterationAsync_UsesRealExecution_WhenSqliteConnectionIsAvailable()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"vsa-bench-{Guid.NewGuid():N}.db");
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY, name TEXT); INSERT INTO t(name) VALUES ('a');";
            await cmd.ExecuteNonQueryAsync();
        }

        var config = new ConnectionConfig(
            Provider: DatabaseProvider.SQLite,
            Host: string.Empty,
            Port: 0,
            Database: dbPath,
            Username: string.Empty,
            Password: string.Empty);
        var fallback = new CountingFallbackExecutor(latencyMs: 999);
        var sut = new AdaptiveBenchmarkIterationExecutor(
            connectionResolver: () => config,
            sqlResolver: () => "SELECT id, name FROM t",
            fallbackExecutor: fallback);

        double latency = await sut.ExecuteIterationAsync(CancellationToken.None);

        Assert.True(latency >= 0);
        Assert.Equal(0, fallback.CallCount);

        DeleteFileWithRetry(dbPath);
    }

    private sealed class CountingFallbackExecutor(double latencyMs) : IBenchmarkIterationExecutor
    {
        public int CallCount { get; private set; }

        public Task<double> ExecuteIterationAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(latencyMs);
        }
    }
}

