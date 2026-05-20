namespace AkkornStudio.Expressions.Columns;

/// <summary>
/// A table column reference: table.column — the "output pin" of a DataSource node.
/// Every column on the canvas becomes one of these.
/// </summary>
public sealed record ColumnExpr(
    string TableAlias,
    string ColumnName,
    PinDataType OutputType = PinDataType.ColumnRef
) : ISqlExpression
{
    public string Emit(EmitContext ctx)
    {
        if (ColumnName == "*")
        {
            if (OutputType != PinDataType.ColumnSet)
            {
                throw new InvalidOperationException(
                    "Wildcard column '*' is only allowed for ColumnSet projections.");
            }

            return string.IsNullOrEmpty(TableAlias) ? "*" : $"{ctx.QuoteIdentifier(TableAlias)}.*";
        }

        return string.IsNullOrEmpty(TableAlias)
            ? ctx.QuoteIdentifier(ColumnName)
            : $"{ctx.QuoteIdentifier(TableAlias)}.{ctx.QuoteIdentifier(ColumnName)}";
    }
}
