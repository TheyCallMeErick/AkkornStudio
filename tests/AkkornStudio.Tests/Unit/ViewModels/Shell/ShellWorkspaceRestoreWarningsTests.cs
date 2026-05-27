using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class ShellWorkspaceRestoreWarningsTests
{
    [Fact]
    public void RestoreWorkspaceDocuments_WithInvalidAndDuplicateTypes_ShowsWarningToast()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();

        Guid queryId = Guid.NewGuid();
        var workspace = new SavedWorkspaceDocumentsCanvas(
            Version: 5,
            Documents:
            [
                new SavedWorkspaceDocument(
                    DocumentId: queryId,
                    DocumentType: WorkspaceDocumentType.QueryCanvas.ToString(),
                    Title: "Query A",
                    IsDirty: false,
                    PersistenceSchemaVersion: "1.0",
                    CanvasPayload: EmptyCanvasPayload()),
                new SavedWorkspaceDocument(
                    DocumentId: Guid.NewGuid(),
                    DocumentType: WorkspaceDocumentType.QueryCanvas.ToString(),
                    Title: "Query B",
                    IsDirty: false,
                    PersistenceSchemaVersion: "1.0",
                    CanvasPayload: EmptyCanvasPayload()),
                new SavedWorkspaceDocument(
                    DocumentId: Guid.NewGuid(),
                    DocumentType: "UnknownType",
                    Title: "Broken",
                    IsDirty: false,
                    PersistenceSchemaVersion: "1.0",
                    CanvasPayload: EmptyCanvasPayload()),
            ],
            ActiveDocumentId: queryId);

        shell.RestoreWorkspaceDocuments(workspace);

        Assert.True(shell.Toasts.IsVisible);
        Assert.Equal(ToastSeverity.Warning, shell.Toasts.Severity);
        Assert.Contains("Workspace restaurado com avisos", shell.Toasts.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tipo duplicado", shell.Toasts.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tipo invalido", shell.Toasts.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static SavedCanvas EmptyCanvasPayload()
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
