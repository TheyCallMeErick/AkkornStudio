using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.Services.Input.ShortcutRegistry;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services;

/// <summary>
/// Centralizes keyboard input handling.
/// Routes 15+ keyboard shortcuts to appropriate commands and overlays.
/// Resolves the target canvas via <see cref="IActiveCanvasProvider"/> so that
/// shortcuts work for both the Query and DDL canvases.
/// </summary>
public class KeyboardInputHandler
{
    private static readonly IShortcutRegistry VolatileRegistry =
        new global::AkkornStudio.UI.Services.Input.ShortcutRegistry.ShortcutRegistry(
            customizationStore: new NoOpShortcutCustomizationStore());

    private readonly Window? _window;
    private readonly IActiveCanvasProvider _canvasProvider;
    private readonly FileOperationsService? _fileOps;
    private readonly Action? _onCreateNewCanvas;
    private readonly Action? _showShortcutsAction;
    private readonly Action? _openSearchAction;
    private readonly Action? _openConnectionManagerAction;
    private readonly CommandPaletteViewModel? _commandPalette;
    private readonly Func<bool>? _canHandleCanvasShortcuts;
    private readonly ShortcutExecutionService _shortcutExecutionService;

    private CanvasViewModel Vm => _canvasProvider.GetActive();

    /// <summary>
    /// Primary constructor used by the application shell.
    /// Accepts an <see cref="IActiveCanvasProvider"/> so shortcuts always route
    /// to whichever canvas (Query or DDL) is active at the time of the key press.
    /// </summary>
    public KeyboardInputHandler(
        Window window,
        IActiveCanvasProvider canvasProvider,
        FileOperationsService fileOps,
        CommandPaletteViewModel? commandPalette = null,
        Action? onCreateNewCanvas = null,
        Action? showShortcutsAction = null,
        Func<bool>? canHandleCanvasShortcuts = null,
        Action? openConnectionManagerAction = null,
        IShortcutRegistry? shortcutRegistry = null
    )
    {
        _window = window;
        _canvasProvider = canvasProvider;
        _fileOps = fileOps;
        _commandPalette = commandPalette;
        _onCreateNewCanvas = onCreateNewCanvas;
        _showShortcutsAction = showShortcutsAction;
        _openSearchAction = null;
        _canHandleCanvasShortcuts = canHandleCanvasShortcuts;
        _openConnectionManagerAction = openConnectionManagerAction;
        _shortcutExecutionService = new ShortcutExecutionService(shortcutRegistry ?? VolatileRegistry);
    }

    /// <summary>
    /// Test-friendly constructor that pins the handler to a fixed canvas.
    /// Internally wraps the canvas in a trivial <see cref="ActiveCanvasProvider"/>.
    /// </summary>
    public KeyboardInputHandler(
        CanvasViewModel vm,
        CommandPaletteViewModel? commandPalette = null,
        Action? onCreateNewCanvas = null,
        Action? showShortcutsAction = null,
        Action? openSearchAction = null,
        Func<bool>? canHandleCanvasShortcuts = null,
        Action? openConnectionManagerAction = null,
        IShortcutRegistry? shortcutRegistry = null
    )
    {
        _canvasProvider = new ActiveCanvasProvider(() => vm);
        _commandPalette = commandPalette;
        _onCreateNewCanvas = onCreateNewCanvas;
        _showShortcutsAction = showShortcutsAction;
        _openSearchAction = openSearchAction;
        _window = null;
        _fileOps = null;
        _canHandleCanvasShortcuts = canHandleCanvasShortcuts;
        _openConnectionManagerAction = openConnectionManagerAction;
        _shortcutExecutionService = new ShortcutExecutionService(shortcutRegistry ?? VolatileRegistry);
    }

    /// <summary>
    /// Constructor that accepts an <see cref="IActiveCanvasProvider"/> without a <c>Window</c>.
    /// Used in tests and in headless scenarios where no window reference is available.
    /// </summary>
    public KeyboardInputHandler(IActiveCanvasProvider canvasProvider, IShortcutRegistry? shortcutRegistry = null)
    {
        _canvasProvider = canvasProvider;
        _window = null;
        _fileOps = null;
        _commandPalette = null;
        _onCreateNewCanvas = null;
        _showShortcutsAction = null;
        _openSearchAction = null;
        _canHandleCanvasShortcuts = null;
        _openConnectionManagerAction = null;
        _shortcutExecutionService = new ShortcutExecutionService(shortcutRegistry ?? VolatileRegistry);
    }

    public void Wire()
    {
        if (_window is not null)
            _window.KeyDown += OnKeyDown;
    }

    public void OnKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        if (HandleShortcut(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    public bool HandleShortcut(Key key, KeyModifiers modifiers)
    {
        if (!CanHandleCanvasShortcuts())
            return HandleGlobalShortcutWhenCanvasShortcutsDisabled(key, modifiers);

        bool isTextInputFocused = IsTextInputFocused();
        bool isCanvasInteractionBlocked = Vm.ConnectionManager.IsVisible
            || Vm.SqlImporter.IsVisible
            || Vm.Benchmark.IsVisible
            || Vm.ExplainPlan.IsVisible
            || Vm.AutoJoin.IsVisible
            || Vm.FlowVersions.IsVisible
            || Vm.FileHistory.IsVisible
            || Vm.IsInCteEditor
            || _commandPalette?.IsVisible == true;

        // Handle overlay escape keys
        if (key == Key.Escape)
        {
            if (_commandPalette?.IsVisible == true)
            {
                _commandPalette.Close();
                return true;
            }
            if (Vm.SearchMenu.IsVisible)
            {
                Vm.SearchMenu.Close();
                return true;
            }
            if (Vm.AutoJoin.IsVisible)
            {
                Vm.AutoJoin.Dismiss();
                return true;
            }
            if (Vm.DataPreview.IsVisible)
            {
                Vm.DataPreview.IsVisible = false;
                return true;
            }
            if (Vm.ConnectionManager.IsVisible)
            {
                Vm.ConnectionManager.IsVisible = false;
                return true;
            }
            if (Vm.Benchmark.IsVisible)
            {
                Vm.Benchmark.IsVisible = false;
                return true;
            }
            if (Vm.ExplainPlan.IsVisible)
            {
                Vm.ExplainPlan.Close();
                return true;
            }
            if (Vm.SqlImporter.IsVisible)
            {
                Vm.SqlImporter.Close();
                return true;
            }
            if (Vm.FlowVersions.IsVisible)
            {
                Vm.FlowVersions.Close();
                return true;
            }
            if (Vm.FileHistory.IsVisible)
            {
                Vm.FileHistory.Close();
                return true;
            }
            if (Vm.IsInCteEditor)
            {
                Vm.ExitCteEditorCommand.Execute(null);
                return true;
            }
        }

        if (key == Key.Enter && modifiers.HasFlag(KeyModifiers.Control) && modifiers.HasFlag(KeyModifiers.Alt))
        {
            if (Vm.IsInCteEditor)
                Vm.ExitCteEditorCommand.Execute(null);
            else
                Vm.EnterCteEditorCommand.Execute(null);

            return true;
        }

        if (TryHandleRegisteredShortcut(
                key,
                modifiers,
                ShortcutContext.Canvas,
                allowScopedShortcuts: true,
                isTextInputFocused,
                isCanvasInteractionBlocked))
        {
            return true;
        }

        // Shortcuts requiring overlay checks
        if (
            key == Key.A
            && modifiers.HasFlag(KeyModifiers.Shift)
            && !Vm.SearchMenu.IsVisible
            && !isTextInputFocused
            && !isCanvasInteractionBlocked
        )
        {
            OpenSearch();
            return true;
        }
        if (
            key == Key.F
            && modifiers.HasFlag(KeyModifiers.Control)
            && !Vm.SearchMenu.IsVisible
            && !isTextInputFocused
            && !isCanvasInteractionBlocked
        )
        {
            OpenSearch();
            return true;
        }

        // Delete selected nodes
        if ((key == Key.Delete || key == Key.Back) && modifiers == KeyModifiers.None)
        {
            if (isTextInputFocused || isCanvasInteractionBlocked)
                return false;

            if (Vm.DeleteSelectedWireBreakpoint())
            {
                _window?.FindControl<InfiniteCanvas>("TheCanvas")?.InvalidateWires();
                return true;
            }

            if (Vm.DeleteSelectedConnection())
                return true;

            Vm.DeleteSelected();
            return true;
        }

        // Zoom
        if (
            (key == Key.OemPlus || key == Key.Add)
            && modifiers.HasFlag(KeyModifiers.Control)
        )
        {
            if (isTextInputFocused || isCanvasInteractionBlocked)
                return false;

            Vm.ZoomInCommand.Execute(null);
            return true;
        }
        if (
            (key == Key.OemMinus || key == Key.Subtract)
            && modifiers.HasFlag(KeyModifiers.Control)
        )
        {
            if (isTextInputFocused || isCanvasInteractionBlocked)
                return false;

            Vm.ZoomOutCommand.Execute(null);
            return true;
        }
        if (
            (key == Key.D0 || key == Key.NumPad0)
            && modifiers.HasFlag(KeyModifiers.Control)
        )
        {
            if (isTextInputFocused || isCanvasInteractionBlocked)
                return false;

            Vm.ResetZoomCommand.Execute(null);
            return true;
        }

        // Legacy command palette shortcut kept for backward compatibility.
        if (key == Key.K && modifiers.HasFlag(KeyModifiers.Control))
        {
            if (_commandPalette is null)
                return false;

            _commandPalette.Open();
            return true;
        }

        return false;
    }

    private bool CanHandleCanvasShortcuts()
    {
        return _canHandleCanvasShortcuts?.Invoke() ?? true;
    }

    private bool HandleGlobalShortcutWhenCanvasShortcutsDisabled(Key key, KeyModifiers modifiers)
    {
        if (TryHandleRegisteredShortcut(
                key,
                modifiers,
                ShortcutContext.Global,
                allowScopedShortcuts: false,
                isTextInputFocused: false,
                isCanvasInteractionBlocked: false))
        {
            return true;
        }

        if (key == Key.Escape && _commandPalette?.IsVisible == true)
        {
            _commandPalette.Close();
            return true;
        }

        if (key == Key.P && modifiers.HasFlag(KeyModifiers.Control) && modifiers.HasFlag(KeyModifiers.Shift))
        {
            if (_commandPalette is null)
                return false;

            _commandPalette.Open();
            return true;
        }

        if (key == Key.F1)
        {
            if (_showShortcutsAction is not null)
                _showShortcutsAction();
            else if (_window is not null)
                new KeyboardShortcutsWindow().Show(_window);
            return true;
        }

        if (key == Key.C
            && modifiers.HasFlag(KeyModifiers.Control)
            && modifiers.HasFlag(KeyModifiers.Shift))
        {
            if (_openConnectionManagerAction is null)
                return false;

            _openConnectionManagerAction();
            return true;
        }

        if (key == Key.K && modifiers.HasFlag(KeyModifiers.Control))
        {
            if (_commandPalette is null)
                return false;

            _commandPalette.Open();
            return true;
        }

        return false;
    }

    private bool TryHandleRegisteredShortcut(
        Key key,
        KeyModifiers modifiers,
        ShortcutContext preferredContext,
        bool allowScopedShortcuts,
        bool isTextInputFocused,
        bool isCanvasInteractionBlocked)
    {
        return _shortcutExecutionService.TryExecute(
            new ShortcutExecutionContext(key, modifiers, preferredContext, allowScopedShortcuts),
            definition => ExecuteRegisteredShortcut(
                definition.ActionId.Value,
                isTextInputFocused,
                isCanvasInteractionBlocked,
                allowScopedShortcuts));
    }

    private bool ExecuteRegisteredShortcut(
        string actionId,
        bool isTextInputFocused,
        bool isCanvasInteractionBlocked,
        bool allowScopedShortcuts)
    {
        switch (actionId)
        {
            case ShortcutActionIds.OpenShortcutsReference:
                if (_showShortcutsAction is not null)
                    _showShortcutsAction();
                else if (_window is not null)
                    new KeyboardShortcutsWindow().Show(_window);
                return true;

            case ShortcutActionIds.OpenCommandPalette:
                if (_commandPalette is null)
                    return false;
                _commandPalette.Open();
                return true;

            case ShortcutActionIds.OpenConnectionManager:
                if (isTextInputFocused)
                    return false;
                if (!allowScopedShortcuts)
                {
                    if (_openConnectionManagerAction is null)
                        return false;
                    _openConnectionManagerAction();
                    return true;
                }
                Vm.ConnectionManager.Open();
                return true;

            case ShortcutActionIds.OpenFlowVersions:
                if (isTextInputFocused)
                    return false;
                Vm.FlowVersions.Open();
                return true;

            case ShortcutActionIds.OpenFileHistory:
                if (isTextInputFocused)
                    return false;
                Vm.FileHistory.Open();
                return true;

            case ShortcutActionIds.NewCanvas:
                _onCreateNewCanvas?.Invoke();
                return true;

            case ShortcutActionIds.OpenFile:
                if (_fileOps is null)
                    return false;
                _ = _fileOps.OpenAsync();
                return true;

            case ShortcutActionIds.Save:
                if (_fileOps is null)
                    return false;
                _ = _fileOps.SaveAsync(saveAs: false);
                return true;

            case ShortcutActionIds.Undo:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.UndoRedo.Undo();
                return true;

            case ShortcutActionIds.Redo:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.UndoRedo.Redo();
                return true;

            case ShortcutActionIds.OpenNodeSearch:
                if (Vm.SearchMenu.IsVisible || isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                OpenSearch();
                return true;

            case ShortcutActionIds.AutoLayout:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.RunAutoLayout();
                _window?.FindControl<InfiniteCanvas>("TheCanvas")?.InvalidateWires();
                return true;

            case ShortcutActionIds.ToggleSnapToGrid:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.ToggleSnapCommand.Execute(null);
                return true;

            case ShortcutActionIds.BringForward:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.BringSelectionForwardCommand.Execute(null);
                return true;

            case ShortcutActionIds.SendBackward:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.SendSelectionBackwardCommand.Execute(null);
                return true;

            case ShortcutActionIds.BringToFront:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.BringSelectionToFrontCommand.Execute(null);
                return true;

            case ShortcutActionIds.SendToBack:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.SendSelectionToBackCommand.Execute(null);
                return true;

            case ShortcutActionIds.TogglePreview:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                if (TryExecuteCommandPaletteShortcut(ShortcutActionIds.TogglePreview, "F3"))
                    return true;
                Vm.DataPreview.Toggle();
                return true;

            case ShortcutActionIds.RunPreview:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                return false;

            case ShortcutActionIds.ExplainPlan:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.ExplainPlan.Open();
                return true;

            case ShortcutActionIds.ZoomIn:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.ZoomInCommand.Execute(null);
                return true;

            case ShortcutActionIds.ZoomOut:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.ZoomOutCommand.Execute(null);
                return true;

            case ShortcutActionIds.ZoomReset:
                if (isTextInputFocused || isCanvasInteractionBlocked)
                    return false;
                Vm.ResetZoomCommand.Execute(null);
                return true;

            case ShortcutActionIds.ToggleCteEditor:
                if (Vm.IsInCteEditor)
                    Vm.ExitCteEditorCommand.Execute(null);
                else
                    Vm.EnterCteEditorCommand.Execute(null);
                return true;
        }

        return false;
    }

    private void OpenSearch()
    {
        if (_openSearchAction is not null)
        {
            _openSearchAction();
            return;
        }

        if (_window is null)
            return;

        InfiniteCanvas? canvas = _window.FindControl<InfiniteCanvas>("TheCanvas");
        Point ctr = canvas is not null
            ? Vm.ScreenToCanvas(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2))
            : new Point(400, 300);
        Vm.SearchMenu.Open(ctr);
    }

    private bool TryExecuteCommandPaletteShortcut(string actionId, string legacyShortcutText)
    {
        if (_commandPalette is null)
            return false;

        PaletteCommandItem? command = _commandPalette.Results.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ActionId)
            && string.Equals(item.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
            ?? _commandPalette.Results.FirstOrDefault(item =>
                string.Equals(item.Shortcut, legacyShortcutText, StringComparison.OrdinalIgnoreCase));

        if (command is null)
            return false;

        command.Execute();
        return true;
    }

    private bool IsTextInputFocused()
    {
        if (_window is null)
            return false;

        IInputElement? focused = _window.FocusManager?.GetFocusedElement();
        if (focused is null)
            return false;

        if (focused is TextBox || focused is ComboBox)
            return true;
        if (focused is TextEditor || focused is TextArea)
            return true;

        if (focused is not Visual visual)
            return false;

        return visual.GetVisualAncestors().Any(static ancestor =>
            ancestor is TextBox or ComboBox or TextEditor or TextArea);
    }
}
