using AkkornStudio.Core;
using AkkornStudio.Registry;
using AdvancedEmitContext = AkkornStudio.Expressions.EmitContext;
using AdvancedPinDataType = AkkornStudio.Expressions.PinDataType;
using AdvancedRawSqlExpr = AkkornStudio.Expressions.Columns.RawSqlExpr;

namespace AkkornStudio.Tests.Unit.Expressions.Columns;

public sealed class RawSqlExprAdvancedTests
{
    private static AdvancedEmitContext Ctx(DatabaseProvider provider) => new(provider, new SqlFunctionRegistry(provider));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Emit_Throws_WhenSqlIsEmptyOrWhitespace(string sql)
    {
        var expr = new AdvancedRawSqlExpr(sql);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.Postgres)));

        Assert.Equal("Raw SQL expression cannot be null or whitespace.", ex.Message);
    }

    [Fact]
    public void Emit_Throws_WhenSqlIsNull()
    {
        var expr = new AdvancedRawSqlExpr(null!);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.SQLite)));

        Assert.Equal("Raw SQL expression cannot be null or whitespace.", ex.Message);
    }

    [Fact]
    public void Emit_ReturnsRawSql_WhenValid()
    {
        var expr = new AdvancedRawSqlExpr("COUNT(*)", AdvancedPinDataType.Number);

        string sql = expr.Emit(Ctx(DatabaseProvider.MySql));

        Assert.Equal("COUNT(*)", sql);
        Assert.Equal(AdvancedPinDataType.Number, expr.OutputType);
    }
}
