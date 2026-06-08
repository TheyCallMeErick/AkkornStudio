using AkkornStudio.Core;
using AkkornStudio.Registry;
using AdvancedCastExpr = AkkornStudio.Expressions.Advanced.CastExpr;
using AdvancedCastTargetType = AkkornStudio.Expressions.Advanced.CastTargetType;
using AdvancedEmitContext = AkkornStudio.Expressions.EmitContext;
using AdvancedLiteralExpr = AkkornStudio.Expressions.Literals.LiteralExpr;
using AdvancedPinDataType = AkkornStudio.Expressions.PinDataType;

namespace AkkornStudio.Tests.Unit.Expressions.Advanced;

public sealed class CastExprAdvancedTests
{
    private static AdvancedEmitContext Ctx(DatabaseProvider provider) => new(provider, new SqlFunctionRegistry(provider));

    public static IEnumerable<object[]> ProviderTypeMappings()
    {
        yield return [DatabaseProvider.SqlServer, AdvancedCastTargetType.Text, "NVARCHAR(MAX)"];
        yield return [DatabaseProvider.Postgres, AdvancedCastTargetType.Text, "TEXT"];
        yield return [DatabaseProvider.MySql, AdvancedCastTargetType.Text, "TEXT"];
        yield return [DatabaseProvider.Postgres, AdvancedCastTargetType.Integer, "INTEGER"];
        yield return [DatabaseProvider.MySql, AdvancedCastTargetType.Integer, "INT"];
        yield return [DatabaseProvider.Postgres, AdvancedCastTargetType.BigInt, "BIGINT"];
        yield return [DatabaseProvider.Postgres, AdvancedCastTargetType.Decimal, "DECIMAL(18,4)"];
        yield return [DatabaseProvider.Postgres, AdvancedCastTargetType.Float, "DOUBLE PRECISION"];
        yield return [DatabaseProvider.SqlServer, AdvancedCastTargetType.Float, "FLOAT"];
        yield return [DatabaseProvider.SqlServer, AdvancedCastTargetType.Boolean, "BIT"];
        yield return [DatabaseProvider.MySql, AdvancedCastTargetType.Boolean, "BOOLEAN"];
        yield return [DatabaseProvider.Postgres, AdvancedCastTargetType.Date, "DATE"];
        yield return [DatabaseProvider.Postgres, AdvancedCastTargetType.DateTime, "TIMESTAMP"];
        yield return [DatabaseProvider.MySql, AdvancedCastTargetType.DateTime, "DATETIME"];
        yield return [DatabaseProvider.SqlServer, AdvancedCastTargetType.Timestamp, "DATETIMEOFFSET"];
        yield return [DatabaseProvider.Postgres, AdvancedCastTargetType.Timestamp, "TIMESTAMPTZ"];
        yield return [DatabaseProvider.SqlServer, AdvancedCastTargetType.Uuid, "UNIQUEIDENTIFIER"];
        yield return [DatabaseProvider.Postgres, AdvancedCastTargetType.Uuid, "UUID"];
    }

    [Theory]
    [MemberData(nameof(ProviderTypeMappings))]
    public void Emit_UsesExpectedProviderType(
        DatabaseProvider provider,
        AdvancedCastTargetType targetType,
        string expectedSqlType)
    {
        var expr = new AdvancedCastExpr(new AdvancedLiteralExpr("1"), targetType);

        string sql = expr.Emit(Ctx(provider));

        Assert.Equal($"CAST(1 AS {expectedSqlType})", sql);
    }

    [Theory]
    [InlineData(AdvancedCastTargetType.Text, AdvancedPinDataType.Text)]
    [InlineData(AdvancedCastTargetType.Integer, AdvancedPinDataType.Number)]
    [InlineData(AdvancedCastTargetType.BigInt, AdvancedPinDataType.Number)]
    [InlineData(AdvancedCastTargetType.Decimal, AdvancedPinDataType.Number)]
    [InlineData(AdvancedCastTargetType.Float, AdvancedPinDataType.Number)]
    [InlineData(AdvancedCastTargetType.Boolean, AdvancedPinDataType.Boolean)]
    [InlineData(AdvancedCastTargetType.Date, AdvancedPinDataType.DateTime)]
    [InlineData(AdvancedCastTargetType.DateTime, AdvancedPinDataType.DateTime)]
    [InlineData(AdvancedCastTargetType.Timestamp, AdvancedPinDataType.DateTime)]
    [InlineData(AdvancedCastTargetType.Uuid, AdvancedPinDataType.Expression)]
    public void OutputType_MatchesExpectedPinType(AdvancedCastTargetType targetType, AdvancedPinDataType expected)
    {
        var expr = new AdvancedCastExpr(new AdvancedLiteralExpr("1"), targetType);

        Assert.Equal(expected, expr.OutputType);
    }

    [Fact]
    public void OutputType_ThrowsForUnsupportedTargetType()
    {
        var expr = new AdvancedCastExpr(new AdvancedLiteralExpr("1"), (AdvancedCastTargetType)(-1));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = expr.OutputType;
        });

        Assert.Contains("Unsupported cast target type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ThrowsForUnsupportedTargetType()
    {
        var expr = new AdvancedCastExpr(new AdvancedLiteralExpr("1"), (AdvancedCastTargetType)(-1));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            expr.Emit(Ctx(DatabaseProvider.Postgres)));

        Assert.Contains("Unsupported cast target type", ex.Message, StringComparison.Ordinal);
    }
}
