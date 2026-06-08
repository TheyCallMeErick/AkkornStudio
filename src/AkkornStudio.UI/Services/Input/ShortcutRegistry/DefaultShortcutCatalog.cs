namespace AkkornStudio.UI.Services.Input.ShortcutRegistry;

/// <summary>
/// Canonical immutable catalog of default shortcuts.
/// </summary>
public sealed class DefaultShortcutCatalog
{
    private readonly IShortcutGestureParser _gestureParser;

    public DefaultShortcutCatalog(IShortcutGestureParser? gestureParser = null)
    {
        _gestureParser = gestureParser ?? new ShortcutGestureParser();
    }

    public IReadOnlyList<ShortcutDefinition> Build()
    {
        static void Noop() { }

        return
        [
            Def(ShortcutActionIds.OpenShortcutsReference, "Keyboard Shortcuts", "Open shortcuts reference", "File and General", ShortcutContext.Global, "help", "shortcuts", "F1"),
            Def(ShortcutActionIds.OpenCommandPalette, "Command Palette", "Open command palette", "File and General", ShortcutContext.Global, "palette", "commands", "Ctrl+Shift+P"),
            Def(ShortcutActionIds.OpenConnectionManager, "Connection Manager", "Open connection manager", "Preview and Inspection", ShortcutContext.Global, "connection", "manager", "Ctrl+Shift+C"),
            Def(ShortcutActionIds.OpenFlowVersions, "Flow Version History", "Open flow history", "Preview and Inspection", ShortcutContext.Global, "history", "flow", "Ctrl+Shift+H"),
            Def(ShortcutActionIds.OpenFileHistory, "File Version History", "Open file history", "File and General", ShortcutContext.Global, "history", "file", "Ctrl+Alt+H"),

            Def(ShortcutActionIds.NewCanvas, "New Canvas", "Create a new canvas", "File and General", ShortcutContext.Global, "file", "new", "Ctrl+N"),
            Def(ShortcutActionIds.OpenFile, "Open File", "Open file", "File and General", ShortcutContext.Global, "file", "open", "Ctrl+O"),
            Def(ShortcutActionIds.Save, "Save", "Save current file", "File and General", ShortcutContext.Global, "file", "save", "Ctrl+S"),
            Def(ShortcutActionIds.SaveAs, "Save As", "Save current file with a new name", "File and General", ShortcutContext.Global, "file", "saveAs", "Ctrl+Shift+S"),

            Def(ShortcutActionIds.Undo, "Undo", "Undo last change", "Editing", ShortcutContext.Global, "edit", "undo", "Ctrl+Z"),
            Def(ShortcutActionIds.Redo, "Redo", "Redo last undone change", "Editing", ShortcutContext.Global, "edit", "redo", "Ctrl+Y"),
            Def(ShortcutActionIds.SelectAll, "Select All", "Select all items", "Editing", ShortcutContext.Global, "edit", "selection", "Ctrl+A"),
            Def(ShortcutActionIds.DeleteSelection, "Delete Selection", "Delete selected nodes or wires", "Editing", ShortcutContext.Global, "edit", "delete", "Del"),

            Def(ShortcutActionIds.OpenNodeSearch, "Open Node Search", "Open node search", "Canvas and Navigation", ShortcutContext.Canvas, "search", "canvas", "Shift+A"),
            Def(ShortcutActionIds.AutoLayout, "Auto Layout", "Auto arrange selected nodes", "Canvas and Navigation", ShortcutContext.Canvas, "layout", "canvas", "Ctrl+L"),
            Def(ShortcutActionIds.ToggleSnapToGrid, "Toggle Snap", "Toggle snap to grid", "Canvas and Navigation", ShortcutContext.Canvas, "snap", "grid", "Ctrl+G"),
            Def(ShortcutActionIds.BringForward, "Bring Forward", "Bring selection forward", "Canvas and Navigation", ShortcutContext.Canvas, "layer", "arrange", "Ctrl+PgUp"),
            Def(ShortcutActionIds.SendBackward, "Send Backward", "Send selection backward", "Canvas and Navigation", ShortcutContext.Canvas, "layer", "arrange", "Ctrl+PgDown"),
            Def(ShortcutActionIds.BringToFront, "Bring to Front", "Bring selection to front", "Canvas and Navigation", ShortcutContext.Canvas, "layer", "arrange", "Ctrl+Shift+PgUp"),
            Def(ShortcutActionIds.SendToBack, "Send to Back", "Send selection to back", "Canvas and Navigation", ShortcutContext.Canvas, "layer", "arrange", "Ctrl+Shift+PgDown"),

            Def(ShortcutActionIds.TogglePreview, "Toggle Preview", "Toggle preview panel", "Preview and Inspection", ShortcutContext.Canvas, "preview", "data", "F3"),
            Def(ShortcutActionIds.ExplainPlan, "Explain Plan", "Open explain plan", "Preview and Inspection", ShortcutContext.Canvas, "explain", "plan", "F4"),

            Def(ShortcutActionIds.ZoomIn, "Zoom In", "Increase zoom", "Zoom, pan and precision", ShortcutContext.Canvas, "zoom", "canvas", "Ctrl++"),
            Def(ShortcutActionIds.ZoomOut, "Zoom Out", "Decrease zoom", "Zoom, pan and precision", ShortcutContext.Canvas, "zoom", "canvas", "Ctrl+-"),
            Def(ShortcutActionIds.ZoomReset, "Reset Zoom", "Reset zoom to 100%", "Zoom, pan and precision", ShortcutContext.Canvas, "zoom", "canvas", "Ctrl+0"),
            Def(ShortcutActionIds.ToggleCteEditor, "Toggle CTE Editor", "Enter or exit CTE editor", "Canvas and Navigation", ShortcutContext.Canvas, "cte", "editor", "Ctrl+Alt+Enter"),

            Def(ShortcutActionIds.SqlTabNew, "SQL New Tab", "Open new SQL tab", "SQL Editor", ShortcutContext.SqlEditor, "sql", "tab", "Ctrl+T"),
            Def(ShortcutActionIds.SqlTabClose, "SQL Close Tab", "Close SQL tab", "SQL Editor", ShortcutContext.SqlEditor, "sql", "tab", "Ctrl+W"),
            Def(ShortcutActionIds.SqlTabOpenFile, "SQL Open File", "Open SQL file", "SQL Editor", ShortcutContext.SqlEditor, "sql", "file", "Ctrl+O"),
            Def(ShortcutActionIds.SqlTabSaveFile, "SQL Save File", "Save SQL file", "SQL Editor", ShortcutContext.SqlEditor, "sql", "file", "Ctrl+S"),
            Def(ShortcutActionIds.SqlRunAll, "SQL Run All", "Run all SQL script", "SQL Editor", ShortcutContext.SqlEditor, "sql", "run", "F5"),
            Def(ShortcutActionIds.SqlRunSelection, "SQL Run Selection", "Run selected SQL", "SQL Editor", ShortcutContext.SqlEditor, "sql", "run", "F8"),
            Def(ShortcutActionIds.SqlRunCurrent, "SQL Run Current", "Run current SQL statement", "SQL Editor", ShortcutContext.SqlEditor, "sql", "run", "Ctrl+Enter"),
            Def(ShortcutActionIds.SqlCancelExecution, "SQL Cancel Execution", "Cancel SQL execution", "SQL Editor", ShortcutContext.SqlEditor, "sql", "cancel", "Esc"),
        ];

        ShortcutDefinition Def(
            string actionId,
            string name,
            string description,
            string section,
            ShortcutContext context,
            params string[] gestureAndTags)
        {
            if (gestureAndTags.Length == 0)
                throw new InvalidOperationException($"Catalog entry '{actionId}' must include at least one gesture.");

            string gestureText = gestureAndTags[^1];
            string[] tags = gestureAndTags[..^1];
            ShortcutGesture gesture = ParseOrThrow(gestureText, actionId);

            return new ShortcutDefinition(
                new ShortcutActionId(actionId),
                name,
                description,
                section,
                tags,
                gesture,
                gesture,
                context,
                AllowCustomization: true,
                Execute: Noop);
        }
    }

    private ShortcutGesture ParseOrThrow(string gestureText, string actionId)
    {
        ShortcutGestureParseResult parse = _gestureParser.Parse(gestureText);
        if (parse.Gesture is not null)
            return parse.Gesture;

        string issue = parse.Issue is null
            ? "unknown"
            : $"{parse.Issue.Code}: {parse.Issue.Message}";
        throw new InvalidOperationException($"Invalid default shortcut '{gestureText}' for '{actionId}'. {issue}");
    }
}
