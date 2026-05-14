using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using AkkornStudio.Nodes;
using AkkornStudio.SqlImport.Diagnostics;
using AkkornStudio.UI.Services.SqlImport;
using AkkornStudio.UI.Services.SqlImport.Build;
using AkkornStudio.UI.Services.SqlImport.Execution.Parsing;
using AkkornStudio.UI.Services.SqlImport.Rewriting;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.Services.SqlImport.Execution.Applying;

internal sealed class SqlImportWhereClauseApplier(CanvasViewModel canvas) : ISqlImportApplyStep
{
    private readonly CanvasViewModel _canvas = canvas;

    public SqlImportApplyResult Apply(SqlImportApplyContext context)
    {
        SqlImportParsedQuery query = context.Query;
        ImportBuildContext coreContext = context.CoreContext;
        ObservableCollection<ImportReportItem> report = context.Report;
        var layout = new SqlImportLayoutCalculator(coreContext.Layout);

        if (query.WhereClause is null)
            return new SqlImportApplyResult(0, 0, 0);

        int imported = 0;
        int partial = 0;
        bool fallbackActivated = false;

        var tableNodes = coreContext.TableNodes;
        NodeViewModel result = coreContext.ResultNode;
        int sourceCount = query.FromParts.Count;
        string whereClause = StripOuterParentheses(query.WhereClause);
        List<ImportFromPart> fromParts =
        [
            .. query.FromParts.Select(p => new ImportFromPart(p.Table, p.Alias, p.JoinType, p.OnClause)),
        ];

        if (TryBuildBooleanExpression(
                whereClause,
                query,
                fromParts,
                tableNodes,
                layout,
                sourceCount,
                out NodeViewModel? rootCondition,
            ref fallbackActivated,
                ref imported,
                ref partial))
        {
            SqlImportClauseApplyUtilities.SafeWire(rootCondition!, "result", result, "where", _canvas);

            report.Add(new ImportReportItem(
                $"WHERE {SqlImportClauseApplyUtilities.Truncate(query.WhereClause, 60)}",
                ImportItemStatus.Imported,
                "WHERE predicate mapped to graph condition chain.",
                rootCondition!.Id));

            if (fallbackActivated)
            {
                report.Add(SqlImportReportFactory.Partial(
                    SqlImportDiagnosticCodes.FallbackRegexUsed,
                    "WHERE fallback",
                    SqlImportDiagnosticMessages.WhereColumnFallbackReportNote,
                    rootCondition.Id));
                partial++;
            }
        }
        else
        {
            report.Add(SqlImportReportFactory.WhereUnsupported(
                $"WHERE {SqlImportClauseApplyUtilities.Truncate(query.WhereClause, 60)}"
            ));
            partial++;
        }

        return new SqlImportApplyResult(imported, partial, 0);
    }

    private bool TryBuildBooleanExpression(
        string expression,
        SqlImportParsedQuery query,
        IReadOnlyList<ImportFromPart> fromParts,
        IReadOnlyList<NodeViewModel> tableNodes,
        SqlImportLayoutCalculator layout,
        int sourceCount,
        out NodeViewModel? node,
        ref bool fallbackActivated,
        ref int imported,
        ref int partial)
    {
        node = null;
        string normalized = StripOuterParentheses(expression);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        List<string> orParts = SplitTopLevelByKeyword(normalized, "OR");
        if (orParts.Count > 1)
            return TryBuildLogicalGate(NodeType.Or, orParts, query, fromParts, tableNodes, layout, sourceCount, out node, ref fallbackActivated, ref imported, ref partial);
        if (TryFallbackSplitParenthesizedBinary(normalized, "OR", out IReadOnlyList<string> fallbackOrParts))
            return TryBuildLogicalGate(NodeType.Or, fallbackOrParts, query, fromParts, tableNodes, layout, sourceCount, out node, ref fallbackActivated, ref imported, ref partial);

        List<string> andParts = SplitTopLevelByKeyword(normalized, "AND");
        if (andParts.Count > 1)
            return TryBuildLogicalGate(NodeType.And, andParts, query, fromParts, tableNodes, layout, sourceCount, out node, ref fallbackActivated, ref imported, ref partial);
        if (TryFallbackSplitParenthesizedBinary(normalized, "AND", out IReadOnlyList<string> fallbackAndParts))
            return TryBuildLogicalGate(NodeType.And, fallbackAndParts, query, fromParts, tableNodes, layout, sourceCount, out node, ref fallbackActivated, ref imported, ref partial);

        if (TryStripNotPrefix(normalized, out string inner))
        {
            if (!TryBuildBooleanExpression(inner, query, fromParts, tableNodes, layout, sourceCount, out NodeViewModel? innerNode, ref fallbackActivated, ref imported, ref partial) || innerNode is null)
                return false;

            NodeViewModel notNode = new(NodeDefinitionRegistry.Get(NodeType.Not), layout.ComparisonPosition(sourceCount));
            _canvas.Nodes.Add(notNode);
            SqlImportClauseApplyUtilities.SafeWire(innerNode, "result", notNode, "condition", _canvas);
            imported++;
            node = notNode;
            return true;
        }

        return TryBuildPredicateLeaf(normalized, query, fromParts, tableNodes, layout, sourceCount, out node, ref fallbackActivated, ref imported, ref partial);
    }

    private bool TryBuildLogicalGate(
        NodeType gateType,
        IReadOnlyList<string> parts,
        SqlImportParsedQuery query,
        IReadOnlyList<ImportFromPart> fromParts,
        IReadOnlyList<NodeViewModel> tableNodes,
        SqlImportLayoutCalculator layout,
        int sourceCount,
        out NodeViewModel? node,
        ref bool fallbackActivated,
        ref int imported,
        ref int partial)
    {
        node = null;
        List<NodeViewModel> childNodes = [];

        foreach (string part in parts)
        {
            if (!TryBuildBooleanExpression(part, query, fromParts, tableNodes, layout, sourceCount, out NodeViewModel? child, ref fallbackActivated, ref imported, ref partial) || child is null)
            {
                partial++;
                return false;
            }

            childNodes.Add(child);
        }

        if (childNodes.Count == 0)
            return false;
        if (childNodes.Count == 1)
        {
            node = childNodes[0];
            return true;
        }

        NodeViewModel gate = new(NodeDefinitionRegistry.Get(gateType), layout.ComparisonPosition(sourceCount));
        _canvas.Nodes.Add(gate);

        foreach (NodeViewModel child in childNodes)
            SqlImportClauseApplyUtilities.SafeWire(child, "result", gate, "conditions", _canvas);

        imported++;
        node = gate;
        return true;
    }

    private bool TryBuildPredicateLeaf(
        string expression,
        SqlImportParsedQuery query,
        IReadOnlyList<ImportFromPart> fromParts,
        IReadOnlyList<NodeViewModel> tableNodes,
        SqlImportLayoutCalculator layout,
        int sourceCount,
        out NodeViewModel? node,
        ref bool fallbackActivated,
        ref int imported,
        ref int partial)
    {
        node = null;
        string qualifiedIdentifierPattern = SqlImportIdentifierNormalizer.QualifiedIdentifierPattern;

        Match existsMatch = Regex.Match(
            expression,
            @"^\s*(?<not>NOT\s+)?EXISTS\s*\(\s*(?<subquery>SELECT.+)\s*\)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (existsMatch.Success)
        {
            string subquerySql = existsMatch.Groups["subquery"].Value.Trim();
            bool negate = existsMatch.Groups["not"].Success;

            NodeViewModel existsNode = new(NodeDefinitionRegistry.Get(NodeType.SubqueryExists), layout.SubqueryPosition(sourceCount));
            existsNode.Parameters["query_text"] = subquerySql;
            existsNode.Parameters["query"] = subquerySql;
            existsNode.Parameters["negate"] = negate ? "true" : "false";
            _canvas.Nodes.Add(existsNode);

            string correlatedFields = SqlImportCteRewriteService.DescribeCorrelatedOuterReferences(subquerySql, query.OuterAliases);
            if (!string.IsNullOrWhiteSpace(correlatedFields))
            {
                existsNode.Parameters["correlated_outer_refs"] = correlatedFields;
                existsNode.Parameters["correlation_kind"] = negate ? "anti-semi" : "semi";
                imported++;
            }

            imported++;
            node = existsNode;
            return true;
        }

        Match inSubqueryMatch = Regex.Match(
            expression,
            $@"^\s*(?<left>{qualifiedIdentifierPattern})\s+(?<not>NOT\s+)?IN\s*\((?<subquery>SELECT.+?)\)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (inSubqueryMatch.Success)
        {
            string leftExpr = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(inSubqueryMatch.Groups["left"].Value);
            bool negate = inSubqueryMatch.Groups["not"].Success;
            string subquerySql = inSubqueryMatch.Groups["subquery"].Value.Trim();

            NodeViewModel inNode = new(NodeDefinitionRegistry.Get(NodeType.SubqueryIn), layout.SubqueryPosition(sourceCount));
            inNode.Parameters["query_text"] = subquerySql;
            inNode.Parameters["query"] = subquerySql;
            inNode.Parameters["negate"] = negate ? "true" : "false";
            _canvas.Nodes.Add(inNode);

            TryWireExpressionToPin(leftExpr, inNode, "value", fromParts, tableNodes, ref fallbackActivated);
            imported++;
            node = inNode;
            return true;
        }

        Match inLiteralListMatch = Regex.Match(
            expression,
            $@"^\s*(?<left>{qualifiedIdentifierPattern})\s+(?<not>NOT\s+)?IN\s*\((?<values>.+?)\)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (inLiteralListMatch.Success)
        {
            string leftExpr = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(inLiteralListMatch.Groups["left"].Value);
            bool negate = inLiteralListMatch.Groups["not"].Success;
            string valuesSql = inLiteralListMatch.Groups["values"].Value.Trim();

            NodeViewModel inNode = new(NodeDefinitionRegistry.Get(NodeType.SubqueryIn), layout.SubqueryPosition(sourceCount));
            inNode.Parameters["query_text"] = valuesSql;
            inNode.Parameters["query"] = valuesSql;
            inNode.Parameters["negate"] = negate ? "true" : "false";
            _canvas.Nodes.Add(inNode);

            TryWireExpressionToPin(leftExpr, inNode, "value", fromParts, tableNodes, ref fallbackActivated);
            imported++;
            node = inNode;
            return true;
        }

        Match scalarSubqueryMatch = Regex.Match(
            expression,
            $@"^\s*(?<left>{qualifiedIdentifierPattern})\s*(?<op><>|!=|>=|<=|=|>|<)\s*\(+\s*(?<subquery>SELECT.+?)\s*\)+\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (scalarSubqueryMatch.Success)
        {
            string leftExpr = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(scalarSubqueryMatch.Groups["left"].Value);
            string op = scalarSubqueryMatch.Groups["op"].Value.Trim();
            string subquerySql = scalarSubqueryMatch.Groups["subquery"].Value.Trim();

            NodeViewModel scalarNode = new(NodeDefinitionRegistry.Get(NodeType.SubqueryScalar), layout.SubqueryPosition(sourceCount));
            scalarNode.Parameters["query_text"] = subquerySql;
            scalarNode.Parameters["query"] = subquerySql;
            scalarNode.Parameters["operator"] = op == "!=" ? "<>" : op;
            _canvas.Nodes.Add(scalarNode);

            TryWireExpressionToPin(leftExpr, scalarNode, "left", fromParts, tableNodes, ref fallbackActivated);
            imported++;
            node = scalarNode;
            return true;
        }

        Match isNullMatch = Regex.Match(
            expression,
            $@"^\s*(?<left>{qualifiedIdentifierPattern})\s+IS\s+(?<not>NOT\s+)?NULL\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (isNullMatch.Success)
        {
            string leftExpr = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(isNullMatch.Groups["left"].Value);
            bool negate = isNullMatch.Groups["not"].Success;

            NodeViewModel isNullNode = new(
                NodeDefinitionRegistry.Get(negate ? NodeType.IsNotNull : NodeType.IsNull),
                layout.ComparisonPosition(sourceCount));
            _canvas.Nodes.Add(isNullNode);

            TryWireExpressionToPin(leftExpr, isNullNode, "value", fromParts, tableNodes, ref fallbackActivated);
            imported++;
            node = isNullNode;
            return true;
        }

        Match betweenMatch = Regex.Match(
            expression,
            $@"^\s*(?<left>{qualifiedIdentifierPattern})\s+(?<not>NOT\s+)?BETWEEN\s+(?<low>.+?)\s+AND\s+(?<high>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (betweenMatch.Success)
        {
            string leftExpr = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(betweenMatch.Groups["left"].Value);
            bool negate = betweenMatch.Groups["not"].Success;
            string lowExpr = StripOuterParentheses(betweenMatch.Groups["low"].Value.Trim());
            string highExpr = StripOuterParentheses(betweenMatch.Groups["high"].Value.Trim());

            NodeViewModel betweenNode = new(
                NodeDefinitionRegistry.Get(negate ? NodeType.NotBetween : NodeType.Between),
                layout.ComparisonPosition(sourceCount));
            _canvas.Nodes.Add(betweenNode);

            TryWireExpressionToPin(leftExpr, betweenNode, "value", fromParts, tableNodes, ref fallbackActivated);
            if (!TryWireExpressionToPin(lowExpr, betweenNode, "low", fromParts, tableNodes, ref fallbackActivated)
                && !TryWireLiteralExpressionToPin(lowExpr, betweenNode, "low", layout, sourceCount))
            {
                betweenNode.PinLiterals["low"] = NormalizeRawSqlLiteral(lowExpr);
            }

            if (!TryWireExpressionToPin(highExpr, betweenNode, "high", fromParts, tableNodes, ref fallbackActivated)
                && !TryWireLiteralExpressionToPin(highExpr, betweenNode, "high", layout, sourceCount))
            {
                betweenNode.PinLiterals["high"] = NormalizeRawSqlLiteral(highExpr);
            }

            imported++;
            node = betweenNode;
            return true;
        }

        Match likeMatch = Regex.Match(
            expression,
            $@"^\s*(?<left>{qualifiedIdentifierPattern})\s+(?<not>NOT\s+)?LIKE\s+(?<pattern>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (likeMatch.Success)
        {
            string leftExpr = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(likeMatch.Groups["left"].Value);
            bool negate = likeMatch.Groups["not"].Success;
            string patternExpr = likeMatch.Groups["pattern"].Value.Trim();

            NodeViewModel likeNode = new(NodeDefinitionRegistry.Get(NodeType.Like), layout.ComparisonPosition(sourceCount));
            likeNode.Parameters["pattern"] = UnquoteLiteral(patternExpr);
            _canvas.Nodes.Add(likeNode);
            TryWireExpressionToPin(leftExpr, likeNode, "text", fromParts, tableNodes, ref fallbackActivated);

            if (!negate)
            {
                imported++;
                node = likeNode;
                return true;
            }

            NodeViewModel notNode = new(NodeDefinitionRegistry.Get(NodeType.Not), layout.ComparisonPosition(sourceCount));
            _canvas.Nodes.Add(notNode);
            SqlImportClauseApplyUtilities.SafeWire(likeNode, "result", notNode, "condition", _canvas);

            imported += 2;
            node = notNode;
            return true;
        }

        Match binaryMatch = Regex.Match(
            expression,
            $@"^\s*(?<left>{qualifiedIdentifierPattern})\s*(?<op><>|!=|>=|<=|=|>|<)\s*(?<right>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (binaryMatch.Success)
        {
            string leftExpr = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(binaryMatch.Groups["left"].Value);
            string op = binaryMatch.Groups["op"].Value.Trim();
            string rightExpr = StripOuterParentheses(binaryMatch.Groups["right"].Value.Trim());

            NodeType compType = op switch
            {
                "=" => NodeType.Equals,
                "<>" or "!=" => NodeType.NotEquals,
                ">" => NodeType.GreaterThan,
                ">=" => NodeType.GreaterOrEqual,
                "<" => NodeType.LessThan,
                "<=" => NodeType.LessOrEqual,
                _ => NodeType.Equals,
            };

            NodeViewModel comp = new(NodeDefinitionRegistry.Get(compType), layout.ComparisonPosition(sourceCount));
            _canvas.Nodes.Add(comp);

            TryWireExpressionToPin(leftExpr, comp, "left", fromParts, tableNodes, ref fallbackActivated);

            if (IsQualifiedIdentifier(rightExpr) && TryWireExpressionToPin(rightExpr, comp, "right", fromParts, tableNodes, ref fallbackActivated))
            {
                imported++;
                node = comp;
                return true;
            }

            if (TryWireLiteralExpressionToPin(rightExpr, comp, "right", layout, sourceCount))
            {
                imported++;
                node = comp;
                return true;
            }

            comp.PinLiterals["right"] = UnquoteLiteral(rightExpr);
            imported++;
            node = comp;
            return true;
        }

        Match fallbackBinaryMatch = Regex.Match(
            expression,
            @"^\s*(?<left>.+?)\s*(?<op><>|!=|>=|<=|=|>|<)\s*(?<right>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (fallbackBinaryMatch.Success)
        {
            string leftCandidate = StripOuterParentheses(fallbackBinaryMatch.Groups["left"].Value.Trim());
            if (IsQualifiedIdentifier(leftCandidate))
            {
                string leftExpr = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(leftCandidate);
                string op = fallbackBinaryMatch.Groups["op"].Value.Trim();
                string rightExpr = StripOuterParentheses(fallbackBinaryMatch.Groups["right"].Value.Trim());

                NodeType compType = op switch
                {
                    "=" => NodeType.Equals,
                    "<>" or "!=" => NodeType.NotEquals,
                    ">" => NodeType.GreaterThan,
                    ">=" => NodeType.GreaterOrEqual,
                    "<" => NodeType.LessThan,
                    "<=" => NodeType.LessOrEqual,
                    _ => NodeType.Equals,
                };

                NodeViewModel comp = new(NodeDefinitionRegistry.Get(compType), layout.ComparisonPosition(sourceCount));
                _canvas.Nodes.Add(comp);

                TryWireExpressionToPin(leftExpr, comp, "left", fromParts, tableNodes, ref fallbackActivated);

                if (IsQualifiedIdentifier(rightExpr) && TryWireExpressionToPin(rightExpr, comp, "right", fromParts, tableNodes, ref fallbackActivated))
                {
                    imported++;
                    node = comp;
                    return true;
                }

                if (TryWireLiteralExpressionToPin(rightExpr, comp, "right", layout, sourceCount))
                {
                    imported++;
                    node = comp;
                    return true;
                }

                comp.PinLiterals["right"] = UnquoteLiteral(rightExpr);
                imported++;
                node = comp;
                return true;
            }
        }

        partial++;
        return false;
    }

    private bool TryWireExpressionToPin(
        string expression,
        NodeViewModel targetNode,
        string targetPin,
        IReadOnlyList<ImportFromPart> fromParts,
        IReadOnlyList<NodeViewModel> tableNodes,
        ref bool fallbackActivated)
    {
        if (ImportBuildUtilities.TryResolveExpressionPin(expression, fromParts, tableNodes, out PinViewModel pin))
        {
            SqlImportClauseApplyUtilities.SafeWire(pin.Owner, pin.Name, targetNode, targetPin, _canvas);
            return true;
        }

        if (ImportBuildUtilities.TryResolveSourceAndColumn(expression, fromParts, out int sourceIndex, out string column)
            && sourceIndex >= 0
            && sourceIndex < tableNodes.Count
            && !string.IsNullOrWhiteSpace(column))
        {
            PinViewModel inferred = ImportBuildUtilities.EnsureOutputColumnPin(tableNodes[sourceIndex], column);
            SqlImportClauseApplyUtilities.SafeWire(inferred.Owner, inferred.Name, targetNode, targetPin, _canvas);
            return true;
        }

        string fallbackColumn = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(expression)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fallbackColumn))
            return false;

        PinViewModel? fallbackPin = tableNodes
            .SelectMany(n => n.OutputPins)
            .FirstOrDefault(p => p.Name.Equals(fallbackColumn, StringComparison.OrdinalIgnoreCase));
        if (fallbackPin is null)
            return false;

        fallbackActivated = true;
        SqlImportClauseApplyUtilities.SafeWire(fallbackPin.Owner, fallbackPin.Name, targetNode, targetPin, _canvas);
        return true;
    }

    private bool TryWireLiteralExpressionToPin(
        string expression,
        NodeViewModel targetNode,
        string targetPin,
        SqlImportLayoutCalculator layout,
        int sourceCount)
    {
        string candidate = StripOuterParentheses(expression).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        if (string.Equals(candidate, "NULL", StringComparison.OrdinalIgnoreCase))
            return false;

        NodeType literalType;
        string literalValue;

        if (bool.TryParse(candidate, out bool boolValue))
        {
            literalType = NodeType.ValueBoolean;
            literalValue = boolValue ? "true" : "false";
        }
        else if (double.TryParse(candidate, NumberStyles.Any, CultureInfo.InvariantCulture, out double numericValue))
        {
            literalType = NodeType.ValueNumber;
            literalValue = numericValue.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            string unquoted = UnquoteLiteral(candidate);
            if (string.IsNullOrWhiteSpace(unquoted))
                return false;

            if (LooksLikeIsoDateOrDateTime(unquoted))
            {
                literalType = NodeType.ValueDateTime;
                literalValue = unquoted;
            }
            else
            {
                literalType = NodeType.ValueString;
                literalValue = unquoted;
            }
        }

        NodeViewModel literalNode = new(
            NodeDefinitionRegistry.Get(literalType),
            layout.ComparisonPosition(sourceCount)
        );
        literalNode.Parameters["value"] = literalValue;
        _canvas.Nodes.Add(literalNode);

        SqlImportClauseApplyUtilities.SafeWire(literalNode, "result", targetNode, targetPin, _canvas);
        return true;
    }

    private static bool IsQualifiedIdentifier(string expression)
    {
        string normalized = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(expression);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        string[] segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return false;

        return segments.All(static segment =>
            Regex.IsMatch(segment, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant));
    }

    private static bool TryStripNotPrefix(string expression, out string inner)
    {
        inner = string.Empty;
        string value = expression.TrimStart();
        if (!value.StartsWith("NOT", StringComparison.OrdinalIgnoreCase))
            return false;
        if (value.Length > 3 && (char.IsLetterOrDigit(value[3]) || value[3] == '_'))
            return false;

        inner = StripOuterParentheses(value[3..].Trim());
        return !string.IsNullOrWhiteSpace(inner);
    }

    private static List<string> SplitTopLevelByKeyword(string expression, string keyword)
    {
        var parts = new List<string>();
        int start = 0;
        int depth = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool betweenPending = false;

        for (int i = 0; i < expression.Length; i++)
        {
            char ch = expression[i];

            if (inSingleQuote)
            {
                if (ch == '\'' && i + 1 < expression.Length && expression[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                if (ch == '\'')
                    inSingleQuote = false;

                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '"' && i + 1 < expression.Length && expression[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }

            if (ch == ')')
            {
                if (depth > 0)
                    depth--;
                continue;
            }

            if (depth != 0)
                continue;

            if (MatchesKeywordAt(expression, i, "BETWEEN"))
            {
                betweenPending = true;
                i += "BETWEEN".Length - 1;
                continue;
            }

            if (keyword.Equals("AND", StringComparison.OrdinalIgnoreCase)
                && betweenPending
                && MatchesKeywordAt(expression, i, "AND"))
            {
                betweenPending = false;
                i += "AND".Length - 1;
                continue;
            }

            if (!MatchesKeywordAt(expression, i, keyword))
                continue;

            string segment = expression[start..i].Trim();
            if (!string.IsNullOrWhiteSpace(segment))
                parts.Add(segment);

            i += keyword.Length - 1;
            start = i + 1;
        }

        if (parts.Count == 0)
        {
            parts.Add(expression.Trim());
            return parts;
        }

        string tail = expression[start..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            parts.Add(tail);

        return parts;
    }

    private static bool MatchesKeywordAt(string text, int index, string keyword)
    {
        if (index < 0 || index + keyword.Length > text.Length)
            return false;

        if (!text.AsSpan(index, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        char before = index > 0 ? text[index - 1] : ' ';
        char after = index + keyword.Length < text.Length ? text[index + keyword.Length] : ' ';

        bool beforeIsBoundary = !char.IsLetterOrDigit(before) && before != '_';
        bool afterIsBoundary = !char.IsLetterOrDigit(after) && after != '_';

        return beforeIsBoundary && afterIsBoundary;
    }

    private static bool TryFallbackSplitParenthesizedBinary(
        string expression,
        string keyword,
        out IReadOnlyList<string> parts)
    {
        parts = [];
        Match match = Regex.Match(
            expression,
            $@"^\(\s*(?<left>.+?)\s*\)\s+{Regex.Escape(keyword)}\s+(?<right>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant
        );

        if (!match.Success)
            return false;

        string left = match.Groups["left"].Value.Trim();
        string right = match.Groups["right"].Value.Trim();
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        parts = [left, right];
        return true;
    }

    private static string NormalizeRawSqlLiteral(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
            return "NULL";

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
            return trimmed;

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return $"'{trimmed[1..^1].Replace("'", "''", StringComparison.Ordinal)}'";

        if (double.TryParse(trimmed, out _))
            return trimmed;

        if (trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return trimmed.ToUpperInvariant();

        return $"'{trimmed.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static string UnquoteLiteral(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length < 2)
            return trimmed;

        if (trimmed[0] == '\'' && trimmed[^1] == '\'')
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);

        if (trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);

        return trimmed;
    }

    private static bool LooksLikeIsoDateOrDateTime(string value)
    {
        return Regex.IsMatch(
            value,
            @"^\d{4}-\d{2}-\d{2}(?:[ T]\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?)?$",
            RegexOptions.CultureInvariant);
    }

    private static string StripOuterParentheses(string clause)
    {
        string value = clause.Trim();
        while (value.Length >= 2 && value[0] == '(' && value[^1] == ')')
        {
            int depth = 0;
            bool wrapsWholeExpression = true;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '(')
                    depth++;
                else if (ch == ')')
                    depth--;

                if (depth == 0 && i < value.Length - 1)
                {
                    wrapsWholeExpression = false;
                    break;
                }
            }

            if (!wrapsWholeExpression)
                break;

            value = value[1..^1].Trim();
        }

        return value;
    }
}
