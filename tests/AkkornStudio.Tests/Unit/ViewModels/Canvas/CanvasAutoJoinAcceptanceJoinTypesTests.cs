using AkkornStudio.UI.Services.Canvas.AutoJoin;
using AkkornStudio.UI.Services.Explain;
using System.Reflection;
using Avalonia;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class CanvasAutoJoinAcceptanceJoinTypesTests
{
    [Theory]
    [InlineData("INNER")]
    [InlineData("LEFT")]
    [InlineData("RIGHT")]
    [InlineData("FULL")]
    [InlineData("CROSS")]
    public void OnJoinAccepted_CreatesJoinNode_AndWiresPins_ForAllJoinTypes(string joinType)
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel orders = canvas.SpawnTableNode(
            "public.orders",
            [("id", PinDataType.Number), ("customer_id", PinDataType.Number)],
            new Point(80, 80)
        );
        NodeViewModel customers = canvas.SpawnTableNode(
            "public.customers",
            [("id", PinDataType.Number)],
            new Point(320, 180)
        );

        var suggestion = new JoinSuggestion(
            ExistingTable: "public.orders",
            NewTable: "public.customers",
            JoinType: joinType,
            LeftColumn: "public.orders.customer_id",
            RightColumn: "public.customers.id",
            OnClause: "public.orders.customer_id = public.customers.id",
            Score: 0.99,
            Confidence: JoinConfidence.CatalogDefinedFk,
            Rationale: "test"
        );

        InvokeJoinAccepted(canvas, suggestion);

        NodeViewModel joinNode = Assert.Single(canvas.Nodes, n => n.Type == NodeType.Join);
        Assert.True(joinNode.Parameters.TryGetValue("join_type", out string? actualJoinType));
        Assert.Equal(joinType, actualJoinType);

        ConnectionViewModel leftConn = Assert.Single(canvas.Connections,
            c => c.ToPin?.Owner == joinNode && c.ToPin.Name == "left");
        ConnectionViewModel rightConn = Assert.Single(canvas.Connections,
            c => c.ToPin?.Owner == joinNode && c.ToPin.Name == "right");

        Assert.Equal(orders, leftConn.FromPin.Owner);
        Assert.Equal("customer_id", leftConn.FromPin.Name);
        Assert.Equal(customers, rightConn.FromPin.Owner);
        Assert.Equal("id", rightConn.FromPin.Name);
    }

    [Fact]
    public void OnJoinAccepted_DoesNotCreateDuplicateJoin_ForSamePair()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        canvas.SpawnTableNode(
            "public.orders",
            [("id", PinDataType.Number), ("customer_id", PinDataType.Number)],
            new Point(80, 80)
        );
        canvas.SpawnTableNode(
            "public.customers",
            [("id", PinDataType.Number)],
            new Point(320, 180)
        );

        var suggestion = new JoinSuggestion(
            ExistingTable: "public.orders",
            NewTable: "public.customers",
            JoinType: "INNER",
            LeftColumn: "public.orders.customer_id",
            RightColumn: "public.customers.id",
            OnClause: "public.orders.customer_id = public.customers.id",
            Score: 0.99,
            Confidence: JoinConfidence.CatalogDefinedFk,
            Rationale: "test"
        );

        InvokeJoinAccepted(canvas, suggestion);
        InvokeJoinAccepted(canvas, suggestion);

        Assert.Single(canvas.Nodes, n => n.Type == NodeType.Join);
    }

    [Fact]
    public void OnJoinAccepted_WhenSuggestionAnchorBecomesStale_DoesNotApplyJoin()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        canvas.SpawnTableNode(
            "public.orders",
            [("id", PinDataType.Number), ("customer_id", PinDataType.Number)],
            new Point(80, 80)
        );
        NodeViewModel originalCustomers = canvas.SpawnTableNode(
            "public.customers",
            [("id", PinDataType.Number)],
            new Point(320, 180)
        );

        var suggestion = new JoinSuggestion(
            ExistingTable: "public.orders",
            NewTable: "public.customers",
            JoinType: "INNER",
            LeftColumn: "public.orders.customer_id",
            RightColumn: "public.customers.id",
            OnClause: "public.orders.customer_id = public.customers.id",
            Score: 0.99,
            Confidence: JoinConfidence.CatalogDefinedFk,
            Rationale: "test"
        );

        InvokeShowSuggestions(canvas, [suggestion]);

        canvas.Nodes.Remove(originalCustomers);
        canvas.SpawnTableNode(
            "public.customers",
            [("id", PinDataType.Number)],
            new Point(420, 220)
        );

        InvokeJoinAccepted(canvas, suggestion);

        Assert.DoesNotContain(canvas.Nodes, n => n.Type == NodeType.Join);
        Assert.Empty(canvas.Connections);
    }

    private static void InvokeJoinAccepted(CanvasViewModel canvas, JoinSuggestion suggestion)
    {
        FieldInfo controllerField = typeof(CanvasViewModel)
            .GetField("_autoJoinController", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? controller = controllerField.GetValue(canvas);
        Assert.NotNull(controller);

        MethodInfo method = controller.GetType()
            .GetMethod("ApplyJoinSuggestion", BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(controller, [suggestion]);
    }

    private static void InvokeShowSuggestions(CanvasViewModel canvas, IReadOnlyList<JoinSuggestion> suggestions)
    {
        FieldInfo controllerField = typeof(CanvasViewModel)
            .GetField("_autoJoinController", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? controller = controllerField.GetValue(canvas);
        Assert.NotNull(controller);

        MethodInfo method = controller.GetType()
            .GetMethod("ShowAutoJoinSuggestionsMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(controller, [suggestions]);
    }
}

