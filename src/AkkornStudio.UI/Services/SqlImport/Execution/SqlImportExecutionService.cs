using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.SqlImport.Contracts;
using AkkornStudio.SqlImport.Diagnostics;
using AkkornStudio.SqlImport.IR;
using AkkornStudio.SqlImport.Outcome;
using AkkornStudio.UI.Services.SqlImport.Build;
using AkkornStudio.UI.Services.SqlImport.Contracts;
using AkkornStudio.UI.Services.SqlImport.Execution.Applying;
using AkkornStudio.UI.Services.SqlImport.Execution.Parsing;
using AkkornStudio.UI.Services.SqlImport.Mapping;
using AkkornStudio.UI.Services.SqlImport.Rewriting;
using AkkornStudio.UI.Services.SqlImport.Validation;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.Services.SqlImport.Execution;

public sealed class SqlImportExecutionService(
    CanvasViewModel canvas,
    SqlImportSyntaxValidator syntaxValidator,
    SqlImportCteRewriteService cteRewriteService
) : ISqlImportExecutionService
{
    private readonly CanvasViewModel _canvas = canvas;
    private readonly SqlImportSyntaxValidator _syntaxValidator = syntaxValidator;
    private readonly SqlImportClauseParser _clauseParser = new(cteRewriteService);
    private readonly SqlImportClauseApplier _clauseApplier = new(canvas);
    private readonly SqlImportAstToIrMapper _astToIrMapper = new();

    public SqlImportExecutionResult Execute(
        string sql,
        ObservableCollection<ImportReportItem> report,
        CancellationToken cancellationToken,
        bool roundTripEquivalenceCheckEnabled = false
    )
    {
        (int imported, int partial, int skipped, SqlImportTiming timing, ImportOutcome outcome) = BuildGraph(
            sql,
            report,
            cancellationToken,
            roundTripEquivalenceCheckEnabled
        );

        return new SqlImportExecutionResult(imported, partial, skipped, timing, outcome);
    }

    private (int imported, int partial, int skipped, SqlImportTiming timing, ImportOutcome outcome) BuildGraph(
        string sql,
        ObservableCollection<ImportReportItem> report,
        CancellationToken cancellationToken,
        bool roundTripEquivalenceCheckEnabled
    )
    {
        int imported = 0;
        int partial = 0;
        int skipped = 0;
        var astIrDiagnostics = new List<SqlImportDiagnostic>();
        string? queryId = null;

        var totalWatch = Stopwatch.StartNew();
        var parseWatch = Stopwatch.StartNew();

        cancellationToken.ThrowIfCancellationRequested();
        _syntaxValidator.ValidateBasicSyntax(sql);

        SqlImportParseResult parseResult = _clauseParser.Parse(sql, report, cancellationToken);
        imported += parseResult.Imported;
        partial += parseResult.Partial;
        skipped += parseResult.Skipped;

        parseWatch.Stop();

        if (parseResult.ShouldStop || parseResult.Query is null)
        {
            totalWatch.Stop();
            ImportOutcome earlyOutcome = BuildOutcome(
                astIrDiagnostics,
                report,
                partial,
                skipped,
                queryId
            );
            return (
                imported,
                partial,
                skipped,
                new SqlImportTiming(parseWatch.Elapsed, TimeSpan.Zero, TimeSpan.Zero, totalWatch.Elapsed),
                earlyOutcome
            );
        }

        var mapWatch = Stopwatch.StartNew();
        SqlImportParsedQuery parsed = parseResult.Query;

        try
        {
            SqlToNodeIR ir = _astToIrMapper.MapSelectFrom(
                parsed,
                sql,
                _canvas.ActiveConnectionConfig?.Provider ?? AkkornStudio.Core.DatabaseProvider.Postgres
            );

            queryId = ir.QueryId;
            astIrDiagnostics.AddRange(ir.Diagnostics);
            (int astIrPartial, int astIrSkipped) = AppendAstIrDiagnosticsToReport(ir.Diagnostics, report);
            partial += astIrPartial;
            skipped += astIrSkipped;
        }
        catch (Exception ex)
        {
            report.Add(SqlImportReportFactory.Partial(
                SqlImportDiagnosticCodes.AstUnsupported,
                "AST → IR",
                $"AST→IR base mapping failed and was isolated from current import path: {ex.Message}"
            ));
            partial++;
        }

        var coreBuilder = new ImportModelToCanvasBuilder(_canvas);
        var coreInput = new ImportBuildInput(
            parsed.FromParts.Select(p => new ImportFromPart(p.Table, p.Alias, p.JoinType, p.OnClause)).ToList(),
            parsed.SelectedColumns.Select(c => new ImportSelectTerm(c.Expr, c.Alias)).ToList(),
            parsed.IsStar,
            parsed.StarQualifier,
            SqlImportLayoutPolicy.Default
        );
        ImportBuildContext coreContext = coreBuilder.BuildCore(coreInput, report, cancellationToken);

        imported += coreContext.Imported;
        partial += coreContext.Partial;
        skipped += coreContext.Skipped;

        mapWatch.Stop();
        var buildWatch = Stopwatch.StartNew();

        SqlImportApplyResult applyResult = _clauseApplier.Apply(parsed, coreContext, report, cancellationToken);
        imported += applyResult.Imported;
        partial += applyResult.Partial;
        skipped += applyResult.Skipped;

        int importedSetOperations = ApplySetOperationNodes(parsed, coreContext, report, cancellationToken);
        imported += importedSetOperations;

        buildWatch.Stop();
        totalWatch.Stop();

        ImportOutcome outcome = BuildOutcome(
            astIrDiagnostics,
            report,
            partial,
            skipped,
            queryId
        );

        return (
            imported,
            partial,
            skipped,
            new SqlImportTiming(parseWatch.Elapsed, mapWatch.Elapsed, buildWatch.Elapsed, totalWatch.Elapsed),
            outcome
        );
    }

    private int ApplySetOperationNodes(
        SqlImportParsedQuery parsed,
        ImportBuildContext coreContext,
        ObservableCollection<ImportReportItem> report,
        CancellationToken cancellationToken)
    {
        if (parsed.SetOperations.Count == 0)
            return 0;

        int imported = 0;
        for (int index = 0; index < parsed.SetOperations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SqlImportSetOperation operation = parsed.SetOperations[index];
            string operatorText = operation.IsAll ? $"{operation.Operator} ALL" : operation.Operator;

            NodeViewModel setNode = new(
                NodeDefinitionRegistry.Get(NodeType.SetOperation),
                new Point(coreContext.ResultNode.Position.X - 260, coreContext.ResultNode.Position.Y + 160 + (index * 130)));
            setNode.Parameters["operator"] = operatorText;
            _canvas.Nodes.Add(setNode);

            NodeViewModel queryTextNode = new(
                NodeDefinitionRegistry.Get(NodeType.ValueString),
                new Point(setNode.Position.X - 260, setNode.Position.Y + 40));
            queryTextNode.Parameters["value"] = operation.RightSql.Trim();
            _canvas.Nodes.Add(queryTextNode);

            PinViewModel setResultPin = setNode.OutputPins.Single(pin => pin.Name.Equals("result", StringComparison.OrdinalIgnoreCase));
            PinViewModel resultSetOperationPin = coreContext.ResultNode.InputPins.Single(pin => pin.Name.Equals("set_operation", StringComparison.OrdinalIgnoreCase));
            PinViewModel queryValuePin = queryTextNode.OutputPins.Single(pin => pin.Name.Equals("result", StringComparison.OrdinalIgnoreCase));
            PinViewModel queryTextPin = setNode.InputPins.Single(pin => pin.Name.Equals("query_text", StringComparison.OrdinalIgnoreCase));

            _canvas.ConnectPins(queryValuePin, queryTextPin);
            _canvas.ConnectPins(setResultPin, resultSetOperationPin);

            report.Add(new ImportReportItem(
                operatorText,
                ImportItemStatus.Imported,
                "Set operation node connected to Result Output.",
                setNode.Id));
            imported++;
        }

        return imported;
    }

    private static (int partial, int skipped) AppendAstIrDiagnosticsToReport(
        IReadOnlyList<SqlImportDiagnostic> diagnostics,
        ICollection<ImportReportItem> report
    )
    {
        if (diagnostics.Count == 0)
            return (0, 0);

        int partial = 0;
        int skipped = 0;

        foreach (SqlImportDiagnostic diagnostic in diagnostics)
        {
            ImportItemStatus status = diagnostic.Severity == SqlImportDiagnosticSeverity.Error
                ? ImportItemStatus.Skipped
                : ImportItemStatus.Partial;

            if (status == ImportItemStatus.Skipped)
                skipped++;
            else
                partial++;

            report.Add(new ImportReportItem(
                $"AST → IR {diagnostic.Code}",
                status,
                diagnostic.Message,
                diagnosticCode: diagnostic.Code
            ));
        }

        return (partial, skipped);
    }

    private static ImportOutcome BuildOutcome(
        IReadOnlyList<SqlImportDiagnostic> astIrDiagnostics,
        IReadOnlyCollection<ImportReportItem> report,
        int partial,
        int skipped,
        string? queryId
    )
    {
        IReadOnlyList<SqlImportDiagnostic> aggregatedDiagnostics = CollectExecutionDiagnostics(
            astIrDiagnostics,
            report,
            queryId
        );

        IReadOnlyList<SqlImportDiagnostic> blockingDiagnostics = aggregatedDiagnostics
            .Where(diagnostic =>
                diagnostic.Severity == SqlImportDiagnosticSeverity.Error
                || IsContextualBlockingDiagnostic(diagnostic)
            )
            .ToArray();

        IReadOnlyList<SqlImportDiagnostic> nonBlockingDiagnostics = aggregatedDiagnostics
            .Where(diagnostic =>
                diagnostic.Severity != SqlImportDiagnosticSeverity.Error
                && !IsContextualBlockingDiagnostic(diagnostic)
            )
            .ToArray();

        bool hasStructuralDegradation = partial > 0 || skipped > 0;
        bool hasPartialSignals = hasStructuralDegradation || HasPartialSignals(nonBlockingDiagnostics);
        bool hasDegradedGraph = hasPartialSignals;

        if (blockingDiagnostics.Count > 0)
        {
            return new ImportOutcome(
                ImportOutcomeStatus.Failed,
                ImportEquivalenceClass.NotEquivalent,
                hasDegradedGraph,
                blockingDiagnostics,
                nonBlockingDiagnostics
            );
        }

        if (hasPartialSignals)
        {
            IReadOnlyList<SqlImportDiagnostic> partialDiagnostics = nonBlockingDiagnostics;

            if (partialDiagnostics.Count == 0)
            {
                string effectiveQueryId = string.IsNullOrWhiteSpace(queryId) ? "execution" : queryId;
                string summaryMessage =
                    $"Import produced degraded graph (partial={partial}, skipped={skipped}) without clause-level AST diagnostics.";

                partialDiagnostics =
                [
                    SqlImportDiagnosticCatalog.Create(
                        SqlImportDiagnosticCodes.AstUnsupported,
                        SqlImportClause.Unknown,
                        summaryMessage,
                        effectiveQueryId
                    ),
                ];
            }

            return new ImportOutcome(
                ImportOutcomeStatus.Partial,
                ImportEquivalenceClass.Partial,
                true,
                [],
                partialDiagnostics
            );
        }

        if (nonBlockingDiagnostics.Count > 0)
        {
            return new ImportOutcome(
                ImportOutcomeStatus.EquivalentTolerant,
                ImportEquivalenceClass.EquivalentTolerant,
                false,
                [],
                nonBlockingDiagnostics
            );
        }

        return new ImportOutcome(
            ImportOutcomeStatus.EquivalentTotal,
            ImportEquivalenceClass.EquivalentTotal,
            false,
            [],
            []
        );
    }

    private static bool HasPartialSignals(IReadOnlyList<SqlImportDiagnostic> diagnostics)
    {
        return diagnostics.Any(diagnostic =>
            diagnostic.Category is SqlImportDiagnosticCategory.PartialImport
                or SqlImportDiagnosticCategory.UnsupportedFeature
                or SqlImportDiagnosticCategory.FallbackActivated
                or SqlImportDiagnosticCategory.AmbiguityUnresolved
        );
    }

    private static bool IsContextualBlockingDiagnostic(SqlImportDiagnostic diagnostic)
    {
        return diagnostic.Code switch
        {
            SqlImportDiagnosticCodes.ParseFatal => true,
            SqlImportDiagnosticCodes.AstUnsupported => IsStructuralCriticalClause(diagnostic.Clause),
            SqlImportDiagnosticCodes.AliasNormalizationLoss => false,
            SqlImportDiagnosticCodes.AliasNormalizationCollision => false,
            SqlImportDiagnosticCodes.ColumnAmbiguous => true,
            SqlImportDiagnosticCodes.ColumnUnresolved => IsStructuralCriticalClause(diagnostic.Clause),
            SqlImportDiagnosticCodes.SetOperandPrecedenceAmbiguous => false,
            SqlImportDiagnosticCodes.SetOperandArityMismatch => false,
            SqlImportDiagnosticCodes.SetOperandSemanticMismatch => false,
            SqlImportDiagnosticCodes.FunctionGenericPreserved => IsStructuralCriticalClause(diagnostic.Clause),
            SqlImportDiagnosticCodes.FunctionUnsupported => IsStructuralCriticalClause(diagnostic.Clause),
            SqlImportDiagnosticCodes.FunctionGenericForbiddenContext => true,
            SqlImportDiagnosticCodes.FallbackRegexUsed => false,
            SqlImportDiagnosticCodes.TypeInferenceFallback => false,
            SqlImportDiagnosticCodes.ProjectionDroppedBlocked => true,
            SqlImportDiagnosticCodes.ValueMapLegacyCompat => false,
            SqlImportDiagnosticCodes.ValueMapStructInvalid => true,
            SqlImportDiagnosticCodes.StarPreservedMissingMetadata => false,
            SqlImportDiagnosticCodes.StarAliasUnresolved => true,
            SqlImportDiagnosticCodes.RoundtripNotEquivalent => true,
            SqlImportDiagnosticCodes.RoundtripCheckDisabled => false,
            _ => false,
        };
    }

    private static bool IsStructuralCriticalClause(SqlImportClause clause)
    {
        return clause is SqlImportClause.Where
            or SqlImportClause.Join
            or SqlImportClause.Having
            or SqlImportClause.GroupBy;
    }

    private static IReadOnlyList<SqlImportDiagnostic> CollectExecutionDiagnostics(
        IReadOnlyList<SqlImportDiagnostic> astIrDiagnostics,
        IReadOnlyCollection<ImportReportItem> report,
        string? queryId
    )
    {
        string effectiveQueryId = string.IsNullOrWhiteSpace(queryId) ? "execution" : queryId;
        var diagnostics = new List<SqlImportDiagnostic>(astIrDiagnostics.Count + report.Count);
        diagnostics.AddRange(astIrDiagnostics);

        foreach (ImportReportItem item in report.Where(r => r.IsPartial || r.IsSkipped))
        {
            if (item.Label.StartsWith("AST → IR ", StringComparison.Ordinal))
                continue;

            string message = string.IsNullOrWhiteSpace(item.Note)
                ? item.Label
                : $"{item.Label}: {item.Note}";

            string code = string.IsNullOrWhiteSpace(item.DiagnosticCode)
                ? SqlImportDiagnosticCodes.AstUnsupported
                : item.DiagnosticCode;

            SqlImportDiagnostic syntheticDiagnostic = SqlImportDiagnosticCatalog.Create(
                code,
                InferClauseFromLabel(item.Label),
                message,
                effectiveQueryId
            );

            bool alreadyExists = diagnostics.Any(existing =>
                existing.Code == syntheticDiagnostic.Code
                && existing.Clause == syntheticDiagnostic.Clause
                && string.Equals(existing.Message, syntheticDiagnostic.Message, StringComparison.Ordinal)
            );

            if (!alreadyExists)
                diagnostics.Add(syntheticDiagnostic);
        }

        return diagnostics;
    }

    private static SqlImportClause InferClauseFromLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return SqlImportClause.Unknown;

        if (label.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.Select;

        if (label.Contains("FROM", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.From;

        if (label.Contains("JOIN", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.Join;

        if (label.Contains("WHERE", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.Where;

        if (label.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.GroupBy;

        if (label.Contains("HAVING", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.Having;

        if (label.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.OrderBy;

        if (label.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) || label.Contains("TOP", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.Limit;

        if (label.Contains("FUNCTION", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.Function;

        if (label.Contains("VALUE MAP", StringComparison.OrdinalIgnoreCase) || label.Contains("VALUEMAP", StringComparison.OrdinalIgnoreCase))
            return SqlImportClause.ValueMap;

        if (label.Contains("STAR", StringComparison.OrdinalIgnoreCase) || label.Contains("*", StringComparison.Ordinal))
            return SqlImportClause.Star;

        return SqlImportClause.Unknown;
    }
}
