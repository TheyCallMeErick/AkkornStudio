using System.Text.Json;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public class ShellWorkspaceDocumentPayloadRestoreTests
{
    [Fact]
    public void RestoreWorkspaceDocuments_PreservesNonCanvasDocumentPayload()
    {
        var shell = new ShellViewModel(
            connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();

        Guid queryId = Guid.NewGuid();
        Guid ddlId = Guid.NewGuid();
        Guid sqlId = Guid.NewGuid();
        JsonElement sqlPayload = JsonSerializer.SerializeToElement(new
        {
            selectedTabId = "tab-restored",
            draftSql = "SELECT now();"
        });

        var workspace = new SavedWorkspaceDocumentsCanvas(
            Version: CanvasSerializer.CurrentSchemaVersion,
            Documents:
            [
                new SavedWorkspaceDocument(
                    DocumentId: queryId,
                    DocumentType: WorkspaceDocumentType.QueryCanvas.ToString(),
                    Title: "Query",
                    IsDirty: false,
                    PersistenceSchemaVersion: "1.0",
                    CanvasPayload: EmptyCanvasPayload()),
                new SavedWorkspaceDocument(
                    DocumentId: ddlId,
                    DocumentType: WorkspaceDocumentType.DdlCanvas.ToString(),
                    Title: "DDL",
                    IsDirty: false,
                    PersistenceSchemaVersion: "1.0",
                    CanvasPayload: EmptyCanvasPayload()),
                new SavedWorkspaceDocument(
                    DocumentId: sqlId,
                    DocumentType: WorkspaceDocumentType.SqlEditor.ToString(),
                    Title: "SQL",
                    IsDirty: true,
                    PersistenceSchemaVersion: "1.0",
                    CanvasPayload: null,
                    DocumentPayload: sqlPayload),
            ],
            ActiveDocumentId: sqlId);

        shell.RestoreWorkspaceDocuments(workspace);

        OpenWorkspaceDocument sqlDocument = Assert.Single(shell.OpenWorkspaceDocuments,
            document => document.Descriptor.DocumentType == WorkspaceDocumentType.SqlEditor);
        Assert.Equal(sqlId, shell.ActiveWorkspaceDocumentId);
        Assert.Equal("tab-restored", sqlDocument.Descriptor.Payload.GetProperty("selectedTabId").GetString());
        Assert.Equal("SELECT now();", sqlDocument.Descriptor.Payload.GetProperty("draftSql").GetString());
    }

    private static SavedCanvas EmptyCanvasPayload()
    {
        return new SavedCanvas(
            Version: CanvasSerializer.CurrentCanvasSchemaVersion,
            DatabaseProvider: "Postgres",
            ConnectionName: "test",
            Zoom: 1,
            PanX: 0,
            PanY: 0,
            Nodes: [],
            Connections: [],
            SelectBindings: [],
            WhereBindings: []);
    }
}
