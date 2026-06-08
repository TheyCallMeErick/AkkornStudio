using AkkornStudio.Nodes;
using AkkornStudio.Nodes.Pins;

namespace AkkornStudio.Tests.Unit.Nodes.Pins;

public sealed class PinCapabilityScalarCompatibilityTests
{
    [Fact]
    public void IntegerToDecimal_IsAllowedByNumericFamilyPolicy()
    {
        PinModel source = CreatePin("source", PinDirection.Output, PinDataType.Integer, NodeType.ValueNumber);
        PinModel destination = CreatePin("destination", PinDirection.Input, PinDataType.Decimal, NodeType.Sum);

        PinConnectionDecision decision = destination.CanConnect(source, PinConnectionContext.ValidationOnly());

        Assert.True(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.None, decision.ReasonCode);
    }

    [Fact]
    public void BooleanToNumber_IsRejectedAsScalarMismatch()
    {
        PinModel source = CreatePin("source", PinDirection.Output, PinDataType.Boolean, NodeType.ValueBoolean);
        PinModel destination = CreatePin("destination", PinDirection.Input, PinDataType.Number, NodeType.Sum);

        PinConnectionDecision decision = destination.CanConnect(source, PinConnectionContext.ValidationOnly());

        Assert.False(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.ScalarTypeMismatch, decision.ReasonCode);
    }

    [Theory]
    [InlineData(PinDataType.Decimal)]
    [InlineData(PinDataType.Number)]
    public void DecimalOrNumberToInteger_IsRejectedAsScalarMismatch(PinDataType sourceType)
    {
        PinModel source = CreatePin("source", PinDirection.Output, sourceType, NodeType.ValueNumber);
        PinModel destination = CreatePin("destination", PinDirection.Input, PinDataType.Integer, NodeType.Sum);

        PinConnectionDecision decision = destination.CanConnect(source, PinConnectionContext.ValidationOnly());

        Assert.False(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.ScalarTypeMismatch, decision.ReasonCode);
    }

    private static PinModel CreatePin(string pinName, PinDirection direction, PinDataType dataType, NodeType nodeType)
    {
        string nodeId = $"{nodeType}:{Guid.NewGuid():N}";
        var descriptor = new PinDescriptor(pinName, direction, dataType);
        var owner = new PinModelOwner(nodeId, nodeType);
        var pinId = new PinId($"{nodeId}:{pinName}:{direction}");
        return PinModelFactory.Create(pinId, descriptor, owner, dataType, null);
    }
}
