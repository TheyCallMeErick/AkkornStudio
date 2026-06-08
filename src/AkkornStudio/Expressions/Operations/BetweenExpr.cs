namespace AkkornStudio.Expressions.Operations;

/// <summary>BETWEEN … AND … or NOT BETWEEN</summary>
public sealed record BetweenExpr(
    ISqlExpression Input,
    ISqlExpression Lo,
    ISqlExpression Hi,
    bool Negate = false
) : ISqlExpression
{
    public PinDataType OutputType => PinDataType.Boolean;

    public string Emit(EmitContext ctx)
    {
        EnsureComparableTypes();
        string keyword = Negate ? "NOT BETWEEN" : "BETWEEN";
        return $"({Input.Emit(ctx)} {keyword} {Lo.Emit(ctx)} AND {Hi.Emit(ctx)})";
    }

    private void EnsureComparableTypes()
    {
        PinDataType inputType = Input.OutputType;
        PinDataType loType = Lo.OutputType;
        PinDataType hiType = Hi.OutputType;
        if (IsUnknown(inputType) || IsUnknown(loType) || IsUnknown(hiType))
            return;

        if (inputType == loType && loType == hiType)
            return;

        if (IsNumeric(inputType) && IsNumeric(loType) && IsNumeric(hiType))
            return;

        throw new InvalidOperationException(
            $"BETWEEN requires comparable types. Received Input={inputType}, Lo={loType}, Hi={hiType}.");
    }

    private static bool IsUnknown(PinDataType type) => type == PinDataType.Expression;

    private static bool IsNumeric(PinDataType type) =>
        type is PinDataType.Integer or PinDataType.Decimal or PinDataType.Number;
}
