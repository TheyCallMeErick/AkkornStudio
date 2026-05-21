using System.Reflection;
using AkkornStudio.Core;
using AkkornStudio.UI.Services;
using Xunit;

namespace AkkornStudio.Tests.Unit.Services;

public class QueryExecutorServiceSecurityTests
{
    [Theory]
    [InlineData("INSERT INTO users(id) VALUES (1)")]
    [InlineData("UPDATE users SET name = 'x'")]
    [InlineData("DELETE FROM users")]
    [InlineData("WITH c AS (SELECT 1) DELETE FROM users")]
    [InlineData("WITH c AS (SELECT 1) UPDATE users SET name = 'x'")]
    [InlineData("WITH c AS (SELECT 'it''s' AS txt) DELETE FROM users")]
    [InlineData("DROP TABLE users")]
    [InlineData("SELECT 1; DELETE FROM users")]
    public void ValidatePreviewQuery_RejectsMutatingOrMultiStatementSql(string sql)
    {
        MethodInfo validate = typeof(QueryExecutorService)
            .GetMethod("ValidatePreviewQuery", BindingFlags.NonPublic | BindingFlags.Static)!;

        var ex = Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, [sql, null]));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Theory]
    [InlineData("SELECT * FROM users WHERE id = @id")]
    [InlineData("SELECT * FROM users WHERE id = :id")]
    [InlineData("SELECT * FROM users WHERE id = ?")]
    [InlineData("SELECT * FROM users WHERE id = $1")]
    public void ValidatePreviewQuery_RejectsParameterPlaceholdersWithoutValues(string sql)
    {
        MethodInfo validate = typeof(QueryExecutorService)
            .GetMethod("ValidatePreviewQuery", BindingFlags.NonPublic | BindingFlags.Static)!;

        var ex = Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, [sql, null]));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("WITH q AS (SELECT 1) SELECT * FROM q")]
    [InlineData("EXPLAIN SELECT * FROM users")]
    [InlineData("SELECT @@VERSION")]
    [InlineData("SELECT * FROM users; -- comentario final")]
    [InlineData("SELECT * FROM users; /* bloco de comentario */")]
    public void ValidatePreviewQuery_AcceptsReadOnlyStatements(string sql)
    {
        MethodInfo validate = typeof(QueryExecutorService)
            .GetMethod("ValidatePreviewQuery", BindingFlags.NonPublic | BindingFlags.Static)!;

        Exception? ex = Record.Exception(() => validate.Invoke(null, [sql, null]));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("SELECT * FROM users WHERE id = @id", "@id", 1)]
    [InlineData("SELECT * FROM users WHERE id = :id", "id", 1)]
    public void ValidatePreviewQuery_AcceptsNamedParametersWhenValuesAreProvided(string sql, string parameterName, int value)
    {
        MethodInfo validate = typeof(QueryExecutorService)
            .GetMethod("ValidatePreviewQuery", BindingFlags.NonPublic | BindingFlags.Static)!;

        var parameters = new[] { new QueryParameter(parameterName, value) };

        Exception? ex = Record.Exception(() => validate.Invoke(null, [sql, parameters]));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("SELECT * FROM users WHERE id = ?", 1)]
    [InlineData("SELECT * FROM users WHERE id = $1", 1)]
    public void ValidatePreviewQuery_AcceptsPositionalParametersWhenValuesAreProvided(string sql, int value)
    {
        MethodInfo validate = typeof(QueryExecutorService)
            .GetMethod("ValidatePreviewQuery", BindingFlags.NonPublic | BindingFlags.Static)!;

        var parameters = new[] { new QueryParameter(null, value) };

        Exception? ex = Record.Exception(() => validate.Invoke(null, [sql, parameters]));
        Assert.Null(ex);
    }

    [Fact]
    public void WrapWithPreviewLimit_ClampsMaxRows()
    {
        MethodInfo wrap = typeof(QueryExecutorService)
            .GetMethod("WrapWithPreviewLimit", BindingFlags.NonPublic | BindingFlags.Static)!;

        string sqlLow = (string)wrap.Invoke(null, ["SELECT * FROM users", DatabaseProvider.Postgres, 0])!;
        string sqlHigh = (string)wrap.Invoke(null, ["SELECT * FROM users", DatabaseProvider.Postgres, 50000])!;

        Assert.Contains("LIMIT 1", sqlLow);
        Assert.Contains("LIMIT 10000", sqlHigh);
    }

    [Fact]
    public void WrapWithPreviewLimit_TrailingLineComment_DoesNotCommentOutLimitOrWrapper()
    {
        MethodInfo wrap = typeof(QueryExecutorService)
            .GetMethod("WrapWithPreviewLimit", BindingFlags.NonPublic | BindingFlags.Static)!;
        const string sql = "SELECT 1\n-- comentario final";

        string postgres = (string)wrap.Invoke(null, [sql, DatabaseProvider.Postgres, 10])!;
        string mySql = (string)wrap.Invoke(null, [sql, DatabaseProvider.MySql, 10])!;
        string sqlServer = (string)wrap.Invoke(null, [sql, DatabaseProvider.SqlServer, 10])!;
        string sqlite = (string)wrap.Invoke(null, [sql, DatabaseProvider.SQLite, 10])!;

        Assert.Contains("\nLIMIT 10", postgres, StringComparison.Ordinal);
        Assert.Contains("\n) AS __preview", mySql, StringComparison.Ordinal);
        Assert.Contains("\n) AS __preview", sqlServer, StringComparison.Ordinal);
        Assert.Contains("\n) AS __preview", sqlite, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapWithPreviewLimit_WhenNoLimitRequested_ReturnsOriginalQueryWithoutWrapper()
    {
        MethodInfo wrap = typeof(QueryExecutorService)
            .GetMethod("WrapWithPreviewLimit", BindingFlags.NonPublic | BindingFlags.Static)!;

        string sql = (string)wrap.Invoke(null, ["SELECT * FROM users;", DatabaseProvider.Postgres, PreviewExecutionOptions.NoLimit])!;

        Assert.Equal("SELECT * FROM users", sql);
    }

    [Fact]
    public void QueryExecutorService_ExposesConfigurableCommandTimeout()
    {
        var executor = new QueryExecutorService();
        Assert.Equal(300, executor.CommandTimeoutSeconds);

        executor.CommandTimeoutSeconds = 42;
        Assert.Equal(42, executor.CommandTimeoutSeconds);
    }
}
