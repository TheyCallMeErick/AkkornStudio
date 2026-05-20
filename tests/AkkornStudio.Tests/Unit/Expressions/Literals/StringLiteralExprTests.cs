using AkkornStudio.Core;
using AkkornStudio.Registry;
using AdvancedEmitContext = AkkornStudio.Expressions.EmitContext;
using AdvancedPinDataType = AkkornStudio.Expressions.PinDataType;
using AdvancedStringLiteralExpr = AkkornStudio.Expressions.Literals.StringLiteralExpr;

namespace AkkornStudio.Tests.Unit.Expressions.Literals;

public sealed class StringLiteralExprTests
{
    private static AdvancedEmitContext Ctx(DatabaseProvider provider) => new(provider, new SqlFunctionRegistry(provider));

    [Fact]
    public void Emit_Throws_WhenValueIsNull()
    {
        var expr = new AdvancedStringLiteralExpr(null!);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.Postgres)));

        Assert.Equal("String literal value cannot be null.", ex.Message);
    }

    [Fact]
    public void Emit_QuotesAndEscapesSingleQuotes()
    {
        var expr = new AdvancedStringLiteralExpr("O'Brien");

        string sql = expr.Emit(Ctx(DatabaseProvider.MySql));

        Assert.Equal("'O''Brien'", sql);
    }

    [Fact]
    public void OutputType_IsText()
    {
        var expr = new AdvancedStringLiteralExpr("abc");

        Assert.Equal(AdvancedPinDataType.Text, expr.OutputType);
    }
}
