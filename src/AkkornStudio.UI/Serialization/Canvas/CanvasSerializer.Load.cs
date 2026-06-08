using System.Text.Json;
using Avalonia;
using AkkornStudio.Core;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Serialization;

public static partial class CanvasSerializer
{
    /// <summary>
    /// Rebuilds a <see cref="CanvasViewModel"/> from JSON.
    /// Clears the existing canvas before loading.
    /// Returns a <see cref="CanvasLoadResult"/>.
    /// </summary>
    /// <param name="columnLookup">Optional catalog to restore TableSource column pins.</param>
    public static CanvasLoadResult Deserialize(
        string json,
        CanvasViewModel vm,
        IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>>? columnLookup =
            null
    )
    {
        if (LooksLikeDocumentWorkspaceEnvelope(json) || LooksLikeLegacyWorkspaceEnvelope(json))
            return DeserializeWorkspace(json, vm, null, columnLookup);

        SavedCanvas? raw;
        try
        {
            raw = JsonSerializer.Deserialize<SavedCanvas>(json, _opts);
        }
        catch (JsonException ex)
        {
            return CanvasLoadResult.Fail($"Invalid JSON: {ex.Message}");
        }

        if (raw is null)
            return CanvasLoadResult.Fail("Canvas file is empty or could not be parsed.");

        return DeserializeSavedCanvas(raw, vm, columnLookup);
    }

    public static CanvasLoadResult DeserializeWorkspace(
        string json,
        CanvasViewModel queryVm,
        CanvasViewModel? ddlVm,
        IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>>? columnLookup =
            null
    )
    {
        if (!LooksLikeDocumentWorkspaceEnvelope(json) && !LooksLikeLegacyWorkspaceEnvelope(json))
            return CanvasLoadResult.Fail("Unsupported file format. Expected workspace canvas envelope.");

        if (LooksLikeDocumentWorkspaceEnvelope(json))
            return DeserializeDocumentWorkspace(json, queryVm, ddlVm, columnLookup);

        return DeserializeLegacyWorkspace(json, queryVm, ddlVm, columnLookup);
    }

    private static CanvasLoadResult DeserializeDocumentWorkspace(
        string json,
        CanvasViewModel queryVm,
        CanvasViewModel? ddlVm,
        IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>>? columnLookup)
    {
        SavedWorkspaceDocumentsCanvas? workspace;
        try
        {
            workspace = JsonSerializer.Deserialize<SavedWorkspaceDocumentsCanvas>(json, _opts);
        }
        catch (JsonException ex)
        {
            return CanvasLoadResult.Fail($"Invalid workspace JSON: {ex.Message}");
        }

        if (workspace is null)
            return CanvasLoadResult.Fail("Workspace file is empty or could not be parsed.");

        if (workspace.Version < 5 || workspace.Version > CurrentSchemaVersion)
            return CanvasLoadResult.Fail(
                $"Unsupported workspace schema version {workspace.Version}. "
                    + $"This build supports versions 5–{CurrentSchemaVersion}."
            );

        List<SavedWorkspaceDocument> documents = workspace.Documents ?? [];
        if (documents.Count == 0)
            return CanvasLoadResult.Fail("Workspace does not contain any document.");

        SavedWorkspaceDocument? queryDocument = documents.FirstOrDefault(document =>
            IsDocumentType(document, WorkspaceDocumentType.QueryCanvas));
        if (queryDocument?.CanvasPayload is null)
            return CanvasLoadResult.Fail("Workspace query document payload is missing.");

        CanvasLoadResult queryLoad = DeserializeSavedCanvas(
            queryDocument.CanvasPayload,
            queryVm,
            columnLookup,
            CanvasNodeFamily.Query
        );
        if (!queryLoad.Success)
            return queryLoad;

        var warnings = queryLoad.Warnings?.ToList() ?? [];

        SavedWorkspaceDocument? ddlDocument = documents.FirstOrDefault(document =>
            IsDocumentType(document, WorkspaceDocumentType.DdlCanvas));
        if (ddlDocument?.CanvasPayload is not null && ddlVm is not null)
        {
            CanvasLoadResult ddlLoad = ApplyDdlFromSavedCanvas(ddlDocument.CanvasPayload, ddlVm);
            if (!ddlLoad.Success)
                warnings.Add($"DDL canvas restore skipped: {ddlLoad.Error}");
            else if (ddlLoad.Warnings is { Count: > 0 })
                warnings.AddRange(ddlLoad.Warnings.Select(w => $"DDL: {w}"));
        }
        else if (ddlDocument?.CanvasPayload is not null)
        {
            warnings.Add("File contains a DDL document snapshot, but no DDL target canvas was provided during load.");
        }

        WorkspaceDocumentType activeDocumentType = ResolveActiveDocumentType(workspace, documents, warnings);
        return CanvasLoadResult.Ok(
            warnings.Count > 0 ? warnings : null,
            activeDocumentType,
            queryLoad.SqlEditorSeedScripts);
    }

    private static CanvasLoadResult DeserializeLegacyWorkspace(
        string json,
        CanvasViewModel queryVm,
        CanvasViewModel? ddlVm,
        IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>>? columnLookup)
    {
        SavedWorkspaceCanvas? workspace;
        try
        {
            workspace = JsonSerializer.Deserialize<SavedWorkspaceCanvas>(json, _opts);
        }
        catch (JsonException ex)
        {
            return CanvasLoadResult.Fail($"Invalid workspace JSON: {ex.Message}");
        }

        if (workspace is null)
            return CanvasLoadResult.Fail("Workspace file is empty or could not be parsed.");

        if (workspace.Version != 4)
            return CanvasLoadResult.Fail(
                $"Unsupported workspace schema version {workspace.Version}. "
                    + "This build supports legacy version 4 and document schema v5+."
            );

        CanvasLoadResult queryLoad = DeserializeSavedCanvas(
            workspace.QueryCanvas,
            queryVm,
            columnLookup,
            CanvasNodeFamily.Query
        );
        if (!queryLoad.Success)
            return queryLoad;

        var warnings = queryLoad.Warnings?.ToList() ?? [];

        if (ddlVm is not null)
        {
            CanvasLoadResult ddlLoad = ApplyDdlFromSavedCanvas(workspace.DdlCanvas, ddlVm);
            if (!ddlLoad.Success)
                warnings.Add($"DDL canvas restore skipped: {ddlLoad.Error}");
            else if (ddlLoad.Warnings is { Count: > 0 })
                warnings.AddRange(ddlLoad.Warnings.Select(w => $"DDL: {w}"));
        }
        else if (workspace.DdlCanvas.Nodes.Count > 0 || workspace.DdlCanvas.Connections.Count > 0)
        {
            warnings.Add("File contains a DDL canvas snapshot, but no DDL target canvas was provided during load.");
        }

        warnings.Add("Legacy workspace envelope migrated to document-oriented workspace model during load.");
        WorkspaceDocumentType activeType = ResolveLegacyActiveDocumentType(workspace);
        return CanvasLoadResult.Ok(
            warnings.Count > 0 ? warnings : null,
            activeType,
            queryLoad.SqlEditorSeedScripts);
    }

    private static CanvasLoadResult ApplyDdlFromSavedCanvas(
        SavedCanvas saved,
        CanvasViewModel ddlVm
    )
    {
        var scratch = new CanvasViewModel();
        scratch.Nodes.Clear();
        scratch.Connections.Clear();

        CanvasLoadResult load = DeserializeSavedCanvas(saved, scratch, null, CanvasNodeFamily.Ddl);
        if (!load.Success)
            return load;

        ddlVm.ReplaceGraph(scratch.Nodes.ToList(), scratch.Connections.ToList());
        if (Enum.TryParse(saved.DatabaseProvider, true, out DatabaseProvider provider))
            ddlVm.Provider = provider;

        return load;
    }

    private static CanvasLoadResult DeserializeSavedCanvas(
        SavedCanvas raw,
        CanvasViewModel vm,
        IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>>? columnLookup,
        CanvasNodeFamily allowedFamily = CanvasNodeFamily.Any
    )
    {
        if (raw.Version != CurrentCanvasSchemaVersion)
            return CanvasLoadResult.Fail(
                $"Unsupported canvas schema version {raw.Version}. "
                    + $"This build supports only version {CurrentCanvasSchemaVersion}."
            );

        SavedCanvas saved = raw;
        var warnings = new List<string>();
        IReadOnlyList<string> sqlEditorSeedScripts = allowedFamily == CanvasNodeFamily.Query
            ? ExtractLegacyReportSqlScripts(saved)
            : [];
        if (sqlEditorSeedScripts.Count > 0)
        {
            warnings.Add(
                $"Migrated {sqlEditorSeedScripts.Count} legacy report SQL script(s) to SQL Editor seed drafts during load.");
        }

        vm.Connections.Clear();
        vm.Nodes.Clear();
        vm.UndoRedo.Clear();

        vm.Zoom = saved.Zoom;
        vm.PanOffset = new Point(saved.PanX, saved.PanY);
        vm.ReplacePreviewParameterInputs(saved.PreviewParameterInputs);

        var nodeMap = new Dictionary<string, NodeViewModel>(StringComparer.Ordinal);
        var skippedNodes = new List<(string NodeId, string NodeType, string Reason)>();

        foreach (SavedNode sn in saved.Nodes)
        {
            (NodeViewModel? nodeVm, string? skipReason) = BuildNodeVm(sn, columnLookup, allowedFamily);
            if (nodeVm is null)
            {
                skippedNodes.Add((sn.NodeId, sn.NodeType, skipReason ?? "Unknown error"));
                continue;
            }

            ValidateEmbeddedSubgraphPayloads(nodeVm, warnings);
            nodeMap[sn.NodeId] = nodeVm;
            vm.Nodes.Add(nodeVm);
        }

        var loadOrderById = saved.Nodes
            .Select((n, i) => (n.NodeId, i))
            .ToDictionary(x => x.NodeId, x => x.i, StringComparer.Ordinal);

        int z = 0;
        foreach (NodeViewModel n in vm.Nodes
                     .OrderBy(n => n.ZOrder)
                     .ThenBy(n => loadOrderById.TryGetValue(n.Id, out int idx) ? idx : int.MaxValue))
            n.ZOrder = z++;

        if (skippedNodes.Count > 0)
        {
            var skippedSummary = string.Join(", ",
                skippedNodes.GroupBy(s => s.NodeType)
                    .Select(g => $"{g.Count()} {g.Key}"));
            warnings.Add(
                $"Warning: {skippedNodes.Count} node(s) could not be loaded and were skipped: {skippedSummary}. " +
                "This may indicate the file was created with a newer version or has unsupported node types."
            );

            int familyFiltered = skippedNodes.Count(s =>
                s.Reason.Contains("canvas family mismatch", StringComparison.OrdinalIgnoreCase)
            );
            if (familyFiltered > 0)
            {
                string target = allowedFamily == CanvasNodeFamily.Ddl ? "DDL" : "Query";
                warnings.Add(
                    $"Skipped {familyFiltered} legacy node(s) that belong to the opposite canvas family while loading {target} canvas."
                );
            }
        }

        ConnectionRebuildStats connectionStats = RebuildConnections(saved.Connections, nodeMap, vm.Connections);
        AddConnectionRebuildWarnings(connectionStats, warnings);

        foreach (SavedNode sn in saved.Nodes)
        {
            if (!nodeMap.TryGetValue(sn.NodeId, out NodeViewModel? nodeVm))
                continue;
            if (nodeVm.Type == NodeType.ResultOutput)
            {
                nodeVm.SyncOutputColumns(vm.Connections);

                if (!sn.Parameters.TryGetValue("__colOrder", out string? colOrderStr))
                    continue;

                string[] savedOrder = colOrderStr.Split('|', StringSplitOptions.RemoveEmptyEntries);
                ApplySavedColumnOrder(nodeVm, savedOrder);
                continue;
            }

            if (nodeVm.Type == NodeType.CteSource)
                nodeVm.SyncCteSourceColumns(vm.Connections);
        }

        return CanvasLoadResult.Ok(
            warnings.Count > 0 ? warnings : null,
            activeDocumentType: null,
            sqlEditorSeedScripts: sqlEditorSeedScripts.Count > 0 ? sqlEditorSeedScripts : null);
    }

    private static void ValidateEmbeddedSubgraphPayloads(NodeViewModel nodeVm, ICollection<string> warnings)
    {
        ValidateEmbeddedCtePayload(nodeVm, warnings);

        ValidateEmbeddedPayload(
            nodeVm,
            SubquerySubgraphParameterKey,
            payload => JsonSerializer.Deserialize<SavedSubquerySubgraph>(payload, _opts) is not null,
            warnings);

        ValidateEmbeddedPayload(
            nodeVm,
            ViewSubgraphParameterKey,
            payload =>
            {
                using JsonDocument _ = JsonDocument.Parse(payload);
                return true;
            },
            warnings);

        ValidateEmbeddedPayload(
            nodeVm,
            ViewEditorCanvasParameterKey,
            payload =>
            {
                using JsonDocument _ = JsonDocument.Parse(payload);
                return true;
            },
            warnings);
    }

    private static void ValidateEmbeddedCtePayload(NodeViewModel nodeVm, ICollection<string> warnings)
    {
        if (!nodeVm.Parameters.TryGetValue(CteSubgraphParameterKey, out string? payload)
            || string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            SavedCteSubgraph? subgraph = JsonSerializer.Deserialize<SavedCteSubgraph>(payload, _opts);
            if (subgraph is null)
                throw new JsonException("CTE subgraph payload is empty.");

            if (TryGetCteSubgraphDepth(subgraph, out int depth) && depth > MaxCteSubgraphDepth)
            {
                nodeVm.Parameters.Remove(CteSubgraphParameterKey);
                warnings.Add(
                    $"Node '{nodeVm.Id}' has CTE subgraph depth {depth} exceeding supported limit {MaxCteSubgraphDepth}. The payload was ignored during load.");
                return;
            }

            return;
        }
        catch
        {
            // Fall through to remove malformed payload.
        }

        nodeVm.Parameters.Remove(CteSubgraphParameterKey);
        warnings.Add(
            $"Node '{nodeVm.Id}' has malformed embedded payload '{CteSubgraphParameterKey}'. The payload was ignored during load."
        );
    }

    private static void ValidateEmbeddedPayload(
        NodeViewModel nodeVm,
        string parameterKey,
        Func<string, bool> validator,
        ICollection<string> warnings)
    {
        if (!nodeVm.Parameters.TryGetValue(parameterKey, out string? payload)
            || string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            if (validator(payload))
                return;
        }
        catch
        {
            // Fall through to remove malformed payload.
        }

        nodeVm.Parameters.Remove(parameterKey);
        warnings.Add(
            $"Node '{nodeVm.Id}' has malformed embedded payload '{parameterKey}'. The payload was ignored during load."
        );
    }

    private static IReadOnlyList<string> ExtractLegacyReportSqlScripts(SavedCanvas saved)
    {
        Dictionary<string, SavedNode> nodesById = saved.Nodes
            .ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        HashSet<string> scripts = [];
        foreach (SavedNode reportOutput in saved.Nodes.Where(node =>
                 string.Equals(node.NodeType, "ReportOutput", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (SavedConnection wire in saved.Connections.Where(connection =>
                         string.Equals(connection.ToNodeId, reportOutput.NodeId, StringComparison.Ordinal)
                         && string.Equals(connection.ToPinName, "query", StringComparison.OrdinalIgnoreCase)))
            {
                if (!nodesById.TryGetValue(wire.FromNodeId, out SavedNode? sourceNode))
                    continue;
                if (!string.Equals(sourceNode.NodeType, "RawSqlQuery", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? sql = ResolveLegacyReportSql(sourceNode);
                if (!string.IsNullOrWhiteSpace(sql))
                    scripts.Add(sql.Trim());
            }
        }

        return scripts.ToList();
    }

    private static string? ResolveLegacyReportSql(SavedNode rawSqlNode)
    {
        if (rawSqlNode.Parameters.TryGetValue("sql_text", out string? sqlText)
            && !string.IsNullOrWhiteSpace(sqlText))
        {
            return sqlText;
        }

        if (rawSqlNode.Parameters.TryGetValue("sql", out string? sql)
            && !string.IsNullOrWhiteSpace(sql))
        {
            return sql;
        }

        return null;
    }

    private static bool LooksLikeLegacyWorkspaceEnvelope(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(nameof(SavedWorkspaceCanvas.QueryCanvas), out _)
                && root.TryGetProperty(nameof(SavedWorkspaceCanvas.DdlCanvas), out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeDocumentWorkspaceEnvelope(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(nameof(SavedWorkspaceDocumentsCanvas.Documents), out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDocumentType(SavedWorkspaceDocument document, WorkspaceDocumentType documentType)
    {
        return string.Equals(document.DocumentType, documentType.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static WorkspaceDocumentType ResolveActiveDocumentType(
        SavedWorkspaceDocumentsCanvas workspace,
        IReadOnlyList<SavedWorkspaceDocument> documents,
        ICollection<string> warnings)
    {
        if (workspace.ActiveDocumentId is not Guid activeId)
            return WorkspaceDocumentType.QueryCanvas;

        SavedWorkspaceDocument? active = documents.FirstOrDefault(document => document.DocumentId == activeId);
        if (active is null)
        {
            warnings.Add(
                $"Workspace active document id '{activeId}' was not found; defaulted active document to Query canvas.");
            return WorkspaceDocumentType.QueryCanvas;
        }

        if (!Enum.TryParse(active.DocumentType, true, out WorkspaceDocumentType activeType))
        {
            warnings.Add(
                $"Workspace active document '{active.DocumentId}' has unknown type '{active.DocumentType}'; defaulted active document to Query canvas.");
            return WorkspaceDocumentType.QueryCanvas;
        }

        return activeType;
    }

    private static WorkspaceDocumentType ResolveLegacyActiveDocumentType(SavedWorkspaceCanvas workspace)
    {
        bool hasQueryContent = workspace.QueryCanvas.Nodes.Count > 0 || workspace.QueryCanvas.Connections.Count > 0;
        bool hasDdlContent = workspace.DdlCanvas.Nodes.Count > 0 || workspace.DdlCanvas.Connections.Count > 0;

        if (!hasQueryContent && hasDdlContent)
            return WorkspaceDocumentType.DdlCanvas;

        return WorkspaceDocumentType.QueryCanvas;
    }
}
