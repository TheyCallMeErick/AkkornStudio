using AkkornStudio.Core;

namespace AkkornStudio.Expressions.Advanced;

/// <summary>
/// TOP / LIMIT clause: restricts the result set to N rows.
/// Emits as TOP N (SQL Server) or LIMIT N (PostgreSQL/MySQL).
/// </summary>
public sealed record TopExpr(ISqlExpression Result, ISqlExpression Count) : ISqlExpression
{
    public PinDataType OutputType => Result.OutputType;

    public string Emit(EmitContext ctx)
    {
        string resultSql = Result.Emit(ctx).Trim().TrimEnd(';').TrimEnd();
        string countSql = Count.Emit(ctx).Trim();
        if (string.IsNullOrWhiteSpace(countSql))
            throw new InvalidOperationException("TOP/LIMIT count expression is required.");

        if (ctx.Provider == DatabaseProvider.SqlServer)
        {
            if (resultSql.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
                return resultSql.Insert(6, $" TOP ({countSql})");

            return $"SELECT TOP ({countSql}) * FROM ({resultSql}) AS __top_expr";
        }

        return $"{resultSql} LIMIT {countSql}";
    }
}
