using AkkornStudio.Core;
using AkkornStudio.QueryEngine;
using AkkornStudio.Registry;
using Xunit;

namespace AkkornStudio.Tests.Unit.Queries;

// ─────────────────────────────────────────────────────────────────────────────
// SqlFunctionRegistry Tests
// ─────────────────────────────────────────────────────────────────────────────

public class SqlFunctionRegistryTests
{
    // ── REGEX ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Regex_Postgres_UsesNativeTildeOperator()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        string sql = reg.GetFunction(SqlFn.Regex, "email", "'@gmail\\.com'");
        Assert.Equal("email ~ '@gmail\\.com'", sql);
    }

    [Fact]
    public void Regex_MySQL_UsesRegexpOperator()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.MySql);
        string sql = reg.GetFunction(SqlFn.Regex, "email", "'@gmail\\.com'");
        Assert.Equal("email REGEXP '@gmail\\.com'", sql);
    }

    [Fact]
    public void Regex_SqlServer_UsesPATINDEX()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SqlServer);
        string sql = reg.GetFunction(SqlFn.Regex, "email", "'%@gmail%'");
        Assert.Equal("PATINDEX('%@gmail%', email) > 0", sql);
    }

    // ── DATE_DIFF ─────────────────────────────────────────────────────────────

    [Fact]
    public void DateDiff_Postgres_UsesExtract()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        string sql = reg.GetFunction(SqlFn.DateDiff, "created_at", "NOW()");
        Assert.Contains("EXTRACT(DAY FROM", sql);
    }

    [Fact]
    public void DateDiff_MySQL_UsesDATEDIFF()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.MySql);
        string sql = reg.GetFunction(SqlFn.DateDiff, "created_at", "NOW()");
        Assert.StartsWith("DATEDIFF(", sql);
    }

    [Fact]
    public void DateDiff_SqlServer_UsesDATEDIFF()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SqlServer);
        string sql = reg.GetFunction(SqlFn.DateDiff, "created_at", "GETDATE()");
        Assert.StartsWith("DATEDIFF(DAY,", sql);
    }

    // ── STRING_AGG ────────────────────────────────────────────────────────────

    [Fact]
    public void StringAgg_MySQL_UsesGroupConcat()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.MySql);
        string sql = reg.GetFunction(SqlFn.StringAgg, "name", "','");
        Assert.Contains("GROUP_CONCAT", sql);
        Assert.Contains("SEPARATOR", sql);
    }

    [Fact]
    public void StringAgg_SqlServerAndPostgres_UsesStringAgg()
    {
        foreach (
            DatabaseProvider p in new[] { DatabaseProvider.SqlServer, DatabaseProvider.Postgres }
        )
        {
            var reg = new SqlFunctionRegistry(p);
            string sql = reg.GetFunction(SqlFn.StringAgg, "name", "','");
            Assert.Contains("STRING_AGG", sql);
        }
    }

    [Fact]
    public void StringAgg_Sqlite_UsesGroupConcatTranslation()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SQLite);
        string sql = reg.GetFunction(SqlFn.StringAgg, "name", "','");
        Assert.Equal("GROUP_CONCAT(name, ',')", sql);
    }

    [Fact]
    public void RegexReplace_SqlServer_UsesFallbackReplace()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SqlServer);
        string sql = reg.GetFunction(SqlFn.RegexReplace, "email", "'corp'", "'team'");
        Assert.Equal("REPLACE(email, 'corp', 'team')", sql);
    }

    [Fact]
    public void RegexReplace_Sqlite_UsesFallbackReplace()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SQLite);
        string sql = reg.GetFunction(SqlFn.RegexReplace, "email", "'corp'", "'team'");
        Assert.Equal("REPLACE(email, 'corp', 'team')", sql);
    }

    [Fact]
    public void RegexExtract_SqlServer_UsesFallbackPatIndexGuard()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SqlServer);
        string sql = reg.GetFunction(SqlFn.RegexExtract, "email", "'%@corp%'");
        Assert.Equal("CASE WHEN PATINDEX('%@corp%', email) > 0 THEN '%@corp%' ELSE NULL END", sql);
    }

    [Fact]
    public void RegexExtract_Sqlite_UsesFallbackInstrGuard()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SQLite);
        string sql = reg.GetFunction(SqlFn.RegexExtract, "email", "'corp'");
        Assert.Equal("CASE WHEN INSTR(email, 'corp') > 0 THEN 'corp' ELSE NULL END", sql);
    }

    // ── GREATEST (SQL Server emulation) ───────────────────────────────────────

    [Fact]
    public void Greatest_SqlServer_UsesValuesSubquery()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SqlServer);
        string sql = reg.GetFunction(SqlFn.Greatest, "a", "b", "c");
        Assert.Contains("SELECT MAX(v) FROM (VALUES", sql);
    }

    // ── IsSupported ───────────────────────────────────────────────────────────

    [Fact]
    public void IsSupported_ReturnsTrueForMappedFunctions()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        Assert.True(reg.IsSupported(SqlFn.Regex));
        Assert.True(reg.IsSupported(SqlFn.DateDiff));
    }

    [Fact]
    public void IsSupported_ReturnsFalseForUnknownFunctions()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        Assert.False(reg.IsSupported("MAGIC_NONEXISTENT_FUNCTION"));
    }

    // ── Unsupported throws ────────────────────────────────────────────────────

    [Fact]
    public void GetFunction_UnknownName_ThrowsNotSupported()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.MySql);
        Assert.Throws<NotSupportedException>(() => reg.GetFunction("FAKE_FUNCTION", "col"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// QueryBuilderService Tests
// ─────────────────────────────────────────────────────────────────────────────

public class QueryBuilderServiceTests
{
    // ── Basic SELECT * ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseProvider.Postgres)]
    [InlineData(DatabaseProvider.MySql)]
    [InlineData(DatabaseProvider.SqlServer)]
    public void Compile_SimpleSelect_ContainsFromClause(DatabaseProvider p)
    {
        var svc = QueryBuilderService.Create(p, "users");
        CompiledQuery result = svc.Compile(new VisualQuerySpec("users"));
        Assert.Contains("users", result.Sql);
    }

    // ── Column quoting ────────────────────────────────────────────────────────

    [Fact]
    public void Compile_SqlServer_QuotesIdentifiersWithBrackets()
    {
        var svc = QueryBuilderService.Create(DatabaseProvider.SqlServer, "users");
        CompiledQuery result = svc.Compile(
            new VisualQuerySpec("users", Selects: [new SelectColumn("name", "UserName")])
        );
        Assert.Contains("[UserName]", result.Sql);
    }

    [Fact]
    public void Compile_Postgres_QuotesIdentifiersWithDoubleQuotes()
    {
        var svc = QueryBuilderService.Create(DatabaseProvider.Postgres, "users");
        CompiledQuery result = svc.Compile(
            new VisualQuerySpec("users", Selects: [new SelectColumn("name", "UserName")])
        );
        Assert.Contains("\"UserName\"", result.Sql);
    }

    // ── REGEX filter via registry ─────────────────────────────────────────────

    [Fact]
    public void Compile_RegexFilter_Postgres_InjectsNativeOperator()
    {
        var svc = QueryBuilderService.Create(DatabaseProvider.Postgres, "users");
        CompiledQuery result = svc.Compile(
            new VisualQuerySpec(
                "users",
                Filters: [new FilterDefinition("email", "REGEX", "'@corp\\.io'")]
            )
        );
        Assert.Contains("~", result.Sql);
    }

    [Fact]
    public void Compile_RegexFilter_SqlServer_UsesPATINDEX()
    {
        var svc = QueryBuilderService.Create(DatabaseProvider.SqlServer, "users");
        CompiledQuery result = svc.Compile(
            new VisualQuerySpec(
                "users",
                Filters: [new FilterDefinition("email", "REGEX", "'%@corp%'")]
            )
        );
        Assert.Contains("PATINDEX", result.Sql);
    }

    // ── Pagination ────────────────────────────────────────────────────────────

    [Fact]
    public void Compile_Pagination_Postgres_UsesLimitOffset()
    {
        var svc = QueryBuilderService.Create(DatabaseProvider.Postgres, "users");
        CompiledQuery result = svc.Compile(new VisualQuerySpec("orders", Limit: 50, Offset: 100));
        Assert.Contains("LIMIT", result.Sql.ToUpper());
        Assert.Contains("OFFSET", result.Sql.ToUpper());
    }

    [Fact]
    public void Compile_Pagination_SqlServer_UsesOffsetFetch()
    {
        var svc = QueryBuilderService.Create(DatabaseProvider.SqlServer, "users");
        CompiledQuery result = svc.Compile(
            new VisualQuerySpec(
                "orders",
                Orders: [new OrderDefinition("id")],
                Limit: 50,
                Offset: 100
            )
        );
        // SqlKata uses OFFSET … ROWS FETCH NEXT … ROWS ONLY for SQL Server
        Assert.Contains("OFFSET", result.Sql.ToUpper());
        Assert.Contains("FETCH", result.Sql.ToUpper());
    }

    // ── JOIN ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Compile_LeftJoin_ContainsLeftJoinClause()
    {
        var svc = QueryBuilderService.Create(DatabaseProvider.Postgres, "users");
        CompiledQuery result = svc.Compile(
            new VisualQuerySpec(
                "orders",
                Joins:
                [
                    new JoinDefinition("customers", "orders.customer_id", "customers.id", "LEFT"),
                ]
            )
        );
        Assert.Contains("left join", result.Sql.ToLower());
        Assert.Contains("customers", result.Sql.ToLower());
    }

    // ── CanonicalFn filter ────────────────────────────────────────────────────

    [Fact]
    public void Compile_CanonicalFnFilter_InjectsRegistryFragment()
    {
        var svc = QueryBuilderService.Create(DatabaseProvider.Postgres, "users");
        CompiledQuery result = svc.Compile(
            new VisualQuerySpec(
                "products",
                Filters:
                [
                    new FilterDefinition(
                        Column: "price",
                        Operator: "=",
                        Value: null,
                        CanonicalFn: SqlFn.IfNull,
                        FnArgs: ["price", "0"]
                    ),
                ]
            )
        );
        Assert.Contains("COALESCE", result.Sql); // Postgres maps IFNULL → COALESCE
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ConnectionConfig Tests
// ─────────────────────────────────────────────────────────────────────────────

public class ConnectionConfigTests
{
    [Fact]
    public void BuildConnectionString_SqlServer_ContainsServer()
    {
        var cfg = new ConnectionConfig(
            DatabaseProvider.SqlServer,
            "myserver",
            1433,
            "mydb",
            "sa",
            "pass"
        );
        Assert.Contains("myserver", cfg.BuildConnectionString());
        Assert.Contains("mydb", cfg.BuildConnectionString());
    }

    [Fact]
    public void BuildConnectionString_Postgres_ContainsHost()
    {
        var cfg = new ConnectionConfig(
            DatabaseProvider.Postgres,
            "pghost",
            5432,
            "pgdb",
            "pguser",
            "pgpass"
        );
        string cs = cfg.BuildConnectionString();
        Assert.Contains("pghost", cs);
        Assert.Contains("pgdb", cs);
        Assert.Contains("pguser", cs);
    }

    [Fact]
    public void BuildConnectionString_MySQL_ContainsServer()
    {
        var cfg = new ConnectionConfig(
            DatabaseProvider.MySql,
            "mysqlhost",
            3306,
            "mydb",
            "root",
            "secret"
        );
        string cs = cfg.BuildConnectionString();
        Assert.Contains("mysqlhost", cs);
        Assert.Contains("mydb", cs);
    }
}



