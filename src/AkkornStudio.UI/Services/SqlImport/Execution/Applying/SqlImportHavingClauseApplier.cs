using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AkkornStudio.Nodes;
using AkkornStudio.SqlImport.Diagnostics;
using AkkornStudio.UI.Services.SqlImport.Build;
using AkkornStudio.UI.Services.SqlImport.Execution.Parsing;
using AkkornStudio.UI.Services.SqlImport.Rewriting;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.Services.SqlImport.Execution.Applying;

internal sealed class SqlImportHavingClauseApplier(CanvasViewModel canvas) : ISqlImportApplyStep
{
    private readonly CanvasViewModel _canvas = canvas;

    public SqlImportApplyResult Apply(SqlImportApplyContext context)
    {
        SqlImportParsedQuery query = context.Query;
        ImportBuildContext coreContext = context.CoreContext;
        ObservableCollection<ImportReportItem> report = context.Report;
        var layout = new SqlImportLayoutCalculator(coreContext.Layout);

        if (query.HavingClause is null)
            return new SqlImportApplyResult(0, 0, 0);

        int imported = 0;
        int partial = 0;

        NodeViewModel result = coreContext.ResultNode;
        List<ImportFromPart> fromParts =
        [
            .. query.FromParts.Select(part => new ImportFromPart(part.Table, part.Alias, part.JoinType, part.OnClause)),
        ];
        IReadOnlyList<NodeViewModel> tableNodes = coreContext.TableNodes;
        string qualifiedIdentifierPattern = SqlImportIdentifierNormalizer.QualifiedIdentifierPattern;

        Match countHavingMatch = Regex.Match(
            query.HavingClause,
            @"^\s*\(?\s*(COUNT\s*\(\s*(?:\*|1)\s*\))\s*(<>|!=|>=|<=|=|>|<)\s*(.+?)\s*\)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        Match aggregateHavingMatch = Regex.Match(
            query.HavingClause,
            $@"^\s*\(?\s*(?<func>COUNT|SUM|AVG|MIN|MAX)\s*\(\s*(?<distinct>DISTINCT\s+)?(?<arg>{qualifiedIdentifierPattern}|\*|1)\s*\)\s*(?<op><>|!=|>=|<=|=|>|<)\s*(?<right>.+?)\s*\)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant
        );

        Match stringAggHavingMatch = Regex.Match(
            query.HavingClause,
            $@"^\s*\(?\s*STRING_AGG\s*\(\s*(?<distinct>DISTINCT\s+)?(?<arg>{qualifiedIdentifierPattern})\s*,\s*(?<sep>'(?:[^']|'')*'|""(?:[^""]|"""")*"")(?<order>\s+ORDER\s+BY\s+(?<orderArg>{qualifiedIdentifierPattern})(?:\s+(?<orderDir>ASC|DESC))?)?\s*\)\s*(?<op><>|!=|>=|<=|=|>|<)\s*(?<right>.+?)\s*\)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant
        );

        if (countHavingMatch.Success)
        {
            string op = countHavingMatch.Groups[2].Value.Trim();
            string rightExpr = countHavingMatch.Groups[3].Value.Trim().Trim('\'', '"');

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

            NodeViewModel countNode = new(
                NodeDefinitionRegistry.Get(NodeType.CountStar),
                layout.HavingCountPosition(query.FromParts.Count)
            );
            _canvas.Nodes.Add(countNode);

            NodeViewModel comp = new(
                NodeDefinitionRegistry.Get(compType),
                layout.HavingComparisonPosition(query.FromParts.Count)
            );
            comp.PinLiterals["right"] = rightExpr;
            _canvas.Nodes.Add(comp);

            SqlImportClauseApplyUtilities.SafeWire(countNode, "count", comp, "left", _canvas);
            SqlImportClauseApplyUtilities.SafeWire(comp, "result", result, "having", _canvas);

            report.Add(
                new ImportReportItem(
                    $"HAVING COUNT(*) {op} {rightExpr}",
                    ImportItemStatus.Imported,
                    sourceNodeId: comp.Id
                )
            );
            imported++;
        }
        else if (aggregateHavingMatch.Success)
        {
            string functionName = aggregateHavingMatch.Groups["func"].Value.Trim().ToUpperInvariant();
            bool isDistinctCount = aggregateHavingMatch.Groups["distinct"].Success;
            string argumentExpression = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(aggregateHavingMatch.Groups["arg"].Value.Trim());
            string op = aggregateHavingMatch.Groups["op"].Value.Trim();
            string rightExpr = aggregateHavingMatch.Groups["right"].Value.Trim().Trim('\'', '"');

            if (functionName == "COUNT"
                && (argumentExpression == "*" || argumentExpression == "1"))
            {
                // COUNT(*) is already handled by the dedicated branch above.
            }
            else if (functionName == "COUNT")
            {
                if (!ImportBuildUtilities.TryResolveExpressionPin(argumentExpression, fromParts, tableNodes, out PinViewModel countSourcePin))
                {
                    report.Add(
                        SqlImportReportFactory.HavingUnsupported(
                            $"HAVING {SqlImportClauseApplyUtilities.Truncate(query.HavingClause, 40)}",
                            result.Id
                        )
                    );

                    report.Add(SqlImportReportFactory.Partial(
                        SqlImportDiagnosticCodes.FallbackRegexUsed,
                        "HAVING fallback",
                        SqlImportDiagnosticMessages.HavingFallbackReportNote,
                        result.Id));
                    partial++;

                    return new SqlImportApplyResult(imported, partial, 0);
                }

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

                NodeViewModel countNode = new(
                    NodeDefinitionRegistry.Get(NodeType.CountDistinct),
                    layout.HavingCountPosition(query.FromParts.Count)
                );
                countNode.Parameters["distinct"] = isDistinctCount ? "true" : "false";
                _canvas.Nodes.Add(countNode);

                SqlImportClauseApplyUtilities.SafeWire(
                    countSourcePin.Owner,
                    countSourcePin.Name,
                    countNode,
                    "value",
                    _canvas
                );

                NodeViewModel comp = new(
                    NodeDefinitionRegistry.Get(compType),
                    layout.HavingComparisonPosition(query.FromParts.Count)
                );
                comp.PinLiterals["right"] = rightExpr;
                _canvas.Nodes.Add(comp);

                SqlImportClauseApplyUtilities.SafeWire(countNode, "count", comp, "left", _canvas);
                SqlImportClauseApplyUtilities.SafeWire(comp, "result", result, "having", _canvas);

                report.Add(
                    new ImportReportItem(
                        $"HAVING {functionName}({(isDistinctCount ? "DISTINCT " : string.Empty)}{argumentExpression}) {op} {rightExpr}",
                        ImportItemStatus.Imported,
                        sourceNodeId: comp.Id
                    )
                );
                imported++;
            }
            else if (TryMapAggregate(functionName, out NodeType aggregateNodeType, out string aggregateOutputPin))
            {
                NodeViewModel aggregateNode = new(
                    NodeDefinitionRegistry.Get(aggregateNodeType),
                    layout.HavingCountPosition(query.FromParts.Count)
                );
                _canvas.Nodes.Add(aggregateNode);

                if (ImportBuildUtilities.TryResolveExpressionPin(argumentExpression, fromParts, tableNodes, out PinViewModel sourcePin))
                    SqlImportClauseApplyUtilities.SafeWire(sourcePin.Owner, sourcePin.Name, aggregateNode, "value", _canvas);

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

                NodeViewModel comp = new(
                    NodeDefinitionRegistry.Get(compType),
                    layout.HavingComparisonPosition(query.FromParts.Count)
                );
                comp.PinLiterals["right"] = rightExpr;
                _canvas.Nodes.Add(comp);

                SqlImportClauseApplyUtilities.SafeWire(aggregateNode, aggregateOutputPin, comp, "left", _canvas);
                SqlImportClauseApplyUtilities.SafeWire(comp, "result", result, "having", _canvas);

                report.Add(
                    new ImportReportItem(
                        $"HAVING {functionName}({argumentExpression}) {op} {rightExpr}",
                        ImportItemStatus.Imported,
                        sourceNodeId: comp.Id
                    )
                );
                imported++;
            }
            else
            {
                report.Add(
                    SqlImportReportFactory.HavingUnsupported(
                        $"HAVING {SqlImportClauseApplyUtilities.Truncate(query.HavingClause, 40)}",
                        result.Id
                    )
                );

                report.Add(SqlImportReportFactory.Partial(
                    SqlImportDiagnosticCodes.FallbackRegexUsed,
                    "HAVING fallback",
                    SqlImportDiagnosticMessages.HavingFallbackReportNote,
                    result.Id));
                partial++;
            }
        }
        else if (stringAggHavingMatch.Success)
        {
            bool isDistinct = stringAggHavingMatch.Groups["distinct"].Success;
            string argumentExpression = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(stringAggHavingMatch.Groups["arg"].Value.Trim());
            string separator = stringAggHavingMatch.Groups["sep"].Value.Trim().Trim('\'', '"');
            string? orderByExpression = stringAggHavingMatch.Groups["orderArg"].Success
                ? SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(stringAggHavingMatch.Groups["orderArg"].Value.Trim())
                : null;
            bool orderDescending = stringAggHavingMatch.Groups["orderDir"].Success
                && stringAggHavingMatch.Groups["orderDir"].Value.Equals("DESC", StringComparison.OrdinalIgnoreCase);
            string op = stringAggHavingMatch.Groups["op"].Value.Trim();
            string rightExpr = stringAggHavingMatch.Groups["right"].Value.Trim().Trim('\'', '"');

            if (!ImportBuildUtilities.TryResolveExpressionPin(argumentExpression, fromParts, tableNodes, out PinViewModel sourcePin))
            {
                report.Add(
                    SqlImportReportFactory.HavingUnsupported(
                        $"HAVING {SqlImportClauseApplyUtilities.Truncate(query.HavingClause, 40)}",
                        result.Id
                    )
                );

                report.Add(SqlImportReportFactory.Partial(
                    SqlImportDiagnosticCodes.FallbackRegexUsed,
                    "HAVING fallback",
                    SqlImportDiagnosticMessages.HavingFallbackReportNote,
                    result.Id));
                partial++;

                return new SqlImportApplyResult(imported, partial, 0);
            }

            NodeViewModel aggregateNode = new(
                NodeDefinitionRegistry.Get(NodeType.StringAgg),
                layout.HavingCountPosition(query.FromParts.Count)
            );
            aggregateNode.Parameters["separator"] = separator;
            aggregateNode.Parameters["distinct"] = isDistinct ? "true" : "false";
            if (orderDescending)
                aggregateNode.Parameters["order_1_desc"] = "true";
            _canvas.Nodes.Add(aggregateNode);
            SqlImportClauseApplyUtilities.SafeWire(sourcePin.Owner, sourcePin.Name, aggregateNode, "value", _canvas);

            if (!string.IsNullOrWhiteSpace(orderByExpression)
                && ImportBuildUtilities.TryResolveExpressionPin(orderByExpression, fromParts, tableNodes, out PinViewModel orderByPin))
            {
                SqlImportClauseApplyUtilities.SafeWire(orderByPin.Owner, orderByPin.Name, aggregateNode, "order_by", _canvas);
            }

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

            NodeViewModel comp = new(
                NodeDefinitionRegistry.Get(compType),
                layout.HavingComparisonPosition(query.FromParts.Count)
            );
            comp.PinLiterals["right"] = rightExpr;
            _canvas.Nodes.Add(comp);

            SqlImportClauseApplyUtilities.SafeWire(aggregateNode, "result", comp, "left", _canvas);
            SqlImportClauseApplyUtilities.SafeWire(comp, "result", result, "having", _canvas);

            report.Add(
                new ImportReportItem(
                    $"HAVING STRING_AGG({(isDistinct ? "DISTINCT " : string.Empty)}{argumentExpression}, '{separator}'{(string.IsNullOrWhiteSpace(orderByExpression) ? string.Empty : $" ORDER BY {orderByExpression}{(orderDescending ? " DESC" : string.Empty)}")}) {op} {rightExpr}",
                    ImportItemStatus.Imported,
                    sourceNodeId: comp.Id
                )
            );
            imported++;
        }
        else
        {
            report.Add(
                SqlImportReportFactory.HavingUnsupported(
                    $"HAVING {SqlImportClauseApplyUtilities.Truncate(query.HavingClause, 40)}",
                    result.Id
                )
            );

            report.Add(SqlImportReportFactory.Partial(
                SqlImportDiagnosticCodes.FallbackRegexUsed,
                "HAVING fallback",
                SqlImportDiagnosticMessages.HavingFallbackReportNote,
                result.Id));
            partial++;
        }

        return new SqlImportApplyResult(imported, partial, 0);
    }

    private static bool TryMapAggregate(string functionName, out NodeType nodeType, out string outputPin)
    {
        switch (functionName)
        {
            case "SUM":
                nodeType = NodeType.Sum;
                outputPin = "total";
                return true;
            case "AVG":
                nodeType = NodeType.Avg;
                outputPin = "average";
                return true;
            case "MIN":
                nodeType = NodeType.Min;
                outputPin = "minimum";
                return true;
            case "MAX":
                nodeType = NodeType.Max;
                outputPin = "maximum";
                return true;
            default:
                nodeType = default;
                outputPin = string.Empty;
                return false;
        }
    }
}
