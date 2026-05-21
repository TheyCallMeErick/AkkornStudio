using AkkornStudio.Expressions;
using AkkornStudio.Expressions.Operations;
using AkkornStudio.Core;
using AkkornStudio.Nodes.Definitions;

namespace AkkornStudio.Nodes.Compilers;

/// <summary>
/// Compiles output and modifier nodes: ResultOutput, Top, CompileWhere.
/// These nodes are terminal nodes that don't produce expressions but control query assembly.
/// </summary>
public sealed class OutputCompiler : INodeCompiler
{
    public bool CanCompile(NodeType nodeType) =>
        nodeType is NodeType.ResultOutput
            or NodeType.Top
            or NodeType.CompileWhere
            or NodeType.SetOperation;

    public ISqlExpression Compile(NodeInstance node, INodeCompilationContext ctx, string pinName)
    {
        return node.Type switch
        {
            NodeType.ResultOutput => NullExpr.Instance, // Terminal node
            NodeType.Top => NullExpr.Instance, // Modifier node
            NodeType.CompileWhere => CompileWhere(node, ctx),
            NodeType.SetOperation => NullExpr.Instance, // Query combiner metadata node

            _ => throw new NotSupportedException($"Cannot compile {node.Type}"),
        };
    }

    private static ISqlExpression CompileWhere(NodeInstance node, INodeCompilationContext ctx)
    {
        IReadOnlyList<ISqlExpression> conditions = ResolveCompileWhereConditions(node, ctx);

        if (conditions.Count == 0)
            return new LiteralExpr(GetTruePredicateSql(ctx.EmitContext.Provider), PinDataType.Boolean);

        if (conditions.Count == 1)
            return conditions[0];

        return new LogicGateExpr(LogicOperator.And, conditions);
    }

    private static IReadOnlyList<ISqlExpression> ResolveCompileWhereConditions(
        NodeInstance node,
        INodeCompilationContext ctx
    )
    {
        IReadOnlyList<ISqlExpression> directConditions = ctx.ResolveInputs(node.Id, "conditions");
        if (directConditions.Count > 0)
            return directConditions;

        IReadOnlyList<Connection> dynamicConditions = ctx.Graph
            .GetInputConnections(node.Id, "conditions")
            .Concat(
                ctx.Graph.Connections
                    .Where(c =>
                        c.ToNodeId == node.Id
                        && c.ToPinName.StartsWith("cond_", StringComparison.OrdinalIgnoreCase)
                    )
            )
            .Distinct()
            .OrderBy(c => c.ToPinName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (dynamicConditions.Count == 0)
            return [];

        return dynamicConditions.Select(c => ctx.Resolve(c.FromNodeId, c.FromPinName)).ToList();
    }

    private static string GetTruePredicateSql(DatabaseProvider provider) =>
        provider == DatabaseProvider.Postgres ? "TRUE" : "1 = 1";
}
