using AkkornStudio.Core;
using AkkornStudio.Registry;
using AdvancedEmitContext = AkkornStudio.Expressions.EmitContext;
using AdvancedISqlExpression = AkkornStudio.Expressions.ISqlExpression;
using AdvancedPinDataType = AkkornStudio.Expressions.PinDataType;
using AdvancedTopExpr = AkkornStudio.Expressions.Advanced.TopExpr;

namespace AkkornStudio.Tests.Unit.Expressions.Advanced;

public sealed class TopExprAdvancedTests
{
    private static AdvancedEmitContext Ctx(DatabaseProvider provider) => new(provider, new SqlFunctionRegistry(provider));

    [Fact]
    public void OutputType_UsesResultOutputType()
    {
        var expr = new AdvancedTopExpr(
            new StaticExpr("SELECT 1", AdvancedPinDataType.Json),
            new StaticExpr("10", AdvancedPinDataType.Number));

        Assert.Equal(AdvancedPinDataType.Json, expr.OutputType);
    }

    [Fact]
    public void Emit_Postgres_AppendsLimit_AndTrimsSemicolon()
    {
        var expr = new AdvancedTopExpr(
            new StaticExpr("SELECT * FROM users; ", AdvancedPinDataType.RowSet),
            new StaticExpr("5", AdvancedPinDataType.Number));

        string sql = expr.Emit(Ctx(DatabaseProvider.Postgres));

        Assert.Equal("SELECT * FROM users LIMIT 5", sql);
    }

    [Fact]
    public void Emit_SqlServer_SelectStatement_InsertsTopClause()
    {
        var expr = new AdvancedTopExpr(
            new StaticExpr("SELECT id FROM users", AdvancedPinDataType.RowSet),
            new StaticExpr("3", AdvancedPinDataType.Number));

        string sql = expr.Emit(Ctx(DatabaseProvider.SqlServer));

        Assert.Equal("SELECT TOP (3) id FROM users", sql);
    }

    [Fact]
    public void Emit_SqlServer_NonSelect_WrapsAsTopSubquery()
    {
        var expr = new AdvancedTopExpr(
            new StaticExpr("\"users\".\"id\"", AdvancedPinDataType.ColumnSet),
            new StaticExpr("2", AdvancedPinDataType.Number));

        string sql = expr.Emit(Ctx(DatabaseProvider.SqlServer));

        Assert.Equal("SELECT TOP (2) * FROM (\"users\".\"id\") AS __top_expr", sql);
    }

    [Fact]
    public void Emit_Throws_WhenCountIsEmpty()
    {
        var expr = new AdvancedTopExpr(
            new StaticExpr("SELECT * FROM users", AdvancedPinDataType.RowSet),
            new StaticExpr("   ", AdvancedPinDataType.Number));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.MySql)));

        Assert.Equal("TOP/LIMIT count expression is required.", ex.Message);
    }

    private sealed record StaticExpr(string Sql, AdvancedPinDataType OutputType) : AdvancedISqlExpression
    {
        public string Emit(AdvancedEmitContext ctx)
        {
            _ = ctx;
            return Sql;
        }
    }
}
