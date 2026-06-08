using System.Text.Json;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Serialization;

public class CanvasSerializerCteSubgraphPersistenceTests
{
    [Fact]
    public void Serialize_CteDefinition_WithQueryWire_PersistsCteSubgraph()
    {
        var vm = new CanvasViewModel();
        vm.Nodes.Clear();
        vm.Connections.Clear();

        NodeViewModel table = new("public.orders", [("id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel colList = new(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Point(120, 0));
        NodeViewModel innerResult = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(240, 0));
        NodeViewModel cteDef = new(NodeDefinitionRegistry.Get(NodeType.CteDefinition), new Point(360, 0));

        vm.Nodes.Add(table);
        vm.Nodes.Add(colList);
        vm.Nodes.Add(innerResult);
        vm.Nodes.Add(cteDef);

        Connect(vm, table, "id", colList, "columns");
        Connect(vm, colList, "result", innerResult, "columns");
        Connect(vm, innerResult, "result", cteDef, "query");

        string json = CanvasSerializer.Serialize(vm);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement cteNode = doc
            .RootElement
            .GetProperty("Nodes")
            .EnumerateArray()
            .First(n => n.GetProperty("NodeType").GetString() == nameof(NodeType.CteDefinition));

        Assert.True(cteNode.TryGetProperty("CteSubgraph", out JsonElement cteSubgraph));
        Assert.True(cteSubgraph.GetProperty("Nodes").GetArrayLength() >= 2);
        Assert.True(cteSubgraph.GetProperty("Connections").GetArrayLength() >= 1);
        Assert.False(string.IsNullOrWhiteSpace(cteSubgraph.GetProperty("ResultOutputNodeId").GetString()));
    }

    [Fact]
    public void Deserialize_CteDefinition_WithOnlyPersistedSubgraph_PersistsInsideNode()
    {
        var vm = new CanvasViewModel();

        string json =
            """
            {
              "Version": 3,
              "DatabaseProvider": "Postgres",
              "ConnectionName": "test",
              "Zoom": 1.0,
              "PanX": 0.0,
              "PanY": 0.0,
              "Nodes": [
                {
                  "NodeId": "cte1",
                  "NodeType": "CteDefinition",
                  "X": 100,
                  "Y": 120,
                  "Parameters": { "name": "persisted_cte" },
                  "PinLiterals": {},
                  "CteSubgraph": {
                    "Nodes": [
                      {
                        "NodeId": "inner_result_1",
                        "NodeType": "ResultOutput",
                        "X": 20,
                        "Y": 20,
                        "Parameters": {},
                        "PinLiterals": {}
                      }
                    ],
                    "Connections": [],
                    "ResultOutputNodeId": "inner_result_1"
                  }
                }
              ],
              "Connections": [],
              "SelectBindings": [],
              "WhereBindings": []
            }
            """;

        CanvasLoadResult result = CanvasSerializer.Deserialize(json, vm);

        Assert.True(result.Success);
        NodeViewModel cte = Assert.Single(vm.Nodes);
        Assert.Equal(NodeType.CteDefinition, cte.Type);
        Assert.Empty(vm.Connections);
        Assert.True(cte.Parameters.ContainsKey(CanvasSerializer.CteSubgraphParameterKey));
    }

    [Fact]
    public void Deserialize_CteDefinition_WithNestedSubgraphDepthAboveLimit_RemovesPayloadAndWarns()
    {
        var vm = new CanvasViewModel();
        SavedCteSubgraph nested = BuildNestedCteSubgraph(CanvasSerializer.MaxCteSubgraphDepth + 1);

        var saved = new SavedCanvas(
            Version: CanvasSerializer.CurrentCanvasSchemaVersion,
            DatabaseProvider: "Postgres",
            ConnectionName: "test",
            Zoom: 1,
            PanX: 0,
            PanY: 0,
            Nodes:
            [
                new SavedNode(
                    NodeId: "cte_root",
                    NodeType: nameof(NodeType.CteDefinition),
                    X: 100,
                    Y: 120,
                    ZOrder: null,
                    Alias: null,
                    TableFullName: null,
                    Parameters: new Dictionary<string, string>(),
                    PinLiterals: new Dictionary<string, string>(),
                    CteSubgraph: nested)
            ],
            Connections: [],
            SelectBindings: [],
            WhereBindings: []);

        string json = JsonSerializer.Serialize(saved);
        CanvasLoadResult result = CanvasSerializer.Deserialize(json, vm);

        Assert.True(result.Success, result.Error);
        NodeViewModel cte = Assert.Single(vm.Nodes);
        Assert.False(cte.Parameters.ContainsKey(CanvasSerializer.CteSubgraphParameterKey));
        Assert.Contains(result.Warnings ?? [], warning =>
            warning.Contains("depth", StringComparison.OrdinalIgnoreCase));
    }

    private static void Connect(
        CanvasViewModel canvas,
        NodeViewModel fromNode,
        string fromPin,
        NodeViewModel toNode,
        string toPin)
    {
        PinViewModel from = fromNode.OutputPins.First(p => p.Name == fromPin);
        PinViewModel to = toNode.InputPins.First(p => p.Name == toPin);

        canvas.Connections.Add(new ConnectionViewModel(from, from.AbsolutePosition, to.AbsolutePosition)
        {
            ToPin = to,
        });
    }

    private static SavedCteSubgraph BuildNestedCteSubgraph(int depth)
    {
        SavedCteSubgraph? current = null;
        for (int level = depth; level >= 1; level--)
        {
            string cteNodeId = $"cte_level_{level}";
            string resultNodeId = $"result_level_{level}";

            var resultNode = new SavedNode(
                NodeId: resultNodeId,
                NodeType: nameof(NodeType.ResultOutput),
                X: level * 10,
                Y: level * 10,
                ZOrder: null,
                Alias: null,
                TableFullName: null,
                Parameters: new Dictionary<string, string>(),
                PinLiterals: new Dictionary<string, string>());

            var cteNode = new SavedNode(
                NodeId: cteNodeId,
                NodeType: nameof(NodeType.CteDefinition),
                X: level * 10 + 1,
                Y: level * 10 + 1,
                ZOrder: null,
                Alias: null,
                TableFullName: null,
                Parameters: new Dictionary<string, string>(),
                PinLiterals: new Dictionary<string, string>(),
                CteSubgraph: current);

            current = new SavedCteSubgraph(
                Nodes: [cteNode, resultNode],
                Connections: [],
                ResultOutputNodeId: resultNodeId);
        }

        return current ?? new SavedCteSubgraph([], [], null);
    }
}
