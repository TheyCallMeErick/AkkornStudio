using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.SqlImport.Diagnostics;
using AkkornStudio.UI.Services.SqlImport;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.Services.SqlImport.Build;

public readonly record struct ImportFromPart(string Table, string? Alias, string? JoinType, string? OnClause);

public readonly record struct ImportSelectTerm(string Expr, string? Alias);

public readonly record struct ImportLayout(double BaseX, double BaseY, double ColGap, double RowGap);

public sealed record ImportBuildInput(
    IReadOnlyList<ImportFromPart> FromParts,
    IReadOnlyList<ImportSelectTerm> SelectedColumns,
    bool IsStar,
    string? StarQualifier,
    ImportLayout Layout
);

public sealed record ImportBuildContext(
    CanvasViewModel Canvas,
    List<NodeViewModel> TableNodes,
    NodeViewModel ResultNode,
    Dictionary<string, PinViewModel> ProjectedAliases,
    ImportLayout Layout,
    double ResultY,
    int Imported,
    int Partial,
    int Skipped
);

public sealed class ImportModelToCanvasBuilder(CanvasViewModel canvas)
{
    private readonly CanvasViewModel _canvas = canvas;
    private readonly IReadOnlyList<IImportBuildStep> _steps =
    [
        new ImportBuildResetCanvasStep(),
        new ImportBuildSourceTablesStep(),
        new ImportBuildJoinNodesStep(),
        new ImportBuildProjectionStep(),
    ];

    public ImportBuildContext BuildCore(
        ImportBuildInput input,
        ObservableCollection<ImportReportItem> report,
        CancellationToken ct
    )
    {
        var state = new ImportBuildState();
        var context = new ImportBuildPipelineContext(_canvas, input, report, ct, state);

        foreach (IImportBuildStep step in _steps)
            step.Apply(context);

        return new ImportBuildContext(
            _canvas,
            state.TableNodes,
            state.ResultNode!,
            state.ProjectedAliases,
            input.Layout,
            state.ResultY,
            state.Imported,
            state.Partial,
            state.Skipped
        );
    }

    private sealed class ImportBuildState
    {
        public List<NodeViewModel> TableNodes { get; } = [];
        public List<string> EffectiveSourceNames { get; } = [];
        public Dictionary<string, PinViewModel> ProjectedAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public NodeViewModel? ResultNode { get; set; }
        public double ResultY { get; set; }
        public int Imported { get; set; }
        public int Partial { get; set; }
        public int Skipped { get; set; }
    }

    private sealed record ImportBuildPipelineContext(
        CanvasViewModel Canvas,
        ImportBuildInput Input,
        ObservableCollection<ImportReportItem> Report,
        CancellationToken CancellationToken,
        ImportBuildState State
    );

    private interface IImportBuildStep
    {
        void Apply(ImportBuildPipelineContext context);
    }

    private sealed class ImportBuildResetCanvasStep : IImportBuildStep
    {
        public void Apply(ImportBuildPipelineContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            context.Canvas.Connections.Clear();
            context.Canvas.Nodes.Clear();
            context.Canvas.UndoRedo.Clear();
        }
    }

    private sealed class ImportBuildSourceTablesStep : IImportBuildStep
    {
        public void Apply(ImportBuildPipelineContext context)
        {
            var calculator = new SqlImportLayoutCalculator(context.Input.Layout);

            for (int i = 0; i < context.Input.FromParts.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                string table = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(context.Input.FromParts[i].Table);
                table = NormalizeImportedSourceTable(table, context.Input.FromParts[i].Alias);
                Point pos = calculator.TablePosition(i);

                (string resolvedFullName, IReadOnlyList<(string Name, PinDataType Type)> resolvedColumns) =
                    ResolveTableReference(table, context.Canvas.DatabaseMetadata);

                NodeViewModel tableNode = resolvedColumns.Count > 0
                    ? new NodeViewModel(resolvedFullName, resolvedColumns.Select(c => (c.Name, c.Type)), pos)
                    : new NodeViewModel(resolvedFullName, [], pos);

                ApplySourceIdentifierParameters(tableNode, resolvedFullName);
                tableNode.Alias = string.IsNullOrWhiteSpace(context.Input.FromParts[i].Alias)
                    ? null
                    : context.Input.FromParts[i].Alias!.Trim();
                context.Canvas.Nodes.Add(tableNode);
                context.State.TableNodes.Add(tableNode);
                context.State.EffectiveSourceNames.Add(resolvedFullName);

                context.Report.Add(
                    new ImportReportItem(
                        context.Input.FromParts[i].JoinType is not null
                            ? $"{context.Input.FromParts[i].JoinType}: {table}"
                            : $"FROM: {table}",
                        ImportItemStatus.Imported,
                        sourceNodeId: tableNode.Id
                    )
                );
                context.State.Imported++;
            }
        }
    }

    private sealed class ImportBuildJoinNodesStep : IImportBuildStep
    {
        public void Apply(ImportBuildPipelineContext context)
        {
            var calculator = new SqlImportLayoutCalculator(context.Input.Layout);

            for (int i = 1; i < context.Input.FromParts.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (
                    string.IsNullOrWhiteSpace(context.Input.FromParts[i].JoinType)
                    || string.IsNullOrWhiteSpace(context.Input.FromParts[i].OnClause)
                )
                {
                    continue;
                }

                NodeViewModel joinNode = new(
                    NodeDefinitionRegistry.Get(NodeType.Join),
                    calculator.JoinPosition(i)
                );
                joinNode.Parameters["join_type"] = ImportBuildUtilities.NormalizeJoinType(context.Input.FromParts[i].JoinType!);
                string? rightAlias = context.Input.FromParts[i].Alias;
                string resolvedRightSource = i < context.State.EffectiveSourceNames.Count
                    ? context.State.EffectiveSourceNames[i]
                    : SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(context.Input.FromParts[i].Table);
                joinNode.Parameters["right_source"] = string.IsNullOrWhiteSpace(rightAlias)
                    ? resolvedRightSource
                    : rightAlias!.Trim();
                context.Canvas.Nodes.Add(joinNode);

                string onClause = context.Input.FromParts[i].OnClause!;
                string rewrittenOnRaw = ImportBuildUtilities.RewriteKnownQualifiersToAliasInExpression(onClause, context.Input.FromParts);
                if (TryResolveJoinBinaryPredicate(
                        i,
                        onClause,
                        context.Input.FromParts,
                        out string leftExpression,
                        out string joinOperator,
                        out string rightExpression,
                        out bool shouldPreserveRawOnClause))
                {
                    string leftExpressionAliased = ImportBuildUtilities.RewriteQualifierToAlias(leftExpression, context.Input.FromParts);
                    string rightExpressionAliased = ImportBuildUtilities.RewriteQualifierToAlias(rightExpression, context.Input.FromParts);
                    int leftSourceIndex = ResolveSourceIndex(leftExpression, context.Input.FromParts);
                    int rightSourceIndex = ResolveSourceIndex(rightExpression, context.Input.FromParts);

                    if (ShouldSwapJoinSides(i, leftSourceIndex, rightSourceIndex))
                    {
                        (leftExpressionAliased, rightExpressionAliased) = (rightExpressionAliased, leftExpressionAliased);
                    }

                    joinNode.Parameters["left_expr"] = leftExpressionAliased;
                    joinNode.Parameters["operator"] = joinOperator == "!="
                        ? "<>"
                        : joinOperator;
                    joinNode.Parameters["right_expr"] = rightExpressionAliased;
                    if (shouldPreserveRawOnClause)
                        joinNode.Parameters["on_raw"] = rewrittenOnRaw;

                    PinViewModel? leftPin = null;
                    PinViewModel? rightPin = null;
                    if (ImportBuildUtilities.TryResolveExpressionPin(
                            leftExpression,
                            context.Input.FromParts,
                            context.State.TableNodes,
                            out PinViewModel resolvedLeftPin))
                    {
                        leftPin = resolvedLeftPin;
                    }

                    if (ImportBuildUtilities.TryResolveExpressionPin(
                            rightExpression,
                            context.Input.FromParts,
                            context.State.TableNodes,
                            out PinViewModel resolvedRightPin))
                    {
                        rightPin = resolvedRightPin;
                    }

                    if (ShouldSwapJoinSides(i, leftSourceIndex, rightSourceIndex))
                    {
                        (leftPin, rightPin) = (rightPin, leftPin);
                    }

                    if (leftPin is not null)
                        ImportBuildUtilities.SafeWire(leftPin.Owner, leftPin.Name, joinNode, "left", context.Canvas);

                    if (rightPin is not null)
                        ImportBuildUtilities.SafeWire(rightPin.Owner, rightPin.Name, joinNode, "right", context.Canvas);
                }
                else
                {
                    joinNode.Parameters["on_raw"] = rewrittenOnRaw;
                    context.Report.Add(SqlImportReportFactory.Partial(
                        SqlImportDiagnosticCodes.FallbackRegexUsed,
                        "JOIN ON fallback",
                        SqlImportDiagnosticMessages.JoinFallbackRegexReportNote,
                        joinNode.Id));
                    context.State.Partial++;

                    string firstConjunct = Regex.Split(rewrittenOnRaw, @"\bAND\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                        .FirstOrDefault()?.Trim()
                        ?? rewrittenOnRaw;

                    if (ImportBuildUtilities.TryParseSimpleBinaryPredicate(
                            firstConjunct,
                            out string fallbackLeftExpression,
                            out string fallbackJoinOperator,
                            out string fallbackRightExpression))
                    {
                        int fallbackLeftSourceIndex = ResolveSourceIndex(fallbackLeftExpression, context.Input.FromParts);
                        int fallbackRightSourceIndex = ResolveSourceIndex(fallbackRightExpression, context.Input.FromParts);

                        string fallbackLeftExpressionAliased = fallbackLeftExpression;
                        string fallbackRightExpressionAliased = fallbackRightExpression;
                        if (ShouldSwapJoinSides(i, fallbackLeftSourceIndex, fallbackRightSourceIndex))
                        {
                            (fallbackLeftExpressionAliased, fallbackRightExpressionAliased) = (fallbackRightExpressionAliased, fallbackLeftExpressionAliased);
                        }

                        joinNode.Parameters["left_expr"] = fallbackLeftExpressionAliased;
                        joinNode.Parameters["operator"] = fallbackJoinOperator == "!="
                            ? "<>"
                            : fallbackJoinOperator;
                        joinNode.Parameters["right_expr"] = fallbackRightExpressionAliased;

                        PinViewModel? fallbackLeftPin = null;
                        PinViewModel? fallbackRightPin = null;

                        if (ImportBuildUtilities.TryResolveExpressionPin(
                                fallbackLeftExpression,
                                context.Input.FromParts,
                                context.State.TableNodes,
                                out PinViewModel resolvedLeftPin))
                        {
                            fallbackLeftPin = resolvedLeftPin;
                        }

                        if (ImportBuildUtilities.TryResolveExpressionPin(
                                fallbackRightExpression,
                                context.Input.FromParts,
                                context.State.TableNodes,
                                out PinViewModel resolvedRightPin))
                        {
                            fallbackRightPin = resolvedRightPin;
                        }

                        if (ShouldSwapJoinSides(i, fallbackLeftSourceIndex, fallbackRightSourceIndex))
                        {
                            (fallbackLeftPin, fallbackRightPin) = (fallbackRightPin, fallbackLeftPin);
                        }

                        if (fallbackLeftPin is not null)
                            ImportBuildUtilities.SafeWire(fallbackLeftPin.Owner, fallbackLeftPin.Name, joinNode, "left", context.Canvas);

                        if (fallbackRightPin is not null)
                            ImportBuildUtilities.SafeWire(fallbackRightPin.Owner, fallbackRightPin.Name, joinNode, "right", context.Canvas);
                    }
                }
            }
        }

        private static bool TryResolveJoinBinaryPredicate(
            int currentJoinSourceIndex,
            string onClause,
            IReadOnlyList<ImportFromPart> fromParts,
            out string leftExpression,
            out string joinOperator,
            out string rightExpression,
            out bool shouldPreserveRawOnClause)
        {
            shouldPreserveRawOnClause = false;
            if (ImportBuildUtilities.TryParseSimpleBinaryPredicate(
                    onClause,
                    out leftExpression,
                    out joinOperator,
                    out rightExpression))
            {
                return true;
            }

            IReadOnlyList<string> conjuncts = SplitTopLevelAndConjuncts(onClause);
            for (int conjunctIndex = 0; conjunctIndex < conjuncts.Count; conjunctIndex++)
            {
                string conjunct = conjuncts[conjunctIndex];
                if (!ImportBuildUtilities.TryParseSimpleBinaryPredicate(
                        conjunct,
                        out string candidateLeft,
                        out string candidateOperator,
                        out string candidateRight))
                {
                    continue;
                }

                int leftSourceIndex = ResolveSourceIndex(candidateLeft, fromParts);
                int rightSourceIndex = ResolveSourceIndex(candidateRight, fromParts);
                bool linksCurrentJoinToPrevious =
                    (leftSourceIndex == currentJoinSourceIndex && rightSourceIndex >= 0 && rightSourceIndex < currentJoinSourceIndex)
                    || (rightSourceIndex == currentJoinSourceIndex && leftSourceIndex >= 0 && leftSourceIndex < currentJoinSourceIndex);

                if (!linksCurrentJoinToPrevious)
                    continue;

                if (!AreRemainingConjunctsCurrentSourceFilters(
                        conjuncts,
                        conjunctIndex,
                        currentJoinSourceIndex,
                        fromParts))
                {
                    continue;
                }

                leftExpression = candidateLeft;
                joinOperator = candidateOperator;
                rightExpression = candidateRight;
                shouldPreserveRawOnClause = conjuncts.Count > 1;
                return true;
            }

            leftExpression = string.Empty;
            joinOperator = string.Empty;
            rightExpression = string.Empty;
            return false;
        }

        private static bool AreRemainingConjunctsCurrentSourceFilters(
            IReadOnlyList<string> conjuncts,
            int chosenJoinConjunctIndex,
            int currentJoinSourceIndex,
            IReadOnlyList<ImportFromPart> fromParts)
        {
            for (int index = 0; index < conjuncts.Count; index++)
            {
                if (index == chosenJoinConjunctIndex)
                    continue;

                if (!IsCurrentSourceLocalFilter(conjuncts[index], currentJoinSourceIndex, fromParts))
                    return false;
            }

            return true;
        }

        private static IReadOnlyList<string> SplitTopLevelAndConjuncts(string expression)
        {
            var parts = new List<string>();
            if (string.IsNullOrWhiteSpace(expression))
                return parts;

            int start = 0;
            int depth = 0;
            int index = 0;
            while (index < expression.Length)
            {
                char current = expression[index];
                if (current == '(')
                {
                    depth++;
                    index++;
                    continue;
                }

                if (current == ')')
                {
                    depth = Math.Max(0, depth - 1);
                    index++;
                    continue;
                }

                if (depth == 0 && IsAndAt(expression, index))
                {
                    string segment = expression[start..index].Trim();
                    if (!string.IsNullOrWhiteSpace(segment))
                        parts.Add(segment);
                    index += 3;
                    start = index;
                    continue;
                }

                index++;
            }

            string tail = expression[start..].Trim();
            if (!string.IsNullOrWhiteSpace(tail))
                parts.Add(tail);

            return parts;
        }

        private static bool IsAndAt(string expression, int index)
        {
            if (index < 0 || index + 3 > expression.Length)
                return false;

            if (!expression.AsSpan(index, 3).Equals("AND", StringComparison.OrdinalIgnoreCase))
                return false;

            bool leftBoundary = index == 0 || !char.IsLetterOrDigit(expression[index - 1]);
            bool rightBoundary = index + 3 >= expression.Length || !char.IsLetterOrDigit(expression[index + 3]);
            return leftBoundary && rightBoundary;
        }

        private static bool IsCurrentSourceLocalFilter(
            string expression,
            int currentJoinSourceIndex,
            IReadOnlyList<ImportFromPart> fromParts)
        {
            if (!TryParseBinaryPredicateOperands(expression, out string leftOperand, out _, out string rightOperand))
                return false;

            bool leftIsIdentifier = ImportBuildUtilities.TryResolveSourceAndColumn(leftOperand, fromParts, out int leftSourceIndex, out _);
            bool rightIsIdentifier = ImportBuildUtilities.TryResolveSourceAndColumn(rightOperand, fromParts, out int rightSourceIndex, out _);

            if (leftIsIdentifier && !rightIsIdentifier)
                return leftSourceIndex == currentJoinSourceIndex && IsLiteralOperand(rightOperand);

            if (rightIsIdentifier && !leftIsIdentifier)
                return rightSourceIndex == currentJoinSourceIndex && IsLiteralOperand(leftOperand);

            return false;
        }

        private static bool TryParseBinaryPredicateOperands(
            string expression,
            out string leftOperand,
            out string @operator,
            out string rightOperand)
        {
            string candidate = StripOuterParentheses(expression);
            Match match = Regex.Match(
                candidate,
                @"^\s*(?<left>.+?)\s*(?<op><>|!=|>=|<=|=|>|<)\s*(?<right>.+?)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                leftOperand = string.Empty;
                @operator = string.Empty;
                rightOperand = string.Empty;
                return false;
            }

            leftOperand = StripOuterParentheses(match.Groups["left"].Value.Trim());
            @operator = match.Groups["op"].Value.Trim();
            rightOperand = StripOuterParentheses(match.Groups["right"].Value.Trim());
            return true;
        }

        private static bool IsLiteralOperand(string operand)
        {
            string trimmed = operand.Trim();
            if (trimmed.Length == 0)
                return false;

            if (trimmed.StartsWith('\'') && trimmed.EndsWith('\'') && trimmed.Length >= 2)
                return true;

            if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
                return true;

            if (trimmed.StartsWith('@') || trimmed.StartsWith(':') || trimmed.StartsWith('$'))
                return true;

            if (trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return decimal.TryParse(trimmed, out _);
        }

        private static string StripOuterParentheses(string value)
        {
            string result = value?.Trim() ?? string.Empty;
            while (result.Length >= 2 && result[0] == '(' && result[^1] == ')')
            {
                result = result[1..^1].Trim();
            }

            return result;
        }

        private static bool ShouldSwapJoinSides(int currentJoinSourceIndex, int leftSourceIndex, int rightSourceIndex)
        {
            return leftSourceIndex == currentJoinSourceIndex
                && rightSourceIndex >= 0
                && rightSourceIndex < currentJoinSourceIndex;
        }

        private static int ResolveSourceIndex(string expression, IReadOnlyList<ImportFromPart> fromParts)
        {
            if (!ImportBuildUtilities.TryResolveSourceAndColumn(expression, fromParts, out int sourceIndex, out _))
                return -1;

            return sourceIndex;
        }
    }

    private sealed class ImportBuildProjectionStep : IImportBuildStep
    {
        public void Apply(ImportBuildPipelineContext context)
        {
            if (context.State.TableNodes.Count == 0)
                throw new InvalidOperationException("Cannot build projection without source tables.");

            var calculator = new SqlImportLayoutCalculator(context.Input.Layout);
            context.State.ResultY = calculator.ResultY(context.Input.FromParts.Count);

            NodeViewModel result = new(
                NodeDefinitionRegistry.Get(NodeType.ResultOutput),
                calculator.ResultPosition(context.State.ResultY)
            );
            context.Canvas.Nodes.Add(result);
            context.State.ResultNode = result;

            NodeViewModel columnList = new(
                NodeDefinitionRegistry.Get(NodeType.ColumnSetBuilder),
                calculator.ColumnSetPosition(context.State.ResultY)
            );
            context.Canvas.Nodes.Add(columnList);
            ImportBuildUtilities.SafeWire(columnList, "result", result, "columns", context.Canvas);

            NodeViewModel primaryNode = context.State.TableNodes[0];
            if (context.Input.IsStar)
            {
                IEnumerable<NodeViewModel> starSources;
                if (string.IsNullOrWhiteSpace(context.Input.StarQualifier))
                {
                    starSources = context.State.TableNodes;
                }
                else if (ImportBuildUtilities.TryFindSourceIndexForQualifier(
                    context.Input.StarQualifier!,
                    context.Input.FromParts,
                    out int sourceIndex)
                    && sourceIndex >= 0
                    && sourceIndex < context.State.TableNodes.Count)
                {
                    starSources = [context.State.TableNodes[sourceIndex]];
                }
                else
                {
                    starSources = [primaryNode];
                }

                foreach (NodeViewModel source in starSources)
                {
                    PinViewModel? wildcardPin = source.OutputPins.FirstOrDefault(p =>
                        p.Name.Equals("*", StringComparison.OrdinalIgnoreCase));
                    if (wildcardPin is not null)
                    {
                        ImportBuildUtilities.SafeWire(source, wildcardPin.Name, columnList, "columns", context.Canvas);
                        continue;
                    }

                    foreach (PinViewModel pin in source.OutputPins.Where(p =>
                        !p.Name.Equals("*", StringComparison.OrdinalIgnoreCase)))
                    {
                        ImportBuildUtilities.SafeWire(source, pin.Name, columnList, "columns", context.Canvas);
                    }
                }

                context.Report.Add(
                    new ImportReportItem(
                        "SELECT *",
                        ImportItemStatus.Imported,
                        "All columns wired",
                        result.Id
                    )
                );
                context.State.Imported++;
                return;
            }

            if (context.Input.SelectedColumns.Count == 0)
                return;

            int wired = 0;
            foreach ((string expr, string? alias) in context.Input.SelectedColumns)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (!TryExtractProjectedColumnName(expr, out string colName))
                {
                    context.Report.Add(
                        SqlImportReportFactory.Skipped(
                            SqlImportDiagnosticCodes.TypeInferenceFallback,
                            $"Column: {expr}",
                            SqlImportDiagnosticMessages.SelectComplexExpressionManualWireReportNote
                        )
                    );
                    context.State.Skipped++;
                    continue;
                }

                PinViewModel? pin = context.State.TableNodes
                    .SelectMany(n => n.OutputPins)
                    .FirstOrDefault(p => p.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));

                if (pin is null
                    && ImportBuildUtilities.TryResolveSourceAndColumn(expr, context.Input.FromParts, out int sourceIndex, out string inferredColumn)
                    && sourceIndex >= 0
                    && sourceIndex < context.State.TableNodes.Count)
                {
                    pin = ImportBuildUtilities.EnsureOutputColumnPin(context.State.TableNodes[sourceIndex], inferredColumn);
                }

                if (pin is not null)
                {
                    ImportBuildUtilities.SafeWire(pin.Owner, pin.Name, columnList, "columns", context.Canvas);
                    if (!string.IsNullOrWhiteSpace(alias))
                        context.State.ProjectedAliases[alias.Trim()] = pin;
                    wired++;
                }
                else
                {
                    context.Report.Add(
                        SqlImportReportFactory.Skipped(
                            SqlImportDiagnosticCodes.ColumnUnresolved,
                            $"Column: {expr}",
                            SqlImportDiagnosticMessages.SelectComplexExpressionManualWireReportNote
                        )
                    );
                    context.State.Skipped++;
                }
            }

            context.Report.Add(SqlImportReportFactory.Diagnostic(
                SqlImportDiagnosticCodes.ColumnUnresolved,
                $"SELECT ({wired}/{context.Input.SelectedColumns.Count} columns)",
                wired == context.Input.SelectedColumns.Count
                    ? ImportItemStatus.Imported
                    : ImportItemStatus.Partial,
                wired == context.Input.SelectedColumns.Count
                    ? null
                    : SqlImportDiagnosticMessages.SelectColumnsPartialAutoWireReportNote,
                result.Id
            ));

            if (wired == context.Input.SelectedColumns.Count)
                context.State.Imported++;
            else
                context.State.Partial++;
        }

        private static bool TryExtractProjectedColumnName(string expression, out string columnName)
        {
            columnName = string.Empty;
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            string trimmedExpression = expression.Trim();
            Match simpleIdentifierMatch = Regex.Match(
                trimmedExpression,
                $@"^(?<identifier>{SqlImportIdentifierNormalizer.QualifiedIdentifierPattern})$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );

            if (!simpleIdentifierMatch.Success)
                return false;

            string normalized = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(simpleIdentifierMatch.Groups["identifier"].Value);
            string[] segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
                return false;

            columnName = segments[^1];
            return !string.IsNullOrWhiteSpace(columnName);
        }
    }

    private static void ApplySourceIdentifierParameters(NodeViewModel tableNode, string normalizedTable)
    {
        string fullName = normalizedTable.Trim();
        string shortName = fullName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
            ?? fullName;

        tableNode.Parameters["table_full_name"] = fullName;
        tableNode.Parameters["table"] = shortName;
        tableNode.Parameters["source_table"] = fullName;
        tableNode.Parameters["from_table"] = fullName;
    }

    private static string NormalizeImportedSourceTable(string normalizedTable, string? alias)
    {
        string value = normalizedTable.Trim();
        if (value.Length == 0)
            return value;

        if (value.Contains(' ', StringComparison.Ordinal)
            && !value.Contains('"', StringComparison.Ordinal)
            && !value.Contains('[', StringComparison.Ordinal)
            && !value.Contains('`', StringComparison.Ordinal))
        {
            string[] segments = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > 1)
                value = segments[0];
        }

        if (!string.IsNullOrWhiteSpace(alias))
        {
            string aliasToken = alias.Trim();
            if (value.EndsWith(" " + aliasToken, StringComparison.OrdinalIgnoreCase))
                value = value[..(value.Length - aliasToken.Length)].TrimEnd();
        }

        Match embeddedAlias = Regex.Match(
            value,
            @"^(?<table>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)$",
            RegexOptions.CultureInvariant);
        if (embeddedAlias.Success)
            return embeddedAlias.Groups["table"].Value;

        return value;
    }

    private static (string FullName, IReadOnlyList<(string Name, PinDataType Type)> Columns) ResolveTableReference(
        string normalizedReference,
        DbMetadata? metadata)
    {
        if (metadata is not null)
        {
            TableMetadata? table = ResolveFromMetadata(metadata, normalizedReference);
            if (table is not null)
            {
                IReadOnlyList<(string Name, PinDataType Type)> columns = table.Columns
                    .Select(column => (column.Name, SchemaViewModel.MapSqlTypeToPinDataType(column.DataType)))
                    .ToList();
                return (table.FullName, columns);
            }
        }

        var catalogEntry = CanvasViewModel.DemoCatalog.FirstOrDefault(t =>
            t.FullName.Equals(normalizedReference, StringComparison.OrdinalIgnoreCase)
            || t.FullName.EndsWith("." + normalizedReference, StringComparison.OrdinalIgnoreCase)
        );

        if (catalogEntry != default)
            return (catalogEntry.FullName, catalogEntry.Cols.Select(c => (c.Name, c.Type)).ToList());

        return (normalizedReference, []);
    }

    private static TableMetadata? ResolveFromMetadata(DbMetadata metadata, string normalizedReference)
    {
        TableMetadata? exact = metadata.AllTables.FirstOrDefault(table =>
            string.Equals(table.FullName, normalizedReference, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        string shortName = normalizedReference.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault()
            ?? normalizedReference;

        List<TableMetadata> byName = metadata.AllTables
            .Where(table => string.Equals(table.Name, shortName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byName.Count == 1)
            return byName[0];

        return byName.FirstOrDefault(table =>
            string.Equals(table.Schema, "public", StringComparison.OrdinalIgnoreCase));
    }
}
