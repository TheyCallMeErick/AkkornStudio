using AkkornStudio.UI.Controls;
using Xunit;
using System.Reflection;

namespace AkkornStudio.Tests.Unit.Controls;

public class InfiniteCanvasBindingTests
{
    [Fact]
    public void Constructor_CreatesExpectedVisualLayers()
    {
        var canvas = new InfiniteCanvas();

        Assert.Single(canvas.Children);
        Assert.IsType<InfiniteCanvasCoreControl>(canvas.Children[0]);
    }

    [Fact]
    public void InfiniteCanvas_HasNodeControlCacheFieldForFastLookup()
    {
        var cacheField = typeof(InfiniteCanvas).GetField(
            "_nodeControlCache",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );

        Assert.NotNull(cacheField);
        Assert.Contains("Dictionary", cacheField!.FieldType.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_ConfiguresWireLayer_AsNonHitTestable()
    {
        var canvas = new InfiniteCanvas();
        FieldInfo? wiresField = typeof(InfiniteCanvas).GetField(
            "_wires",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(wiresField);
        var wires = Assert.IsType<BezierWireLayer>(wiresField!.GetValue(canvas));
        Assert.False(wires.IsHitTestVisible);
    }
}
