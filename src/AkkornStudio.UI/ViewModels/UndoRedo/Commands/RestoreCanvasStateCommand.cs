using AkkornStudio.UI.ViewModels.Canvas;
using AkkornStudio.UI.Serialization;

namespace AkkornStudio.UI.ViewModels.UndoRedo;

/// <summary>
/// Command that captures and restores a canvas state.
/// Used when operations like SQL import destroy the canvas and need to provide undo capability.
///
/// Pattern:
/// - The operation (e.g., SQL import) happens BEFORE this command is added to the undo stack
/// - When Execute() is called by the undo stack, it does nothing (operation already done)
/// - When Undo() is called, it restores the pre-operation state
/// - When Redo() is called, it clears the canvas (redo the operation)
/// </summary>
public sealed class RestoreCanvasStateCommand : ICanvasCommand
{
    private readonly string _savedCanvasJson;
    private string? _afterCanvasJson;
    private bool _hasAfterSnapshot;
    private bool _hasBeenRegistered;

    public string Description { get; }

    /// <summary>
    /// Creates a command that preserves canvas state for undo capability after destructive operations.
    /// </summary>
    /// <param name="canvas">Current canvas to snapshot</param>
    /// <param name="operationDescription">Description of what operation can be undone (e.g., "SQL Import")</param>
    public RestoreCanvasStateCommand(CanvasViewModel canvas, string operationDescription = "Canvas Change")
    {
        Description = $"Undo {operationDescription}";

        // Snapshot current state (before the destructive operation)
        _savedCanvasJson = CanvasSerializer.Serialize(canvas);
    }

    public void Execute(CanvasViewModel canvas)
    {
        // First Execute call happens when command is pushed to undo stack.
        // Operation is already applied by caller, so keep current state unchanged.
        if (!_hasBeenRegistered)
        {
            _hasBeenRegistered = true;
            return;
        }

        // Subsequent Execute calls are Redo — restore post-operation state when available.
        if (_hasAfterSnapshot)
            Restore(canvas, _afterCanvasJson!);
    }

    public void Undo(CanvasViewModel canvas)
    {
        // Undo should restore the pre-operation state.
        Restore(canvas, _savedCanvasJson);
    }

    /// <summary>
    /// Captures the post-operation state so Redo can reapply it after Undo.
    /// </summary>
    public void CaptureAfterState(CanvasViewModel canvas)
    {
        _afterCanvasJson = CanvasSerializer.Serialize(canvas);
        _hasAfterSnapshot = true;
    }

    private static void Restore(CanvasViewModel canvas, string snapshotJson)
    {
        using var snapshotCanvas = new CanvasViewModel();
        CanvasLoadResult load = CanvasSerializer.Deserialize(snapshotJson, snapshotCanvas);
        if (!load.Success)
            throw new InvalidOperationException($"Failed to restore canvas snapshot: {load.Error}.");

        canvas.Connections.Clear();
        canvas.Nodes.Clear();
        canvas.Zoom = snapshotCanvas.Zoom;
        canvas.PanOffset = snapshotCanvas.PanOffset;
        canvas.ReplacePreviewParameterInputs(snapshotCanvas.PreviewParameterInputs);

        foreach (NodeViewModel node in snapshotCanvas.Nodes)
            canvas.Nodes.Add(node);

        foreach (ConnectionViewModel conn in snapshotCanvas.Connections)
            canvas.Connections.Add(conn);
    }
}
