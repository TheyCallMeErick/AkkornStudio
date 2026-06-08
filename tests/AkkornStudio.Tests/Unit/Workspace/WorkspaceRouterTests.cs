using System.Text.Json;
using AkkornStudio.UI.Services.Workspace;
using AkkornStudio.UI.Services.Workspace.Models;

namespace AkkornStudio.Tests.Unit.Workspace;

public class WorkspaceRouterTests
{
    [Fact]
    public void OpenDocument_RegistersDocumentAndSetsActiveDocument()
    {
        var router = new WorkspaceRouter();
        OpenWorkspaceDocument queryDocument = CreateDocument(WorkspaceDocumentType.QueryCanvas, "Query");

        router.OpenDocument(queryDocument);

        Assert.Single(router.OpenDocuments);
        Assert.Equal(queryDocument.Descriptor.DocumentId, router.ActiveDocumentId);
        Assert.Same(queryDocument, router.ActiveDocument);
    }

    [Fact]
    public void TryActivateByType_ActivatesMatchingDocument()
    {
        var router = new WorkspaceRouter();
        OpenWorkspaceDocument queryDocument = CreateDocument(WorkspaceDocumentType.QueryCanvas, "Query");
        OpenWorkspaceDocument ddlDocument = CreateDocument(WorkspaceDocumentType.DdlCanvas, "DDL");
        router.OpenDocument(queryDocument);
        router.OpenDocument(ddlDocument);

        bool changed = router.TryActivateByType(WorkspaceDocumentType.QueryCanvas);

        Assert.True(changed);
        Assert.Equal(queryDocument.Descriptor.DocumentId, router.ActiveDocumentId);
    }

    [Fact]
    public void TryClose_WhenClosingActiveDocument_SelectsAdjacentDocumentDeterministically()
    {
        var router = new WorkspaceRouter();
        OpenWorkspaceDocument queryDocument = CreateDocument(WorkspaceDocumentType.QueryCanvas, "Query");
        OpenWorkspaceDocument ddlDocument = CreateDocument(WorkspaceDocumentType.DdlCanvas, "DDL");
        OpenWorkspaceDocument sqlEditorDocument = CreateDocument(WorkspaceDocumentType.SqlEditor, "SQL");
        router.OpenDocument(queryDocument);
        router.OpenDocument(ddlDocument);
        router.OpenDocument(sqlEditorDocument);

        bool closed = router.TryClose(ddlDocument.Descriptor.DocumentId);

        Assert.True(closed);
        Assert.Equal(sqlEditorDocument.Descriptor.DocumentId, router.ActiveDocumentId);
        Assert.Equal(2, router.OpenDocuments.Count);
    }

    [Fact]
    public void TryActivate_WhenDocumentIdDoesNotExist_DoesNotChangeActiveDocument()
    {
        var router = new WorkspaceRouter();
        OpenWorkspaceDocument queryDocument = CreateDocument(WorkspaceDocumentType.QueryCanvas, "Query");
        OpenWorkspaceDocument ddlDocument = CreateDocument(WorkspaceDocumentType.DdlCanvas, "DDL");
        router.OpenDocument(queryDocument);
        router.OpenDocument(ddlDocument);
        Guid activeBefore = router.ActiveDocumentId ?? Guid.Empty;

        bool changed = router.TryActivate(Guid.NewGuid());

        Assert.False(changed);
        Assert.Equal(activeBefore, router.ActiveDocumentId);
    }

    [Fact]
    public void TryActivate_SwitchingDocuments_PreservesDirtyFlagInBackgroundDocument()
    {
        var router = new WorkspaceRouter();
        OpenWorkspaceDocument queryDirty = CreateDocument(
            WorkspaceDocumentType.QueryCanvas,
            "Query",
            isDirty: true);
        OpenWorkspaceDocument ddlClean = CreateDocument(
            WorkspaceDocumentType.DdlCanvas,
            "DDL",
            isDirty: false);

        router.OpenDocument(queryDirty);
        router.OpenDocument(ddlClean);

        bool changed = router.TryActivate(queryDirty.Descriptor.DocumentId);

        Assert.True(changed);
        OpenWorkspaceDocument reopenedQuery = Assert.Single(router.OpenDocuments,
            document => document.Descriptor.DocumentId == queryDirty.Descriptor.DocumentId);
        Assert.True(reopenedQuery.Descriptor.IsDirty);
    }

    [Fact]
    public void ReplaceDocuments_WhenActiveIdIsInvalid_PrefersQueryCanvasAsFallback()
    {
        var router = new WorkspaceRouter();
        OpenWorkspaceDocument ddlDocument = CreateDocument(WorkspaceDocumentType.DdlCanvas, "DDL");
        OpenWorkspaceDocument queryDocument = CreateDocument(WorkspaceDocumentType.QueryCanvas, "Query");

        router.ReplaceDocuments(
            [ddlDocument, queryDocument],
            activeDocumentId: Guid.NewGuid());

        Assert.Equal(queryDocument.Descriptor.DocumentId, router.ActiveDocumentId);
    }

    private static OpenWorkspaceDocument CreateDocument(
        WorkspaceDocumentType type,
        string title,
        bool isDirty = false)
    {
        WorkspaceDocumentDescriptor descriptor = new(
            DocumentId: Guid.NewGuid(),
            DocumentType: type,
            Title: title,
            IsDirty: isDirty,
            PersistenceSchemaVersion: "1.0",
            Payload: JsonSerializer.SerializeToElement(new { }));

        return new OpenWorkspaceDocument(
            Descriptor: descriptor,
            DocumentViewModel: new object(),
            PageViewModel: null,
            PageState: null);
    }
}
