namespace AkkornStudio.Expressions.Columns;

/// <summary>
/// Passes a raw SQL fragment through unchanged (escape hatch for advanced users).
/// </summary>
public sealed record RawSqlExpr(string Sql, PinDataType OutputType = PinDataType.Expression)
    : ISqlExpression
{
    public string Emit(EmitContext ctx)
    {
        if (string.IsNullOrWhiteSpace(Sql))
            throw new InvalidOperationException("Raw SQL expression cannot be null or whitespace.");

        return Sql;
    }
}
