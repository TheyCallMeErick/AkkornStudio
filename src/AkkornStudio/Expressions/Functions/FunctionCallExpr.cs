using AkkornStudio.Registry;

namespace AkkornStudio.Expressions.Functions;

/// <summary>
/// Calls a canonical function through the <see cref="ISqlFunctionRegistry"/>.
/// Each child expression is emitted first; the resulting strings are passed
/// as args to the registry.
///
/// Example: FunctionCallExpr(SqlFn.Upper, [ColumnExpr("users","email")])
///   Postgres/MySQL/SQL Server → UPPER("users"."email")
/// </summary>
public sealed record FunctionCallExpr(
    string FunctionName,
    IReadOnlyList<ISqlExpression> Args,
    PinDataType OutputType = PinDataType.Expression
) : ISqlExpression
{
    public string Emit(EmitContext ctx)
    {
        if (string.IsNullOrWhiteSpace(FunctionName))
            throw new InvalidOperationException("Function name is required.");

        if (Args is null)
            throw new InvalidOperationException($"Function '{FunctionName}' requires an argument list.");

        if (!ctx.Registry.IsSupported(FunctionName))
            throw new InvalidOperationException(
                $"Function '{FunctionName}' is not supported for provider '{ctx.Provider}'.");

        PortabilityWarning? warning = ctx.Registry
            .CheckPortability([FunctionName])
            .FirstOrDefault();
        if (warning is not null)
            throw new InvalidOperationException(
                $"{warning.Message} Provider: {ctx.Provider}. Suggestion: {warning.Suggestion}");

        string[] emittedArgs = Args
            .Select((arg, index) =>
            {
                if (arg is null)
                    throw new InvalidOperationException(
                        $"Function '{FunctionName}' has null argument at index {index}.");

                return arg.Emit(ctx);
            })
            .ToArray();

        try
        {
            return ctx.Registry.GetFunction(FunctionName, emittedArgs);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException(
                $"Function '{FunctionName}' is not supported for provider '{ctx.Provider}'.",
                ex);
        }
    }
}
