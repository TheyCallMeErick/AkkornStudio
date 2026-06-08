using AkkornStudio.UI.Services.Canvas.AutoJoin;
using AkkornStudio.UI.Services.Explain;
using System.Collections.ObjectModel;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class SelectionManagerCanExecuteTests
{
    private static NodeViewModel Node(string name) =>
        new(name, [("id", PinDataType.Number)], new Point(0, 0));

    [Fact]
    public void AlignCommands_RequireAtLeastTwoSelectedNodes()
    {
        var nodes = new ObservableCollection<NodeViewModel>
        {
            Node("a"),
            Node("b"),
            Node("c")
        };

        var undo = new UndoRedoStack(new CanvasViewModel());
        var panel = new PropertyPanelViewModel(undo);
        var manager = new SelectionManager(nodes, panel, undo);

        nodes[0].IsSelected = true;
        Assert.False(manager.AlignLeftCommand.CanExecute(null));

        nodes[1].IsSelected = true;
        Assert.True(manager.AlignLeftCommand.CanExecute(null));
    }

    [Fact]
    public void DistributeCommands_RequireAtLeastThreeSelectedNodes()
    {
        var nodes = new ObservableCollection<NodeViewModel>
        {
            Node("a"),
            Node("b"),
            Node("c")
        };

        var undo = new UndoRedoStack(new CanvasViewModel());
        var panel = new PropertyPanelViewModel(undo);
        var manager = new SelectionManager(nodes, panel, undo);

        nodes[0].IsSelected = true;
        nodes[1].IsSelected = true;
        Assert.False(manager.DistributeHCommand.CanExecute(null));

        nodes[2].IsSelected = true;
        Assert.True(manager.DistributeHCommand.CanExecute(null));
    }

    [Fact]
    public void SelectionChange_NotifiesAlignmentCommandsCanExecute()
    {
        var nodes = new ObservableCollection<NodeViewModel>
        {
            Node("a"),
            Node("b"),
        };

        var undo = new UndoRedoStack(new CanvasViewModel());
        var panel = new PropertyPanelViewModel(undo);
        var manager = new SelectionManager(nodes, panel, undo);
        int notifications = 0;
        manager.AlignLeftCommand.CanExecuteChanged += (_, _) => notifications++;

        nodes[0].IsSelected = true;

        Assert.True(notifications > 0);
    }

    [Fact]
    public void AlignNodes_DistributeMode_RevalidatesSelectionThresholdOnExecute()
    {
        var nodes = new ObservableCollection<NodeViewModel>
        {
            Node("a"),
            Node("b"),
            Node("c")
        };

        var canvas = new CanvasViewModel();
        var undo = new UndoRedoStack(canvas);
        var panel = new PropertyPanelViewModel(undo);
        var manager = new SelectionManager(nodes, panel, undo);

        nodes[0].IsSelected = true;
        nodes[1].IsSelected = true;

        manager.AlignNodes(AlignMode.DistributeH);

        Assert.Equal(0, undo.UndoDepth);
    }
}

