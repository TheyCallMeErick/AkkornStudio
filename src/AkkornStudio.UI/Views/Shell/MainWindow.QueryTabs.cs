using Avalonia.Controls;
using AkkornStudio.UI.Controls.Shell;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI;

public partial class MainWindow
{
    private static string CreateFreshCanvasSnapshot()
    {
        using var vm = new CanvasViewModel();
        return CanvasSerializer.Serialize(vm);
    }

    private void InvalidateActiveDiagramCanvasWires()
    {
        if (CurrentShell.IsDdlDocumentPageActive)
        {
            this.FindControl<DiagramDocumentPageControl>("DdlDocumentPage")?.InvalidateCanvasWires();
            return;
        }

        this.FindControl<DiagramDocumentPageControl>("QueryDocumentPage")?.InvalidateCanvasWires();
    }

    private void ResetCurrentCanvas()
    {
        CanvasViewModel? activeCanvas = CurrentShell.ActiveCanvas;
        if (activeCanvas is null)
            return;

        try
        {
            CanvasLoadResult result = CanvasSerializer.Deserialize(CreateFreshCanvasSnapshot(), activeCanvas);
            if (!result.Success)
            {
                activeCanvas.DataPreview.ShowError(LF("tab.resetFailed", "Falha ao limpar documento ativo: {0}", result.Error ?? string.Empty), null);
                return;
            }

            activeCanvas.CurrentFilePath = null;
            activeCanvas.IsDirty = false;
            Title = activeCanvas.WindowTitle;
            InvalidateActiveDiagramCanvasWires();
            TrackCriticalFlow("CF-03-canvas-core-editing", "reset_active_canvas", "ok");
        }
        catch (Exception ex)
        {
            CurrentShell.Toasts.ShowError("Falha ao criar novo diagrama.", ex.Message);
            TrackCriticalFlow(
                "CF-03-canvas-core-editing",
                "reset_active_canvas",
                "fail",
                new Dictionary<string, object?>
                {
                    ["errorType"] = ex.GetType().Name,
                    ["errorMessage"] = ex.Message,
                });
        }
    }

    private void CreateNewQueryTab()
    {
        if (CurrentShell.ActiveWorkspaceDocumentType == WorkspaceDocumentType.SqlEditor)
        {
            CurrentShell.ActivateDocument(WorkspaceDocumentType.QueryCanvas);
            SyncModeToggleState();
        }

        EnterCanvasMode();
        ResetCurrentCanvas();
    }
}
