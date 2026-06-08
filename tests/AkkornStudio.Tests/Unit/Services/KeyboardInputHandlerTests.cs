using AkkornStudio.UI.Services;
using AkkornStudio.UI.Services.Input.ShortcutRegistry;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.Nodes;
using Xunit;

namespace AkkornStudio.Tests.Unit.Services;

/// <summary>
/// Tests for KeyboardInputHandler to ensure keyboard shortcuts are properly integrated.
/// Regression tests for bug where Ctrl+S and Ctrl+O were handled but not executed.
/// </summary>
public class KeyboardInputHandlerTests
{
    [Fact]
    public void Ctrl0_ResetsViewport()
    {
        var canvas = new CanvasViewModel();
        canvas.Zoom = 1.75;
        canvas.PanOffset = new Avalonia.Point(180, -90);

        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.D0, Avalonia.Input.KeyModifiers.Control);

        Assert.True(handled);
        Assert.Equal(1.0, canvas.Zoom);
        Assert.Equal(new Avalonia.Point(0, 0), canvas.PanOffset);
    }

    [Fact]
    public void F1_InvokesShortcutsCallback()
    {
        var canvas = new CanvasViewModel();
        bool opened = false;
        var handler = new KeyboardInputHandler(
            canvas,
            showShortcutsAction: () => opened = true
        );

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.F1, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.True(opened);
    }

    [Fact]
    public void CtrlPageUp_BringsSelectionForward()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        var a = new NodeViewModel("public.a", [], new Avalonia.Point(0, 0)) { ZOrder = 0 };
        var b = new NodeViewModel("public.b", [], new Avalonia.Point(100, 0)) { ZOrder = 1, IsSelected = true };
        var c = new NodeViewModel("public.c", [], new Avalonia.Point(200, 0)) { ZOrder = 2 };
        canvas.Nodes.Add(a);
        canvas.Nodes.Add(b);
        canvas.Nodes.Add(c);

        var handler = new KeyboardInputHandler(canvas);
        bool handled = handler.HandleShortcut(Avalonia.Input.Key.PageUp, Avalonia.Input.KeyModifiers.Control);

        Assert.True(handled);
        Assert.Equal(2, b.ZOrder);
    }

    [Fact]
    public void CtrlShiftPageDown_SendsSelectionToBack()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        var a = new NodeViewModel("public.a", [], new Avalonia.Point(0, 0)) { ZOrder = 0 };
        var b = new NodeViewModel("public.b", [], new Avalonia.Point(100, 0)) { ZOrder = 1, IsSelected = true };
        var c = new NodeViewModel("public.c", [], new Avalonia.Point(200, 0)) { ZOrder = 2 };
        canvas.Nodes.Add(a);
        canvas.Nodes.Add(b);
        canvas.Nodes.Add(c);

        var handler = new KeyboardInputHandler(canvas);
        bool handled = handler.HandleShortcut(
            Avalonia.Input.Key.PageDown,
            Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift
        );

        Assert.True(handled);
        Assert.Equal(0, b.ZOrder);
    }

    [Fact]
    public void CtrlF_OpensSearchCallback()
    {
        var canvas = new CanvasViewModel();
        bool opened = false;
        var handler = new KeyboardInputHandler(
            canvas,
            openSearchAction: () => opened = true
        );

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.F, Avalonia.Input.KeyModifiers.Control);

        Assert.True(handled);
        Assert.True(opened);
    }

    [Fact]
    public void ShiftA_OpensSearchCallback()
    {
        var canvas = new CanvasViewModel();
        bool opened = false;
        var handler = new KeyboardInputHandler(
            canvas,
            openSearchAction: () => opened = true
        );

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.A, Avalonia.Input.KeyModifiers.Shift);

        Assert.True(handled);
        Assert.True(opened);
    }

    [Fact]
    public void CtrlF_DoesNotOpenSearch_WhenSearchAlreadyVisible()
    {
        var canvas = new CanvasViewModel();
        bool opened = false;
        canvas.SearchMenu.Open(new Avalonia.Point(30, 30));
        var handler = new KeyboardInputHandler(
            canvas,
            openSearchAction: () => opened = true
        );

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.F, Avalonia.Input.KeyModifiers.Control);

        Assert.False(handled);
        Assert.False(opened);
    }

    [Fact]
    public void CanvasLocalKeys_AreNotHandledByGlobalHandler()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        Assert.False(handler.HandleShortcut(Avalonia.Input.Key.F, Avalonia.Input.KeyModifiers.None));
        Assert.False(handler.HandleShortcut(Avalonia.Input.Key.Left, Avalonia.Input.KeyModifiers.None));
        Assert.False(handler.HandleShortcut(Avalonia.Input.Key.Space, Avalonia.Input.KeyModifiers.None));
    }

    [Fact]
    public void Escape_ClosesOverlay_WhenVisible()
    {
        var canvas = new CanvasViewModel();
        var commandPalette = new CommandPaletteViewModel();
        commandPalette.Open();
        var handler = new KeyboardInputHandler(canvas, commandPalette);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(commandPalette.IsVisible);
    }

    [Fact]
    public void Escape_ClosesSearchMenu_WhenVisible()
    {
        var canvas = new CanvasViewModel();
        canvas.SearchMenu.Open(new Avalonia.Point(20, 20));
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(canvas.SearchMenu.IsVisible);
    }

    [Fact]
    public void Escape_ClosesDataPreview_WhenVisible()
    {
        var canvas = new CanvasViewModel();
        canvas.DataPreview.IsVisible = true;
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(canvas.DataPreview.IsVisible);
    }

    [Fact]
    public void Escape_ClosesConnectionManager_WhenVisible()
    {
        var canvas = new CanvasViewModel();
        canvas.ConnectionManager.Open();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(canvas.ConnectionManager.IsVisible);
    }

    [Fact]
    public void Escape_ClosesBenchmark_WhenVisible()
    {
        var canvas = new CanvasViewModel();
        canvas.Benchmark.Open();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(canvas.Benchmark.IsVisible);
    }

    [Fact]
    public void Escape_ClosesExplain_WhenVisible()
    {
        var canvas = new CanvasViewModel();
        canvas.ExplainPlan.Open();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(canvas.ExplainPlan.IsVisible);
    }

    [Fact]
    public void Escape_ClosesSqlImporter_WhenVisible()
    {
        var canvas = new CanvasViewModel();
        canvas.SqlImporter.Open();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(canvas.SqlImporter.IsVisible);
    }

    [Fact]
    public void Escape_ClosesFlowVersions_WhenVisible()
    {
        var canvas = new CanvasViewModel();
        canvas.FlowVersions.Open();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(canvas.FlowVersions.IsVisible);
    }

    [Fact]
    public void Escape_ClosesFileHistory_WhenVisible()
    {
        var canvas = new CanvasViewModel();
        canvas.FileHistory.Open();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(canvas.FileHistory.IsVisible);
    }

    [Fact]
    public void CtrlAltH_OpensFileHistoryOverlay()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(
            Avalonia.Input.Key.H,
            Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt
        );

        Assert.True(handled);
        Assert.True(canvas.FileHistory.IsVisible);
    }

    [Fact]
    public void CtrlShiftH_OpensFlowVersionsOverlay()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(
            Avalonia.Input.Key.H,
            Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift
        );

        Assert.True(handled);
        Assert.True(canvas.FlowVersions.IsVisible);
    }

    [Fact]
    public void CtrlShiftC_OpensConnectionManager()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(
            Avalonia.Input.Key.C,
            Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift
        );

        Assert.True(handled);
        Assert.True(canvas.ConnectionManager.IsVisible);
    }

    [Fact]
    public void CtrlK_OpensCommandPalette_WhenProvided()
    {
        var canvas = new CanvasViewModel();
        var commandPalette = new CommandPaletteViewModel();
        var handler = new KeyboardInputHandler(canvas, commandPalette);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.K, Avalonia.Input.KeyModifiers.Control);

        Assert.True(handled);
        Assert.True(commandPalette.IsVisible);
    }

    [Fact]
    public void CtrlK_IsNotHandled_WhenNoCommandPaletteWasInjected()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.K, Avalonia.Input.KeyModifiers.Control);

        Assert.False(handled);
    }

    [Fact]
    public void RegistryOverride_CtrlShiftK_OpensCommandPalette()
    {
        var canvas = new CanvasViewModel();
        var commandPalette = new CommandPaletteViewModel();
        var registry = new global::AkkornStudio.UI.Services.Input.ShortcutRegistry.ShortcutRegistry(
            customizationStore: new NoOpShortcutCustomizationStore());
        ShortcutUpdateResult overrideResult = registry.TryOverride(
            ShortcutActionIds.OpenCommandPalette,
            "Ctrl+Shift+K");
        Assert.True(overrideResult.Success);

        var handler = new KeyboardInputHandler(
            canvas,
            commandPalette,
            shortcutRegistry: registry);

        bool handled = handler.HandleShortcut(
            Avalonia.Input.Key.K,
            Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift);

        Assert.True(handled);
        Assert.True(commandPalette.IsVisible);
    }

    [Fact]
    public void F4_OpensExplainPlan()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.F4, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.True(canvas.ExplainPlan.IsVisible);
    }

    [Fact]
    public void F3_TogglesDataPreview()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        Assert.False(canvas.DataPreview.IsVisible);
        Assert.True(handler.HandleShortcut(Avalonia.Input.Key.F3, Avalonia.Input.KeyModifiers.None));
        Assert.True(canvas.DataPreview.IsVisible);
        Assert.True(handler.HandleShortcut(Avalonia.Input.Key.F3, Avalonia.Input.KeyModifiers.None));
        Assert.False(canvas.DataPreview.IsVisible);
    }

    [Fact]
    public void F3_ExecutesCommandPaletteShortcut_WhenAvailable()
    {
        var canvas = new CanvasViewModel();
        bool executed = false;
        var commandPalette = new CommandPaletteViewModel();
        commandPalette.SetCommands([
            new PaletteCommandItem
            {
                Name = "Toggle Preview",
                Shortcut = "F3",
                Execute = () => executed = true,
            },
        ]);
        var handler = new KeyboardInputHandler(canvas, commandPalette);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.F3, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.True(executed);
    }

    [Fact]
    public void F3_ExecutesCommandPaletteCommand_ByActionIdEvenWhenDisplayedShortcutDiffers()
    {
        var canvas = new CanvasViewModel();
        bool executed = false;
        var commandPalette = new CommandPaletteViewModel();
        commandPalette.SetCommands([
            new PaletteCommandItem
            {
                Name = "Toggle Preview",
                ActionId = ShortcutActionIds.TogglePreview,
                Shortcut = "Ctrl+Alt+P",
                Execute = () => executed = true,
            },
        ]);
        var handler = new KeyboardInputHandler(canvas, commandPalette);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.F3, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.True(executed);
    }

    [Fact]
    public void CtrlG_TogglesSnapToGrid()
    {
        var canvas = new CanvasViewModel();
        bool initial = canvas.SnapToGrid;
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.G, Avalonia.Input.KeyModifiers.Control);

        Assert.True(handled);
        Assert.NotEqual(initial, canvas.SnapToGrid);
    }

    [Fact]
    public void CtrlN_InvokesCreateNewCanvasCallback()
    {
        var canvas = new CanvasViewModel();
        bool invoked = false;
        var handler = new KeyboardInputHandler(
            canvas,
            onCreateNewCanvas: () => invoked = true
        );

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.N, Avalonia.Input.KeyModifiers.Control);

        Assert.True(handled);
        Assert.True(invoked);
    }

    [Fact]
    public void F5_IsNotHandled_WhenCommandPaletteWasNotInjected()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.F5, Avalonia.Input.KeyModifiers.None);

        Assert.False(handled);
    }

    [Fact]
    public void F5_DoesNotExecuteCommandPaletteShortcut_WhenRunPreviewIsDisabled()
    {
        var canvas = new CanvasViewModel();
        bool executed = false;
        var commandPalette = new CommandPaletteViewModel();
        commandPalette.SetCommands([
            new PaletteCommandItem
            {
                Name = "Run Preview",
                Shortcut = "F5",
                Execute = () => executed = true,
            },
        ]);
        var handler = new KeyboardInputHandler(canvas, commandPalette);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.F5, Avalonia.Input.KeyModifiers.None);

        Assert.False(handled);
        Assert.False(executed);
    }

    [Fact]
    public void F5_DoesNotExecuteCommandPaletteCommand_ByActionIdWhenRunPreviewIsDisabled()
    {
        var canvas = new CanvasViewModel();
        bool executed = false;
        var commandPalette = new CommandPaletteViewModel();
        commandPalette.SetCommands([
            new PaletteCommandItem
            {
                Name = "Run Preview",
                ActionId = ShortcutActionIds.RunPreview,
                Shortcut = "Ctrl+Alt+R",
                Execute = () => executed = true,
            },
        ]);
        var handler = new KeyboardInputHandler(canvas, commandPalette);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.F5, Avalonia.Input.KeyModifiers.None);

        Assert.False(handled);
        Assert.False(executed);
    }

    [Fact]
    public void CtrlPlusAndCtrlMinus_AdjustZoom()
    {
        var canvas = new CanvasViewModel();
        canvas.Zoom = 1.0;
        var handler = new KeyboardInputHandler(canvas);

        Assert.True(handler.HandleShortcut(Avalonia.Input.Key.OemPlus, Avalonia.Input.KeyModifiers.Control));
        Assert.True(canvas.Zoom > 1.0);

        double afterZoomIn = canvas.Zoom;
        Assert.True(handler.HandleShortcut(Avalonia.Input.Key.OemMinus, Avalonia.Input.KeyModifiers.Control));
        Assert.True(canvas.Zoom < afterZoomIn);
    }

    [Fact]
    public void CtrlAddAndCtrlSubtract_AdjustZoom()
    {
        var canvas = new CanvasViewModel();
        canvas.Zoom = 1.0;
        var handler = new KeyboardInputHandler(canvas);

        Assert.True(handler.HandleShortcut(Avalonia.Input.Key.Add, Avalonia.Input.KeyModifiers.Control));
        Assert.True(canvas.Zoom > 1.0);

        double afterZoomIn = canvas.Zoom;
        Assert.True(handler.HandleShortcut(Avalonia.Input.Key.Subtract, Avalonia.Input.KeyModifiers.Control));
        Assert.True(canvas.Zoom < afterZoomIn);
    }

    [Fact]
    public void Backspace_DeletesSelectedNodes()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        var a = new NodeViewModel("public.a", [], new Avalonia.Point(0, 0)) { IsSelected = true };
        var b = new NodeViewModel("public.b", [], new Avalonia.Point(100, 0));
        canvas.Nodes.Add(a);
        canvas.Nodes.Add(b);

        var handler = new KeyboardInputHandler(canvas);
        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Back, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.Single(canvas.Nodes);
    }

    [Fact]
    public void CtrlZAndCtrlY_RunUndoRedo()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        var a = new NodeViewModel("public.a", [], new Avalonia.Point(0, 0)) { IsSelected = true };
        var b = new NodeViewModel("public.b", [], new Avalonia.Point(100, 0));
        canvas.Nodes.Add(a);
        canvas.Nodes.Add(b);

        var handler = new KeyboardInputHandler(canvas);
        Assert.True(handler.HandleShortcut(Avalonia.Input.Key.Delete, Avalonia.Input.KeyModifiers.None));
        Assert.Single(canvas.Nodes);

        Assert.True(handler.HandleShortcut(Avalonia.Input.Key.Z, Avalonia.Input.KeyModifiers.Control));
        Assert.Equal(2, canvas.Nodes.Count);

        Assert.True(handler.HandleShortcut(Avalonia.Input.Key.Y, Avalonia.Input.KeyModifiers.Control));
        Assert.Single(canvas.Nodes);
    }

    [Fact]
    public void CtrlL_IsHandledForAutoLayout()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(Avalonia.Input.Key.L, Avalonia.Input.KeyModifiers.Control);

        Assert.True(handled);
    }

    [Fact]
    public void CtrlAltEnter_IsHandled()
    {
        var canvas = new CanvasViewModel();
        var handler = new KeyboardInputHandler(canvas);

        bool handled = handler.HandleShortcut(
            Avalonia.Input.Key.Enter,
            Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt
        );

        Assert.True(handled);
    }

    [Fact]
    public void CtrlShiftPageUp_BringsSelectionToFront()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        var a = new NodeViewModel("public.a", [], new Avalonia.Point(0, 0)) { ZOrder = 0 };
        var b = new NodeViewModel("public.b", [], new Avalonia.Point(100, 0)) { ZOrder = 1, IsSelected = true };
        var c = new NodeViewModel("public.c", [], new Avalonia.Point(200, 0)) { ZOrder = 2 };
        canvas.Nodes.Add(a);
        canvas.Nodes.Add(b);
        canvas.Nodes.Add(c);

        var handler = new KeyboardInputHandler(canvas);
        bool handled = handler.HandleShortcut(
            Avalonia.Input.Key.PageUp,
            Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift
        );

        Assert.True(handled);
        Assert.Equal(2, b.ZOrder);
    }

    [Fact]
    public void CtrlPageDown_SendsSelectionBackward()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        var a = new NodeViewModel("public.a", [], new Avalonia.Point(0, 0)) { ZOrder = 0 };
        var b = new NodeViewModel("public.b", [], new Avalonia.Point(100, 0)) { ZOrder = 2, IsSelected = true };
        var c = new NodeViewModel("public.c", [], new Avalonia.Point(200, 0)) { ZOrder = 1 };
        canvas.Nodes.Add(a);
        canvas.Nodes.Add(b);
        canvas.Nodes.Add(c);

        var handler = new KeyboardInputHandler(canvas);
        bool handled = handler.HandleShortcut(Avalonia.Input.Key.PageDown, Avalonia.Input.KeyModifiers.Control);

        Assert.True(handled);
        Assert.Equal(1, b.ZOrder);
    }

    [Fact]
    public async Task Escape_ExitsCteEditor_WhenInCteEditorMode()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        var table = new NodeViewModel("public.orders", [("id", PinDataType.Number)], new Avalonia.Point(0, 0));
        var colList = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Avalonia.Point(120, 0));
        var result = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Avalonia.Point(240, 0));
        var cte = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.CteDefinition), new Avalonia.Point(360, 0));

        canvas.Nodes.Add(table);
        canvas.Nodes.Add(colList);
        canvas.Nodes.Add(result);
        canvas.Nodes.Add(cte);

        Connect(canvas, table, "id", colList, "columns");
        Connect(canvas, colList, "result", result, "columns");
        Connect(canvas, result, "result", cte, "query");

        cte.IsSelected = true;
        Assert.True(await canvas.EnterSelectedCteEditorAsync());
        Assert.True(canvas.IsInCteEditor);

        var handler = new KeyboardInputHandler(canvas);
        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.False(canvas.IsInCteEditor);
    }

    [Fact]
    public void Delete_DeletesSelectedNodes()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        var a = new NodeViewModel("public.a", [], new Avalonia.Point(0, 0)) { IsSelected = true };
        var b = new NodeViewModel("public.b", [], new Avalonia.Point(100, 0));
        canvas.Nodes.Add(a);
        canvas.Nodes.Add(b);

        var handler = new KeyboardInputHandler(canvas);
        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Delete, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.Single(canvas.Nodes);
        Assert.Equal("b", canvas.Nodes[0].Title);
    }

    [Fact]
    public void Delete_RemovesSelectedBreakpoint_BeforeDeletingSelectedWire()
    {
        var canvas = new CanvasViewModel();
        canvas.InitializeDemoNodes();
        ConnectionViewModel wire = canvas.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;
        wire.SetBreakpoints([new WireBreakpoint(new Avalonia.Point(220, 180))]);
        canvas.SelectConnection(wire);
        canvas.SelectWireBreakpoint(wire, 0);

        var handler = new KeyboardInputHandler(canvas);
        bool handled = handler.HandleShortcut(Avalonia.Input.Key.Delete, Avalonia.Input.KeyModifiers.None);

        Assert.True(handled);
        Assert.Empty(wire.Breakpoints);
        Assert.Contains(canvas.Connections, c => ReferenceEquals(c, wire));
        Assert.Same(wire, canvas.SelectedConnection);
        Assert.False(canvas.HasSelectedBreakpoint);
    }

    [Fact]
    public void CanvasShortcutsDisabled_OnlyHandlesGlobalShortcuts()
    {
        var canvas = new CanvasViewModel();
        var commandPalette = new CommandPaletteViewModel();
        bool openedConnectionManager = false;
        var handler = new KeyboardInputHandler(
            canvas,
            commandPalette: commandPalette,
            canHandleCanvasShortcuts: () => false,
            openConnectionManagerAction: () => openedConnectionManager = true);

        bool deleteHandled = handler.HandleShortcut(Avalonia.Input.Key.Delete, Avalonia.Input.KeyModifiers.None);
        Assert.False(deleteHandled);

        bool paletteHandled = handler.HandleShortcut(
            Avalonia.Input.Key.P,
            Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift);
        Assert.True(paletteHandled);
        Assert.True(commandPalette.IsVisible);

        bool escapeHandled = handler.HandleShortcut(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);
        Assert.True(escapeHandled);
        Assert.False(commandPalette.IsVisible);

        bool openConnectionHandled = handler.HandleShortcut(
            Avalonia.Input.Key.C,
            Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift);
        Assert.True(openConnectionHandled);
        Assert.True(openedConnectionManager);
    }

    private static void Connect(
        CanvasViewModel canvas,
        NodeViewModel fromNode,
        string fromPin,
        NodeViewModel toNode,
        string toPin)
    {
        PinViewModel from = fromNode.OutputPins.First(p => p.Name == fromPin);
        PinViewModel to = toNode.InputPins.First(p => p.Name == toPin);

        canvas.Connections.Add(new ConnectionViewModel(from, from.AbsolutePosition, to.AbsolutePosition)
        {
            ToPin = to,
        });
    }
}
