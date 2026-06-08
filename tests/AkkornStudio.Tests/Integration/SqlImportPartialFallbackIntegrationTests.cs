
using AkkornStudio.SqlImport.Diagnostics;
using AkkornStudio.UI.ViewModels.Canvas;
using Xunit;

namespace Integration;

public class SqlImportPartialFallbackIntegrationTests
{
    [Fact]
    public async Task ImportAsync_WithValidSimpleCte_ImportsWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH cte AS (SELECT id FROM public.orders) SELECT id FROM cte";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, r =>
            r.Status == ImportItemStatus.Imported
            && r.Label.Contains("CTE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(canvas.SqlImporter.Report, r =>
            r.Status == ImportItemStatus.Partial
            && string.Equals(r.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, r =>
            r.Status == ImportItemStatus.Skipped
            && r.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_WithPartiallySupportedClauses_CompletesAndProvidesClearDiagnostics()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT id FROM public.orders UNION SELECT id, name FROM public.customers";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);

        var problematic = canvas.SqlImporter.Report
            .Where(r => r.Status is ImportItemStatus.Partial or ImportItemStatus.Skipped)
            .ToList();

        Assert.NotEmpty(problematic);
        Assert.All(problematic, item => Assert.False(string.IsNullOrWhiteSpace(item.Note)));
        Assert.Contains(problematic, item => item.Label.Contains("UNION", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_WithPartiallySupportedClauses_PartialOrSkippedItemsAlwaysExposeDiagnosticCode()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT id FROM public.orders UNION SELECT id, name FROM public.customers";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);

        var problematic = canvas.SqlImporter.Report
            .Where(r => r.Status is ImportItemStatus.Partial or ImportItemStatus.Skipped)
            .ToList();

        Assert.NotEmpty(problematic);
        Assert.All(problematic, item => Assert.False(string.IsNullOrWhiteSpace(item.DiagnosticCode)));
    }

    [Fact]
    public async Task ImportAsync_WithSimpleUnion_ImportsSetOperationWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT id FROM public.orders UNION SELECT id FROM public.customers";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.Nodes, node => node.Type == AkkornStudio.Nodes.NodeType.SetOperation);
        Assert.Contains("UNION", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT id FROM public.customers", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("UNION", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithMultiOperandUnion_ImportsSetOperationAndPreservesTailSql()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT id FROM public.orders UNION ALL SELECT id FROM public.customers UNION SELECT id FROM public.archived_orders";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.Nodes, node => node.Type == AkkornStudio.Nodes.NodeType.SetOperation);
        Assert.Contains("UNION ALL", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public.archived_orders", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("UNION", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithCorrelatedExists_ImportsAsSemiJoinPatternWithoutUnsupportedDiagnostic()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT o.id FROM public.orders o WHERE EXISTS (SELECT 1 FROM public.order_items i WHERE i.order_id = o.id)";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        NodeViewModel existsNode = Assert.Single(canvas.Nodes, node => node.Type == NodeType.SubqueryExists);
        Assert.Equal("semi", existsNode.Parameters["correlation_kind"]);
        Assert.Contains("o.id", existsNode.Parameters["correlated_outer_refs"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            (item.Status is ImportItemStatus.Partial or ImportItemStatus.Skipped)
            && item.DiagnosticCode == SqlImportDiagnosticCodes.AstUnsupported);
    }

    [Fact]
    public async Task ImportAsync_WithCorrelatedExistsWithMultipleOuterRefs_PreservesExternalCorrelationFields()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT o.id FROM public.orders o WHERE EXISTS (SELECT 1 FROM public.order_items i WHERE i.order_id = o.id AND i.customer_id = o.customer_id)";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        NodeViewModel existsNode = Assert.Single(canvas.Nodes, node => node.Type == NodeType.SubqueryExists);
        Assert.Contains("o.id", existsNode.Parameters["correlated_outer_refs"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("o.customer_id", existsNode.Parameters["correlated_outer_refs"], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            (item.Status is ImportItemStatus.Partial or ImportItemStatus.Skipped)
            && item.DiagnosticCode == SqlImportDiagnosticCodes.AstUnsupported);
    }

    [Fact]
    public async Task ImportAsync_WithCorrelatedNotExists_ImportsAsAntiSemiJoinPatternWithoutUnsupportedDiagnostic()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT o.id FROM public.orders o WHERE NOT EXISTS (SELECT 1 FROM public.order_items i WHERE i.order_id = o.id)";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        NodeViewModel existsNode = Assert.Single(canvas.Nodes, node => node.Type == NodeType.SubqueryExists);
        Assert.Equal("true", existsNode.Parameters["negate"]);
        Assert.Equal("anti-semi", existsNode.Parameters["correlation_kind"]);
        Assert.Contains("NOT EXISTS", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            (item.Status is ImportItemStatus.Partial or ImportItemStatus.Skipped)
            && item.DiagnosticCode == SqlImportDiagnosticCodes.AstUnsupported);
    }

    [Fact]
    public async Task ImportAsync_WithInvalidCteName_ReportsDiagnostic()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH 1cte AS (SELECT id FROM public.orders) SELECT id FROM 1cte";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && item.Label.Contains("CTE name diagnostics", StringComparison.OrdinalIgnoreCase)
            && (item.Note ?? string.Empty).Contains("Invalid CTE name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_WithDuplicateCteName_ReportsDiagnostic()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH cte AS (SELECT id FROM public.orders), cte AS (SELECT id FROM public.customers) SELECT id FROM cte";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && item.Label.Contains("CTE name diagnostics", StringComparison.OrdinalIgnoreCase)
            && (item.Note ?? string.Empty).Contains("Duplicate CTE name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_WithSimpleCte_ImportsSuccessfully()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH cte_orders AS (SELECT id FROM public.orders) SELECT id FROM cte_orders";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("CTE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithChainedSimpleCtes_ImportsSuccessfully()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH cte_a AS (SELECT id FROM public.orders), cte_b AS (SELECT id FROM cte_a) SELECT id FROM cte_b";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("CTE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithSimpleCteColumnList_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH cte_orders(id) AS (SELECT id FROM public.orders) SELECT id FROM cte_orders";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("CTE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithFilteredCte_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH recent_orders AS (SELECT id, status FROM public.orders src WHERE src.status = 'OPEN') SELECT ro.id FROM recent_orders ro WHERE ro.id > 10";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("CTE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("status", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OPEN", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithFilteredCteInInnerJoin_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH active_customers AS (SELECT id, active FROM public.customers src WHERE src.active = true) SELECT o.id FROM public.orders o JOIN active_customers c ON o.customer_id = c.id WHERE o.id > 10";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("CTE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canvas.Nodes, node => node.Type == AkkornStudio.Nodes.NodeType.Join);
        Assert.Contains("active", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("true", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithFilteredCteInLeftJoin_ReportsSafetyDiagnostic()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH active_customers AS (SELECT id, active FROM public.customers src WHERE src.active = true) SELECT o.id FROM public.orders o LEFT JOIN active_customers c ON o.customer_id = c.id";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && item.Label.Contains("CTE safety diagnostics", StringComparison.OrdinalIgnoreCase)
            && (item.Note ?? string.Empty).Contains("blocked", StringComparison.OrdinalIgnoreCase)
            && (item.Note ?? string.Empty).Contains("outer join semantics", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_WithSimpleJoinInsideCte_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH order_customers AS (SELECT o.id AS order_id, c.name AS customer_name FROM public.orders o JOIN public.customers c ON o.customer_id = c.id) SELECT oc.order_id FROM order_customers oc WHERE oc.order_id > 10";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("CTE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canvas.Nodes, node => node.Type == AkkornStudio.Nodes.NodeType.Join);
        Assert.Contains("customer_id", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithOrderedLimitedCte_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "WITH recent_orders AS (SELECT id AS order_id, status FROM public.orders WHERE status = 'OPEN' ORDER BY id DESC LIMIT 5) SELECT ro.order_id FROM recent_orders ro";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("CTE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canvas.Nodes, node => node.Type == AkkornStudio.Nodes.NodeType.Top);
        Assert.Contains("ORDER BY", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT 5", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithSimpleFromSubquery_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT id FROM (SELECT id FROM public.orders) o";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("FROM sub-query", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithFilteredFromSubquery_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT o.id FROM (SELECT id, status FROM public.orders src WHERE src.status = 'OPEN') o WHERE o.id > 10";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("FROM sub-query", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("status", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OPEN", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithProjectionAliasFromSubquery_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT o.order_id FROM (SELECT id AS order_id, status FROM public.orders) o WHERE o.order_id > 10";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("FROM sub-query", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("id", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("order_id", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithSimpleJoinSubquery_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT o.id FROM public.orders o JOIN (SELECT id FROM public.customers) c ON o.customer_id = c.id";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("JOIN sub-query", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canvas.Nodes, node => node.Type == AkkornStudio.Nodes.NodeType.Join);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithFilteredInnerJoinSubquery_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT o.id FROM public.orders o JOIN (SELECT id, active FROM public.customers src WHERE src.active = true) c ON o.customer_id = c.id WHERE o.id > 10";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("JOIN sub-query", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canvas.Nodes, node => node.Type == AkkornStudio.Nodes.NodeType.Join);
        Assert.Contains("active", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("true", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithFilteredLeftJoinSubquery_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT o.id FROM public.orders o LEFT JOIN (SELECT id, active FROM public.customers src WHERE src.active = true) c ON o.customer_id = c.id";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("JOIN sub-query", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(canvas.Nodes, node => node.Type == AkkornStudio.Nodes.NodeType.Join);
        Assert.Contains("active", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("true", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }

    [Fact]
    public async Task ImportAsync_WithMultipleFilteredJoinSubqueries_ImportsSuccessfullyWithoutFallback()
    {
        var canvas = new CanvasViewModel();

        canvas.SqlImporter.ImportStartDelayMs = 0;
        canvas.SqlImporter.ImportTimeout = TimeSpan.FromSeconds(5);
        canvas.SqlImporter.SqlInput =
            "SELECT o.id FROM public.orders o JOIN (SELECT id, region_id, active FROM public.customers src WHERE src.active = true) c ON o.customer_id = c.id JOIN (SELECT id, enabled FROM public.regions rr WHERE rr.enabled = true) r ON c.region_id = r.id WHERE o.id > 10";

        await canvas.SqlImporter.ImportAsync();
        SqlImportWiringAssertions.AssertGraphWiringIfGraphExists(canvas);
        canvas.LiveSql.Recompile();

        Assert.True(canvas.SqlImporter.HasReport);
        Assert.Contains(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Imported
            && item.Label.Contains("JOIN sub-query", StringComparison.OrdinalIgnoreCase));
        Assert.True(canvas.Nodes.Count(node => node.Type == AkkornStudio.Nodes.NodeType.Join) >= 2);
        Assert.Contains("active", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled", canvas.LiveSql.RawSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Status == ImportItemStatus.Partial
            && string.Equals(item.DiagnosticCode, SqlImportDiagnosticCodes.FallbackRegexUsed, StringComparison.Ordinal));
        Assert.DoesNotContain(canvas.SqlImporter.Report, item =>
            item.Label.Contains("CTE / sub-query", StringComparison.OrdinalIgnoreCase)
            && item.Status == ImportItemStatus.Skipped);
    }
}
