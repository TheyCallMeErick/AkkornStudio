namespace AkkornStudio.Tests.Unit.Core;

public sealed class ConnectionConfigHardeningTests
{
    [Fact]
    public void BuildConnectionString_Sqlite_WithParentTraversal_ThrowsInvalidOperationException()
    {
        var config = BuildSqliteConfig($"..{Path.DirectorySeparatorChar}secrets.db");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => config.BuildConnectionString()
        );

        Assert.Contains("parent traversal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConnectionString_Sqlite_WithMissingParentDirectory_ThrowsInvalidOperationException()
    {
        string missingParent = Path.Combine(
            Path.GetTempPath(),
            $"akkorn-missing-{Guid.NewGuid():N}",
            "data.db"
        );
        var config = BuildSqliteConfig(missingParent);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => config.BuildConnectionString()
        );

        Assert.Contains("parent directory does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConnectionString_Sqlite_WithExistingParentDirectory_ReturnsResolvedPath()
    {
        string parent = Path.Combine(Path.GetTempPath(), $"akkorn-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        try
        {
            string dbPath = Path.Combine(parent, "sample.db");
            var config = BuildSqliteConfig(dbPath);

            string connectionString = config.BuildConnectionString();

            Assert.Contains($"Data Source={Path.GetFullPath(dbPath)};", connectionString, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void BuildConnectionString_Sqlite_InMemory_RemainsSupported()
    {
        var config = BuildSqliteConfig(":memory:");

        string connectionString = config.BuildConnectionString();

        Assert.Contains("Data Source=:memory:;", connectionString, StringComparison.Ordinal);
    }

    private static ConnectionConfig BuildSqliteConfig(string databasePath) =>
        new(
            Provider: DatabaseProvider.SQLite,
            Host: string.Empty,
            Port: 0,
            Database: databasePath,
            Username: string.Empty,
            Password: string.Empty
        );
}
