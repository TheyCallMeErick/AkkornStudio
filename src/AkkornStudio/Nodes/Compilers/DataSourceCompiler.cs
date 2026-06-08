using AkkornStudio.Expressions;
using AkkornStudio.Nodes.Definitions;

namespace AkkornStudio.Nodes.Compilers;

/// <summary>
/// Compiles data source nodes: TableSource and Alias.
/// These are the entry points to the query — the FROM clause sources.
/// </summary>
public sealed class DataSourceCompiler : INodeCompiler
{
    public bool CanCompile(NodeType nodeType) =>
        nodeType is NodeType.TableSource
            or NodeType.Join
            or NodeType.RowSetJoin
            or NodeType.RowSetFilter
            or NodeType.RowSetAggregate
            or NodeType.Subquery
            or NodeType.SubqueryDefinition
            or NodeType.SubqueryReference
            or NodeType.Alias
            or NodeType.CteSource
            or NodeType.CteDefinition;

    public ISqlExpression Compile(NodeInstance node, INodeCompilationContext ctx, string pinName)
    {
        return node.Type switch
        {
            NodeType.TableSource => CompileTableSource(node, pinName),
            NodeType.Join => NullExpr.Instance,
            NodeType.RowSetJoin => NullExpr.Instance,
            NodeType.RowSetFilter => NullExpr.Instance,
            NodeType.RowSetAggregate => NullExpr.Instance,
            NodeType.Subquery => NullExpr.Instance,
            NodeType.SubqueryDefinition => NullExpr.Instance,
            NodeType.SubqueryReference => NullExpr.Instance,
            NodeType.Alias => CompileAlias(node, ctx),
            NodeType.CteSource => CompileCteSource(node, pinName, ctx),
            NodeType.CteDefinition => CompileCteDefinition(),
            _ => throw new NotSupportedException($"Cannot compile {node.Type}"),
        };
    }

    private static ISqlExpression CompileCteDefinition() => NullExpr.Instance;

    private static ISqlExpression CompileTableSource(NodeInstance node, string pinName)
    {
        if (node.TableFullName is null)
            throw new InvalidOperationException(
                $"TableSource node '{node.Id}' has no TableFullName."
            );

        PinDataType outputType = node.ColumnPinTypes is not null
            && node.ColumnPinTypes.TryGetValue(pinName, out PinDataType typed)
            ? typed
            : PinDataType.ColumnRef;

        // Prefer explicit alias when provided; otherwise default to table name.
        if (!string.IsNullOrWhiteSpace(node.Alias))
            return new ColumnExpr(node.Alias.Trim(), pinName, outputType);

        if (
            node.Parameters.TryGetValue("alias", out string? aliasParam)
            && !string.IsNullOrWhiteSpace(aliasParam)
        )
        {
            return new ColumnExpr(aliasParam.Trim(), pinName, outputType);
        }

        // Use the last segment after '.' as the default alias; full name stays in FROM.
        string[] parts = node.TableFullName.Split('.');
        string alias = parts.Last();
        return new ColumnExpr(alias, pinName, outputType);
    }

    private static ISqlExpression CompileAlias(NodeInstance node, INodeCompilationContext ctx)
    {
        ISqlExpression inner = ctx.ResolveInput(node.Id, "expression", PinDataType.Expression);
        string? aliasFromInput = ResolveTextInput(node, "alias_text", ctx);
        string aliasName =
            !string.IsNullOrWhiteSpace(aliasFromInput)
                ? aliasFromInput
                : node.Parameters.TryGetValue("alias", out string? a)
                && !string.IsNullOrWhiteSpace(a)
                    ? a.Trim()
                    : "alias";
        return new AliasExpr(inner, aliasName);
    }

    private static ISqlExpression CompileCteSource(
        NodeInstance node,
        string pinName,
        INodeCompilationContext ctx
    )
    {
        string cteName =
            ResolveCteName(node, pinName, ctx)
            ?? throw new InvalidOperationException(
                $"CTE source node '{node.Id}' could not resolve a CTE name."
            );

        string? aliasFromInput = ResolveTextInput(node, "alias_text", ctx);
        string alias =
            !string.IsNullOrWhiteSpace(aliasFromInput)
                ? aliasFromInput
                : node.Parameters.TryGetValue("alias", out string? a)
                && !string.IsNullOrWhiteSpace(a)
                    ? a.Trim()
                    : cteName;

        PinDataType outputType = pinName.Equals("result", StringComparison.OrdinalIgnoreCase)
            ? PinDataType.RowSet
            : node.ColumnPinTypes is not null
                && node.ColumnPinTypes.TryGetValue(pinName, out PinDataType typed)
                ? typed
                : PinDataType.ColumnRef;

        return new ColumnExpr(alias, pinName, outputType);
    }

    private static string? ResolveTextInput(
        NodeInstance node,
        string pinName,
        INodeCompilationContext ctx
    )
    {
        Connection? wire = ctx.Graph.GetSingleInputConnection(node.Id, pinName);
        bool hasLiteral = node.PinLiterals.ContainsKey(pinName);
        if (wire is null && !hasLiteral)
            return null;

        ISqlExpression expr = ctx.ResolveInput(node.Id, pinName, PinDataType.Text);
        return expr switch
        {
            StringLiteralExpr s when !string.IsNullOrWhiteSpace(s.Value)
                => NormalizeTextLiteralRaw(s.Value),
            LiteralExpr l when !string.IsNullOrWhiteSpace(l.RawValue)
                => NormalizeTextLiteralRaw(l.RawValue),
            _ => null,
        };
    }

    private static string? ResolveCteName(
        NodeInstance node,
        string pinName,
        INodeCompilationContext? ctx
    )
    {
        if (ctx is not null)
        {
            string? fromInput = ResolveTextInput(node, "cte_name_text", ctx);
            if (!string.IsNullOrWhiteSpace(fromInput))
                return fromInput;
        }

        if (
            node.Parameters.TryGetValue("cte_name", out string? cte)
            && !string.IsNullOrWhiteSpace(cte)
        )
        {
            return cte.Trim();
        }

        if (ctx is null)
            return null;

        Connection? cteWire = ctx.Graph.GetSingleInputConnection(node.Id, "cte");
        if (cteWire is null)
            return null;

        if (!ctx.Graph.NodeMap.TryGetValue(cteWire.FromNodeId, out NodeInstance? sourceNode))
            return null;

        if (sourceNode.Type != NodeType.CteDefinition)
            return null;

        string? fromNameInput = ResolveTextInputForNode(sourceNode, "name_text", ctx);
        if (!string.IsNullOrWhiteSpace(fromNameInput))
            return fromNameInput;

        if (
            sourceNode.Parameters.TryGetValue("name", out string? name)
            && !string.IsNullOrWhiteSpace(name)
        )
        {
            return name.Trim();
        }

        if (
            sourceNode.Parameters.TryGetValue("cte_name", out string? legacyName)
            && !string.IsNullOrWhiteSpace(legacyName)
        )
        {
            return legacyName.Trim();
        }

        return null;
    }

    private static string? ResolveTextInputForNode(
        NodeInstance targetNode,
        string pinName,
        INodeCompilationContext ctx
    )
    {
        Connection? wire = ctx.Graph.GetSingleInputConnection(targetNode.Id, pinName);
        bool hasLiteral = targetNode.PinLiterals.ContainsKey(pinName);
        if (wire is null && !hasLiteral)
            return null;

        if (wire is not null)
        {
            ISqlExpression expr = ctx.Resolve(wire.FromNodeId, wire.FromPinName);
            return expr switch
            {
                StringLiteralExpr s when !string.IsNullOrWhiteSpace(s.Value)
                    => NormalizeTextLiteralRaw(s.Value),
                LiteralExpr l when !string.IsNullOrWhiteSpace(l.RawValue)
                    => NormalizeTextLiteralRaw(l.RawValue),
                _ => null,
            };
        }

        if (
            targetNode.PinLiterals.TryGetValue(pinName, out string? literal)
            && !string.IsNullOrWhiteSpace(literal)
        )
            return NormalizeTextLiteralRaw(literal);

        return null;
    }

    private static string? NormalizeTextLiteralRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string trimmed = raw.Trim();
        if (trimmed.Length >= 2)
        {
            if (trimmed[0] == '"' && trimmed[^1] == '"')
            {
                string inner = trimmed[1..^1];
                // Accept both JSON-style escaping and doubled SQL quotes.
                inner = inner.Replace("\\\"", "\"", StringComparison.Ordinal);
                inner = inner.Replace("\"\"", "\"", StringComparison.Ordinal);
                return inner.Trim();
            }

            if (trimmed[0] == '\'' && trimmed[^1] == '\'')
            {
                string inner = trimmed[1..^1];
                inner = inner.Replace("\\'", "'", StringComparison.Ordinal);
                inner = inner.Replace("''", "'", StringComparison.Ordinal);
                return inner.Trim();
            }
        }

        return trimmed;
    }
}
