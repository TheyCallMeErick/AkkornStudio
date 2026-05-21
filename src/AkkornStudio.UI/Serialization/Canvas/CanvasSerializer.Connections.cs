using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Serialization;

public static partial class CanvasSerializer
{
    private readonly record struct ConnectionRebuildStats(
        int Malformed,
        int Unresolved,
        int Incompatible,
        int MigratedLegacyProjectionPins
    );

    private static ConnectionRebuildStats RebuildConnections(
        IEnumerable<SavedConnection> savedConnections,
        IReadOnlyDictionary<string, NodeViewModel> nodeMap,
        ICollection<ConnectionViewModel> targetConnections
    )
    {
        int malformed = 0;
        int incompatible = 0;
        int migratedLegacyProjectionPins = 0;
        List<SavedConnection> pending = [];

        foreach (SavedConnection sc in savedConnections)
        {
            if (string.IsNullOrWhiteSpace(sc.FromNodeId)
                || string.IsNullOrWhiteSpace(sc.ToNodeId)
                || string.IsNullOrWhiteSpace(sc.FromPinName)
                || string.IsNullOrWhiteSpace(sc.ToPinName))
            {
                malformed++;
                continue;
            }

            if (!nodeMap.TryGetValue(sc.FromNodeId, out NodeViewModel? fromNode)
                || !nodeMap.TryGetValue(sc.ToNodeId, out NodeViewModel? toNode))
            {
                pending.Add(sc);
                continue;
            }

            PinViewModel? fromPin =
                fromNode.OutputPins.FirstOrDefault(p => p.Name == sc.FromPinName)
                ?? fromNode.InputPins.FirstOrDefault(p => p.Name == sc.FromPinName);
            PinViewModel? toPin =
                toNode.InputPins.FirstOrDefault(p => p.Name == sc.ToPinName)
                ?? toNode.OutputPins.FirstOrDefault(p => p.Name == sc.ToPinName);

            // ColumnList/ColumnSetBuilder: redirect old dynamic pins (col_N)
            // to the canonical fixed "columns" pin.
            if (
                toPin is null
                && (toNode.IsColumnList || toNode.Type == NodeType.ColumnSetBuilder)
                && sc.ToPinName.StartsWith("col_", StringComparison.OrdinalIgnoreCase)
            )
            {
                toPin = toNode.InputPins.FirstOrDefault(p => p.Name == "columns");
                if (toPin is not null)
                    migratedLegacyProjectionPins++;
            }

            // AND/OR migration: legacy cond_N pins are now normalized to the
            // single variadic "conditions" input pin.
            if (
                toPin is null
                && toNode.IsLogicGate
                && sc.ToPinName.StartsWith("cond_", StringComparison.OrdinalIgnoreCase)
            )
            {
                toPin = toNode.InputPins.FirstOrDefault(p => p.Name == "conditions");
            }

            if (
                toPin is null
                && toNode.Type == NodeType.CompileWhere
                && string.Equals(sc.ToPinName, "condition", StringComparison.OrdinalIgnoreCase)
            )
            {
                toPin = toNode.InputPins.FirstOrDefault(p => p.Name == "conditions");
            }

            // WindowFunction dynamic pins: create partition_N/order_N on-the-fly if missing.
            if (
                toPin is null
                && toNode.IsWindowFunction
                && (
                    sc.ToPinName.StartsWith("partition_", StringComparison.OrdinalIgnoreCase)
                    || sc.ToPinName.StartsWith("order_", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var dynPin = new PinViewModel(
                    new PinDescriptor(
                        sc.ToPinName,
                        PinDirection.Input,
                        PinDataType.ColumnRef,
                        IsRequired: false,
                        Description: "Connect a column or expression"
                    ),
                    toNode
                );
                toNode.InputPins.Add(dynPin);
                toPin = dynPin;
            }

            if (fromPin is null || toPin is null)
            {
                pending.Add(sc);
                continue;
            }

            if (!TryConnect(targetConnections, fromPin, toPin, sc))
            {
                incompatible++;
                continue;
            }
        }

        foreach (NodeViewModel node in nodeMap.Values.Where(n => n.Type == NodeType.CteSource))
            node.SyncCteSourceColumns(targetConnections);

        int unresolved = 0;
        foreach (SavedConnection sc in pending)
        {
            if (!nodeMap.TryGetValue(sc.FromNodeId, out NodeViewModel? fromNode)
                || !nodeMap.TryGetValue(sc.ToNodeId, out NodeViewModel? toNode))
            {
                unresolved++;
                continue;
            }

            if (!TryResolvePins(fromNode, sc.FromPinName, toNode, sc.ToPinName, out PinViewModel? fromPin, out PinViewModel? toPin))
            {
                unresolved++;
                continue;
            }

            if (!TryConnect(targetConnections, fromPin!, toPin!, sc))
            {
                incompatible++;
                continue;
            }
        }

        return new ConnectionRebuildStats(malformed, unresolved, incompatible, migratedLegacyProjectionPins);
    }

    private static void AddConnectionRebuildWarnings(ConnectionRebuildStats stats, List<string> warnings)
    {
        if (stats.Malformed > 0)
            warnings.Add(
                $"Skipped {stats.Malformed} malformed connection(s) with missing endpoint data."
            );

        if (stats.Unresolved > 0)
            warnings.Add(
                $"Skipped {stats.Unresolved} connection(s) that reference missing nodes or pins."
            );

        if (stats.Incompatible > 0)
            warnings.Add(
                $"Skipped {stats.Incompatible} incompatible connection(s) due to type mismatch."
            );

        if (stats.MigratedLegacyProjectionPins > 0)
            warnings.Add(
                $"Migrated {stats.MigratedLegacyProjectionPins} legacy projection connection(s) from dynamic 'col_*' pins to canonical 'columns' pin."
            );
    }

    private static bool TryResolvePins(
        NodeViewModel fromNode,
        string fromPinName,
        NodeViewModel toNode,
        string toPinName,
        out PinViewModel? fromPin,
        out PinViewModel? toPin
    )
    {
        fromPin = fromNode.OutputPins.FirstOrDefault(p => p.Name == fromPinName)
            ?? fromNode.InputPins.FirstOrDefault(p => p.Name == fromPinName);
        toPin = toNode.InputPins.FirstOrDefault(p => p.Name == toPinName)
            ?? toNode.OutputPins.FirstOrDefault(p => p.Name == toPinName);

        return fromPin is not null && toPin is not null;
    }

    private static bool TryConnect(
        ICollection<ConnectionViewModel> connections,
        PinViewModel fromPin,
        PinViewModel toPin,
        SavedConnection savedConnection
    )
    {
        if (!IsConnectionCompatible(fromPin, toPin))
            return false;

        var conn = new ConnectionViewModel(fromPin, default, default) { ToPin = toPin };
        ApplyWireMetadata(conn, savedConnection);
        fromPin.IsConnected = true;
        toPin.IsConnected = true;
        connections.Add(conn);
        return true;
    }

    private static bool TryConnect(
        ICollection<ConnectionViewModel> connections,
        PinViewModel fromPin,
        PinViewModel toPin)
    {
        SavedConnection savedConnection = new(
            FromNodeId: fromPin.Owner.Id,
            FromPinName: fromPin.Name,
            ToNodeId: toPin.Owner.Id,
            ToPinName: toPin.Name);
        return TryConnect(connections, fromPin, toPin, savedConnection);
    }

    private static void ApplyWireMetadata(ConnectionViewModel connection, SavedConnection savedConnection)
    {
        if (Enum.TryParse(savedConnection.RoutingMode, true, out CanvasWireRoutingMode routingMode))
            connection.RoutingMode = routingMode;

        if (savedConnection.Breakpoints is not { Count: > 0 })
            return;

        List<WireBreakpoint> validBreakpoints = [];
        foreach (SavedWireBreakpoint b in savedConnection.Breakpoints)
        {
            if (!double.IsFinite(b.X) || !double.IsFinite(b.Y))
            {
                RaiseWarning(
                    $"Skipped invalid wire breakpoint for connection '{savedConnection.FromNodeId}.{savedConnection.FromPinName} -> {savedConnection.ToNodeId}.{savedConnection.ToPinName}' (X={b.X}, Y={b.Y})."
                );
                continue;
            }

            validBreakpoints.Add(new WireBreakpoint(new(b.X, b.Y)));
        }

        if (validBreakpoints.Count == 0)
            return;

        connection.SetBreakpoints(validBreakpoints);
    }

    private static bool IsConnectionCompatible(PinViewModel fromPin, PinViewModel toPin)
    {
        try
        {
            return toPin.EvaluateConnection(fromPin).IsAllowed;
        }
        catch (Exception ex)
        {
            string fromLabel = fromPin is null
                ? "<null>"
                : $"{fromPin.Owner.Id}.{fromPin.Name}";
            string toLabel = toPin is null
                ? "<null>"
                : $"{toPin.Owner.Id}.{toPin.Name}";
            RaiseWarning(
                $"Skipped connection '{fromLabel} -> {toLabel}' due to compatibility evaluation error: {ex.Message}"
            );
            return false;
        }
    }
}
