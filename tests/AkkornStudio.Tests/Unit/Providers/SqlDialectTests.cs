using AkkornStudio.Providers.Dialects;

namespace AkkornStudio.Tests.Unit.Providers;

/// <summary>
/// Tests for ISqlDialect implementations (PostgreSQL, MySQL, SQL Server).
/// Validates that each dialect correctly handles provider-specific SQL syntax.
/// </summary>
public class SqlDialectTests
{
    [Theory]
    [InlineData(DatabaseProvider.Postgres)]
    [InlineData(DatabaseProvider.MySql)]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.SQLite)]
    public void WrapWithPreviewLimit_ProducesValidSQL(DatabaseProvider provider)
    {
        // Arrange
        var dialect = CreateDialect(provider);
        string originalSql = "SELECT id, name FROM users";
        int maxRows = 1000;

        // Act
        string wrapped = dialect.WrapWithPreviewLimit(originalSql, maxRows);

        // Assert
        Assert.NotEmpty(wrapped);
        Assert.Contains(originalSql, wrapped);
        Assert.Contains(maxRows.ToString(), wrapped);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "LIMIT")]
    [InlineData(DatabaseProvider.MySql, "LIMIT")]
    [InlineData(DatabaseProvider.SqlServer, "TOP")]
    [InlineData(DatabaseProvider.SQLite, "LIMIT")]
    public void WrapWithPreviewLimit_UsesCorrectSyntax(DatabaseProvider provider, string expectedKeyword)
    {
        // Arrange
        var dialect = CreateDialect(provider);
        string sql = "SELECT id FROM users";

        // Act
        string wrapped = dialect.WrapWithPreviewLimit(sql, 100);

        // Assert
        Assert.Contains(expectedKeyword, wrapped.ToUpper());
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres)]
    [InlineData(DatabaseProvider.MySql)]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.SQLite)]
    public void FormatPagination_ProducesValidSQL(DatabaseProvider provider)
    {
        // Arrange
        var dialect = CreateDialect(provider);

        // Act
        string pagination = dialect.FormatPagination(offset: 100, limit: 50);

        // Assert
        Assert.NotEmpty(pagination);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "\"username\"")]
    [InlineData(DatabaseProvider.MySql, "`username`")]
    [InlineData(DatabaseProvider.SqlServer, "[username]")]
    [InlineData(DatabaseProvider.SQLite, "\"username\"")]
    public void QuoteIdentifier_UsesCorrectQuotingStyle(DatabaseProvider provider, string expected)
    {
        // Arrange
        var dialect = CreateDialect(provider);

        // Act
        string quoted = dialect.QuoteIdentifier("username");

        // Assert
        Assert.Equal(expected, quoted);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres)]
    [InlineData(DatabaseProvider.MySql)]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.SQLite)]
    public void QuoteIdentifier_HandlesMultipleParts(DatabaseProvider provider)
    {
        // Arrange
        var dialect = CreateDialect(provider);

        // Act
        string quoted = dialect.QuoteIdentifier("public.users");

        // Assert
        Assert.NotEmpty(quoted);
        // Should have quoted both parts
        Assert.True(quoted.Contains("public") && quoted.Contains("users"));
    }

    [Fact]
    public void PostgresDialect_SpecificBehaviors()
    {
        // Arrange
        var dialect = new PostgresDialect();

        // Act & Assert - PostgreSQL specific tests
        Assert.Equal("LIMIT 100", dialect.FormatPagination(offset: 0, limit: 100));
        Assert.Equal("LIMIT 100 OFFSET 50", dialect.FormatPagination(offset: 50, limit: 100));
    }

    [Fact]
    public void MySqlDialect_SpecificBehaviors()
    {
        // Arrange
        var dialect = new MySqlDialect();

        // Act & Assert - MySQL specific tests
        Assert.Equal("LIMIT 100", dialect.FormatPagination(offset: 0, limit: 100));
        Assert.Equal("LIMIT 100 OFFSET 50", dialect.FormatPagination(offset: 50, limit: 100));
    }

    [Fact]
    public void SqlServerDialect_SpecificBehaviors()
    {
        // Arrange
        var dialect = new SqlServerDialect();

        // Act & Assert - SQL Server specific tests
        // SQL Server uses OFFSET...FETCH
        string pagination = dialect.FormatPagination(offset: 50, limit: 100);
        Assert.Contains("OFFSET", pagination.ToUpper());
        Assert.Contains("FETCH", pagination.ToUpper());
    }

    [Fact]
    public void ApplyQueryHints_SqlServer_AppendsOptionClause()
    {
        var dialect = new SqlServerDialect();

        string sql = dialect.ApplyQueryHints("SELECT * FROM users", "MAXDOP 1");

        Assert.Contains("OPTION", sql.ToUpperInvariant());
        Assert.Contains("MAXDOP 1", sql.ToUpperInvariant());
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres)]
    [InlineData(DatabaseProvider.MySql)]
    public void ApplyQueryHints_SelectCommentDialects_InjectComment(DatabaseProvider provider)
    {
        ISqlDialect dialect = CreateDialect(provider);

        string sql = dialect.ApplyQueryHints("SELECT id FROM users", "BKA(users)");

        Assert.Contains("/*+", sql);
    }

    [Fact]
    public void ApplyQueryHints_Sqlite_IsNoOp()
    {
        var dialect = new SqliteDialect();

        string sql = dialect.ApplyQueryHints("SELECT * FROM users;", "ANY_HINT");

        Assert.Equal("SELECT * FROM users", sql);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    [Fact]
    public void WrapWithPreviewLimit_TrailingLineComment_DoesNotCommentOutLimitOrWrapper()
    {
        const string sql = "SELECT 1\n-- comentário final";

        string postgresWrapped = new PostgresDialect().WrapWithPreviewLimit(sql, 10);
        string mySqlWrapped = new MySqlDialect().WrapWithPreviewLimit(sql, 10);
        string sqlServerWrapped = new SqlServerDialect().WrapWithPreviewLimit(sql, 10);
        string sqliteWrapped = new SqliteDialect().WrapWithPreviewLimit(sql, 10);

        Assert.Contains("\nLIMIT 10", postgresWrapped, StringComparison.Ordinal);
        Assert.Contains("\n) AS __preview", mySqlWrapped, StringComparison.Ordinal);
        Assert.Contains("\n) AS __preview", sqlServerWrapped, StringComparison.Ordinal);
        Assert.Contains("\n) AS __preview", sqliteWrapped, StringComparison.Ordinal);
    }

    private static ISqlDialect CreateDialect(DatabaseProvider provider) =>
        provider switch
        {
            DatabaseProvider.Postgres => new PostgresDialect(),
            DatabaseProvider.MySql => new MySqlDialect(),
            DatabaseProvider.SqlServer => new SqlServerDialect(),
            DatabaseProvider.SQLite => new SqliteDialect(),
            _ => throw new NotSupportedException($"Provider {provider} not supported in tests")
        };
}
