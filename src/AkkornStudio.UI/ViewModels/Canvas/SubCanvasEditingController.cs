using System.Text.Json;
using AkkornStudio.CanvasKit;
using AkkornStudio.Core;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels.Canvas;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;
using AkkornStudio.UI.Services.QueryPreview;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// Manages CTE and View sub-editor sessions, removing that responsibility
/// from CanvasViewModel (SRP). Exposes session state as plain properties;
/// CanvasViewModel forwards them to the UI layer.
/// </summary>
internal sealed class SubCanvasEditingController
{
    private sealed record CteEditorSession(
        string ParentCanvasJson,
        string ParentCteNodeId,
        bool ParentWasDirty,
        string CteDisplayName,
        NodeType EditorNodeType,
        IReadOnlyList<SavedSubqueryInputBinding>? SubqueryInputBindings = null)
    {
        // Backward-compatible constructor for tests/reflection paths that still expect
        // the original CTE-only signature.
        public CteEditorSession(
            string parentCanvasJson,
            string parentCteNodeId,
            bool parentWasDirty,
            string cteDisplayName)
            : this(parentCanvasJson, parentCteNodeId, parentWasDirty, cteDisplayName, NodeType.CteDefinition)
        {
        }
    }

    private sealed record ViewEditorSession(
        string ParentCanvasJson,
        string ParentViewNodeId,
        bool ParentWasDirty,
        string ViewDisplayName
    );

    private readonly CanvasViewModel _canvas;
    private readonly ICanvasDomainStrategy _domainStrategy;
    private readonly ISelectionManager _selectionManager;
    private readonly AppDiagnosticsViewModel _diagnostics;
    private readonly ILocalizationService _localizationService;
    private readonly Action _notifyStateChanged;
    private readonly Func<object?>? _getCteSession;
    private readonly Action<object?>? _setCteSession;
    private readonly Func<object?>? _getViewSession;
    private readonly Action<object?>? _setViewSession;

    private CteEditorSession? _cteEditorSession;
    private ViewEditorSession? _viewEditorSession;
    private CanvasSubEditorSessionState _sessionState = CanvasSubEditorSessionState.Empty;

    private CteEditorSession? CteSession
    {
        get => _getCteSession is not null ? _getCteSession() as CteEditorSession : _cteEditorSession;
        set
        {
            _cteEditorSession = value;
            _setCteSession?.Invoke(value);
        }
    }

    private ViewEditorSession? ViewSession
    {
        get => _getViewSession is not null ? _getViewSession() as ViewEditorSession : _viewEditorSession;
        set
        {
            _viewEditorSession = value;
            _setViewSession?.Invoke(value);
        }
    }

    public SubCanvasEditingController(
        CanvasViewModel canvas,
        ICanvasDomainStrategy domainStrategy,
        ISelectionManager selectionManager,
        AppDiagnosticsViewModel diagnostics,
        ILocalizationService localizationService,
        Action notifyStateChanged,
        Func<object?>? getCteSession = null,
        Action<object?>? setCteSession = null,
        Func<object?>? getViewSession = null,
        Action<object?>? setViewSession = null)
    {
        _canvas = canvas;
        _domainStrategy = domainStrategy;
        _selectionManager = selectionManager;
        _diagnostics = diagnostics;
        _localizationService = localizationService;
        _notifyStateChanged = notifyStateChanged;
        _getCteSession = getCteSession;
        _setCteSession = setCteSession;
        _getViewSession = getViewSession;
        _setViewSession = setViewSession;
    }

    public bool IsInCteEditor => _sessionState.IsActive;
    public bool IsInViewEditor => _sessionState.IsViewEditor;
    public bool IsCanvasDimmedBySubcanvas => IsInViewEditor;

    public string CteEditorBreadcrumb =>
        _sessionState.Mode switch
        {
            CanvasSubEditorMode.Cte =>
                $"{_localizationService["main.cteEditor.editingPrefix"]}{_sessionState.DisplayName}",
            CanvasSubEditorMode.View =>
                $"{_localizationService["main.viewEditor.editingPrefix"]}{_sessionState.DisplayName}",
            _ => string.Empty,
        };

    public string EditorExitLabel =>
        IsInViewEditor
            ? _localizationService["main.viewEditor.backToCanvas"]
            : _localizationService["main.cteEditor.backToCanvas"];

    public string EditorExitA11y =>
        IsInViewEditor
            ? _localizationService["main.viewEditor.exitA11y"]
            : _localizationService["main.cteEditor.exitA11y"];

    public void AbortActiveEditorSession()
    {
        if (!_sessionState.IsActive)
            return;

        _sessionState = CanvasSubEditorStateMachine.Exit();
        _setCteSession?.Invoke(null);
        _setViewSession?.Invoke(null);
        _notifyStateChanged();
    }

    public bool CanEnterSelectedCteEditor()
    {
        if (IsInCteEditor)
            return false;

        return _canvas.Nodes.Count(n => n.IsSelected && _domainStrategy.CanEnterSubEditor(n)) == 1;
    }

    public async Task<bool> EnterSelectedCteEditorAsync()
    {
        if (!CanEnterSelectedCteEditor())
            return false;

        NodeViewModel selectedNode = _canvas.Nodes.Single(n => n.IsSelected && _domainStrategy.CanEnterSubEditor(n));
        if (_domainStrategy.CanEnterSubEditor(selectedNode) && selectedNode.Parameters.ContainsKey("ViewName"))
            return await EnterViewEditorAsync(selectedNode);
        if (selectedNode.Type is NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition)
            return await EnterSubqueryEditorAsync(selectedNode);

        NodeViewModel cteNode = selectedNode;
        string cteName = ResolveCteDisplayName(cteNode);

        string parentJson = CanvasSerializer.Serialize(_canvas);
        bool parentWasDirty = _canvas.IsDirty;

        (List<SavedNode> nodes, List<SavedConnection> conns) = ExtractCteEditableSubgraph(cteNode);
        CanvasLoadResult load = await LoadSubEditorCanvasAsync(
            cteNode, nodes, conns, $"CTE editor: {cteName}"
        );
        if (!load.Success)
            return false;

        CteSession = new CteEditorSession(parentJson, cteNode.Id, parentWasDirty, cteName, NodeType.CteDefinition);
        _sessionState = CanvasSubEditorStateMachine.EnterCte(cteName);
        _canvas.IsDirty = false;
        _notifyStateChanged();
        return true;
    }

    public async Task<bool> EnterSubqueryEditorAsync(NodeViewModel subqueryNode)
    {
        if (IsInCteEditor)
            return false;
        if (!_domainStrategy.CanEnterSubEditor(subqueryNode))
            return false;
        if (!_canvas.Nodes.Contains(subqueryNode))
            return false;

        _selectionManager.DeselectAll();
        subqueryNode.IsSelected = true;

        string parentJson = CanvasSerializer.Serialize(_canvas);
        bool parentWasDirty = _canvas.IsDirty;
        string displayName = ResolveSubqueryDisplayName(subqueryNode);

        IReadOnlyList<SavedSubqueryInputBinding> inputBindings = BuildSubqueryInputBindings(subqueryNode, _canvas.Connections);
        (List<SavedNode> nodes, List<SavedConnection> conns) = ExtractSubqueryEditableSubgraph(subqueryNode, inputBindings);

        CanvasLoadResult load = await LoadSubEditorCanvasAsync(
            subqueryNode, nodes, conns, $"Subquery editor: {displayName}"
        );
        if (!load.Success)
            return false;

        CteSession = new CteEditorSession(
            parentJson,
            subqueryNode.Id,
            parentWasDirty,
            displayName,
            subqueryNode.Type,
            inputBindings);
        _sessionState = CanvasSubEditorStateMachine.EnterCte(displayName);
        _canvas.IsDirty = false;
        _notifyStateChanged();
        return true;
    }

    public async Task<bool> EnterCteEditorAsync(NodeViewModel cteNode)
    {
        if (IsInCteEditor)
            return false;
        if (!_domainStrategy.CanEnterSubEditor(cteNode))
            return false;
        if (!_canvas.Nodes.Contains(cteNode))
            return false;

        _selectionManager.DeselectAll();
        cteNode.IsSelected = true;
        return await EnterSelectedCteEditorAsync();
    }

    public async Task<bool> ExitCteEditorAsync(bool forceDiscard = false)
    {
        if (ViewSession is not null)
            return await ExitViewEditorAsync(forceDiscard);

        if (CteSession is null)
            return false;

        CteEditorSession session = CteSession;
        (List<SavedNode> editedNodes, List<SavedConnection> editedConnections) =
            CanvasSerializer.SerialiseSubgraph(_canvas.Nodes, _canvas.Connections);
        string? compiledSubquerySql = null;
        List<string>? subqueryBuildErrors = null;
        if (session.EditorNodeType is NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition)
        {
            _ = TryBuildSqlFromSavedSubgraph(
                editedNodes,
                editedConnections,
                out compiledSubquerySql,
                out subqueryBuildErrors);
        }

        CanvasLoadResult restore = CanvasSerializer.Deserialize(session.ParentCanvasJson, _canvas);
        if (!restore.Success)
        {
            CteSession = null;
            _sessionState = CanvasSubEditorStateMachine.Exit();
            _diagnostics.AddWarning(
                area: L("diagnostics.area.cteEditor", "CTE Editor"),
                message: L("diagnostics.cteEditor.restoreParentFailed", "Failed to restore the parent canvas. CTE edits were discarded."),
                recommendation: L("diagnostics.recommendation.reloadFileIfNeeded", "Reload the file if needed."),
                openPanel: true
            );
            _notifyStateChanged();
            return false;
        }

        NodeViewModel? cteNode = _canvas.Nodes.FirstOrDefault(n =>
            n.Id == session.ParentCteNodeId && _domainStrategy.CanEnterSubEditor(n)
        );
        if (cteNode is null)
        {
            CteSession = null;
            _sessionState = CanvasSubEditorStateMachine.Exit();
            _notifyStateChanged();
            return false;
        }

        if (session.EditorNodeType is NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition)
        {
            PersistSubquerySubgraph(cteNode, editedNodes, editedConnections, session.SubqueryInputBindings ?? []);
            _canvas.NotifyNodeParameterChanged(cteNode, CanvasSerializer.SubquerySubgraphParameterKey);

            if (!string.IsNullOrWhiteSpace(compiledSubquerySql))
            {
                cteNode.Parameters["query"] = compiledSubquerySql!;
                _canvas.NotifyNodeParameterChanged(cteNode, "query");
            }
            else if (!forceDiscard)
            {
                string? firstError = subqueryBuildErrors?.FirstOrDefault();
                _diagnostics.AddWarning(
                    area: L("diagnostics.area.subEditor", "Sub-editor"),
                    message: string.Format(
                        L("diagnostics.subqueryEditor.queryBuildFailed", "Could not compile subquery SQL: {0}"),
                        firstError ?? L("diagnostics.subEditor.canvasIncomplete", "the subcanvas is incomplete.")),
                    recommendation: L("diagnostics.subEditor.executeRecommendation", "Try again. If it persists, reload the canvas."),
                    openPanel: true);
            }
        }
        else
        {
            RemoveExistingCteQuerySubgraph(cteNode);
            PersistCteSubgraph(_domainStrategy, cteNode, editedNodes, editedConnections);
            _canvas.NotifyNodeParameterChanged(cteNode, CanvasSerializer.CteSubgraphParameterKey);
        }

        CteSession = null;
        _sessionState = CanvasSubEditorStateMachine.Exit();
        _canvas.IsDirty = true;
        _notifyStateChanged();
        return true;
    }

    private bool TryBuildSqlFromSavedSubgraph(
        List<SavedNode> editedNodes,
        List<SavedConnection> editedConnections,
        out string? selectSql,
        out List<string>? errors)
    {
        selectSql = null;
        errors = null;

        using var tempCanvas = new CanvasViewModel();
        string payload = BuildTemporaryCanvasJson(editedNodes, editedConnections, "subquery-editor-compile");
        CanvasLoadResult load = CanvasSerializer.Deserialize(payload, tempCanvas);
        if (!load.Success)
        {
            errors = [load.Error ?? "Failed to load subquery subgraph for compilation."];
            return false;
        }

        QueryGraphBuilder builder = new(tempCanvas, _canvas.ActiveConnectionConfig?.Provider ?? DatabaseProvider.Postgres);
        bool graphBuilt = builder.TryBuildGraphSnapshot(
            out _,
            out _,
            out selectSql,
            out List<string> buildErrors);

        errors = buildErrors;
        return graphBuilt && !string.IsNullOrWhiteSpace(selectSql);
    }

    public async Task<bool> EnterViewEditorAsync(NodeViewModel viewNode)
    {
        if (IsInCteEditor)
            return false;
        if (!_domainStrategy.CanEnterSubEditor(viewNode))
            return false;
        if (!_canvas.Nodes.Contains(viewNode))
            return false;

        string parentJson = CanvasSerializer.Serialize(_canvas);
        bool parentWasDirty = _canvas.IsDirty;
        string viewName = ResolveViewDisplayName(viewNode);

        (List<SavedNode> nodes, List<SavedConnection> conns) = ExtractViewEditableSubgraph(viewNode);
        CanvasLoadResult load = await LoadSubEditorCanvasAsync(
            viewNode, nodes, conns, $"View editor: {viewName}"
        );
        if (!load.Success)
            return false;

        ViewSession = new ViewEditorSession(parentJson, viewNode.Id, parentWasDirty, viewName);
        _sessionState = CanvasSubEditorStateMachine.EnterView(viewName);
        _canvas.IsDirty = false;
        _notifyStateChanged();
        return true;
    }

    private Task<bool> ExitViewEditorAsync(bool forceDiscard = false)
    {
        if (ViewSession is null)
            return Task.FromResult(false);

        ViewEditorSession session = ViewSession;
        QueryGraphBuilder builder = new(_canvas, _canvas.ActiveConnectionConfig?.Provider ?? DatabaseProvider.Postgres);
        bool graphBuilt = builder.TryBuildGraphSnapshot(
            out NodeGraph graph,
            out string? fromTable,
            out string? selectSql,
            out List<string> buildErrors
        );
        if (!graphBuilt && !forceDiscard)
        {
            string? buildError = buildErrors.FirstOrDefault();
            _diagnostics.AddWarning(
                area: L("diagnostics.area.viewEditor", "View Editor"),
                message: string.Format(
                    L("diagnostics.viewEditor.exitFailed", "Could not exit: {0}"),
                    buildError ?? L("diagnostics.viewEditor.canvasIncomplete", "the canvas is incomplete.")
                ),
                recommendation: L("diagnostics.viewEditor.exitRecommendation", "Connect a valid ResultOutput or use the discard command."),
                openPanel: true
            );
            return Task.FromResult(false);
        }

        CanvasLoadResult restore = CanvasSerializer.Deserialize(session.ParentCanvasJson, _canvas);
        if (!restore.Success)
        {
            ViewSession = null;
            _sessionState = CanvasSubEditorStateMachine.Exit();
            _diagnostics.AddWarning(
                area: L("diagnostics.area.viewEditor", "View Editor"),
                message: L("diagnostics.viewEditor.restoreParentFailed", "Failed to restore the parent canvas. The subgraph was discarded."),
                recommendation: L("diagnostics.recommendation.reloadFileIfNeeded", "Reload the file if needed."),
                openPanel: true
            );
            _notifyStateChanged();
            return Task.FromResult(false);
        }

        NodeViewModel? viewNode = _canvas.Nodes.FirstOrDefault(n =>
            n.Id == session.ParentViewNodeId && _domainStrategy.CanEnterSubEditor(n)
        );
        if (viewNode is null)
        {
            ViewSession = null;
            _sessionState = CanvasSubEditorStateMachine.Exit();
            _notifyStateChanged();
            return Task.FromResult(false);
        }

        if (graphBuilt)
        {
            PersistViewEditorState(
                viewNode,
                CanvasSerializer.Serialize(_canvas),
                graph,
                fromTable,
                selectSql);
        }

        ViewSession = null;
        _sessionState = CanvasSubEditorStateMachine.Exit();
        _canvas.IsDirty = true;
        _notifyStateChanged();
        return Task.FromResult(true);
    }

    public string SerializeForPersistence()
    {
        if (CteSession is { } cteSession)
            return SerializeCteParentSnapshot(cteSession);

        if (ViewSession is { } viewSession)
            return SerializeViewParentSnapshot(viewSession);

        return CanvasSerializer.Serialize(_canvas);
    }

    private static string ResolveCteDisplayName(NodeViewModel cteNode)
    {
        if (cteNode.Parameters.TryGetValue("name", out string? name) && !string.IsNullOrWhiteSpace(name))
            return name.Trim();

        if (cteNode.Parameters.TryGetValue("cte_name", out string? legacy) && !string.IsNullOrWhiteSpace(legacy))
            return legacy.Trim();

        return cteNode.Title;
    }

    private static string ResolveViewDisplayName(NodeViewModel viewNode)
    {
        if (viewNode.Parameters.TryGetValue("ViewName", out string? name) && !string.IsNullOrWhiteSpace(name))
            return name.Trim();

        return viewNode.Title;
    }

    private static string ResolveSubqueryDisplayName(NodeViewModel subqueryNode)
    {
        if (subqueryNode.Parameters.TryGetValue("alias", out string? alias)
            && !string.IsNullOrWhiteSpace(alias))
        {
            return alias.Trim();
        }

        return subqueryNode.Title;
    }

    private static (List<SavedNode> Nodes, List<SavedConnection> Connections) ExtractViewEditableSubgraph(NodeViewModel viewNode)
    {
        if (viewNode.Parameters.TryGetValue(CanvasSerializer.ViewEditorCanvasParameterKey, out string? editorCanvasJson)
            && !string.IsNullOrWhiteSpace(editorCanvasJson))
        {
            try
            {
                SavedCanvas? editorCanvas = JsonSerializer.Deserialize<SavedCanvas>(editorCanvasJson);
                if (editorCanvas is not null)
                    return (editorCanvas.Nodes, editorCanvas.Connections);
            }
            catch
            {
                // Fall back to the compiled graph representation.
            }
        }

        if (!viewNode.Parameters.TryGetValue(CanvasSerializer.ViewSubgraphParameterKey, out string? payload)
            || string.IsNullOrWhiteSpace(payload))
            return ([], []);

        NodeGraph? graph;
        try { graph = JsonSerializer.Deserialize<NodeGraph>(payload); }
        catch { return ([], []); }

        if (graph is null || graph.Nodes.Count == 0)
            return ([], []);

        List<SavedNode> nodes = graph.Nodes.Select(n =>
        {
            List<SavedColumn>? columns = null;
            if (!string.IsNullOrWhiteSpace(n.TableFullName) && n.ColumnPins is { Count: > 0 })
            {
                columns = n.ColumnPins
                    .Select(cp =>
                    {
                        string typeLabel = "ColumnRef";
                        if (n.ColumnPinTypes is not null && n.ColumnPinTypes.TryGetValue(cp.Value, out PinDataType t))
                            typeLabel = t.ToString();

                        return new SavedColumn(cp.Key, typeLabel);
                    })
                    .ToList();
            }

            return new SavedNode(
                NodeId: n.Id,
                NodeType: n.Type.ToString(),
                X: 140, Y: 120, ZOrder: null,
                Alias: n.Alias,
                TableFullName: !string.IsNullOrWhiteSpace(n.TableFullName) ? n.TableFullName : null,
                Parameters: new Dictionary<string, string>(n.Parameters),
                PinLiterals: new Dictionary<string, string>(n.PinLiterals),
                Columns: columns
            );
        }).ToList();

        for (int i = 0; i < nodes.Count; i++)
        {
            SavedNode n = nodes[i];
            nodes[i] = n with
            {
                X = 120 + ((i % 4) * 220),
                Y = 80 + ((i / 4) * 170),
            };
        }

        List<SavedConnection> connections = graph.Connections
            .Select(c => new SavedConnection(c.FromNodeId, c.FromPinName, c.ToNodeId, c.ToPinName))
            .ToList();

        return (nodes, connections);
    }

    private (List<SavedNode> Nodes, List<SavedConnection> Connections) ExtractCteEditableSubgraph(NodeViewModel cteNode)
        => _domainStrategy is ICanvasDomainStrategyExt ext
            ? ext.ExtractCteEditableSubgraph(cteNode, _canvas.Nodes, _canvas.Connections)
            : ([], []);

    private static IReadOnlyList<SavedSubqueryInputBinding> BuildSubqueryInputBindings(
        NodeViewModel subqueryNode,
        IEnumerable<ConnectionViewModel> allConnections)
    {
        List<SavedSubqueryInputBinding> bindings = [];
        HashSet<string> usedBridgePinNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConnectionViewModel wire in allConnections.Where(connection =>
                     connection.ToPin?.Owner == subqueryNode
                     && connection.ToPin.Name.StartsWith("input_", StringComparison.OrdinalIgnoreCase)))
        {
            string bridgePinName = $"{wire.FromPin.Owner.Title}_{wire.FromPin.Name}"
                .ToLowerInvariant()
                .Replace(' ', '_')
                .Replace('.', '_')
                .Replace('-', '_');

            if (string.IsNullOrWhiteSpace(bridgePinName))
                bridgePinName = "outer_input";

            string uniqueBridgePinName = bridgePinName;
            int suffix = 2;
            while (!usedBridgePinNames.Add(uniqueBridgePinName))
            {
                uniqueBridgePinName = $"{bridgePinName}_{suffix}";
                suffix++;
            }

            bindings.Add(new SavedSubqueryInputBinding(
                wire.ToPin!.Name,
                uniqueBridgePinName,
                wire.FromPin.Owner.Id,
                wire.FromPin.Name,
                $"{wire.FromPin.Owner.Title}.{wire.FromPin.Name}"));
        }

        return bindings;
    }

    private static (List<SavedNode> Nodes, List<SavedConnection> Connections) ExtractSubqueryEditableSubgraph(
        NodeViewModel subqueryNode,
        IReadOnlyList<SavedSubqueryInputBinding> inputBindings)
    {
        List<SavedNode> nodes = [];
        List<SavedConnection> connections = [];

        if (subqueryNode.Parameters.TryGetValue(CanvasSerializer.SubquerySubgraphParameterKey, out string? payload)
            && !string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                SavedSubquerySubgraph? persisted = JsonSerializer.Deserialize<SavedSubquerySubgraph>(payload);
                if (persisted is not null)
                {
                    nodes = persisted.Nodes;
                    connections = persisted.Connections;
                }
            }
            catch
            {
                // Fallback to seed graph.
            }
        }

        if (nodes.Count == 0)
        {
            NodeViewModel seedResult = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Avalonia.Point(300, 160));
            (nodes, connections) = CanvasSerializer.SerialiseSubgraph([seedResult], []);
        }

        return EnsureSubqueryInputBridge(nodes, connections, inputBindings);
    }

    private static (List<SavedNode> Nodes, List<SavedConnection> Connections) EnsureSubqueryInputBridge(
        List<SavedNode> nodes,
        List<SavedConnection> connections,
        IReadOnlyList<SavedSubqueryInputBinding> inputBindings)
    {
        List<SavedNode> editedNodes = [.. nodes.Where(node =>
            !string.Equals(node.NodeId, CanvasSerializer.SubqueryInputBridgeNodeId, StringComparison.OrdinalIgnoreCase))];
        List<SavedConnection> editedConnections = [.. connections.Where(connection =>
            !string.Equals(connection.FromNodeId, CanvasSerializer.SubqueryInputBridgeNodeId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(connection.ToNodeId, CanvasSerializer.SubqueryInputBridgeNodeId, StringComparison.OrdinalIgnoreCase))];

        if (inputBindings.Count == 0)
            return (editedNodes, editedConnections);

        var bridgeColumns = inputBindings
            .Select(binding => new SavedColumn(binding.BridgePinName, PinDataType.ColumnRef.ToString()))
            .ToList();

        editedNodes.Add(new SavedNode(
            NodeId: CanvasSerializer.SubqueryInputBridgeNodeId,
            NodeType: nameof(NodeType.TableSource),
            X: 40,
            Y: 120,
            ZOrder: null,
            Alias: "outer_inputs",
            TableFullName: "outer_inputs",
            Parameters: [],
            PinLiterals: [],
            Columns: bridgeColumns));

        return (editedNodes, editedConnections);
    }

    private static void PersistSubquerySubgraph(
        NodeViewModel subqueryNode,
        List<SavedNode> editedNodes,
        List<SavedConnection> editedConnections,
        IReadOnlyList<SavedSubqueryInputBinding> inputBindings)
    {
        string? resultOutputNodeId = editedNodes.FirstOrDefault(node =>
            string.Equals(node.NodeType, nameof(NodeType.ResultOutput), StringComparison.OrdinalIgnoreCase))?.NodeId;

        var saved = new SavedSubquerySubgraph(
            editedNodes,
            editedConnections,
            resultOutputNodeId,
            CanvasSerializer.SubqueryInputBridgeNodeId,
            inputBindings.ToList());

        subqueryNode.Parameters[CanvasSerializer.SubquerySubgraphParameterKey] = JsonSerializer.Serialize(saved);
    }

    private static void PersistCteSubgraph(
        ICanvasDomainStrategy domainStrategy,
        NodeViewModel cteNode,
        List<SavedNode> editedNodes,
        List<SavedConnection> editedConnections)
    {
        string? resultOutputNodeId = domainStrategy is ICanvasDomainStrategyExt ext
            ? ext.ResolvePrimaryOutputNodeId(editedNodes)
            : null;

        var subgraph = new SavedCteSubgraph(
            Nodes: editedNodes,
            Connections: editedConnections,
            ResultOutputNodeId: resultOutputNodeId
        );

        cteNode.Parameters[CanvasSerializer.CteSubgraphParameterKey] = JsonSerializer.Serialize(subgraph);
    }

    private static void PersistViewEditorState(
        NodeViewModel viewNode,
        string editorCanvasJson,
        NodeGraph? graph,
        string? fromTable,
        string? selectSql)
    {
        viewNode.Parameters[CanvasSerializer.ViewEditorCanvasParameterKey] = editorCanvasJson;

        if (graph is not null)
            viewNode.Parameters[CanvasSerializer.ViewSubgraphParameterKey] = JsonSerializer.Serialize(graph);

        if (!string.IsNullOrWhiteSpace(fromTable))
            viewNode.Parameters[CanvasSerializer.ViewFromTableParameterKey] = fromTable;

        if (!string.IsNullOrWhiteSpace(selectSql))
            viewNode.Parameters["SelectSql"] = selectSql;
    }

    private void RemoveExistingCteQuerySubgraph(NodeViewModel cteNode)
    {
        if (_domainStrategy is ICanvasDomainStrategyExt ext)
            ext.RemoveExistingCteQuerySubgraph(cteNode, _canvas.Nodes, _canvas.Connections);
    }

    private async Task<CanvasLoadResult> LoadSubEditorCanvasAsync(
        NodeViewModel editorNode,
        List<SavedNode> nodes,
        List<SavedConnection> connections,
        string description)
    {
        if (nodes.Count > 0)
            return CanvasSerializer.Deserialize(
                BuildTemporaryCanvasJson(nodes, connections, description),
                _canvas
            );

        CanvasSnapshot? seed = await _domainStrategy.GetSubEditorSeedAsync(editorNode).ConfigureAwait(true);
        if (seed is null || string.IsNullOrWhiteSpace(seed.JsonPayload))
            return CanvasLoadResult.Fail(
                string.Format(
                    L("main.subEditor.noSeedProvided", "No {0} sub-editor seed was provided."),
                    _domainStrategy.DomainName
                )
            );

        return CanvasSerializer.Deserialize(seed.JsonPayload, _canvas);
    }

    private static string BuildTemporaryCanvasJson(
        List<SavedNode> nodes,
        List<SavedConnection> connections,
        string description)
    {
        var saved = new SavedCanvas(
            Version: CanvasSerializer.CurrentCanvasSchemaVersion,
            DatabaseProvider: "Postgres",
            ConnectionName: "cte-editor",
            Zoom: 1.0, PanX: 0, PanY: 0,
            Nodes: nodes,
            Connections: connections,
            SelectBindings: [],
            WhereBindings: [],
            AppVersion: CanvasSerializer.AppVersion,
            CreatedAt: DateTime.UtcNow.ToString("o"),
            Description: description
        );

        return JsonSerializer.Serialize(saved);
    }

    private string SerializeCteParentSnapshot(CteEditorSession session)
    {
        (List<SavedNode> editedNodes, List<SavedConnection> editedConnections) =
            CanvasSerializer.SerialiseSubgraph(_canvas.Nodes, _canvas.Connections);

        using var parentCanvas = new CanvasViewModel();
        CanvasLoadResult restore = CanvasSerializer.Deserialize(session.ParentCanvasJson, parentCanvas);
        if (!restore.Success)
            return session.ParentCanvasJson;

        NodeViewModel? cteNode = parentCanvas.Nodes.FirstOrDefault(n =>
            n.Id == session.ParentCteNodeId && _domainStrategy.CanEnterSubEditor(n));
        if (cteNode is null)
            return session.ParentCanvasJson;

        if (session.EditorNodeType is NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition)
        {
            PersistSubquerySubgraph(
                cteNode,
                editedNodes,
                editedConnections,
                session.SubqueryInputBindings ?? []);
        }
        else
        {
            if (_domainStrategy is ICanvasDomainStrategyExt ext)
                ext.RemoveExistingCteQuerySubgraph(cteNode, parentCanvas.Nodes, parentCanvas.Connections);

            PersistCteSubgraph(_domainStrategy, cteNode, editedNodes, editedConnections);
        }

        return CanvasSerializer.Serialize(parentCanvas);
    }

    private string SerializeViewParentSnapshot(ViewEditorSession session)
    {
        string editorCanvasJson = CanvasSerializer.Serialize(_canvas);

        QueryGraphBuilder builder = new(_canvas, _canvas.ActiveConnectionConfig?.Provider ?? DatabaseProvider.Postgres);
        bool graphBuilt = builder.TryBuildGraphSnapshot(
            out NodeGraph graph,
            out string? fromTable,
            out string? selectSql,
            out _);

        using var parentCanvas = new CanvasViewModel();
        CanvasLoadResult restore = CanvasSerializer.Deserialize(session.ParentCanvasJson, parentCanvas);
        if (!restore.Success)
            return session.ParentCanvasJson;

        NodeViewModel? viewNode = parentCanvas.Nodes.FirstOrDefault(n =>
            n.Id == session.ParentViewNodeId && _domainStrategy.CanEnterSubEditor(n));
        if (viewNode is null)
            return session.ParentCanvasJson;

        PersistViewEditorState(
            viewNode,
            editorCanvasJson,
            graphBuilt ? graph : null,
            graphBuilt ? fromTable : null,
            graphBuilt ? selectSql : null);

        return CanvasSerializer.Serialize(parentCanvas);
    }

    public async Task RunSubEditorActionSafeAsync(Func<Task<bool>> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            _diagnostics.AddWarning(
                area: L("diagnostics.area.subEditor", "Sub-editor"),
                message: string.Format(
                    L("diagnostics.subEditor.executeFailed", "Failed to execute editor action: {0}"),
                    ex.Message
                ),
                recommendation: L("diagnostics.subEditor.executeRecommendation", "Try again. If it persists, reload the canvas."),
                openPanel: true
            );
        }
    }

    private string L(string key, string fallback)
    {
        string value = _localizationService[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
