using AkkornStudio.Core;
using AkkornStudio.Registry;
using AdvancedBetweenExpr = AkkornStudio.Expressions.Operations.BetweenExpr;
using AdvancedEmitContext = AkkornStudio.Expressions.EmitContext;
using AdvancedISqlExpression = AkkornStudio.Expressions.ISqlExpression;
using AdvancedPinDataType = AkkornStudio.Expressions.PinDataType;

namespace AkkornStudio.Tests.Unit.Expressions.Operations;

public sealed class BetweenExprAdvancedTests
{
    private static AdvancedEmitContext Ctx(DatabaseProvider provider) => new(provider, new SqlFunctionRegistry(provider));

    [Fact]
    public void Emit_Between_WithNumericTypes_ProducesExpectedSql()
    {
        var expr = new AdvancedBetweenExpr(
            new StaticExpr("score", AdvancedPinDataType.Number),
            new StaticExpr("10", AdvancedPinDataType.Integer),
            new StaticExpr("20.5", AdvancedPinDataType.Decimal));

        string sql = expr.Emit(Ctx(DatabaseProvider.Postgres));

        Assert.Equal("(score BETWEEN 10 AND 20.5)", sql);
        Assert.Equal(AdvancedPinDataType.Boolean, expr.OutputType);
    }

    [Fact]
    public void Emit_NotBetween_WithSameTypes_ProducesExpectedSql()
    {
        var expr = new AdvancedBetweenExpr(
            new StaticExpr("created_at", AdvancedPinDataType.DateTime),
            new StaticExpr("'2024-01-01'", AdvancedPinDataType.DateTime),
            new StaticExpr("'2024-12-31'", AdvancedPinDataType.DateTime),
            Negate: true);

        string sql = expr.Emit(Ctx(DatabaseProvider.MySql));

        Assert.Equal("(created_at NOT BETWEEN '2024-01-01' AND '2024-12-31')", sql);
    }

    [Fact]
    public void Emit_AllowsUnknownExpressionType()
    {
        var expr = new AdvancedBetweenExpr(
            new StaticExpr("payload", AdvancedPinDataType.Expression),
            new StaticExpr("'a'", AdvancedPinDataType.Text),
            new StaticExpr("'z'", AdvancedPinDataType.Text));

        string sql = expr.Emit(Ctx(DatabaseProvider.Postgres));

        Assert.Equal("(payload BETWEEN 'a' AND 'z')", sql);
    }

    [Fact]
    public void Emit_Throws_WhenTypesAreNotComparable()
    {
        var expr = new AdvancedBetweenExpr(
            new StaticExpr("total", AdvancedPinDataType.Number),
            new StaticExpr("'a'", AdvancedPinDataType.Text),
            new StaticExpr("'z'", AdvancedPinDataType.Text));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.SqlServer)));

        Assert.Equal(
            "BETWEEN requires comparable types. Received Input=Number, Lo=Text, Hi=Text.",
            ex.Message);
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
