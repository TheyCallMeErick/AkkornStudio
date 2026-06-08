namespace AkkornStudio.Expressions.Literals;

/// <summary>A quoted string constant: the canvas writes 'hello world'</summary>
public sealed record StringLiteralExpr(string Value) : ISqlExpression
{
    public PinDataType OutputType => PinDataType.Text;

    public string Emit(EmitContext ctx)
    {
        if (Value is null)
            throw new InvalidOperationException("String literal value cannot be null.");

        return EmitContext.QuoteLiteral(Value);
    }
}
