using Microsoft.Data.Sqlite;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.Explain;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.Tests.Integration.Explain;

public class SqliteExplainExecutorIntegrationTests
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
    [Trait("Category", "Integration")]
    public async Task SqliteExplainExecutor_ReturnsRealPlan_ForLocalDatabase()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"vsa-explain-{Guid.NewGuid():N}.db");
        try
        {
            await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS explain_orders (
    id INTEGER PRIMARY KEY,
    amount REAL NOT NULL
);
INSERT INTO explain_orders(amount)
SELECT 100.0
WHERE NOT EXISTS (SELECT 1 FROM explain_orders);";
                await cmd.ExecuteNonQueryAsync();
            }

            var config = new ConnectionConfig(
                Provider: DatabaseProvider.SQLite,
                Host: string.Empty,
                Port: 0,
                Database: dbPath,
                Username: string.Empty,
                Password: string.Empty,
                TimeoutSeconds: 30);

            var executor = new SqliteExplainExecutor();
            ExplainResult result = await executor.RunAsync(
                "SELECT id, amount FROM explain_orders WHERE id > 0 ORDER BY id;",
                DatabaseProvider.SQLite,
                config,
                new ExplainOptions(IncludeAnalyze: false, IncludeBuffers: false, Format: ExplainFormat.Text),
                CancellationToken.None);

            Assert.False(result.IsSimulated);
            Assert.NotEmpty(result.Nodes);
            Assert.False(string.IsNullOrWhiteSpace(result.RawOutput));
        }
        finally
        {
            DeleteFileWithRetry(dbPath);
        }
    }
}

