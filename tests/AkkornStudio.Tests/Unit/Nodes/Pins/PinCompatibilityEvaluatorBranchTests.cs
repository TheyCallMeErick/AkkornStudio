using AkkornStudio.Nodes;
using AkkornStudio.Nodes.Pins;

namespace AkkornStudio.Tests.Unit.Nodes.Pins;

public sealed class PinCompatibilityEvaluatorBranchTests
{
    [Fact]
    public void CanConnect_RejectsSelfConnection()
    {
        PinModel pin = CreatePin(
            "node-1",
            "value",
            PinDirection.Output,
            PinDataType.Number,
            NodeType.ValueNumber);

        PinConnectionDecision decision = pin.CanConnect(pin, PinConnectionContext.ValidationOnly());

        Assert.False(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.SelfConnectionForbidden, decision.ReasonCode);
    }

    [Fact]
    public void CanConnect_RejectsSameNodeConnection()
    {
        PinModel output = CreatePin("node-1", "a", PinDirection.Output, PinDataType.Number, NodeType.ValueNumber);
        PinModel input = CreatePin("node-1", "b", PinDirection.Input, PinDataType.Number, NodeType.Sum);

        PinConnectionDecision decision = input.CanConnect(output, PinConnectionContext.ValidationOnly());

        Assert.False(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.SameNodeConnectionForbidden, decision.ReasonCode);
    }

    [Fact]
    public void CanConnect_RejectsSameDirection()
    {
        PinModel left = CreatePin("node-1", "left", PinDirection.Output, PinDataType.Number, NodeType.ValueNumber);
        PinModel right = CreatePin("node-2", "right", PinDirection.Output, PinDataType.Number, NodeType.ValueNumber);

        PinConnectionDecision decision = left.CanConnect(right, PinConnectionContext.ValidationOnly());

        Assert.False(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.SameDirectionForbidden, decision.ReasonCode);
    }

    [Fact]
    public void CanConnect_AcceptsWildcardProjectionViaCustomContextPolicy()
    {
        PinModel source = CreatePin("table-1", "*", PinDirection.Output, PinDataType.ColumnSet, NodeType.TableSource);
        PinModel destination = CreatePin("node-2", "custom_projection_pin", PinDirection.Input, PinDataType.ColumnRef, NodeType.Sum);

        var context = new PinConnectionContext(
            new PinConnectionContextData(
                ExistingConnections: [],
                ConnectionsByPin: new Dictionary<PinId, PinConnectionSnapshot[]>(),
                IsValidationOnly: true,
                AllowImplicitReplacement: false,
                ComparisonState: null,
                WildcardContext: new WildcardProjectionContext(
                    IsEnabled: true,
                    AllowedDestinationNodeTypes: new HashSet<NodeType> { NodeType.Sum },
                    AllowedDestinationPinNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "custom_projection_pin" })),
            new Dictionary<string, object?>());

        PinConnectionDecision decision = destination.CanConnect(source, context);

        Assert.True(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.WildcardProjectionOnly, decision.ReasonCode);
    }

    [Fact]
    public void CanConnect_WhenSameSourceIsAlreadyConnectedToDestination_RejectsDuplicateConnection()
    {
        PinModel source = CreatePin("source-1", "value", PinDirection.Output, PinDataType.Number, NodeType.ValueNumber);
        PinModel destination = CreatePin("node-2", "value", PinDirection.Input, PinDataType.Number, NodeType.Sum);

        var existing = new PinConnectionSnapshot(
            ConnectionId: "wire-1",
            SourcePinId: source.PinId,
            DestinationPinId: destination.PinId,
            SourceNodeId: source.Owner.NodeId,
            DestinationNodeId: destination.Owner.NodeId,
            SourcePinName: source.Name,
            DestinationPinName: destination.Name,
            SourceEffectiveDataType: source.EffectiveDataType,
            DestinationEffectiveDataType: destination.EffectiveDataType,
            SourceResolvedScalarType: PinDataType.Number);

        var context = new PinConnectionContext(
            new PinConnectionContextData(
                ExistingConnections: [existing],
                ConnectionsByPin: new Dictionary<PinId, PinConnectionSnapshot[]>
                {
                    [destination.PinId] = [existing],
                },
                IsValidationOnly: true,
                AllowImplicitReplacement: true,
                ComparisonState: null,
                WildcardContext: null),
            new Dictionary<string, object?>());

        PinConnectionDecision decision = destination.CanConnect(source, context);

        Assert.False(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.MultiplicityExceeded, decision.ReasonCode);
    }

    [Fact]
    public void CanConnect_IsNullComparison_ProducesConcretizeMutation()
    {
        PinModel source = CreatePin("source-1", "result", PinDirection.Output, PinDataType.Number, NodeType.ValueNumber);
        PinModel destination = CreatePin("cmp-1", "value", PinDirection.Input, PinDataType.ColumnRef, NodeType.IsNull);

        PinConnectionDecision decision = destination.CanConnect(source, PinConnectionContext.ValidationOnly());

        Assert.True(decision.IsAllowed);
        ConcretizeComparisonScalarMutation mutation = Assert.IsType<ConcretizeComparisonScalarMutation>(
            Assert.Single(decision.Mutations));
        Assert.Equal("cmp-1", mutation.NodeId);
        Assert.Equal(PinDataType.Number, mutation.ScalarType);
    }

    [Fact]
    public void CanConnect_WithPartialWildcardContext_DoesNotThrowNullReference()
    {
        PinModel source = CreatePin("table-1", "*", PinDirection.Output, PinDataType.ColumnSet, NodeType.TableSource);
        PinModel destination = CreatePin("node-2", "custom_projection_pin", PinDirection.Input, PinDataType.ColumnRef, NodeType.Sum);

        var context = new PinConnectionContext(
            new PinConnectionContextData(
                ExistingConnections: [],
                ConnectionsByPin: new Dictionary<PinId, PinConnectionSnapshot[]>(),
                IsValidationOnly: true,
                AllowImplicitReplacement: false,
                ComparisonState: null,
                WildcardContext: new WildcardProjectionContext(
                    IsEnabled: true,
                    AllowedDestinationNodeTypes: null!,
                    AllowedDestinationPinNames: null!)),
            new Dictionary<string, object?>());

        PinConnectionDecision decision = destination.CanConnect(source, context);

        Assert.False(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.ScalarTypeMismatch, decision.ReasonCode);
    }

    [Fact]
    public void CanConnect_DestinationAllowMultiple_DoesNotAllowDuplicateSourceConnection()
    {
        PinModel source = CreatePin("source-1", "value", PinDirection.Output, PinDataType.Number, NodeType.ValueNumber);
        PinModel destination = CreatePin("node-2", "value", PinDirection.Input, PinDataType.Number, NodeType.Sum, allowMultiple: true);

        var existing = new PinConnectionSnapshot(
            ConnectionId: "wire-1",
            SourcePinId: source.PinId,
            DestinationPinId: destination.PinId,
            SourceNodeId: source.Owner.NodeId,
            DestinationNodeId: destination.Owner.NodeId,
            SourcePinName: source.Name,
            DestinationPinName: destination.Name,
            SourceEffectiveDataType: source.EffectiveDataType,
            DestinationEffectiveDataType: destination.EffectiveDataType,
            SourceResolvedScalarType: PinDataType.Number);

        var context = new PinConnectionContext(
            new PinConnectionContextData(
                ExistingConnections: [existing],
                ConnectionsByPin: new Dictionary<PinId, PinConnectionSnapshot[]>
                {
                    [destination.PinId] = [existing],
                },
                IsValidationOnly: true,
                AllowImplicitReplacement: true,
                ComparisonState: null,
                WildcardContext: null),
            new Dictionary<string, object?>());

        PinConnectionDecision decision = destination.CanConnect(source, context);

        Assert.False(decision.IsAllowed);
        Assert.Equal(PinConnectionReasonCode.MultiplicityExceeded, decision.ReasonCode);
    }

    [Fact]
    public void CanConnect_WildcardPruneMutation_IsScopedToTargetDestinationPin()
    {
        PinModel source = CreatePin("table-1", "*", PinDirection.Output, PinDataType.ColumnSet, NodeType.TableSource);
        PinModel otherSourceColumn = CreatePin("table-1", "order_id", PinDirection.Output, PinDataType.ColumnRef, NodeType.TableSource);
        PinModel destinationColumns = CreatePin("node-2", "columns", PinDirection.Input, PinDataType.ColumnRef, NodeType.ColumnList);
        PinModel destinationMetadata = CreatePin("node-2", "metadata", PinDirection.Input, PinDataType.ColumnRef, NodeType.ColumnList);

        var redundantSameDestinationPin = new PinConnectionSnapshot(
            ConnectionId: "wire-columns",
            SourcePinId: otherSourceColumn.PinId,
            DestinationPinId: destinationColumns.PinId,
            SourceNodeId: otherSourceColumn.Owner.NodeId,
            DestinationNodeId: destinationColumns.Owner.NodeId,
            SourcePinName: otherSourceColumn.Name,
            DestinationPinName: destinationColumns.Name,
            SourceEffectiveDataType: otherSourceColumn.EffectiveDataType,
            DestinationEffectiveDataType: destinationColumns.EffectiveDataType,
            SourceResolvedScalarType: PinDataType.Integer);

        var differentDestinationPin = new PinConnectionSnapshot(
            ConnectionId: "wire-metadata",
            SourcePinId: otherSourceColumn.PinId,
            DestinationPinId: destinationMetadata.PinId,
            SourceNodeId: otherSourceColumn.Owner.NodeId,
            DestinationNodeId: destinationMetadata.Owner.NodeId,
            SourcePinName: otherSourceColumn.Name,
            DestinationPinName: destinationMetadata.Name,
            SourceEffectiveDataType: otherSourceColumn.EffectiveDataType,
            DestinationEffectiveDataType: destinationMetadata.EffectiveDataType,
            SourceResolvedScalarType: PinDataType.Integer);

        var context = new PinConnectionContext(
            new PinConnectionContextData(
                ExistingConnections: [redundantSameDestinationPin, differentDestinationPin],
                ConnectionsByPin: new Dictionary<PinId, PinConnectionSnapshot[]>(),
                IsValidationOnly: true,
                AllowImplicitReplacement: false,
                ComparisonState: null,
                WildcardContext: null),
            new Dictionary<string, object?>());

        PinConnectionDecision decision = destinationColumns.CanConnect(source, context);

        Assert.True(decision.IsAllowed);
        PruneConnectionsMutation mutation = Assert.IsType<PruneConnectionsMutation>(
            Assert.Single(decision.Mutations.OfType<PruneConnectionsMutation>()));
        Assert.Equal(["wire-columns"], mutation.PrunedConnectionIds);
    }

    private static PinModel CreatePin(
        string nodeId,
        string pinName,
        PinDirection direction,
        PinDataType dataType,
        NodeType nodeType,
        bool allowMultiple = false)
    {
        var descriptor = new PinDescriptor(pinName, direction, dataType, AllowMultiple: allowMultiple);
        var owner = new PinModelOwner(nodeId, nodeType);
        var pinId = new PinId($"{nodeId}:{pinName}:{direction}");
        return PinModelFactory.Create(pinId, descriptor, owner, dataType, null);
    }
}
