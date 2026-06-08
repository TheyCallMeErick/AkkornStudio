using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;

namespace AkkornStudio.UI.Controls;

/// <summary>
/// Node drag/click workflow extracted from InfiniteCanvas.
/// Keeps drag state handling and undo/redo integration isolated from render sync plumbing.
/// </summary>
public sealed partial class InfiniteCanvas
{
    private void ResetNodeDragTransientState()
    {
        _dragNode = null;
        _groupDragStarts = null;
    }

    private void OnNodeClicked(object? s, (NodeViewModel Node, bool Shift) a) =>
        ViewModel?.SelectNode(a.Node, a.Shift);

    private void OnNodeDoubleClicked(object? s, NodeViewModel node)
    {
        if (ViewModel is null)
            return;

        if (node.Type == NodeType.CteDefinition)
            _ = ViewModel.EnterCteEditorAsync(node);
        else if (node.Type == NodeType.ViewDefinition)
            _ = ViewModel.EnterViewEditorAsync(node);
        else if (node.Type is NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition)
            _ = ViewModel.EnterSubqueryEditorAsync(node);
    }

    private void OnNodeDragStarted(object? s, (NodeViewModel Node, Point Pos) a)
    {
        // Defensive reset for aborted gestures that never reached completion.
        ResetNodeDragTransientState();

        try
        {
            Log($">>> NODE DRAG STARTED: Node='{a.Node.Title}', NodePos={a.Node.Position}, DragPos={a.Pos}, PanOffset={_panOffset}, Zoom={_zoom}");
            _dragNode = a.Node;
            _nodeDragStart = a.Pos;
            _nodePosStart = a.Node.Position;

            // CRITICAL NodifyM pattern: Capture pan offset at start for compensation during drag
            _startPanOffset = _panOffset;
            _startNodeDragCanvasPos = ScreenToCanvas(a.Pos);
            Log($"    Captured start pan offset: {_startPanOffset}, canvas pos: {_startNodeDragCanvasPos}");

            // Capture other selected nodes so they move together as a group
            if (ViewModel is not null && a.Node.IsSelected)
            {
                _groupDragStarts = ViewModel.Nodes
                    .Where(n => n.IsSelected && n != a.Node)
                    .Select(n => (n, n.Position))
                    .ToList();
                Log($"    Multi-node drag: {_groupDragStarts.Count} additional nodes selected");
            }
            else
            {
                Log($"    Single-node drag");
            }

            EnsureWiresOnTop();
            _wires.InvalidateVisual();
        }
        catch
        {
            ResetNodeDragTransientState();
            throw;
        }
    }

    private void OnNodeDragDelta(object? s, (NodeViewModel Node, Point Pos) a)
    {
        if (_dragNode is null)
        {
            Log("!!! OnNodeDragDelta called but _dragNode is null!");
            return;
        }

        Point d = a.Pos - _nodeDragStart;

        // NodifyM pattern: Apply pan offset compensation
        // If canvas pan offset changed during drag, we need to compensate
        Point panCompensation = _startPanOffset - _panOffset;
        Log($"    Pan compensation: Start={_startPanOffset}, Current={_panOffset}, Delta={panCompensation}");

        double normalizedZoom = CanvasViewportController.NormalizeZoom(_zoom);
        double rawX = _nodePosStart.X + (d.X + panCompensation.X) / normalizedZoom;
        double rawY = _nodePosStart.Y + (d.Y + panCompensation.Y) / normalizedZoom;

        double newX = rawX,
            newY = rawY;
        if (ViewModel?.SnapToGrid == true)
        {
            newX = CanvasViewModel.Snap(rawX);
            newY = CanvasViewModel.Snap(rawY);
        }

        Log($"    DRAG DELTA: Delta Screen={d}, PanComp={panCompensation}, RawPos=({rawX:F2}, {rawY:F2}), SnappedPos=({newX:F2}, {newY:F2}), Zoom={_zoom}");

        _dragNode.Position = new Point(newX, newY);

        // Force immediate layout update for the dragged node so wires sync with current position
        if (_nodeControlCache.TryGetValue(_dragNode, out NodeControl? nc))
        {
            nc.Measure(Size.Infinity);
            nc.Arrange(new Rect(new Point(newX, newY), nc.DesiredSize));
            Log($"    NodeControl layout updated: {nc.DesiredSize}");
        }
        else
        {
            Log($"    WARNING: NodeControl not found in cache for node '{_dragNode.Title}'");
        }

        // Move other selected nodes with the same delta
        if (_groupDragStarts is not null)
        {
            double dx = newX - _nodePosStart.X;
            double dy = newY - _nodePosStart.Y;
            foreach ((NodeViewModel groupNode, Point startPos) in _groupDragStarts)
                groupNode.Position = new Point(startPos.X + dx, startPos.Y + dy);
            Log($"    Moved {_groupDragStarts.Count} group nodes with delta ({dx:F2}, {dy:F2})");
        }

        SyncWires();
        UpdateWireInsertPreview(_dragNode);

        // Only recalculate guides when the node has moved enough (avoids O(n) every frame)
        double gdx = newX - _lastGuideCheckPosition.X;
        double gdy = newY - _lastGuideCheckPosition.Y;
        if (gdx * gdx + gdy * gdy >= GuideRecheckThresholdSq)
        {
            _lastGuideCheckPosition = _dragNode.Position;
            UpdateAlignGuides(_dragNode);
            Log("    Guides updated");
        }
    }

    private void OnNodeDragCompleted(object? s, (NodeViewModel Node, Point Pos) a)
    {
        try
        {
            Log($"<<< NODE DRAG COMPLETED: Node='{a.Node.Title}', FinalPos={a.Node.Position}, PanOffset={_panOffset}");

            if (_dragNode is not null && ViewModel is not null && _dragNode.Position != _nodePosStart)
            {
                Point primaryFinal = _dragNode.Position;
                Log($"    Position changed: {_nodePosStart} -> {primaryFinal}");

                if (_groupDragStarts is { Count: > 0 })
                {
                    Log($"    Creating multi-node move command for {_groupDragStarts.Count + 1} nodes");
                    // Multi-node move — collect commands for every node that actually moved
                    var moves = new List<MoveNodeCommand>();

                    // Primary (dragged) node - IMPORTANT: Don't trigger PropertyChanged handler during reset
                    if (_nodePositionHandlers.TryGetValue(_dragNode, out var dragNodeHandler))
                        _dragNode.PropertyChanged -= dragNodeHandler;

                    _dragNode.Position = _nodePosStart;
                    Log($"    Resetting primary node position to start: {_nodePosStart}");

                    if (_nodePositionHandlers.TryGetValue(_dragNode, out dragNodeHandler))
                        _dragNode.PropertyChanged += dragNodeHandler;

                    moves.Add(new MoveNodeCommand(_dragNode, _nodePosStart, primaryFinal));

                    // Other selected nodes in the group
                    foreach ((NodeViewModel groupNode, Point startPos) in _groupDragStarts)
                    {
                        Point groupFinal = groupNode.Position;
                        if (groupFinal == startPos)
                            continue;

                        // Also unhook handler for group nodes to prevent cascading
                        if (_nodePositionHandlers.TryGetValue(groupNode, out var groupHandler))
                            groupNode.PropertyChanged -= groupHandler;

                        groupNode.Position = startPos;
                        Log($"    Resetting group node '{groupNode.Title}' position to start: {startPos}");

                        if (_nodePositionHandlers.TryGetValue(groupNode, out groupHandler))
                            groupNode.PropertyChanged += groupHandler;

                        moves.Add(new MoveNodeCommand(groupNode, startPos, groupFinal));
                        Log($"      - '{groupNode.Title}': {startPos} -> {groupFinal}");
                    }

                    int movedCount = moves.Count;
                    string label = movedCount == 1 ? moves[0].Description : $"Move {movedCount} nodes";

                    Log($"    BEFORE Execute: PanOffset={_panOffset}, Zoom={_zoom}");
                    Log($"    Executing composite command: {label}");
                    ViewModel.UndoRedo.Execute(
                        movedCount == 1 ? moves[0] : new CompositeCommand(label, moves)
                    );
                    Log($"    AFTER Execute: PanOffset={_panOffset}, Zoom={_zoom}");
                    Log($"    ViewModel.PanOffset={ViewModel.PanOffset}, ViewModel.Zoom={ViewModel.Zoom}");
                    if (_panOffset.X == 0 && _panOffset.Y == 0)
                    {
                        Log("    !!! WARNING: PanOffset reset to 0,0!!!!");
                    }
                }
                else
                {
                    Log("    Creating single-node move command");
                    // Single-node move (original behaviour)
                    // IMPORTANT: Don't trigger PropertyChanged handler during reset
                    if (_nodePositionHandlers.TryGetValue(_dragNode, out var dragNodeHandler))
                        _dragNode.PropertyChanged -= dragNodeHandler;

                    _dragNode.Position = _nodePosStart;
                    Log($"    Resetting node position to start: {_nodePosStart}");

                    if (_nodePositionHandlers.TryGetValue(_dragNode, out dragNodeHandler))
                        _dragNode.PropertyChanged += dragNodeHandler;

                    Log($"    BEFORE Execute: PanOffset={_panOffset}, Zoom={_zoom}");
                    ViewModel.UndoRedo.Execute(new MoveNodeCommand(_dragNode, _nodePosStart, primaryFinal));
                    Log($"    AFTER Execute: PanOffset={_panOffset}, Zoom={_zoom}");
                    Log($"    ViewModel.PanOffset={ViewModel.PanOffset}, ViewModel.Zoom={ViewModel.Zoom}");
                    if (_panOffset.X == 0 && _panOffset.Y == 0)
                    {
                        Log("    !!! WARNING: PanOffset reset to 0,0!!!!");
                    }
                }
            }
            else
            {
                Log("    No position change or _dragNode/ViewModel is null");
            }

            if (_dragNode is not null && TryInsertNodeOnWire(_dragNode))
            {
                Log("    Node inserted on wire using modifier-assisted drop");
                SyncWires();
                InvalidateArrange();
            }

            if (ViewModel is not null)
            {
                CanvasHoverHighlighter.ClearHover(ViewModel);
                SetWireInsertPreviewPins(null, null);
                SetWireInsertPreviewInvalidWire(null);
                _hoveredPin = null;
                _hoveredWire = null;
            }

            _guides.ClearGuides();
            Log("    Drag state cleared");
        }
        finally
        {
            ResetNodeDragTransientState();
            EnsureWiresOnTop();
            _wires.InvalidateVisual();
        }
    }
}
