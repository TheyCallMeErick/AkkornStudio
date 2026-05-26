using System.Reflection;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels;

public sealed class CanvasSubEditorAtomicParameterPersistenceTests
{
    [Fact]
    public void UpdateNodeParametersAtomically_WhenMutationThrows_RollsBackToOriginalState()
    {
        NodeViewModel node = new(NodeDefinitionRegistry.Get(NodeType.ViewDefinition), new Point(0, 0));
        node.Parameters["ViewName"] = "v_orders";
        node.Parameters["SelectSql"] = "SELECT 1";

        Dictionary<string, string> before = node.Parameters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            node.Parameters.Comparer);

        Type controllerType = typeof(CanvasViewModel).Assembly
            .GetType("AkkornStudio.UI.ViewModels.SubCanvasEditingController")
            ?? throw new InvalidOperationException("SubCanvasEditingController type not found.");

        MethodInfo method = controllerType.GetMethod(
                "UpdateNodeParametersAtomically",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("UpdateNodeParametersAtomically method not found.");

        const string transientKey = "__atomic_test_transient__";
        Action<Dictionary<string, string>> mutate = updated =>
        {
            updated[transientKey] = "{\"Nodes\":[]}";
            throw new InvalidOperationException("simulated persistence failure");
        };

        TargetInvocationException ex = Assert.Throws<TargetInvocationException>(
            () => method.Invoke(null, [node, mutate]));
        Assert.IsType<InvalidOperationException>(ex.InnerException);

        Assert.Equal(before.Count, node.Parameters.Count);
        foreach ((string key, string value) in before)
        {
            Assert.True(node.Parameters.TryGetValue(key, out string? restored));
            Assert.Equal(value, restored);
        }

        Assert.False(node.Parameters.ContainsKey(transientKey));
    }
}
