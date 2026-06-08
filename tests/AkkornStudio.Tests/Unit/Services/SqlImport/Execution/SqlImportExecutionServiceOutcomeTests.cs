using System.Collections.ObjectModel;
using AkkornStudio.SqlImport.Contracts;
using AkkornStudio.SqlImport.Diagnostics;
using AkkornStudio.UI.Services.SqlImport.Execution;
using AkkornStudio.UI.Services.SqlImport.Rewriting;
using AkkornStudio.UI.Services.SqlImport.Validation;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.Tests.Unit.Services.SqlImport.Execution;

public sealed class SqlImportExecutionServiceOutcomeTests
{
    [Fact]
    public void Execute_WithSimpleSelect_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
    }

    [Fact]
    public void Execute_WithNormalizationLossWarningOnly_ClassifiesAsEquivalentTolerant()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>
        {
            new(
                "Alias normalization",
                ImportItemStatus.Partial,
                "Normalization loss in alias conversion",
                diagnosticCode: SqlImportDiagnosticCodes.AliasNormalizationLoss
            )
        };

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTolerant, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTolerant, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.Contains(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.AliasNormalizationLoss
        );
    }

    [Fact]
    public void Execute_WithFallbackSignalOnly_ClassifiesAsPartial()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>
        {
            new(
                "Fallback mode",
                ImportItemStatus.Partial,
                "Regex fallback activated for unsupported fragment",
                diagnosticCode: SqlImportDiagnosticCodes.FallbackRegexUsed
            )
        };

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Partial, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.Partial, result.Outcome.EquivalenceClass);
        Assert.True(result.Outcome.HasDegradedGraph);
        Assert.Contains(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithRoundtripDivergenceSignal_AndFlagEnabled_ClassifiesAsFailed()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>
        {
            new(
                "Roundtrip check",
                ImportItemStatus.Skipped,
                "Round-trip not equivalent after Graph to SQL compilation",
                diagnosticCode: SqlImportDiagnosticCodes.RoundtripNotEquivalent
            )
        };

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders",
            report,
            CancellationToken.None,
            roundTripEquivalenceCheckEnabled: true
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Failed, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.NotEquivalent, result.Outcome.EquivalenceClass);
        Assert.Contains(
            result.Outcome.BlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.RoundtripNotEquivalent
        );
    }

    [Fact]
    public void Execute_WithRoundtripDivergenceSignal_AndFlagDisabled_ClassifiesAsEquivalentTolerant()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>
        {
            new(
                "Roundtrip check",
                ImportItemStatus.Skipped,
                "Round-trip not equivalent after Graph to SQL compilation",
                diagnosticCode: SqlImportDiagnosticCodes.RoundtripCheckDisabled
            )
        };

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders",
            report,
            CancellationToken.None,
            roundTripEquivalenceCheckEnabled: false
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTolerant, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTolerant, result.Outcome.EquivalenceClass);
        Assert.DoesNotContain(
            result.Outcome.BlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.RoundtripNotEquivalent
        );
        Assert.Contains(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.RoundtripCheckDisabled
        );
    }

    [Fact]
    public void Execute_WithSimpleUnion_MaterializesSetOperationAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders UNION SELECT id FROM customers",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.Contains(report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("UNION", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.AstUnsupported
        );
    }

    [Fact]
    public void Execute_WithUnionArityMismatch_ClassifiesAsPartial()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders UNION SELECT id, name FROM customers",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Partial, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.Partial, result.Outcome.EquivalenceClass);
        Assert.NotEmpty(result.Outcome.NonBlockingDiagnostics);
        Assert.Contains(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.SetOperandArityMismatch
        );
    }

    [Fact]
    public void Execute_WithJoinOnFallbackRegexPath_ClassifiesAsPartial()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o INNER JOIN customers c ON o.customer_id = c.id AND o.total > c.credit_limit",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Partial, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.Partial, result.Outcome.EquivalenceClass);
        Assert.True(result.Outcome.HasDegradedGraph);
        Assert.Contains(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithWhereSourceFallbackPath_EmitsFallbackDiagnostic()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o INNER JOIN customers c ON o.customer_id = c.id WHERE status = 'OPEN'",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Contains(
            result.Outcome!.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithSimpleCteRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "WITH recent_orders AS (SELECT id FROM orders) SELECT id FROM recent_orders",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithSimpleCteColumnListRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "WITH recent_orders(id) AS (SELECT id FROM orders) SELECT id FROM recent_orders",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithFilteredCteRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "WITH recent_orders AS (SELECT id, status FROM orders src WHERE src.status = 'OPEN') SELECT ro.id FROM recent_orders ro WHERE ro.id > 10",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithFilteredCteInInnerJoinRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "WITH active_customers AS (SELECT id, active FROM customers src WHERE src.active = true) SELECT o.id FROM orders o JOIN active_customers c ON o.customer_id = c.id WHERE o.id > 10",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithFilteredCteInLeftJoin_DoesNotRewriteAsEquivalent()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "WITH active_customers AS (SELECT id, active FROM customers src WHERE src.active = true) SELECT o.id FROM orders o LEFT JOIN active_customers c ON o.customer_id = c.id",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.NotEqual(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Contains(
            result.Outcome.BlockingDiagnostics.Concat(result.Outcome.NonBlockingDiagnostics),
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.AstUnsupported
        );
    }

    [Fact]
    public void Execute_WithSimpleJoinInsideCteRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "WITH order_customers AS (SELECT o.id AS order_id, c.name AS customer_name FROM orders o JOIN customers c ON o.customer_id = c.id) SELECT oc.order_id FROM order_customers oc WHERE oc.order_id > 10",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithOrderedLimitedCteRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "WITH recent_orders AS (SELECT id AS order_id, status FROM orders WHERE status = 'OPEN' ORDER BY id DESC LIMIT 5) SELECT ro.order_id FROM recent_orders ro",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithSimpleFromSubqueryRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM (SELECT id FROM orders) o",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithFilteredFromSubqueryRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM (SELECT id, status FROM orders src WHERE src.status = 'OPEN') o WHERE o.id > 10",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithProjectionAliasFromSubqueryRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.order_id FROM (SELECT id AS order_id, status FROM orders) o WHERE o.order_id > 10",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithSimpleJoinSubqueryRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o JOIN (SELECT id FROM customers) c ON o.customer_id = c.id",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithFilteredInnerJoinSubqueryRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o JOIN (SELECT id, active FROM customers src WHERE src.active = true) c ON o.customer_id = c.id WHERE o.id > 10",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithFilteredLeftJoinSubqueryRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o LEFT JOIN (SELECT id, active FROM customers src WHERE src.active = true) c ON o.customer_id = c.id",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithMultipleFilteredJoinSubqueryRewritePath_ClassifiesAsEquivalentTotal()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o JOIN (SELECT id, region_id, active FROM customers src WHERE src.active = true) c ON o.customer_id = c.id JOIN (SELECT id, enabled FROM regions rr WHERE rr.enabled = true) r ON c.region_id = r.id WHERE o.id > 10",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.EquivalentTotal, result.Outcome.EquivalenceClass);
        Assert.False(result.Outcome.HasDegradedGraph);
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithFilteredRightJoinSubquery_DoesNotRewriteAsEquivalent()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o RIGHT JOIN (SELECT id, active FROM customers src WHERE src.active = true) c ON o.customer_id = c.id",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.NotEqual(ImportOutcomeStatus.EquivalentTotal, result.Outcome!.Status);
        Assert.Contains(
            result.Outcome.BlockingDiagnostics.Concat(result.Outcome.NonBlockingDiagnostics),
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.AstUnsupported
        );
    }

    [Fact]
    public void Execute_WithAmbiguousBooleanColumn_ClassifiesAsFailed()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o INNER JOIN customers c ON o.customer_id = c.id WHERE id > 10",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Failed, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.NotEquivalent, result.Outcome.EquivalenceClass);
        Assert.Contains(result.Outcome.BlockingDiagnostics, diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.ColumnAmbiguous);
    }

    [Fact]
    public void Execute_WithUnsupportedFunctionInWhere_ClassifiesAsFailed()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o WHERE ROW_NUMBER(o.id) = 1",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Failed, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.NotEquivalent, result.Outcome.EquivalenceClass);
        Assert.Contains(
            result.Outcome.BlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FunctionUnsupported
                && diagnostic.Clause == SqlImportClause.Where
        );
    }

    [Fact]
    public void Execute_WithTypeInferenceFallbackInOrderBy_ClassifiesAsPartial()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o ORDER BY o.id + 1",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Partial, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.Partial, result.Outcome.EquivalenceClass);
        Assert.Contains(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.TypeInferenceFallback
                && diagnostic.Clause == SqlImportClause.OrderBy
        );
    }

    [Fact]
    public void Execute_WithOrderBySourceFallbackPath_EmitsFallbackDiagnostic()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o INNER JOIN customers c ON o.customer_id = c.id ORDER BY status",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Contains(
            result.Outcome!.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithGroupBySourceFallbackPath_EmitsFallbackDiagnostic()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.status FROM orders o INNER JOIN customers c ON o.customer_id = c.id GROUP BY status",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Contains(
            result.Outcome!.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithHavingFallbackPath_EmitsFallbackDiagnostic()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o GROUP BY o.id HAVING STDDEV(o.id) > 1",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Contains(
            result.Outcome!.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
        );
    }

    [Fact]
    public void Execute_WithCountColumnHaving_DoesNotEmitFallbackOrTypeInferenceDiagnostic()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o GROUP BY o.id HAVING COUNT(o.id) > 1",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.DoesNotContain(
            result.Outcome!.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
                && diagnostic.Clause == SqlImportClause.Having
        );
        Assert.DoesNotContain(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.TypeInferenceFallback
                && diagnostic.Clause == SqlImportClause.Having
        );
    }

    [Fact]
    public void Execute_WithSimpleAggregateHaving_DoesNotEmitFallbackDiagnostic()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.customer_id FROM orders o GROUP BY o.customer_id HAVING SUM(o.customer_id) > 1",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.DoesNotContain(
            result.Outcome!.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FallbackRegexUsed
                && diagnostic.Clause == SqlImportClause.Having
        );
    }

    [Fact]
    public void Execute_WithUnresolvedColumnInWhere_ClassifiesAsFailed()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id FROM orders o WHERE x.id > 10",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Failed, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.NotEquivalent, result.Outcome.EquivalenceClass);
        Assert.Contains(
            result.Outcome.BlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.ColumnUnresolved
                && diagnostic.Clause == SqlImportClause.Where
        );
    }

    [Fact]
    public void Execute_WithAstUnsupportedInWhereReportSignal_ClassifiesAsFailed()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>
        {
            new(
                "WHERE unsupported",
                ImportItemStatus.Partial,
                "Unsupported AST shape in WHERE clause",
                diagnosticCode: SqlImportDiagnosticCodes.AstUnsupported
            )
        };

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Failed, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.NotEquivalent, result.Outcome.EquivalenceClass);
        Assert.Contains(
            result.Outcome.BlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.AstUnsupported
                && diagnostic.Clause == SqlImportClause.Where
        );
    }

    [Fact]
    public void Execute_WithUnresolvedColumnInOrderBy_ClassifiesAsPartial()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportExecutionResult result = service.Execute(
            "SELECT o.id AS order_id FROM orders o ORDER BY x.id",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Partial, result.Outcome!.Status);
        Assert.Equal(ImportEquivalenceClass.Partial, result.Outcome.EquivalenceClass);
        Assert.Contains(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.ColumnUnresolved
                && diagnostic.Clause == SqlImportClause.OrderBy
        );
    }

    [Fact]
    public void Execute_WithTypedDiagnosticCodeInReport_UsesTypedCode()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>
        {
            new(
                "ORDER BY warning",
                ImportItemStatus.Partial,
                "Parsing expression fallback",
                diagnosticCode: SqlImportDiagnosticCodes.TypeInferenceFallback
            )
        };

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Partial, result.Outcome!.Status);
        Assert.Contains(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.TypeInferenceFallback
        );
    }

    [Fact]
    public void Execute_WithTypedDiagnosticCodeAndConflictingText_PrefersTypedCode()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>
        {
            new(
                "WHERE issue",
                ImportItemStatus.Partial,
                "SQLIMP_0403_FUNCTION_GENERIC_FORBIDDEN_CONTEXT detected",
                diagnosticCode: SqlImportDiagnosticCodes.TypeInferenceFallback
            )
        };

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Partial, result.Outcome!.Status);
        Assert.DoesNotContain(
            result.Outcome.BlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FunctionGenericForbiddenContext
        );
        Assert.Contains(
            result.Outcome.NonBlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.TypeInferenceFallback
        );
    }

    [Fact]
    public void Execute_WithTypedForbiddenCodeInWhereReport_ClassifiesAsFailed()
    {
        var service = CreateService();
        var report = new ObservableCollection<ImportReportItem>
        {
            new(
                "WHERE issue",
                ImportItemStatus.Partial,
                "Generic function in forbidden context detected",
                diagnosticCode: SqlImportDiagnosticCodes.FunctionGenericForbiddenContext
            )
        };

        SqlImportExecutionResult result = service.Execute(
            "SELECT id FROM orders",
            report,
            CancellationToken.None
        );

        Assert.NotNull(result.Outcome);
        Assert.Equal(ImportOutcomeStatus.Failed, result.Outcome!.Status);
        Assert.Contains(
            result.Outcome.BlockingDiagnostics,
            diagnostic => diagnostic.Code == SqlImportDiagnosticCodes.FunctionGenericForbiddenContext
                && diagnostic.Clause == SqlImportClause.Where
        );
    }

    private static SqlImportExecutionService CreateService()
    {
        var canvas = new CanvasViewModel();
        return new SqlImportExecutionService(
            canvas,
            new SqlImportSyntaxValidator(),
            new SqlImportCteRewriteService()
        );
    }
}
