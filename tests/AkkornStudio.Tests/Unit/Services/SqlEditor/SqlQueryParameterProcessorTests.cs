using AkkornStudio.Core;
using AkkornStudio.UI.Services.SqlEditor;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlQueryParameterProcessorTests
{
    private readonly SqlQueryParameterProcessor _sut = new();

    [Fact]
    public void DetectNames_ReturnsEmpty_WhenSqlIsNullOrWhitespace()
    {
        Assert.Empty(_sut.DetectNames(null));
        Assert.Empty(_sut.DetectNames(string.Empty));
        Assert.Empty(_sut.DetectNames("   \n\t  "));
    }

    [Fact]
    public void DetectNames_IgnoresQuotedTextCommentsAndCastMarkers()
    {
        const string sql = """
            SELECT :first, @second, :first
            FROM users
            WHERE note = ':ignored'
              AND kind = "::ignored"
              AND flag = @second
              -- :line_ignored
              /* :block_ignored */
              AND created_at::date >= :third
            """;

        IReadOnlyList<string> names = _sut.DetectNames(sql);

        Assert.Equal(["first", "second", "third"], names);
    }

    [Fact]
    public void TryApply_ReturnsTrue_WithNullOrWhitespaceSql()
    {
        bool applied = _sut.TryApply(
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Equal(string.Empty, resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);

        applied = _sut.TryApply(
            "   ",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DatabaseProvider.Postgres,
            out resolvedSql,
            out missingParameters,
            out errorMessage);

        Assert.True(applied);
        Assert.Equal("   ", resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryApply_ReturnsMissingDistinctNames_AndPreservesOriginalSql()
    {
        const string sql = "SELECT * FROM t WHERE a = :missing OR b = @MISSING OR c = :present";

        bool applied = _sut.TryApply(
            sql,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["present"] = "7"
            },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.False(applied);
        Assert.Equal(sql, resolvedSql);
        Assert.Equal(["missing"], missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryApply_TreatsNullValueAsMissingParameter()
    {
        const string sql = "SELECT :name";

        bool applied = _sut.TryApply(
            sql,
            new Dictionary<string, string> { ["name"] = null! },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.False(applied);
        Assert.Equal(sql, resolvedSql);
        Assert.Equal(["name"], missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryApply_ConvertsWhitespaceOnlyToStringLiteral_InsteadOfNull()
    {
        const string sql = "SELECT :p1, :p2";

        bool applied = _sut.TryApply(
            sql,
            new Dictionary<string, string>
            {
                ["p1"] = string.Empty,
                ["p2"] = "   "
            },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Equal("SELECT '', '   '", resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "true", "1")]
    [InlineData(DatabaseProvider.SqlServer, "false", "0")]
    [InlineData(DatabaseProvider.Postgres, "true", "TRUE")]
    [InlineData(DatabaseProvider.Postgres, "false", "FALSE")]
    public void TryApply_ConvertsBoolean_ByProvider(DatabaseProvider provider, string input, string expectedLiteral)
    {
        bool applied = _sut.TryApply(
            "SELECT :flag",
            new Dictionary<string, string> { ["flag"] = input },
            provider,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Equal($"SELECT {expectedLiteral}", resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryApply_ConvertsNullIntegerDecimalGuidAndEscapedString()
    {
        bool applied = _sut.TryApply(
            "SELECT :n, :i, :d, :g, :s",
            new Dictionary<string, string>
            {
                ["n"] = "null",
                ["i"] = "-42",
                ["d"] = "123.45",
                ["g"] = "f4d5f7ac57da4f949c5fc85f8d6d7af8",
                ["s"] = "O'Hara"
            },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Equal(
            "SELECT NULL, -42, 123.45, 'f4d5f7ac-57da-4f94-9c5f-c85f8d6d7af8', 'O''Hara'",
            resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryApply_FormatsGuidForSqlServer_WithExplicitUniqueIdentifierCast()
    {
        bool applied = _sut.TryApply(
            "SELECT :g",
            new Dictionary<string, string>
            {
                ["g"] = "f4d5f7ac57da4f949c5fc85f8d6d7af8"
            },
            DatabaseProvider.SqlServer,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Equal("SELECT CAST('f4d5f7ac-57da-4f94-9c5f-c85f8d6d7af8' AS uniqueidentifier)", resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Theory]
    [InlineData("2024-06-15T14:00:00Z", "SELECT '2024-06-15 14:00:00.0000000 +00:00'")]
    [InlineData("2024-06-15T14:00:00+02:00", "SELECT '2024-06-15 14:00:00.0000000 +02:00'")]
    public void TryApply_UsesDateTimeOffset_WhenInputHasExplicitOffset(string input, string expectedSql)
    {
        bool applied = _sut.TryApply(
            "SELECT :dt",
            new Dictionary<string, string> { ["dt"] = input },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Equal(expectedSql, resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryApply_UsesDateTime_WhenInputHasNoExplicitOffset()
    {
        bool applied = _sut.TryApply(
            "SELECT :dt",
            new Dictionary<string, string> { ["dt"] = "2024-06-15 14:00:00" },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Equal("SELECT '2024-06-15 14:00:00.0000000'", resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryApply_DoesNotTreatMinusInDateAsOffset()
    {
        bool applied = _sut.TryApply(
            "SELECT :dt",
            new Dictionary<string, string> { ["dt"] = "2024-06-15" },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Equal("SELECT '2024-06-15 00:00:00.0000000'", resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryApply_ReturnsError_WhenValueContainsNullCharacter()
    {
        const string sql = "SELECT :p";

        bool applied = _sut.TryApply(
            sql,
            new Dictionary<string, string> { ["p"] = "abc\0def" },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.False(applied);
        Assert.Equal(sql, resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Equal("Query parameter contains invalid null character.", errorMessage);
    }

    [Fact]
    public void TryApply_IgnoresNonParameterTokensAndReplacesValidOccurrences()
    {
        const string sql = """
            :start + 1
            ::cast
            :9invalid
            @
            ':skip_single'
            " :skip_double "
            /* :skip_block */
            -- :skip_line
            AND code = @ok
            """;

        bool applied = _sut.TryApply(
            sql,
            new Dictionary<string, string> { ["start"] = "1", ["ok"] = "2" },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Contains("1 + 1", resolvedSql, StringComparison.Ordinal);
        Assert.Contains("AND code = 2", resolvedSql, StringComparison.Ordinal);
        Assert.Contains("::cast", resolvedSql, StringComparison.Ordinal);
        Assert.Contains(":9invalid", resolvedSql, StringComparison.Ordinal);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryApply_AppendsTrailingSql_AfterLastParameterOccurrence()
    {
        bool applied = _sut.TryApply(
            "SELECT :value AS v",
            new Dictionary<string, string> { ["value"] = "9" },
            DatabaseProvider.Postgres,
            out string resolvedSql,
            out IReadOnlyList<string> missingParameters,
            out string? errorMessage);

        Assert.True(applied);
        Assert.Equal("SELECT 9 AS v", resolvedSql);
        Assert.Empty(missingParameters);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void DetectNames_HandlesEscapedSingleQuoteAndTerminalMarker()
    {
        const string sql = "SELECT ':escaped''quote', :ok:";

        IReadOnlyList<string> names = _sut.DetectNames(sql);

        Assert.Equal(["ok"], names);
    }
}
