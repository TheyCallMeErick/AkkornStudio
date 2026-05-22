namespace AkkornStudio.Tests.Unit.Nodes;

public sealed class NodeGraphHardeningTests
{
    [Fact]
    public void NodeMap_RebuildsAfterObservableNodesMutation()
    {
        var first = CreateNode("n1");
        var second = CreateNode("n2");
        var nodes = new ObservableCollection<NodeInstance> { first };
        var graph = new NodeGraph { Nodes = nodes, Connections = [] };

        IReadOnlyDictionary<string, NodeInstance> initialMap = graph.NodeMap;
        Assert.True(initialMap.ContainsKey(first.Id));
        Assert.False(initialMap.ContainsKey(second.Id));

        nodes.Add(second);

        IReadOnlyDictionary<string, NodeInstance> updatedMap = graph.NodeMap;
        Assert.True(updatedMap.ContainsKey(second.Id));
        Assert.NotSame(initialMap, updatedMap);
    }

    [Fact]
    public void NodeMap_RebuildsAfterMutableListMutationWithoutNotifications()
    {
        var first = CreateNode("n1");
        var second = CreateNode("n2");
        var nodes = new List<NodeInstance> { first };
        var graph = new NodeGraph { Nodes = nodes, Connections = [] };

        IReadOnlyDictionary<string, NodeInstance> initialMap = graph.NodeMap;
        Assert.True(initialMap.ContainsKey(first.Id));
        Assert.False(initialMap.ContainsKey(second.Id));

        nodes.Add(second);

        IReadOnlyDictionary<string, NodeInstance> updatedMap = graph.NodeMap;
        Assert.True(updatedMap.ContainsKey(second.Id));
        Assert.NotSame(initialMap, updatedMap);
    }

    private static NodeInstance CreateNode(string id) =>
        new(
            id,
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );
}
