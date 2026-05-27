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
    public void MySqlDialect_EmitCreateIndex_ExpressionKey_UsesSingleParenthesis()
    {
        var dialect = new MySqlDialect();

        string sql = dialect.EmitCreateIndex(
            schemaName: "public",
            tableName: "orders",
            indexName: "ix_orders_lower_number",
            isUnique: false,
            keyColumns: [new AkkornStudio.Ddl.DdlIndexKeyExpr(ExpressionSql: "LOWER(order_number)")],
            includeColumns: [],
            ifNotExists: false);

        Assert.Contains("((LOWER(order_number)))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("(((LOWER(order_number))))", sql, StringComparison.Ordinal);
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
    public void SqlServerDialect_FormatPagination_WithOffsetAndNoLimit_StillEmitsFetchClause()
    {
        var dialect = new SqlServerDialect();

        string pagination = dialect.FormatPagination(limit: null, offset: 25);

        Assert.Equal($"OFFSET 25 ROWS FETCH NEXT {long.MaxValue} ROWS ONLY", pagination);
    }

    [Fact]
    public void SqlServerDialect_GetColumnsQuery_UsesPrimaryKeyConstraintInsteadOfIdentityFlag()
    {
        var dialect = new SqlServerDialect();

        string sql = dialect.GetColumnsQuery();

        Assert.Contains("CONSTRAINT_TYPE = 'PRIMARY KEY'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsIdentity", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyQueryHints_SqlServer_AppendsOptionClause()
    {
        var dialect = new SqlServerDialect();

        string sql = dialect.ApplyQueryHints("SELECT * FROM users", "MAXDOP 1");

        Assert.Contains("OPTION", sql.ToUpperInvariant());
        Assert.Contains("MAXDOP 1", sql.ToUpperInvariant());
    }

    [Fact]
    public void ApplyQueryHints_MySql_InjectsSelectCommentHint()
    {
        ISqlDialect dialect = CreateDialect(DatabaseProvider.MySql);

        string sql = dialect.ApplyQueryHints("SELECT id FROM users", "BKA(users)");

        Assert.Contains("/*+", sql);
    }

    [Fact]
    public void ApplyQueryHints_Postgres_IsNoOp()
    {
        ISqlDialect dialect = CreateDialect(DatabaseProvider.Postgres);

        string sql = dialect.ApplyQueryHints("SELECT id FROM users;", "SeqScan(users)");

        Assert.Equal("SELECT id FROM users", sql);
    }

    [Fact]
    public void ApplyQueryHints_Sqlite_IsNoOp()
    {
        var dialect = new SqliteDialect();

        string sql = dialect.ApplyQueryHints("SELECT * FROM users;", "ANY_HINT");

        Assert.Equal("SELECT * FROM users", sql);
    }

    [Fact]
    public void SqliteDialect_AlterColumnType_ThrowsExplicitNotSupportedError()
    {
        var dialect = new SqliteDialect();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            dialect.EmitAlterTableAlterColumnType("main", "orders", "total", "DECIMAL(10,2)", true));

        Assert.Contains("does not support ALTER COLUMN TYPE", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rebuild the table", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqliteDialect_DropColumn_WithIfExists_ThrowsExplicitNotSupportedError()
    {
        var dialect = new SqliteDialect();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            dialect.EmitAlterTableDropColumn("main", "orders", "total", ifExists: true));

        Assert.Contains("does not support IF EXISTS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqliteDialect_DropColumn_WithoutIfExists_EmitsDropColumnStatement()
    {
        var dialect = new SqliteDialect();

        string sql = dialect.EmitAlterTableDropColumn("main", "orders", "total", ifExists: false);

        Assert.Equal("ALTER TABLE \"orders\" DROP COLUMN \"total\";", sql);
    }

    [Fact]
    public void SqlServerDialect_ObjectIdAndRenameStatements_EscapeSingleQuotesInIdentifiers()
    {
        var dialect = new SqlServerDialect();

        string createTable = dialect.EmitCreateTable(
            "dbo'unsafe",
            "users'unsafe",
            ifNotExists: true,
            columnFragments: ["[id] INT NOT NULL"],
            constraintFragments: []);
        string renameColumn = dialect.EmitAlterTableRenameColumn(
            "dbo",
            "users",
            "old'name",
            "new'name");

        Assert.Contains("OBJECT_ID(N'dbo''unsafe.users''unsafe', N'U')", createTable, StringComparison.Ordinal);
        Assert.Contains("sp_rename N'dbo.users.old''name', N'new''name', 'COLUMN'", renameColumn, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres)]
    [InlineData(DatabaseProvider.MySql)]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.SQLite)]
    public void EmitCreateTableColumn_WhenDataTypeIsBlank_ThrowsExplicitError(DatabaseProvider provider)
    {
        ISqlDialect dialect = CreateDialect(provider);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            dialect.EmitCreateTableColumn("id", "", isNullable: false));

        Assert.Contains("data type is required", ex.Message, StringComparison.OrdinalIgnoreCase);
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
