
using Avalonia;
using AkkornStudio.Core;
using AkkornStudio.Nodes;
using AkkornStudio.Registry;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.Services.QueryPreview.Models;
using AkkornStudio.UI.Services.QueryPreview;
using static AkkornStudio.Tests.Unit.ViewModels.QueryPreview.QueryPreviewTestNodeFactory;

namespace AkkornStudio.Tests.Unit.ViewModels.QueryPreview;
public class QueryGraphBuilderDiagnosticsTests
{
    [Fact]
    public void BuildSqlWithDiagnostics_PreservesLegacyMessages()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel orders = Table("orders", "id");
        NodeViewModel columnList = Node(NodeType.ColumnList);
        NodeViewModel result = Node(NodeType.ResultOutput);
        result.Parameters["query_hints"] = "DROP TABLE x";

        Connect(canvas, orders, "id", columnList, "columns");
        Connect(canvas, columnList, "result", result, "columns");

        canvas.Nodes.Add(orders);
        canvas.Nodes.Add(columnList);
        canvas.Nodes.Add(result);

        var sut = new QueryGraphBuilder(canvas, DatabaseProvider.SQLite);

        (string sqlWithLegacy, List<string> errors) = sut.BuildSql();
        QuerySqlBuildResult diagnosticResult = sut.BuildSqlWithDiagnostics();
        string sqlWithDiagnostics = diagnosticResult.PreviewSql;
        List<PreviewDiagnostic> diagnostics = diagnosticResult.Diagnostics;

        Assert.Equal(sqlWithLegacy, sqlWithDiagnostics);
        Assert.Equal(errors, diagnostics.Select(d => d.Message).ToList());
        Assert.Contains(diagnostics, d => d.Category == PreviewDiagnosticCategory.General);
        Assert.Contains(diagnostics, d => d.Code.EndsWith("GEN-001", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSqlWithDiagnostics_AliasAmbiguity_ContainsNodeContext()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel ordersA = Table("orders", "id");
        NodeViewModel ordersB = Table("orders_archive", "id");
        ordersA.Alias = "o";
        ordersB.Alias = "o";

        NodeViewModel columnList = Node(NodeType.ColumnList);
        NodeViewModel result = Node(NodeType.ResultOutput);

        Connect(canvas, ordersA, "id", columnList, "columns");
        Connect(canvas, ordersB, "id", columnList, "columns");
        Connect(canvas, columnList, "result", result, "columns");

        canvas.Nodes.Add(ordersA);
        canvas.Nodes.Add(ordersB);
        canvas.Nodes.Add(columnList);
        canvas.Nodes.Add(result);

        var sut = new QueryGraphBuilder(canvas, DatabaseProvider.Postgres);

        List<PreviewDiagnostic> diagnostics = sut.BuildSqlWithDiagnostics().Diagnostics;

        PreviewDiagnostic ambiguity = Assert.Single(
            diagnostics,
            d => d.Message.Contains("alias ambiguity", StringComparison.OrdinalIgnoreCase)
        );

        Assert.False(string.IsNullOrWhiteSpace(ambiguity.NodeId));
        Assert.Contains(ambiguity.NodeId, new[] { ordersA.Id, ordersB.Id });
    }

    [Fact]
    public void BuildSqlWithDiagnostics_WhenOutputHasNoReachableSource_AddsReachabilityDiagnostic()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel unconnectedTable = Table("public.orders", "id");
        NodeViewModel literal = Node(NodeType.ValueNumber);
        literal.Parameters["value"] = "1";
        NodeViewModel result = Node(NodeType.ResultOutput);

        Connect(canvas, literal, "result", result, "column");

        canvas.Nodes.Add(unconnectedTable);
        canvas.Nodes.Add(literal);
        canvas.Nodes.Add(result);

        var sut = new QueryGraphBuilder(canvas, DatabaseProvider.Postgres);

        List<PreviewDiagnostic> diagnostics = sut.BuildSqlWithDiagnostics().Diagnostics;

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("reachable dataset source", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildSqlWithDiagnostics_WhenMultipleSourcesWithoutJoin_AddsSourceConflictDiagnostic()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel orders = Table("public.orders", "id");
        NodeViewModel customers = Table("public.customers", "id");
        NodeViewModel columnList = Node(NodeType.ColumnList);
        NodeViewModel result = Node(NodeType.ResultOutput);

        Connect(canvas, orders, "id", columnList, "columns");
        Connect(canvas, customers, "id", columnList, "columns");
        Connect(canvas, columnList, "result", result, "columns");

        canvas.Nodes.Add(orders);
        canvas.Nodes.Add(customers);
        canvas.Nodes.Add(columnList);
        canvas.Nodes.Add(result);

        var sut = new QueryGraphBuilder(canvas, DatabaseProvider.Postgres);

        List<PreviewDiagnostic> diagnostics = sut.BuildSqlWithDiagnostics().Diagnostics;

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("no resolvable JOIN path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildSqlWithDiagnostics_WhenMultipleSourcesWithExplicitJoin_DoesNotAddSourceConflictDiagnostic()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel orders = Table("public.orders", "id", "customer_id");
        NodeViewModel customers = Table("public.customers", "id");
        NodeViewModel join = Node(NodeType.Join);
        NodeViewModel eq = Node(NodeType.Equals);
        NodeViewModel columnList = Node(NodeType.ColumnList);
        NodeViewModel result = Node(NodeType.ResultOutput);

        Connect(canvas, orders, "customer_id", eq, "left");
        Connect(canvas, customers, "id", eq, "right");
        Connect(canvas, eq, "result", join, "condition");
        Connect(canvas, orders, "customer_id", join, "left");
        Connect(canvas, customers, "id", join, "right");
        Connect(canvas, orders, "id", columnList, "columns");
        Connect(canvas, customers, "id", columnList, "columns");
        Connect(canvas, columnList, "result", result, "columns");

        canvas.Nodes.Add(orders);
        canvas.Nodes.Add(customers);
        canvas.Nodes.Add(join);
        canvas.Nodes.Add(eq);
        canvas.Nodes.Add(columnList);
        canvas.Nodes.Add(result);

        var sut = new QueryGraphBuilder(canvas, DatabaseProvider.Postgres);

        List<PreviewDiagnostic> diagnostics = sut.BuildSqlWithDiagnostics().Diagnostics;

        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("no resolvable JOIN path", StringComparison.OrdinalIgnoreCase));
    }
}


