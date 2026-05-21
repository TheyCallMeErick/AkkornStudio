using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using Xunit;
using System.Reflection;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class QueryTemplateLibraryWireGuardTests
{
    [Fact]
    public void Wire_WhenSourcePinIsMissing_ThrowsExplicitError()
    {
        var from = new NodeViewModel("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        var to = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(100, 0));

        var wireMethod = typeof(QueryTemplateLibrary).GetMethod(
            "Wire",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        Assert.NotNull(wireMethod);

        TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() =>
            wireMethod!.Invoke(null, [from, "missing_pin", to, "columns"])
        );

        InvalidOperationException inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("source pin 'missing_pin' not found", inner.Message, StringComparison.OrdinalIgnoreCase);
    }
}
