using System.Text.Json;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Serialization;

public sealed class CanvasSerializerEmbeddedSubgraphPayloadValidationTests
{
    [Fact]
    public void Deserialize_MalformedEmbeddedSubgraphPayloads_AreIgnoredWithWarnings()
    {
        var saved = new SavedCanvas(
            Version: CanvasSerializer.CurrentCanvasSchemaVersion,
            DatabaseProvider: "Postgres",
            ConnectionName: "test",
            Zoom: 1.0,
            PanX: 0,
            PanY: 0,
            Nodes:
            [
                new SavedNode(
                    NodeId: "cte-1",
                    NodeType: "CteDefinition",
                    X: 10,
                    Y: 10,
                    ZOrder: null,
                    Alias: null,
                    TableFullName: null,
                    Parameters: new Dictionary<string, string>
                    {
                        ["name"] = "orders_cte",
                        [CanvasSerializer.CteSubgraphParameterKey] = "{ invalid-cte-json",
                    },
                    PinLiterals: []
                ),
                new SavedNode(
                    NodeId: "subquery-1",
                    NodeType: "Subquery",
                    X: 40,
                    Y: 40,
                    ZOrder: null,
                    Alias: null,
                    TableFullName: null,
                    Parameters: new Dictionary<string, string>
                    {
                        [CanvasSerializer.SubquerySubgraphParameterKey] = "{ invalid-subquery-json",
                    },
                    PinLiterals: []
                ),
                new SavedNode(
                    NodeId: "view-1",
                    NodeType: "ViewDefinition",
                    X: 70,
                    Y: 70,
                    ZOrder: null,
                    Alias: null,
                    TableFullName: null,
                    Parameters: new Dictionary<string, string>
                    {
                        ["ViewName"] = "v_orders",
                        [CanvasSerializer.ViewSubgraphParameterKey] = "{ invalid-view-graph",
                        [CanvasSerializer.ViewEditorCanvasParameterKey] = "{ invalid-view-editor-canvas",
                    },
                    PinLiterals: []
                ),
            ],
            Connections: [],
            SelectBindings: [],
            WhereBindings: []
        );

        string json = JsonSerializer.Serialize(saved);
        var vm = new CanvasViewModel();

        CanvasLoadResult result = CanvasSerializer.Deserialize(json, vm);

        Assert.True(result.Success);
        Assert.NotNull(result.Warnings);
        Assert.Contains(result.Warnings!, warning => warning.Contains(CanvasSerializer.CteSubgraphParameterKey, StringComparison.Ordinal));
        Assert.Contains(result.Warnings!, warning => warning.Contains(CanvasSerializer.SubquerySubgraphParameterKey, StringComparison.Ordinal));
        Assert.Contains(result.Warnings!, warning => warning.Contains(CanvasSerializer.ViewSubgraphParameterKey, StringComparison.Ordinal));
        Assert.Contains(result.Warnings!, warning => warning.Contains(CanvasSerializer.ViewEditorCanvasParameterKey, StringComparison.Ordinal));

        NodeViewModel cte = Assert.Single(vm.Nodes.Where(node => node.Id == "cte-1"));
        NodeViewModel subquery = Assert.Single(vm.Nodes.Where(node => node.Id == "subquery-1"));
        NodeViewModel view = Assert.Single(vm.Nodes.Where(node => node.Id == "view-1"));

        Assert.False(cte.Parameters.ContainsKey(CanvasSerializer.CteSubgraphParameterKey));
        Assert.False(subquery.Parameters.ContainsKey(CanvasSerializer.SubquerySubgraphParameterKey));
        Assert.False(view.Parameters.ContainsKey(CanvasSerializer.ViewSubgraphParameterKey));
        Assert.False(view.Parameters.ContainsKey(CanvasSerializer.ViewEditorCanvasParameterKey));
    }
}
