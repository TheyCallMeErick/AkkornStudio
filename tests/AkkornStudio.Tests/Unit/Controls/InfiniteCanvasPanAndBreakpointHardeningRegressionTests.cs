using System.IO;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class InfiniteCanvasPanAndBreakpointHardeningRegressionTests
{
    [Fact]
    public void OnMoved_PanPath_ResyncsWiresToKeepWireAffordancesAligned()
    {
        string source = ReadInfiniteCanvasInteractionSource();

        Assert.Contains("if (_isPanning)", source);
        Assert.Contains("SyncWires();", source);
    }

    [Fact]
    public void ContextPan_DoesNotStartWhileDraggingBreakpointHandle()
    {
        string source = ReadInfiniteCanvasInteractionSource();

        Assert.Contains("_contextMenuPending && !_isPanning && _dragBreakpointWire is null", source);
    }

    [Fact]
    public void BreakpointDrag_UsesGuardedBreakpointLookupBeforeIndexAccess()
    {
        string source = ReadInfiniteCanvasInteractionSource();

        Assert.Contains(
            "TryGetBreakpointPosition(selectedWireCandidate, breakpointIndex, out Point breakpointPosition)",
            source);
        Assert.Contains(
            "TryGetBreakpointPosition(_dragBreakpointWire, _dragBreakpointIndex, out Point currentBreakpointPosition)",
            source);
    }

    [Fact]
    public void NodeDrag_ResetsTransientStateBeforeStartAndOnFailure()
    {
        string source = ReadInfiniteCanvasNodeDragSource();

        Assert.Contains("private void ResetNodeDragTransientState()", source);
        Assert.Contains("ResetNodeDragTransientState();\n\n        try", source);
        Assert.Contains("catch\n        {\n            ResetNodeDragTransientState();", source);
        Assert.Contains("finally\n        {\n            ResetNodeDragTransientState();", source);
    }

    [Fact]
    public void NodePositionSync_DefersWireSyncDuringUndoRedoCommandBatch()
    {
        string source = ReadInfiniteCanvasCoreSource();

        Assert.Contains("ViewModel?.UndoRedo.IsCommandExecutionInProgress == true", source);
        Assert.Contains("_wireSyncDeferredByCommandBatch = true;", source);
        Assert.Contains("CommandExecutionCompleted += _undoCommandExecutionCompletedHandler", source);
    }

    private static string ReadInfiniteCanvasInteractionSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "Controls",
                "InfiniteCanvas",
                "InfiniteCanvas.Interaction.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate InfiniteCanvas.Interaction.cs from test base directory.");
    }

    private static string ReadInfiniteCanvasNodeDragSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "Controls",
                "InfiniteCanvas",
                "InfiniteCanvas.NodeDrag.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate InfiniteCanvas.NodeDrag.cs from test base directory.");
    }

    private static string ReadInfiniteCanvasCoreSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "Controls",
                "InfiniteCanvas",
                "InfiniteCanvas.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate InfiniteCanvas.cs from test base directory.");
    }
}
