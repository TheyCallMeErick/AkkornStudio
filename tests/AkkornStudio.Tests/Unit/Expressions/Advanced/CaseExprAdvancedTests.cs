using AkkornStudio.Core;
using AkkornStudio.Registry;
using AdvancedCaseExpr = AkkornStudio.Expressions.Advanced.CaseExpr;
using AdvancedEmitContext = AkkornStudio.Expressions.EmitContext;
using AdvancedISqlExpression = AkkornStudio.Expressions.ISqlExpression;
using AdvancedPinDataType = AkkornStudio.Expressions.PinDataType;
using AdvancedWhenClause = AkkornStudio.Expressions.Advanced.WhenClause;

namespace AkkornStudio.Tests.Unit.Expressions.Advanced;

public sealed class CaseExprAdvancedTests
{
    private static AdvancedEmitContext Ctx(DatabaseProvider provider) => new(provider, new SqlFunctionRegistry(provider));

    [Fact]
    public void Emit_Throws_WhenWhensIsEmpty()
    {
        var expr = new AdvancedCaseExpr([]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.Postgres)));

        Assert.Equal("CASE expression requires at least one WHEN clause.", ex.Message);
    }

    [Fact]
    public void Emit_Throws_WhenWhensIsNull()
    {
        var expr = new AdvancedCaseExpr(null!);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.MySql)));

        Assert.Equal("CASE expression requires at least one WHEN clause.", ex.Message);
    }

    [Fact]
    public void Emit_BuildsCaseExpression_WithElse()
    {
        var expr = new AdvancedCaseExpr(
            [
                new AdvancedWhenClause(
                    new StaticExpr("\"score\" > 90", AdvancedPinDataType.Boolean),
                    new StaticExpr("'A'", AdvancedPinDataType.Text)),
                new AdvancedWhenClause(
                    new StaticExpr("\"score\" > 80", AdvancedPinDataType.Boolean),
                    new StaticExpr("'B'", AdvancedPinDataType.Text))
            ],
            new StaticExpr("'C'", AdvancedPinDataType.Text));

        string sql = expr.Emit(Ctx(DatabaseProvider.Postgres));

        Assert.Equal("CASE WHEN \"score\" > 90 THEN 'A' WHEN \"score\" > 80 THEN 'B' ELSE 'C' END", sql);
    }

    [Fact]
    public void OutputType_UsesElseOutputType_WhenPresent()
    {
        var expr = new AdvancedCaseExpr(
            [new AdvancedWhenClause(new StaticExpr("1=1", AdvancedPinDataType.Boolean), new StaticExpr("'ok'", AdvancedPinDataType.Text))],
            new StaticExpr("CURRENT_TIMESTAMP", AdvancedPinDataType.DateTime));

        Assert.Equal(AdvancedPinDataType.DateTime, expr.OutputType);
    }

    [Fact]
    public void OutputType_DefaultsToExpression_WhenElseIsMissing()
    {
        var expr = new AdvancedCaseExpr(
            [new AdvancedWhenClause(new StaticExpr("1=1", AdvancedPinDataType.Boolean), new StaticExpr("'ok'", AdvancedPinDataType.Text))]);

        Assert.Equal(AdvancedPinDataType.Expression, expr.OutputType);
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
