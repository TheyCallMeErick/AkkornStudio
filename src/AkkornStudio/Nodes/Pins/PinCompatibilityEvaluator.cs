using AkkornStudio.Nodes.PinTypes;

namespace AkkornStudio.Nodes.Pins;

public static class PinCompatibilityEvaluator
{
    public static PinConnectionDecision Evaluate(
        PinModel self,
        PinModel other,
        PinConnectionContext context)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(context);

        if (self.PinId == other.PinId)
            return PinConnectionDecision.Rejected(PinConnectionReasonCode.SelfConnectionForbidden);

        if (self.Owner.NodeId == other.Owner.NodeId)
            return PinConnectionDecision.Rejected(PinConnectionReasonCode.SameNodeConnectionForbidden);

        if (self.Direction == other.Direction)
            return PinConnectionDecision.Rejected(PinConnectionReasonCode.SameDirectionForbidden);

        PinModel source = self.Direction == PinDirection.Output ? self : other;
        PinModel destination = self.Direction == PinDirection.Output ? other : self;

        if (!PinTypeRegistry.GetType(destination.EffectiveDataType).CanReceiveFrom(PinTypeRegistry.GetType(source.EffectiveDataType)))
            return PinConnectionDecision.Rejected(PinConnectionReasonCode.DomainFamilyMismatch);

        if (IsStructuralMismatch(source.EffectiveDataType, destination.EffectiveDataType))
            return PinConnectionDecision.Rejected(PinConnectionReasonCode.StructuralTypeMismatch);

        bool isWildcardProjection = IsWildcardProjectionToColumnInput(source, destination, context.Data.WildcardContext);
        if (isWildcardProjection)
            return BuildAllowedDecision(
                source,
                destination,
                context,
                PinConnectionReasonCode.WildcardProjectionOnly);

        if (source.EffectiveDataType == PinDataType.ColumnRef && destination.EffectiveDataType == PinDataType.ColumnRef)
            return EvaluateColumnRefToColumnRef(source, destination, context);

        if (source.EffectiveDataType == PinDataType.ColumnRef && (destination.EffectiveDataType.IsScalar() || destination.EffectiveDataType == PinDataType.Expression))
            return BuildAllowedDecision(source, destination, context);

        if (destination.EffectiveDataType == PinDataType.ColumnRef && (source.EffectiveDataType.IsScalar() || source.EffectiveDataType == PinDataType.Expression))
            return BuildAllowedDecision(source, destination, context);

        if (source.EffectiveDataType == PinDataType.Expression && destination.EffectiveDataType.IsScalar())
            return BuildAllowedDecision(source, destination, context);

        if (destination.EffectiveDataType == PinDataType.Expression && source.EffectiveDataType.IsScalar())
            return BuildAllowedDecision(source, destination, context);

        if (source.EffectiveDataType.IsNumericScalar() && destination.EffectiveDataType.IsNumericScalar())
            return EvaluateNumericScalarCompatibility(source, destination, context);

        if (source.EffectiveDataType != destination.EffectiveDataType)
            return PinConnectionDecision.Rejected(PinConnectionReasonCode.ScalarTypeMismatch);

        return BuildAllowedDecision(source, destination, context);
    }

    private static PinConnectionDecision EvaluateColumnRefToColumnRef(
        PinModel source,
        PinModel destination,
        PinConnectionContext context)
    {
        PinDataType? sourceScalar = ResolveColumnScalarType(source);
        PinDataType? destinationScalar = ResolveColumnScalarType(destination);
        if (sourceScalar is not null && destinationScalar is not null && sourceScalar != destinationScalar)
            return PinConnectionDecision.Rejected(PinConnectionReasonCode.ComparisonTypeMismatch);

        return BuildAllowedDecision(source, destination, context);
    }

    private static PinConnectionDecision BuildAllowedDecision(
        PinModel source,
        PinModel destination,
        PinConnectionContext context,
        PinConnectionReasonCode successReasonCode = PinConnectionReasonCode.None)
    {
        var mutations = new List<IPinMutation>();

        PinConnectionDecision multiplicityDecision = EvaluateMultiplicity(
            source,
            destination,
            context,
            successReasonCode);
        if (!multiplicityDecision.IsAllowed)
            return multiplicityDecision;

        mutations.AddRange(multiplicityDecision.Mutations);
        mutations.AddRange(BuildDomainMutations(source, destination, context));

        return PinConnectionDecision.Allowed(
            successReasonCode,
            mutations: mutations);
    }

    private static PinConnectionDecision EvaluateMultiplicity(
        PinModel source,
        PinModel destination,
        PinConnectionContext context,
        PinConnectionReasonCode successReasonCode)
    {
        if (destination.AllowMultiple)
            return PinConnectionDecision.Allowed(successReasonCode);

        if (!context.Data.ConnectionsByPin.TryGetValue(destination.PinId, out PinConnectionSnapshot[]? existing)
            || existing.Length == 0)
        {
            return PinConnectionDecision.Allowed(successReasonCode);
        }

        string[] conflictingConnectionIds = existing
            .Where(snapshot => snapshot.SourcePinId != source.PinId)
            .Select(snapshot => snapshot.ConnectionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (conflictingConnectionIds.Length == 0)
            return PinConnectionDecision.Allowed(successReasonCode);

        if (!context.Data.AllowImplicitReplacement)
            return PinConnectionDecision.Rejected(PinConnectionReasonCode.MultiplicityExceeded);

        var mutation = new ReplaceExistingConnectionMutation(destination.PinId, conflictingConnectionIds);
        return PinConnectionDecision.Allowed(
            successReasonCode,
            mutations: [mutation]);
    }

    private static IReadOnlyList<IPinMutation> BuildDomainMutations(
        PinModel source,
        PinModel destination,
        PinConnectionContext context)
    {
        var mutations = new List<IPinMutation>();

        PruneConnectionsMutation? pruneMutation = BuildWildcardPruneMutation(source, destination, context);
        if (pruneMutation is not null)
            mutations.Add(pruneMutation);

        ConcretizeComparisonScalarMutation? concretizeMutation = BuildComparisonConcretizationMutation(source, destination);
        if (concretizeMutation is not null)
            mutations.Add(concretizeMutation);

        return mutations;
    }

    private static PruneConnectionsMutation? BuildWildcardPruneMutation(
        PinModel source,
        PinModel destination,
        PinConnectionContext context)
    {
        if (!IsWildcardProjectionToColumnInput(source, destination, context.Data.WildcardContext))
            return null;

        string[] redundantConnectionIds = context.Data.ExistingConnections
            .Where(snapshot =>
                snapshot.SourceNodeId == source.Owner.NodeId
                && snapshot.DestinationNodeId == destination.Owner.NodeId
                && IsProjectionDestinationPinName(snapshot.DestinationPinName)
                && snapshot.SourcePinId != source.PinId)
            .Select(snapshot => snapshot.ConnectionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (redundantConnectionIds.Length == 0)
            return null;

        return new PruneConnectionsMutation(
            redundantConnectionIds,
            PruneReason: "Wildcard projection supersedes same-source explicit projection connections");
    }

    private static ConcretizeComparisonScalarMutation? BuildComparisonConcretizationMutation(
        PinModel source,
        PinModel destination)
    {
        if (destination.Direction != PinDirection.Input
            || destination.EffectiveDataType != PinDataType.ColumnRef
            || !IsComparisonNode(destination.Owner.NodeType))
        {
            return null;
        }

        PinDataType? scalarType = ResolveSourceScalarType(source);
        if (scalarType is null)
            return null;

        return new ConcretizeComparisonScalarMutation(destination.Owner.NodeId, scalarType.Value);
    }

    private static PinDataType? ResolveSourceScalarType(PinModel source)
    {
        if (source.EffectiveDataType == PinDataType.ColumnRef)
            return source.ColumnRefMeta?.ScalarType ?? source.ExpectedColumnScalarType;

        if (source.EffectiveDataType.IsScalar())
            return source.EffectiveDataType;

        return null;
    }

    private static PinDataType? ResolveColumnScalarType(PinModel pin) =>
        pin.ColumnRefMeta?.ScalarType ?? pin.ExpectedColumnScalarType;

    private static PinConnectionDecision EvaluateNumericScalarCompatibility(
        PinModel source,
        PinModel destination,
        PinConnectionContext context)
    {
        PinDataType sourceType = source.EffectiveDataType;
        PinDataType destinationType = destination.EffectiveDataType;

        // Prevent precision-loss/narrowing at connection time.
        // Decimal/Number -> Integer can truncate fractional values and should be explicit in the graph.
        if (destinationType == PinDataType.Integer
            && sourceType is PinDataType.Decimal or PinDataType.Number)
        {
            return PinConnectionDecision.Rejected(PinConnectionReasonCode.ScalarTypeMismatch);
        }

        return BuildAllowedDecision(source, destination, context);
    }

    private static bool IsStructuralMismatch(PinDataType source, PinDataType destination)
    {
        bool sourceRowSet = source == PinDataType.RowSet;
        bool destinationRowSet = destination == PinDataType.RowSet;
        return sourceRowSet != destinationRowSet;
    }

    private static bool IsWildcardProjectionToColumnInput(
        PinModel source,
        PinModel destination,
        WildcardProjectionContext? wildcardContext)
    {
        if (source.EffectiveDataType != PinDataType.ColumnSet || destination.EffectiveDataType != PinDataType.ColumnRef)
            return false;

        if (source.Owner.NodeType != NodeType.TableSource
            || !source.Name.Equals("*", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool defaultDestinationAllowed =
            destination.Owner.NodeType is NodeType.ColumnList or NodeType.ColumnSetBuilder;

        bool contextDestinationAllowed = wildcardContext is not null
            && wildcardContext.IsEnabled
            && wildcardContext.AllowedDestinationNodeTypes.Contains(destination.Owner.NodeType)
            && wildcardContext.AllowedDestinationPinNames.Contains(destination.Name);

        bool destinationAllowedByPolicy = defaultDestinationAllowed || contextDestinationAllowed;
        if (!destinationAllowedByPolicy)
            return false;

        return destination.Name.Equals("columns", StringComparison.OrdinalIgnoreCase)
            || destination.Name.Equals("metadata", StringComparison.OrdinalIgnoreCase)
            || (wildcardContext is not null
                && wildcardContext.IsEnabled
                && wildcardContext.AllowedDestinationPinNames.Contains(destination.Name));
    }

    private static bool IsComparisonNode(NodeType nodeType) =>
        nodeType is NodeType.Equals
            or NodeType.NotEquals
            or NodeType.GreaterThan
            or NodeType.GreaterOrEqual
            or NodeType.LessThan
            or NodeType.LessOrEqual
            or NodeType.Between
            or NodeType.NotBetween
            or NodeType.IsNull
            or NodeType.IsNotNull;

    private static bool IsProjectionDestinationPinName(string destinationPinName) =>
        destinationPinName.Equals("columns", StringComparison.OrdinalIgnoreCase)
        || destinationPinName.Equals("metadata", StringComparison.OrdinalIgnoreCase);
}
