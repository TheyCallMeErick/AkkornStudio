using System.Collections.ObjectModel;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.SqlImport.Build;
using AkkornStudio.UI.Services.SqlImport.Execution.Applying;
using AkkornStudio.UI.Services.SqlImport.Execution.Parsing;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.Tests.Unit.Services.SqlImport.Execution.Applying;

public class SqlImportApplyStepsTests
{
    [Fact]
    public void WhereClauseApplier_WithSimpleComparison_ImportsWhereChain()
    {
        var setup = CreateCoreContext();
        var query = CreateQuery(whereClause: "id = 10");
        var context = new SqlImportApplyContext(query, setup.CoreContext, setup.Report, CancellationToken.None);

        var step = new SqlImportWhereClauseApplier(setup.Canvas);

        SqlImportApplyResult result = step.Apply(context);

        Assert.Equal(1, result.Imported);
        Assert.Contains(setup.Canvas.Nodes, n =>
            n.Type is NodeType.Equals
                or NodeType.NotEquals
                or NodeType.GreaterThan
                or NodeType.GreaterOrEqual
                or NodeType.LessThan
                or NodeType.LessOrEqual);
        Assert.Contains(setup.Canvas.Connections, c =>
            c.ToPin?.Owner == setup.CoreContext.ResultNode
            && c.ToPin.Name.Equals("where", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrderingClauseApplier_WithProjectedAlias_CreatesOrderByConnection()
    {
        var setup = CreateCoreContext(selectedColumns: [new ImportSelectTerm("id", "order_id")]);
        var query = CreateQuery(orderBy: "order_id DESC", selectedColumns: [new SqlImportSelectedColumn("id", "order_id")]);
        var context = new SqlImportApplyContext(query, setup.CoreContext, setup.Report, CancellationToken.None);

        var step = new SqlImportOrderingClauseApplier();

        SqlImportApplyResult result = step.Apply(context);

        Assert.Equal(1, result.Imported);
        Assert.Contains(setup.Canvas.Connections, c =>
            c.ToPin?.Owner == setup.CoreContext.ResultNode
            && c.ToPin.Name.Equals("order_by_desc", StringComparison.OrdinalIgnoreCase)
            && c.FromPin.Name.Equals("id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GroupingClauseApplier_WithMappedTerms_CreatesGroupByConnection()
    {
        var setup = CreateCoreContext(selectedColumns: [new ImportSelectTerm("id", null)]);
        var query = CreateQuery(groupBy: "id", selectedColumns: [new SqlImportSelectedColumn("id", null)]);
        var context = new SqlImportApplyContext(query, setup.CoreContext, setup.Report, CancellationToken.None);

        var step = new SqlImportGroupingClauseApplier();

        SqlImportApplyResult result = step.Apply(context);

        Assert.Equal(1, result.Imported);
        Assert.Contains(setup.Canvas.Connections, c =>
            c.ToPin?.Owner == setup.CoreContext.ResultNode
            && c.ToPin.Name.Equals("group_by", StringComparison.OrdinalIgnoreCase)
            && c.FromPin.Name.Equals("id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HavingClauseApplier_WithCountComparison_AddsPredicateNodes()
    {
        var setup = CreateCoreContext();
        var query = CreateQuery(havingClause: "COUNT(*) > 1", groupBy: "id");
        var context = new SqlImportApplyContext(query, setup.CoreContext, setup.Report, CancellationToken.None);

        var step = new SqlImportHavingClauseApplier(setup.Canvas);

        SqlImportApplyResult result = step.Apply(context);

        Assert.Equal(1, result.Imported);
        Assert.Contains(setup.Canvas.Nodes, n => n.Type == NodeType.CountStar);
        Assert.Contains(setup.Canvas.Nodes, n => n.Type == NodeType.GreaterThan);
    }

    [Fact]
    public void WhereClauseApplier_WithAliasedStringComparison_ImportsWhereChain()
    {
        var setup = CreateCoreContext(
            fromParts: [new ImportFromPart("public.orders", "ro", null, null)],
            selectedColumns: [new ImportSelectTerm("ro.id", null)]);
        var query = CreateQuery(
            whereClause: "ro.status = 'OPEN'",
            selectedColumns: [new SqlImportSelectedColumn("ro.id", null)],
            sourceParts: [new SqlImportSourcePart("public.orders", "ro", null, null)]);
        var context = new SqlImportApplyContext(query, setup.CoreContext, setup.Report, CancellationToken.None);

        var step = new SqlImportWhereClauseApplier(setup.Canvas);

        SqlImportApplyResult result = step.Apply(context);

        Assert.Equal(1, result.Imported);
        Assert.DoesNotContain(setup.Report, item =>
            item.DiagnosticCode == AkkornStudio.SqlImport.Diagnostics.SqlImportDiagnosticCodes.AstUnsupported
            && item.Label.Contains("WHERE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WhereClauseApplier_WithParenthesizedAndComparison_ImportsWhereChain()
    {
        var setup = CreateCoreContext(
            fromParts: [new ImportFromPart("public.orders", "ro", null, null)],
            selectedColumns: [new ImportSelectTerm("ro.id", null)]);
        var query = CreateQuery(
            whereClause: "(ro.status = 'OPEN') AND ro.id > 10",
            selectedColumns: [new SqlImportSelectedColumn("ro.id", null)],
            sourceParts: [new SqlImportSourcePart("public.orders", "ro", null, null)]);
        var context = new SqlImportApplyContext(query, setup.CoreContext, setup.Report, CancellationToken.None);

        var step = new SqlImportWhereClauseApplier(setup.Canvas);

        SqlImportApplyResult result = step.Apply(context);

        Assert.True(result.Imported > 0);
        Assert.DoesNotContain(setup.Report, item =>
            item.DiagnosticCode == AkkornStudio.SqlImport.Diagnostics.SqlImportDiagnosticCodes.AstUnsupported
            && item.Label.Contains("WHERE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HavingClauseApplier_WithCountDistinctComparison_AddsDistinctCountNode()
    {
        var setup = CreateCoreContext();
        var query = CreateQuery(havingClause: "COUNT(DISTINCT id) > 1", groupBy: "id");
        var context = new SqlImportApplyContext(query, setup.CoreContext, setup.Report, CancellationToken.None);

        var step = new SqlImportHavingClauseApplier(setup.Canvas);

        SqlImportApplyResult result = step.Apply(context);

        Assert.Equal(1, result.Imported);
        NodeViewModel countNode = Assert.Single(setup.Canvas.Nodes.Where(n => n.Type == NodeType.CountDistinct));
        Assert.Equal("true", countNode.Parameters["distinct"]);
        Assert.Contains(setup.Canvas.Nodes, n => n.Type == NodeType.GreaterThan);
        Assert.Contains(setup.Canvas.Connections, c =>
            c.ToPin?.Owner == countNode
            && c.ToPin.Name.Equals("value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HavingClauseApplier_WithStringAggComparison_AddsStringAggNode()
    {
        var setup = CreateCoreContext();
        var query = CreateQuery(havingClause: "STRING_AGG(DISTINCT id, ',' ORDER BY id DESC) = '1,2'", groupBy: "id");
        var context = new SqlImportApplyContext(query, setup.CoreContext, setup.Report, CancellationToken.None);

        var step = new SqlImportHavingClauseApplier(setup.Canvas);

        SqlImportApplyResult result = step.Apply(context);

        Assert.Equal(1, result.Imported);
        NodeViewModel stringAggNode = Assert.Single(setup.Canvas.Nodes.Where(n => n.Type == NodeType.StringAgg));
        Assert.Equal("true", stringAggNode.Parameters["distinct"]);
        Assert.Equal(",", stringAggNode.Parameters["separator"]);
        Assert.Equal("true", stringAggNode.Parameters["order_1_desc"]);
        Assert.Contains(setup.Canvas.Nodes, n => n.Type == NodeType.Equals);
        Assert.Contains(setup.Canvas.Connections, c =>
            c.ToPin?.Owner == stringAggNode
            && c.ToPin.Name.Equals("value", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setup.Canvas.Connections, c =>
            c.ToPin?.Owner == stringAggNode
            && c.ToPin.Name.Equals("order_by", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResultModifiersApplier_WithLimitAndDistinct_ImportsBothModifiers()
    {
        var setup = CreateCoreContext();
        var query = CreateQuery(limit: 5, isDistinct: true);
        var context = new SqlImportApplyContext(query, setup.CoreContext, setup.Report, CancellationToken.None);

        var step = new SqlImportResultModifiersApplier(setup.Canvas);

        SqlImportApplyResult result = step.Apply(context);

        Assert.Equal(2, result.Imported);
        Assert.Equal("true", setup.CoreContext.ResultNode.Parameters["distinct"]);
        Assert.Contains(setup.Canvas.Nodes, n => n.Type == NodeType.Top);
    }

    private static (CanvasViewModel Canvas, ImportBuildContext CoreContext, ObservableCollection<ImportReportItem> Report) CreateCoreContext(
        IReadOnlyList<ImportFromPart>? fromParts = null,
        IReadOnlyList<ImportSelectTerm>? selectedColumns = null
    )
    {
        var canvas = new CanvasViewModel();
        var report = new ObservableCollection<ImportReportItem>();
        var builder = new ImportModelToCanvasBuilder(canvas);

        var buildInput = new ImportBuildInput(
            fromParts ?? [new ImportFromPart("public.orders", null, null, null)],
            selectedColumns ?? [new ImportSelectTerm("id", null)],
            IsStar: false,
            StarQualifier: null,
            SqlImportLayoutPolicy.Default
        );

        ImportBuildContext core = builder.BuildCore(buildInput, report, CancellationToken.None);
        return (canvas, core, report);
    }

    private static SqlImportParsedQuery CreateQuery(
        string? whereClause = null,
        string? orderBy = null,
        string? groupBy = null,
        string? havingClause = null,
        int? limit = null,
        bool isDistinct = false,
        IReadOnlyList<SqlImportSelectedColumn>? selectedColumns = null,
        IReadOnlyList<SqlImportSourcePart>? sourceParts = null
    ) =>
        new(
            isDistinct,
            IsStar: false,
            StarQualifier: null,
            selectedColumns ?? [new SqlImportSelectedColumn("id", null)],
            sourceParts ?? [new SqlImportSourcePart("public.orders", null, null, null)],
            whereClause,
            orderBy,
            groupBy,
            havingClause,
            limit,
            OuterAliases: [],
            SetOperations: []
        );
}
