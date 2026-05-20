using AkkornStudio.Core;
using AkkornStudio.Registry;
using AdvancedColumnExpr = AkkornStudio.Expressions.Columns.ColumnExpr;
using AdvancedEmitContext = AkkornStudio.Expressions.EmitContext;
using AdvancedPinDataType = AkkornStudio.Expressions.PinDataType;

namespace AkkornStudio.Tests.Unit.Expressions.Columns;

public sealed class ColumnExprAdvancedTests
{
    private static AdvancedEmitContext Ctx(DatabaseProvider provider) => new(provider, new SqlFunctionRegistry(provider));

    [Fact]
    public void Emit_QuotesRegularColumn_WithAndWithoutTableAlias()
    {
        string qualified = new AdvancedColumnExpr("users", "email").Emit(Ctx(DatabaseProvider.Postgres));
        string unqualified = new AdvancedColumnExpr("", "name").Emit(Ctx(DatabaseProvider.Postgres));

        Assert.Equal("\"users\".\"email\"", qualified);
        Assert.Equal("\"name\"", unqualified);
    }

    [Fact]
    public void Emit_Wildcard_AllowsColumnSetProjection()
    {
        string allColumns = new AdvancedColumnExpr("", "*", AdvancedPinDataType.ColumnSet)
            .Emit(Ctx(DatabaseProvider.Postgres));
        string tableColumns = new AdvancedColumnExpr("users", "*", AdvancedPinDataType.ColumnSet)
            .Emit(Ctx(DatabaseProvider.Postgres));

        Assert.Equal("*", allColumns);
        Assert.Equal("\"users\".*", tableColumns);
    }

    [Fact]
    public void Emit_Wildcard_ThrowsForNonColumnSetOutputType()
    {
        var expr = new AdvancedColumnExpr("users", "*", AdvancedPinDataType.ColumnRef);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.Postgres)));

        Assert.Equal("Wildcard column '*' is only allowed for ColumnSet projections.", ex.Message);
    }
}
