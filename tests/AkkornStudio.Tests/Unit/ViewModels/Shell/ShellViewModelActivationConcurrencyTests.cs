using System.Threading;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Workspace;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class ShellViewModelActivationConcurrencyTests
{
    [Fact]
    public async Task ActivateDocument_ConcurrentCalls_DoNotOverlapRouterActivation_AndKeepStateConsistent()
    {
        var workspaceRouter = new ConcurrencyDetectingWorkspaceRouter(activationDelayMs: 3);
        var shell = new ShellViewModel(
            workspaceRouter: workspaceRouter,
            connectionManagerViewModelFactory: ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);
        shell.ActivateDocument(WorkspaceDocumentType.QueryCanvas);

        const int iterations = 80;
        using var startGate = new ManualResetEventSlim(initialState: false);
        Task[] workers =
        [
            Task.Run(() => RunActivationLoop(shell, startGate, iterations, WorkspaceDocumentType.DdlCanvas, WorkspaceDocumentType.SqlEditor)),
            Task.Run(() => RunActivationLoop(shell, startGate, iterations, WorkspaceDocumentType.SqlEditor, WorkspaceDocumentType.QueryCanvas)),
            Task.Run(() => RunActivationLoop(shell, startGate, iterations, WorkspaceDocumentType.QueryCanvas, WorkspaceDocumentType.DdlCanvas)),
        ];

        startGate.Set();
        await Task.WhenAll(workers);

        OpenWorkspaceDocument activeDocument = Assert.IsType<OpenWorkspaceDocument>(shell.ActiveWorkspaceDocument);
        Assert.Equal(activeDocument.Descriptor.DocumentType, shell.ActiveWorkspaceDocumentType);
        Assert.Equal(1, workspaceRouter.MaxConcurrentTryActivateCalls);
    }

    private static void RunActivationLoop(
        ShellViewModel shell,
        ManualResetEventSlim startGate,
        int iterations,
        WorkspaceDocumentType firstType,
        WorkspaceDocumentType secondType)
    {
        startGate.Wait();
        for (int i = 0; i < iterations; i++)
        {
            shell.ActivateDocument(firstType);
            shell.ActivateDocument(secondType);
        }
    }

    private sealed class ConcurrencyDetectingWorkspaceRouter : IWorkspaceRouter
    {
        private readonly WorkspaceRouter _inner = new();
        private readonly int _activationDelayMs;
        private int _activeTryActivateCalls;
        private int _maxConcurrentTryActivateCalls;

        public ConcurrencyDetectingWorkspaceRouter(int activationDelayMs)
        {
            _activationDelayMs = activationDelayMs;
        }

        public int MaxConcurrentTryActivateCalls => Volatile.Read(ref _maxConcurrentTryActivateCalls);

        public IReadOnlyList<OpenWorkspaceDocument> OpenDocuments => _inner.OpenDocuments;

        public Guid? ActiveDocumentId => _inner.ActiveDocumentId;

        public OpenWorkspaceDocument? ActiveDocument => _inner.ActiveDocument;

        public void OpenDocument(OpenWorkspaceDocument document, bool activate = true)
        {
            _inner.OpenDocument(document, activate);
        }

        public bool TryActivate(Guid documentId)
        {
            int activeCalls = Interlocked.Increment(ref _activeTryActivateCalls);
            UpdateMaxConcurrentTryActivateCalls(activeCalls);
            try
            {
                Thread.Sleep(_activationDelayMs);
                return _inner.TryActivate(documentId);
            }
            finally
            {
                Interlocked.Decrement(ref _activeTryActivateCalls);
            }
        }

        public bool TryActivateByType(WorkspaceDocumentType documentType)
        {
            return _inner.TryActivateByType(documentType);
        }

        public bool TryClose(Guid documentId)
        {
            return _inner.TryClose(documentId);
        }

        public void ReplaceDocuments(IReadOnlyList<OpenWorkspaceDocument> documents, Guid? activeDocumentId)
        {
            _inner.ReplaceDocuments(documents, activeDocumentId);
        }

        private void UpdateMaxConcurrentTryActivateCalls(int candidate)
        {
            while (true)
            {
                int current = Volatile.Read(ref _maxConcurrentTryActivateCalls);
                if (candidate <= current)
                    return;

                if (Interlocked.CompareExchange(ref _maxConcurrentTryActivateCalls, candidate, current) == current)
                    return;
            }
        }
    }
}
