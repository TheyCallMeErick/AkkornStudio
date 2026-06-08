using System.Text.Json;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Workspace;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class ShellWorkspaceDocumentIdHardeningTests
{
    [Fact]
    public void Constructor_WithPreloadedQueryDocument_ReusesExistingQueryIdWithoutDuplication()
    {
        var workspaceRouter = new WorkspaceRouter();
        var queryCanvas = new CanvasViewModel();
        Guid queryId = Guid.NewGuid();
        workspaceRouter.OpenDocument(new OpenWorkspaceDocument(
            Descriptor: BuildDescriptor(queryId, WorkspaceDocumentType.QueryCanvas, "Query Canvas"),
            DocumentViewModel: queryCanvas,
            PageViewModel: queryCanvas,
            PageState: null),
            activate: true);

        _ = new ShellViewModel(
            canvas: queryCanvas,
            workspaceRouter: workspaceRouter,
            connectionManagerViewModelFactory: ConnectionManagerViewModelFactory.CreateDefault());

        OpenWorkspaceDocument queryDocument = Assert.Single(workspaceRouter.OpenDocuments
            .Where(document => document.Descriptor.DocumentType == WorkspaceDocumentType.QueryCanvas));
        Assert.Equal(queryId, queryDocument.Descriptor.DocumentId);
        Assert.Same(queryCanvas, queryDocument.DocumentViewModel);
    }

    [Fact]
    public void EnsureDdlCanvas_WithPreloadedDdlDocument_ReusesExistingIdWithoutDuplication()
    {
        var workspaceRouter = new WorkspaceRouter();
        var ddlCanvas = new CanvasViewModel();
        Guid ddlId = Guid.NewGuid();
        workspaceRouter.OpenDocument(new OpenWorkspaceDocument(
            Descriptor: BuildDescriptor(ddlId, WorkspaceDocumentType.DdlCanvas, "DDL Canvas"),
            DocumentViewModel: ddlCanvas,
            PageViewModel: ddlCanvas,
            PageState: null),
            activate: false);

        var shell = new ShellViewModel(
            workspaceRouter: workspaceRouter,
            connectionManagerViewModelFactory: ConnectionManagerViewModelFactory.CreateDefault());

        CanvasViewModel resolved = shell.EnsureDdlCanvas();

        OpenWorkspaceDocument ddlDocument = Assert.Single(workspaceRouter.OpenDocuments
            .Where(document => document.Descriptor.DocumentType == WorkspaceDocumentType.DdlCanvas));
        Assert.Equal(ddlId, ddlDocument.Descriptor.DocumentId);
        Assert.Same(ddlCanvas, ddlDocument.DocumentViewModel);
        Assert.Same(ddlCanvas, resolved);
    }

    [Fact]
    public void RestoreWorkspaceDocuments_SecondRestoreWithoutDdl_DoesNotReuseStaleDdlId()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();

        Guid firstQueryId = Guid.NewGuid();
        Guid firstDdlId = Guid.NewGuid();
        shell.RestoreWorkspaceDocuments(new SavedWorkspaceDocumentsCanvas(
            Version: 5,
            Documents:
            [
                new SavedWorkspaceDocument(firstQueryId, WorkspaceDocumentType.QueryCanvas.ToString(), "Query 1", false, "1.0", EmptyCanvas()),
                new SavedWorkspaceDocument(firstDdlId, WorkspaceDocumentType.DdlCanvas.ToString(), "DDL 1", false, "1.0", EmptyCanvas()),
            ],
            ActiveDocumentId: firstQueryId));

        Guid secondQueryId = Guid.NewGuid();
        shell.RestoreWorkspaceDocuments(new SavedWorkspaceDocumentsCanvas(
            Version: 5,
            Documents:
            [
                new SavedWorkspaceDocument(secondQueryId, WorkspaceDocumentType.QueryCanvas.ToString(), "Query 2", false, "1.0", EmptyCanvas()),
            ],
            ActiveDocumentId: secondQueryId));

        Assert.DoesNotContain(shell.OpenWorkspaceDocuments, document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.DdlCanvas);

        CanvasViewModel ddlCanvas = shell.EnsureDdlCanvas();

        OpenWorkspaceDocument ddlDocument = Assert.Single(shell.OpenWorkspaceDocuments
            .Where(document => document.Descriptor.DocumentType == WorkspaceDocumentType.DdlCanvas));
        Assert.NotEqual(firstDdlId, ddlDocument.Descriptor.DocumentId);
        Assert.Same(ddlCanvas, ddlDocument.DocumentViewModel);
    }

    private static WorkspaceDocumentDescriptor BuildDescriptor(Guid id, WorkspaceDocumentType type, string title)
    {
        return new WorkspaceDocumentDescriptor(
            DocumentId: id,
            DocumentType: type,
            Title: title,
            IsDirty: false,
            PersistenceSchemaVersion: "1.0",
            Payload: JsonSerializer.SerializeToElement(new { }));
    }

    private static SavedCanvas EmptyCanvas()
    {
        return new SavedCanvas(
            Version: 3,
            DatabaseProvider: null,
            ConnectionName: null,
            Zoom: 1,
            PanX: 0,
            PanY: 0,
            Nodes: [],
            Connections: [],
            SelectBindings: [],
            WhereBindings: [],
            AppVersion: "test");
    }
}
