using Avalonia;
using System.Text.Json;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Serialization;

public static partial class CanvasSerializer
{
    public static string Serialize(
        CanvasViewModel vm,
        string provider = "Postgres",
        string connectionName = "untitled",
        string? description = null
    )
    {
        SavedCanvas saved = BuildSavedCanvas(
            vm,
            provider,
            connectionName,
            description,
            persistProviderMetadata: true);
        return System.Text.Json.JsonSerializer.Serialize(saved, _opts);
    }

    public static string SerializeWorkspace(
        CanvasViewModel queryVm,
        CanvasViewModel? ddlVm,
        string provider = "Postgres",
        string connectionName = "untitled",
        string? description = null,
        string? queryCanvasOverrideJson = null,
        WorkspaceDocumentType activeDocumentType = WorkspaceDocumentType.QueryCanvas
    )
    {
        SavedCanvas query = TryDeserializeSavedCanvas(queryCanvasOverrideJson)
            ?? BuildSavedCanvas(queryVm, provider, connectionName, description, persistProviderMetadata: false);
        SavedCanvas ddl = BuildSavedDdlCanvas(ddlVm, provider, connectionName);

        Guid queryDocumentId = Guid.NewGuid();
        Guid ddlDocumentId = Guid.NewGuid();
        Guid sqlEditorDocumentId = Guid.NewGuid();
        Guid activeDocumentId = activeDocumentType switch
        {
            WorkspaceDocumentType.DdlCanvas => ddlDocumentId,
            WorkspaceDocumentType.SqlEditor => sqlEditorDocumentId,
            WorkspaceDocumentType.ErDiagram => queryDocumentId,
            _ => queryDocumentId,
        };

        var documents = new List<SavedWorkspaceDocument>
        {
            new(
                DocumentId: queryDocumentId,
                DocumentType: WorkspaceDocumentType.QueryCanvas.ToString(),
                Title: "Query Canvas",
                IsDirty: queryVm.IsDirty,
                PersistenceSchemaVersion: "1.0",
                CanvasPayload: query,
                DocumentPayload: null),
            new(
                DocumentId: ddlDocumentId,
                DocumentType: WorkspaceDocumentType.DdlCanvas.ToString(),
                Title: "DDL Canvas",
                IsDirty: ddlVm?.IsDirty ?? false,
                PersistenceSchemaVersion: "1.0",
                CanvasPayload: ddl,
                DocumentPayload: null),
            new(
                DocumentId: sqlEditorDocumentId,
                DocumentType: WorkspaceDocumentType.SqlEditor.ToString(),
                Title: "SQL Editor",
                IsDirty: false,
                PersistenceSchemaVersion: "1.0",
                CanvasPayload: null,
                DocumentPayload: null)
        };

        var workspace = new SavedWorkspaceDocumentsCanvas(
            Version: CurrentSchemaVersion,
            Documents: documents,
            ActiveDocumentId: activeDocumentId,
            QueryCanvas: query,
            DdlCanvas: ddl,
            AppVersion: AppVersion,
            CreatedAt: DateTime.UtcNow.ToString("o"),
            Description: description
        );

        return System.Text.Json.JsonSerializer.Serialize(workspace, _opts);
    }

    public static string SerializeWorkspaceDocuments(
        IReadOnlyList<OpenWorkspaceDocument> openDocuments,
        Guid? activeDocumentId,
        string provider = "Postgres",
        string connectionName = "untitled",
        string? description = null,
        IReadOnlyDictionary<Guid, Guid>? documentTabs = null,
        IReadOnlyList<SavedWorkspaceTab>? tabs = null,
        Guid? activeTabId = null)
    {
        if (openDocuments is null || openDocuments.Count == 0)
            throw new InvalidOperationException("Workspace must contain at least one document to be serialized.");

        var documents = new List<SavedWorkspaceDocument>(openDocuments.Count);
        foreach (OpenWorkspaceDocument openDocument in openDocuments)
        {
            Guid documentId = openDocument.Descriptor.DocumentId == Guid.Empty
                ? Guid.NewGuid()
                : openDocument.Descriptor.DocumentId;

            bool isDirty = openDocument.Descriptor.IsDirty;
            SavedCanvas? canvasPayload = null;
            if (openDocument.DocumentViewModel is CanvasViewModel canvasDocument)
            {
                isDirty = canvasDocument.IsDirty;
                canvasPayload = openDocument.Descriptor.DocumentType == WorkspaceDocumentType.DdlCanvas
                    ? BuildSavedDdlCanvas(canvasDocument, provider, connectionName)
                    : BuildSavedCanvas(canvasDocument, provider, connectionName, description, persistProviderMetadata: false);
            }
            JsonElement? documentPayload = openDocument.Descriptor.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : openDocument.Descriptor.Payload.Clone();

            Guid tabId = documentTabs is not null && documentTabs.TryGetValue(documentId, out Guid resolvedTabId)
                ? resolvedTabId
                : Guid.Empty;

            documents.Add(new SavedWorkspaceDocument(
                DocumentId: documentId,
                DocumentType: openDocument.Descriptor.DocumentType.ToString(),
                Title: openDocument.Descriptor.Title,
                IsDirty: isDirty,
                PersistenceSchemaVersion: openDocument.Descriptor.PersistenceSchemaVersion,
                CanvasPayload: canvasPayload,
                DocumentPayload: documentPayload,
                TabId: tabId));
        }

        Guid resolvedActiveDocumentId = activeDocumentId is Guid requestedActiveId
            && documents.Any(document => document.DocumentId == requestedActiveId)
            ? requestedActiveId
            : documents[0].DocumentId;

        var workspace = new SavedWorkspaceDocumentsCanvas(
            Version: CurrentSchemaVersion,
            Documents: documents,
            ActiveDocumentId: resolvedActiveDocumentId,
            QueryCanvas: null,
            DdlCanvas: null,
            AppVersion: AppVersion,
            CreatedAt: DateTime.UtcNow.ToString("o"),
            Description: description,
            Tabs: tabs?.ToList(),
            ActiveTabId: activeTabId);

        return System.Text.Json.JsonSerializer.Serialize(workspace, _opts);
    }

    private static SavedCanvas? TryDeserializeSavedCanvas(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<SavedCanvas>(json, _opts);
        }
        catch
        {
            return null;
        }
    }

    private static SavedCanvas BuildSavedCanvas(
        CanvasViewModel vm,
        string provider,
        string connectionName,
        string? description,
        bool persistProviderMetadata
    )
    {
        return new SavedCanvas(
            Version: CurrentCanvasSchemaVersion,
            DatabaseProvider: persistProviderMetadata ? provider : null,
            ConnectionName: persistProviderMetadata ? connectionName : null,
            Zoom: vm.Zoom,
            PanX: vm.PanOffset.X,
            PanY: vm.PanOffset.Y,
            Nodes: [.. vm.Nodes.Select(n => SerialiseNode(n, vm.Nodes, vm.Connections))],
            Connections: [.. vm.Connections
                .Select(SerialiseConnection)
                .Where(c => c is not null)
                .Select(c => c!)],
            SelectBindings: [],
            WhereBindings: [],
            PreviewParameterInputs: vm.PreviewParameterInputs.Count > 0
                ? new Dictionary<string, string>(vm.PreviewParameterInputs, StringComparer.OrdinalIgnoreCase)
                : null,
            AppVersion: AppVersion,
            CreatedAt: DateTime.UtcNow.ToString("o"),
            Description: description
        );
    }

    private static SavedCanvas BuildSavedDdlCanvas(
        CanvasViewModel? ddlVm,
        string provider,
        string connectionName
    )
    {
        if (ddlVm is null)
        {
            return new SavedCanvas(
                Version: CurrentCanvasSchemaVersion,
                DatabaseProvider: null,
                ConnectionName: null,
                Zoom: 1,
                PanX: 0,
                PanY: 0,
                Nodes: [],
                Connections: [],
                SelectBindings: [],
                WhereBindings: [],
                PreviewParameterInputs: null,
                AppVersion: AppVersion,
                CreatedAt: DateTime.UtcNow.ToString("o")
            );
        }

        return new SavedCanvas(
            Version: CurrentCanvasSchemaVersion,
            DatabaseProvider: null,
            ConnectionName: null,
            Zoom: 1,
            PanX: 0,
            PanY: 0,
            Nodes: [.. ddlVm.Nodes.Select(n => SerialiseNode(n, ddlVm.Nodes, ddlVm.Connections))],
            Connections: [.. ddlVm.Connections
                .Select(SerialiseConnection)
                .Where(c => c is not null)
                .Select(c => c!)],
            SelectBindings: [],
            WhereBindings: [],
            PreviewParameterInputs: ddlVm.PreviewParameterInputs.Count > 0
                ? new Dictionary<string, string>(ddlVm.PreviewParameterInputs, StringComparer.OrdinalIgnoreCase)
                : null,
            AppVersion: AppVersion,
            CreatedAt: DateTime.UtcNow.ToString("o")
        );
    }

    /// <summary>
    /// Serialises a subset of nodes and the connections that are entirely
    /// internal to that subset (both endpoints in <paramref name="nodes"/>).
    /// </summary>
    public static (List<SavedNode> Nodes, List<SavedConnection> Connections) SerialiseSubgraph(
        IEnumerable<NodeViewModel> nodes,
        IEnumerable<ConnectionViewModel> connections
    )
    {
        List<NodeViewModel> nodeList = [.. nodes];
        HashSet<string> ids = nodeList.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        return (
            [.. nodeList.Select(n => SerialiseNode(n, nodeList, connections, includeCteSubgraph: false))],
            [.. connections.Where(c =>
                ids.Contains(c.FromPin.Owner.Id) && ids.Contains(c.ToPin?.Owner.Id ?? "")
            )
                .Select(SerialiseConnection)
                .Where(c => c is not null)
                .Select(c => c!)]
        );
    }

    /// <summary>
    /// Inserts a saved subgraph into the canvas, generating fresh node IDs and
    /// centering the pasted content at <paramref name="canvasPos"/>.
    /// </summary>
    public static void InsertSubgraph(
        List<SavedNode> nodes,
        List<SavedConnection> conns,
        CanvasViewModel vm,
        Point canvasPos,
        IReadOnlyDictionary<
            string,
            IReadOnlyList<(string Name, PinDataType Type)>
        >? columnLookup = null
    )
    {
        if (nodes.Count == 0)
            return;

        double cx = nodes.Average(n => n.X);
        double cy = nodes.Average(n => n.Y);

        var addedNodes = new List<NodeViewModel>(nodes.Count);
        var addedConnections = new List<ConnectionViewModel>(conns.Count);
        var idMap = new Dictionary<string, NodeViewModel>(StringComparer.Ordinal);
        try
        {
            foreach (SavedNode sn in nodes)
            {
                SavedNode positioned = sn with
                {
                    NodeId = Guid.NewGuid().ToString(),
                    X = sn.X - cx + canvasPos.X,
                    Y = sn.Y - cy + canvasPos.Y,
                };
                (NodeViewModel? nvm, _) = BuildNodeVm(positioned, columnLookup);
                if (nvm is null)
                    continue;
                idMap[sn.NodeId] = nvm;
                addedNodes.Add(nvm);
                vm.Nodes.Add(nvm);
            }

            foreach (SavedConnection sc in conns)
            {
                if (!idMap.TryGetValue(sc.FromNodeId, out NodeViewModel? fromNode))
                    throw new InvalidOperationException(
                        $"Subgraph paste failed: missing source node '{sc.FromNodeId}' for connection '{sc.FromPinName} -> {sc.ToPinName}'."
                    );
                if (!idMap.TryGetValue(sc.ToNodeId, out NodeViewModel? toNode))
                    throw new InvalidOperationException(
                        $"Subgraph paste failed: missing destination node '{sc.ToNodeId}' for connection '{sc.FromPinName} -> {sc.ToPinName}'."
                    );

                if (
                    !TryResolvePins(
                        fromNode,
                        sc.FromPinName,
                        toNode,
                        sc.ToPinName,
                        out PinViewModel? fromPin,
                        out PinViewModel? toPin
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"Subgraph paste failed: cannot resolve pins '{sc.FromNodeId}.{sc.FromPinName}' -> '{sc.ToNodeId}.{sc.ToPinName}'."
                    );
                }

                int beforeCount = vm.Connections.Count;
                if (!TryConnect(vm.Connections, fromPin!, toPin!))
                {
                    throw new InvalidOperationException(
                        $"Subgraph paste failed: incompatible pins '{sc.FromNodeId}.{sc.FromPinName}' -> '{sc.ToNodeId}.{sc.ToPinName}'."
                    );
                }

                if (vm.Connections.Count <= beforeCount)
                    throw new InvalidOperationException(
                        "Subgraph paste failed: connection insertion did not mutate collection."
                    );

                ConnectionViewModel created = vm.Connections[^1];
                addedConnections.Add(created);
            }
        }
        catch
        {
            foreach (ConnectionViewModel addedConnection in addedConnections)
                vm.Connections.Remove(addedConnection);

            foreach (NodeViewModel addedNode in addedNodes)
                vm.Nodes.Remove(addedNode);

            RecomputeConnectedFlags(vm);
            throw;
        }
    }

    private static void RecomputeConnectedFlags(CanvasViewModel vm)
    {
        foreach (NodeViewModel node in vm.Nodes)
        {
            foreach (PinViewModel pin in node.InputPins)
            {
                pin.IsConnected = vm.Connections.Any(c => c.ToPin == pin || c.FromPin == pin);
            }

            foreach (PinViewModel pin in node.OutputPins)
            {
                pin.IsConnected = vm.Connections.Any(c => c.FromPin == pin || c.ToPin == pin);
            }
        }
    }

    private static SavedNode SerialiseNode(
        NodeViewModel n,
        IEnumerable<NodeViewModel> allNodes,
        IEnumerable<ConnectionViewModel> allConnections,
        bool includeCteSubgraph = true
    )
    {
        var parameters = new Dictionary<string, string>(n.Parameters);
        parameters.Remove(CteSubgraphParameterKey);
        parameters.Remove("set_operator");
        parameters.Remove("set_query");
        parameters.Remove("import_order_terms");
        parameters.Remove("import_group_terms");
        if (n.Type is NodeType.SubqueryExists or NodeType.SubqueryIn or NodeType.SubqueryScalar or NodeType.SubqueryReference)
            parameters.Remove("query");
        SavedViewSubgraph? viewSubgraph = BuildViewSubgraph(n, parameters);
        if (n.Type == NodeType.ResultOutput && n.OutputColumnOrder.Count > 0)
            parameters["__colOrder"] = string.Join("|", n.OutputColumnOrder.Select(e => e.Key));

        List<SavedColumn>? columns = null;
        if (n.Type == NodeType.TableSource)
        {
            columns = n.OutputPins
                .Select(p => new SavedColumn(
                    p.Name,
                    (p.ColumnRefMeta?.ScalarType ?? p.EffectiveDataType).ToString()))
                .ToList();
        }

        SavedCteSubgraph? cteSubgraph = includeCteSubgraph
            ? BuildCteSubgraph(n, allNodes, allConnections)
            : null;

        return new(
            NodeId: n.Id,
            NodeType: n.Type.ToString(),
            X: n.Position.X,
            Y: n.Position.Y,
            ZOrder: n.ZOrder,
            Alias: n.Alias,
            TableFullName: n.Type == NodeType.TableSource ? n.Subtitle : null,
            Parameters: parameters,
            PinLiterals: new Dictionary<string, string>(n.PinLiterals),
            Columns: columns,
            CteSubgraph: cteSubgraph,
            ViewSubgraph: viewSubgraph
        );
    }

    private static SavedConnection? SerialiseConnection(ConnectionViewModel c)
    {
        if (c.ToPin is null)
            return null;

        return new SavedConnection(
            FromNodeId: c.FromPin.Owner.Id,
            FromPinName: c.FromPin.Name,
            ToNodeId: c.ToPin.Owner.Id,
            ToPinName: c.ToPin.Name,
            RoutingMode: c.RoutingMode.ToString(),
            Breakpoints: [.. c.Breakpoints.Select(b => new SavedWireBreakpoint(b.Position.X, b.Position.Y))]
        );
    }
}
