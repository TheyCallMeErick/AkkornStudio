using AkkornStudio.UI.ViewModels.UndoRedo;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;
using AkkornStudio.UI.Services.Localization;

namespace AkkornStudio.UI.ViewModels;

// ═════════════════════════════════════════════════════════════════════════════
// UNDO / REDO STACK
// ═════════════════════════════════════════════════════════════════════════════

public sealed class UndoRedoStack(CanvasViewModel canvas) : ViewModelBase
{
    // LinkedList gives O(1) push/pop from either end and O(1) oldest-entry trim.
    // Convention: most-recently-executed command is the Last node.
    private readonly LinkedList<ICanvasCommand> _undoStack = new();
    private readonly LinkedList<ICanvasCommand> _redoStack = new();
    private readonly CanvasViewModel _canvas = canvas;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private const int MaxHistory = 200;
    private IReadOnlyList<string> _undoHistoryCache = [];

    // ── Transaction state ─────────────────────────────────────────────────────

    private List<ICanvasCommand>? _txBuffer;
    private string _txLabel = string.Empty;
    private int _commandExecutionDepth;

    public event Action? CommandExecutionCompleted;

    /// <summary>True while a transaction is open (commands are buffered).</summary>
    public bool InTransaction => _txBuffer is not null;
    public bool IsCommandExecutionInProgress => _commandExecutionDepth > 0;

    // ── State ─────────────────────────────────────────────────────────────────

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoDepth => _undoStack.Count;

    public string UndoDescription =>
        _undoStack.Last is not null ? _undoStack.Last.Value.Description : string.Empty;
    public string RedoDescription =>
        _redoStack.Last is not null ? _redoStack.Last.Value.Description : string.Empty;

    public IReadOnlyList<string> UndoHistory => _undoHistoryCache;

    // ── Transaction API ───────────────────────────────────────────────────────

    /// <summary>
    /// Begins a transaction.
    /// All subsequent <see cref="Execute"/> calls are buffered instead of
    /// pushed to the undo stack individually.
    /// Call <see cref="CommitTransaction"/> to flush as a single atomic entry.
    /// </summary>
    public UndoRedoTransaction BeginTransaction(string label)
    {
        if (_txBuffer is not null)
            throw new InvalidOperationException(
                $"Cannot begin transaction '{label}': transaction '{_txLabel}' is already open."
            );

        _txBuffer = [];
        _txLabel  = label;
        return new UndoRedoTransaction(this);
    }

    /// <summary>
    /// Commits the current transaction: wraps all buffered commands in a
    /// <see cref="CompositeCommand"/> and pushes it as one undo entry.
    /// Does nothing if the buffer is empty.
    /// </summary>
    public void CommitTransaction()
    {
        if (_txBuffer is null)
            return;

        List<ICanvasCommand> buffered = _txBuffer;
        string label = _txLabel;
        _txBuffer = null;
        _txLabel  = string.Empty;

        if (buffered.Count == 0)
            return;

        ICanvasCommand entry = buffered.Count == 1
            ? buffered[0]
            : new CompositeCommand(label, buffered);

        PushToUndoStack(entry);
        _redoStack.Clear();
        Notify();
    }

    /// <summary>
    /// Rolls back an open transaction without recording any history entry.
    /// The already-executed side-effects remain on the canvas — callers are
    /// responsible for undoing them if needed.
    /// </summary>
    public void RollbackTransaction()
    {
        int revertedCommands = 0;

        if (_txBuffer is not null)
        {
            using (BeginCommandExecutionScope())
            {
                // Revert already executed side-effects in reverse order
                // so rollback truly restores pre-transaction canvas state.
                foreach (ICanvasCommand command in _txBuffer.AsEnumerable().Reverse())
                {
                    command.Undo(_canvas);
                    revertedCommands++;
                }
            }
        }

        string rollbackLabel = _txLabel;

        _txBuffer = null;
        _txLabel  = string.Empty;

        if (revertedCommands > 0)
        {
            string label = string.IsNullOrWhiteSpace(rollbackLabel)
                ? L("undoRedo.transaction.unnamed", "unnamed transaction")
                : rollbackLabel;
            _canvas.Diagnostics.AddWarning(
                area: L("diagnostics.area.undoRedoTransaction", "Undo/Redo Transaction"),
                message: string.Format(
                    L("undoRedo.rollbackExecuted", "Rollback executed for '{0}' ({1} operation(s) reverted)."),
                    label,
                    revertedCommands),
                recommendation: L("undoRedo.rollbackRecommendation", "Review the canvas state and retry the action if needed."),
                openPanel: true
            );
        }

        Notify();
    }

    public sealed class UndoRedoTransaction(UndoRedoStack stack) : IDisposable
    {
        private bool _completed;

        public void Commit()
        {
            if (_completed)
                return;

            _completed = true;
            stack.CommitTransaction();
        }

        public void Dispose()
        {
            if (_completed)
                return;

            _completed = true;
            stack.RollbackTransaction();
        }
    }

    // ── Operations ────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a command and records it in history.
    ///
    /// If a transaction is open the command is buffered and will be grouped
    /// with others on <see cref="CommitTransaction"/>.
    ///
    /// Otherwise the command is pushed individually, first attempting to
    /// coalesce with the previous command via <see cref="ICanvasCommand.TryMerge"/>.
    /// </summary>
    public void Execute(ICanvasCommand command)
    {
        if (_txBuffer is not null)
        {
            // Buffer first so rollback can compensate even when Execute throws
            // after partially applying side-effects.
            _txBuffer.Add(command);
            using (BeginCommandExecutionScope())
                command.Execute(_canvas);
            return;
        }

        using (BeginCommandExecutionScope())
            command.Execute(_canvas);

        // Attempt to coalesce with the top-of-stack command
        if (_undoStack.Last is not null && _undoStack.Last.Value.TryMerge(command) is ICanvasCommand merged)
        {
            _undoStack.RemoveLast();
            _undoStack.AddLast(merged);
            _redoStack.Clear();
            Notify();
            return;
        }

        PushToUndoStack(command);
        _redoStack.Clear();
        Notify();
    }

    public void Undo()
    {
        if (!CanUndo)
            return;
        ICanvasCommand command = _undoStack.Last!.Value;
        _undoStack.RemoveLast();
        try
        {
            using (BeginCommandExecutionScope())
                command.Undo(_canvas);
        }
        catch
        {
            // Preserve stack integrity if Undo fails.
            _undoStack.AddLast(command);
            Notify();
            throw;
        }

        _redoStack.AddLast(command);
        Notify();
    }

    public void Redo()
    {
        if (!CanRedo)
            return;
        ICanvasCommand command = _redoStack.Last!.Value;
        _redoStack.RemoveLast();
        try
        {
            using (BeginCommandExecutionScope())
                command.Execute(_canvas);
        }
        catch
        {
            // Preserve stack integrity if Redo fails.
            _redoStack.AddLast(command);
            Notify();
            throw;
        }

        _undoStack.AddLast(command);
        Notify();
    }

    public void Clear()
    {
        _txBuffer = null;
        _txLabel  = string.Empty;
        _undoStack.Clear();
        _redoStack.Clear();
        Notify();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void PushToUndoStack(ICanvasCommand command)
    {
        _undoStack.AddLast(command);

        // Trim history to avoid unbounded memory.
        // Keep the most recent MaxHistory commands (newest are at the end).
        if (_undoStack.Count > MaxHistory)
        {
            int droppedCount = _undoStack.Count - MaxHistory;
            ICanvasCommand[] arr = [.. _undoStack];
            _undoStack.Clear();
            // TakeLast preserves order; only keep the newest commands
            foreach (ICanvasCommand c in arr.TakeLast(MaxHistory))
                _undoStack.AddLast(c);

            _canvas.Diagnostics.AddWarning(
                area: L("diagnostics.area.undoRedoHistory", "Undo/Redo History"),
                message: string.Format(
                    L("undoRedo.historyTrimmed", "Undo history reached limit ({0}). Oldest {1} operation(s) were discarded."),
                    MaxHistory,
                    droppedCount),
                recommendation: L("undoRedo.historyTrimRecommendation", "Undo recent operations first or checkpoint work before large bulk edits."),
                openPanel: false
            );
        }
    }

    private void Notify()
    {
        _undoHistoryCache = _undoStack.Select(c => c.Description).ToList();
        RaisePropertyChanged(nameof(CanUndo));
        RaisePropertyChanged(nameof(CanRedo));
        RaisePropertyChanged(nameof(UndoDescription));
        RaisePropertyChanged(nameof(RedoDescription));
        RaisePropertyChanged(nameof(UndoHistory));
    }

    private string L(string key, string fallback)
    {
        string value = _loc[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private CommandExecutionScope BeginCommandExecutionScope()
    {
        _commandExecutionDepth++;
        return new CommandExecutionScope(this);
    }

    private sealed class CommandExecutionScope(UndoRedoStack owner) : IDisposable
    {
        private UndoRedoStack? _owner = owner;

        public void Dispose()
        {
            if (_owner is null)
                return;

            UndoRedoStack owner = _owner;
            _owner = null;

            owner._commandExecutionDepth--;
            if (owner._commandExecutionDepth == 0)
                owner.CommandExecutionCompleted?.Invoke();
        }
    }

}
