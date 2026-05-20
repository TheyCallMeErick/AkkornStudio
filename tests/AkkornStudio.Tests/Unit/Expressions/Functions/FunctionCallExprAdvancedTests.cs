using AkkornStudio.Core;
using AkkornStudio.Registry;
using AdvancedEmitContext = AkkornStudio.Expressions.EmitContext;
using AdvancedFunctionCallExpr = AkkornStudio.Expressions.Functions.FunctionCallExpr;
using AdvancedISqlExpression = AkkornStudio.Expressions.ISqlExpression;
using AdvancedPinDataType = AkkornStudio.Expressions.PinDataType;

namespace AkkornStudio.Tests.Unit.Expressions.Functions;

public sealed class FunctionCallExprAdvancedTests
{
    private static AdvancedEmitContext Ctx(DatabaseProvider provider) => new(provider, new SqlFunctionRegistry(provider));

    [Fact]
    public void Emit_Throws_WhenFunctionNameIsBlank()
    {
        var expr = new AdvancedFunctionCallExpr("  ", []);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.Postgres)));

        Assert.Equal("Function name is required.", ex.Message);
    }

    [Fact]
    public void Emit_Throws_WhenArgsIsNull()
    {
        var expr = new AdvancedFunctionCallExpr(SqlFn.Upper, null!);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.Postgres)));

        Assert.Equal($"Function '{SqlFn.Upper}' requires an argument list.", ex.Message);
    }

    [Fact]
    public void Emit_Throws_WhenFunctionIsUnsupportedForProvider()
    {
        var expr = new AdvancedFunctionCallExpr(
            SqlFn.RegexExtract,
            [new StaticExpr("'abc'", AdvancedPinDataType.Text), new StaticExpr("'a'", AdvancedPinDataType.Text)]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.SQLite)));

        Assert.Contains("not natively supported", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(DatabaseProvider.SQLite.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_Throws_WhenFunctionIsNotMappedInRegistry()
    {
        var expr = new AdvancedFunctionCallExpr(
            "UNKNOWN_FUNCTION",
            [new StaticExpr("1", AdvancedPinDataType.Number)]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.Postgres)));

        Assert.Equal(
            "Function 'UNKNOWN_FUNCTION' is not supported for provider 'Postgres'.",
            ex.Message);
    }

    [Fact]
    public void Emit_WrapsRegistryNotSupportedException_AsInvalidOperationException()
    {
        var expr = new AdvancedFunctionCallExpr(
            "CUSTOM_FN",
            [new StaticExpr("1", AdvancedPinDataType.Number)]);

        var ctx = new AdvancedEmitContext(
            DatabaseProvider.Postgres,
            new ThrowingRegistry());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(ctx));

        Assert.Equal("Function 'CUSTOM_FN' is not supported for provider 'Postgres'.", ex.Message);
        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Fact]
    public void Emit_Throws_WhenAnyArgumentIsNull()
    {
        var expr = new AdvancedFunctionCallExpr(SqlFn.Upper, [null!]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.MySql)));

        Assert.Equal($"Function '{SqlFn.Upper}' has null argument at index 0.", ex.Message);
    }

    [Fact]
    public void Emit_UsesRegistry_WhenFunctionAndArgumentsAreValid()
    {
        var expr = new AdvancedFunctionCallExpr(
            SqlFn.Upper,
            [new StaticExpr("\"users\".\"name\"", AdvancedPinDataType.Text)],
            AdvancedPinDataType.Text);

        string sql = expr.Emit(Ctx(DatabaseProvider.Postgres));

        Assert.Equal("UPPER(\"users\".\"name\")", sql);
        Assert.Equal(AdvancedPinDataType.Text, expr.OutputType);
    }

    private sealed record StaticExpr(string Sql, AdvancedPinDataType OutputType) : AdvancedISqlExpression
    {
        public string Emit(AdvancedEmitContext ctx)
        {
            _ = ctx;
            return Sql;
        }
    }

    private sealed class ThrowingRegistry : ISqlFunctionRegistry
    {
        public string GetFunction(string functionName, params string[] args) =>
            throw new NotSupportedException("custom unsupported");

        public bool IsSupported(string functionName) => true;

        public IReadOnlyList<PortabilityWarning> CheckPortability(IEnumerable<string> functionNames) => [];
    }
}
