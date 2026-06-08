using System.Text.Json;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;

namespace AkkornStudio.Tests.Unit.Serialization;

public class CanvasSerializerDocumentWorkspaceSchemaTests
{
    [Fact]
    public void SerializeWorkspace_WritesDocumentCollectionSchema()
    {
        using var queryVm = new CanvasViewModel();
        using var ddlVm = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());

        string json = CanvasSerializer.SerializeWorkspace(
            queryVm,
            ddlVm,
            activeDocumentType: WorkspaceDocumentType.SqlEditor);

        SavedWorkspaceDocumentsCanvas? saved = JsonSerializer.Deserialize<SavedWorkspaceDocumentsCanvas>(json);
        Assert.NotNull(saved);
        Assert.Equal(CanvasSerializer.CurrentSchemaVersion, saved!.Version);
        Assert.NotNull(saved.ActiveDocumentId);
        Assert.NotEmpty(saved.Documents);
        Assert.Contains(saved.Documents, document =>
            string.Equals(document.DocumentType, WorkspaceDocumentType.QueryCanvas.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(saved.Documents, document =>
            string.Equals(document.DocumentType, WorkspaceDocumentType.DdlCanvas.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(saved.Documents, document =>
            string.Equals(document.DocumentType, WorkspaceDocumentType.SqlEditor.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeserializeWorkspace_LegacyV4_UsesDeterministicActiveDocumentPolicy()
    {
        using var queryVm = new CanvasViewModel();
        using var ddlVm = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());
        ddlVm.SpawnNode(NodeDefinitionRegistry.Get(NodeType.TableDefinition), new Point(40, 40));

        SavedCanvas queryCanvas = JsonSerializer.Deserialize<SavedCanvas>(CanvasSerializer.Serialize(queryVm))!;
        SavedCanvas ddlCanvas = JsonSerializer.Deserialize<SavedCanvas>(CanvasSerializer.Serialize(ddlVm))!;

        string legacyJson = JsonSerializer.Serialize(new SavedWorkspaceCanvas(
            Version: 4,
            QueryCanvas: queryCanvas,
            DdlCanvas: ddlCanvas));

        CanvasLoadResult result = CanvasSerializer.DeserializeWorkspace(legacyJson, queryVm, ddlVm);

        Assert.True(result.Success);
        Assert.Equal(WorkspaceDocumentType.DdlCanvas, result.ActiveDocumentType);
    }

    [Fact]
    public void DeserializeWorkspace_DocumentSchema_WithUnknownActiveDocumentId_DefaultsToQueryWithWarning()
    {
        using var queryVm = new CanvasViewModel();
        using var ddlVm = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());

        SavedCanvas queryCanvas = JsonSerializer.Deserialize<SavedCanvas>(CanvasSerializer.Serialize(queryVm))!;
        SavedCanvas ddlCanvas = JsonSerializer.Deserialize<SavedCanvas>(CanvasSerializer.Serialize(ddlVm))!;

        Guid queryId = Guid.NewGuid();
        Guid ddlId = Guid.NewGuid();
        Guid unknownActiveId = Guid.NewGuid();

        var workspace = new SavedWorkspaceDocumentsCanvas(
            Version: CanvasSerializer.CurrentSchemaVersion,
            Documents:
            [
                new SavedWorkspaceDocument(
                    DocumentId: queryId,
                    DocumentType: WorkspaceDocumentType.QueryCanvas.ToString(),
                    Title: "Query",
                    IsDirty: false,
                    PersistenceSchemaVersion: "v1",
                    CanvasPayload: queryCanvas),
                new SavedWorkspaceDocument(
                    DocumentId: ddlId,
                    DocumentType: WorkspaceDocumentType.DdlCanvas.ToString(),
                    Title: "DDL",
                    IsDirty: false,
                    PersistenceSchemaVersion: "v1",
                    CanvasPayload: ddlCanvas),
            ],
            ActiveDocumentId: unknownActiveId
        );

        string json = JsonSerializer.Serialize(workspace);
        CanvasLoadResult result = CanvasSerializer.DeserializeWorkspace(json, queryVm, ddlVm);

        Assert.True(result.Success, result.Error);
        Assert.Equal(WorkspaceDocumentType.QueryCanvas, result.ActiveDocumentType);
        Assert.Contains(result.Warnings ?? [], warning =>
            warning.Contains("active document id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeserializeWorkspace_DocumentSchema_WithUnknownActiveDocumentType_DefaultsToQueryWithWarning()
    {
        using var queryVm = new CanvasViewModel();
        using var ddlVm = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());

        SavedCanvas queryCanvas = JsonSerializer.Deserialize<SavedCanvas>(CanvasSerializer.Serialize(queryVm))!;

        Guid queryId = Guid.NewGuid();
        Guid invalidId = Guid.NewGuid();

        var workspace = new SavedWorkspaceDocumentsCanvas(
            Version: CanvasSerializer.CurrentSchemaVersion,
            Documents:
            [
                new SavedWorkspaceDocument(
                    DocumentId: queryId,
                    DocumentType: WorkspaceDocumentType.QueryCanvas.ToString(),
                    Title: "Query",
                    IsDirty: false,
                    PersistenceSchemaVersion: "v1",
                    CanvasPayload: queryCanvas),
                new SavedWorkspaceDocument(
                    DocumentId: invalidId,
                    DocumentType: "InvalidDocType",
                    Title: "Broken",
                    IsDirty: false,
                    PersistenceSchemaVersion: "v1",
                    CanvasPayload: null),
            ],
            ActiveDocumentId: invalidId
        );

        string json = JsonSerializer.Serialize(workspace);
        CanvasLoadResult result = CanvasSerializer.DeserializeWorkspace(json, queryVm, ddlVm);

        Assert.True(result.Success, result.Error);
        Assert.Equal(WorkspaceDocumentType.QueryCanvas, result.ActiveDocumentType);
        Assert.Contains(result.Warnings ?? [], warning =>
            warning.Contains("unknown type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeserializeWorkspace_LegacyReportFlow_MigratesSqlScriptsToSqlEditorSeeds()
    {
        using var queryVm = new CanvasViewModel();
        using var ddlVm = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());

        var queryCanvas = new SavedCanvas(
            Version: CanvasSerializer.CurrentCanvasSchemaVersion,
            DatabaseProvider: "Postgres",
            ConnectionName: "legacy",
            Zoom: 1,
            PanX: 0,
            PanY: 0,
            Nodes:
            [
                new SavedNode(
                    NodeId: "raw_1",
                    NodeType: "RawSqlQuery",
                    X: 40,
                    Y: 40,
                    ZOrder: 0,
                    Alias: null,
                    TableFullName: null,
                    Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sql"] = "SELECT 42 AS answer"
                    },
                    PinLiterals: []),
                new SavedNode(
                    NodeId: "report_1",
                    NodeType: "ReportOutput",
                    X: 280,
                    Y: 40,
                    ZOrder: 1,
                    Alias: null,
                    TableFullName: null,
                    Parameters: [],
                    PinLiterals: []),
            ],
            Connections:
            [
                new SavedConnection(
                    FromNodeId: "raw_1",
                    FromPinName: "query",
                    ToNodeId: "report_1",
                    ToPinName: "query")
            ],
            SelectBindings: [],
            WhereBindings: []
        );

        var ddlCanvas = new SavedCanvas(
            Version: CanvasSerializer.CurrentCanvasSchemaVersion,
            DatabaseProvider: null,
            ConnectionName: null,
            Zoom: 1,
            PanX: 0,
            PanY: 0,
            Nodes: [],
            Connections: [],
            SelectBindings: [],
            WhereBindings: []
        );

        string legacyJson = JsonSerializer.Serialize(new SavedWorkspaceCanvas(
            Version: 4,
            QueryCanvas: queryCanvas,
            DdlCanvas: ddlCanvas));

        CanvasLoadResult result = CanvasSerializer.DeserializeWorkspace(legacyJson, queryVm, ddlVm);

        Assert.True(result.Success);
        Assert.Empty(queryVm.Nodes);
        Assert.NotNull(result.SqlEditorSeedScripts);
        Assert.Contains("SELECT 42 AS answer", result.SqlEditorSeedScripts!);
        Assert.Contains(result.Warnings ?? [], warning =>
            warning.Contains("legacy report SQL script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SerializeWorkspaceDocuments_PreservesNonCanvasDocumentPayload()
    {
        JsonElement sqlPayload = JsonSerializer.SerializeToElement(new
        {
            selectedTabId = "tab-1",
            draftSql = "SELECT 1;"
        });
        WorkspaceDocumentDescriptor sqlDescriptor = new(
            DocumentId: Guid.NewGuid(),
            DocumentType: WorkspaceDocumentType.SqlEditor,
            Title: "SQL Editor",
            IsDirty: true,
            PersistenceSchemaVersion: "1.0",
            Payload: sqlPayload);
        OpenWorkspaceDocument sqlDocument = new(
            Descriptor: sqlDescriptor,
            DocumentViewModel: new object(),
            PageViewModel: null,
            PageState: null);

        string workspaceJson = CanvasSerializer.SerializeWorkspaceDocuments(
            [sqlDocument],
            activeDocumentId: sqlDescriptor.DocumentId);

        SavedWorkspaceDocumentsCanvas? saved = JsonSerializer.Deserialize<SavedWorkspaceDocumentsCanvas>(workspaceJson);
        Assert.NotNull(saved);
        SavedWorkspaceDocument sqlSaved = Assert.Single(saved!.Documents);
        Assert.Equal(WorkspaceDocumentType.SqlEditor.ToString(), sqlSaved.DocumentType);
        Assert.NotNull(sqlSaved.DocumentPayload);
        Assert.Equal("tab-1", sqlSaved.DocumentPayload!.Value.GetProperty("selectedTabId").GetString());
        Assert.Equal("SELECT 1;", sqlSaved.DocumentPayload!.Value.GetProperty("draftSql").GetString());
    }
}
