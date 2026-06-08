using AkkornStudio.UI.Services.Workspace.Models;

namespace AkkornStudio.UI.Services.Workspace;

public sealed class WorkspaceRouter : IWorkspaceRouter
{
    private readonly List<OpenWorkspaceDocument> _openDocuments = [];

    public IReadOnlyList<OpenWorkspaceDocument> OpenDocuments => _openDocuments;

    public Guid? ActiveDocumentId { get; private set; }

    public OpenWorkspaceDocument? ActiveDocument =>
        ActiveDocumentId.HasValue
            ? _openDocuments.FirstOrDefault(document => document.Descriptor.DocumentId == ActiveDocumentId.Value)
            : null;

    public void OpenDocument(OpenWorkspaceDocument document, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(document);

        int existingIndex = _openDocuments.FindIndex(existing =>
            existing.Descriptor.DocumentId == document.Descriptor.DocumentId);
        if (existingIndex >= 0)
            _openDocuments[existingIndex] = document;
        else
            _openDocuments.Add(document);

        if (activate)
            ActiveDocumentId = document.Descriptor.DocumentId;
    }

    public bool TryActivate(Guid documentId)
    {
        bool exists = _openDocuments.Any(document => document.Descriptor.DocumentId == documentId);
        if (!exists || ActiveDocumentId == documentId)
            return false;

        ActiveDocumentId = documentId;
        return true;
    }

    public bool TryActivateByType(WorkspaceDocumentType documentType)
    {
        OpenWorkspaceDocument? target = _openDocuments.FirstOrDefault(document =>
            document.Descriptor.DocumentType == documentType);
        if (target is null)
            return false;

        return TryActivate(target.Descriptor.DocumentId);
    }

    public bool TryClose(Guid documentId)
    {
        int index = _openDocuments.FindIndex(document => document.Descriptor.DocumentId == documentId);
        if (index < 0)
            return false;

        bool wasActive = ActiveDocumentId == documentId;
        _openDocuments.RemoveAt(index);

        if (!wasActive)
            return true;

        if (_openDocuments.Count == 0)
        {
            ActiveDocumentId = null;
            return true;
        }

        int newActiveIndex = Math.Min(index, _openDocuments.Count - 1);
        ActiveDocumentId = _openDocuments[newActiveIndex].Descriptor.DocumentId;
        return true;
    }

    public void ReplaceDocuments(IReadOnlyList<OpenWorkspaceDocument> documents, Guid? activeDocumentId)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _openDocuments.Clear();
        _openDocuments.AddRange(documents);

        if (_openDocuments.Count == 0)
        {
            ActiveDocumentId = null;
            return;
        }

        if (activeDocumentId is Guid desiredActiveId
            && _openDocuments.Any(document => document.Descriptor.DocumentId == desiredActiveId))
        {
            ActiveDocumentId = desiredActiveId;
            return;
        }

        OpenWorkspaceDocument? queryDocument = _openDocuments.FirstOrDefault(document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.QueryCanvas);
        ActiveDocumentId = queryDocument?.Descriptor.DocumentId ?? _openDocuments[0].Descriptor.DocumentId;
    }
}
