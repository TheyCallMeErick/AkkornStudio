using System.Reflection;
using Avalonia;
using AkkornStudio.Nodes;

namespace AkkornStudio.Tests.Unit.ViewModels.PropertyPanel;

public sealed class PropertyPanelCommitAndHandlersHardeningTests
{
    [Fact]
    public void CommitDirty_WhenSecondCommandFails_RollsBackFirstCommandAndAvoidsPartialCommit()
    {
        var canvas = new CanvasViewModel();
        var undo = new UndoRedoStack(canvas);
        int executeCount = 0;
        var panel = new PropertyPanelViewModel(
            undo,
            executeCommand: command =>
            {
                executeCount++;
                if (executeCount == 2)
                    throw new InvalidOperationException("Simulated command execution failure.");

                undo.Execute(command);
            });
        var node = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.TableDefinition), new Point(0, 0));
        panel.ShowNode(node);

        ParameterRowViewModel schemaRow = panel.Parameters.First(parameter =>
            string.Equals(parameter.Name, "SchemaName", StringComparison.OrdinalIgnoreCase));
        ParameterRowViewModel tableRow = panel.Parameters.First(parameter =>
            string.Equals(parameter.Name, "TableName", StringComparison.OrdinalIgnoreCase));
        string? schemaOriginal = schemaRow.Value;
        string? tableOriginal = tableRow.Value;

        schemaRow.Value = "sales";
        tableRow.Value = "orders";
        Assert.True(schemaRow.IsDirty);
        Assert.True(tableRow.IsDirty);

        Assert.Throws<InvalidOperationException>(() => panel.CommitDirty());

        Assert.Equal(schemaOriginal, node.Parameters["SchemaName"]);
        Assert.Equal(tableOriginal, node.Parameters["TableName"]);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void ParameterHandlers_WhenRowRemoved_DetachesHandler()
    {
        var panel = CreatePanelWithTableReferenceNode();
        ParameterRowViewModel rowToRemove = panel.Parameters.First(parameter =>
            string.Equals(parameter.Name, "SchemaName", StringComparison.OrdinalIgnoreCase));

        IDictionary<ParameterRowViewModel, PropertyChangedEventHandler> handlersBefore =
            GetParameterHandlers(panel);
        int countBefore = handlersBefore.Count;
        Assert.True(handlersBefore.ContainsKey(rowToRemove));

        panel.Parameters.Remove(rowToRemove);

        IDictionary<ParameterRowViewModel, PropertyChangedEventHandler> handlersAfter =
            GetParameterHandlers(panel);
        Assert.Equal(countBefore - 1, handlersAfter.Count);
        Assert.False(handlersAfter.ContainsKey(rowToRemove));
    }

    [Fact]
    public void ParameterHandlers_WhenRowAdded_AttachesHandler()
    {
        var panel = CreatePanelWithTableReferenceNode();
        IDictionary<ParameterRowViewModel, PropertyChangedEventHandler> handlersBefore =
            GetParameterHandlers(panel);
        int countBefore = handlersBefore.Count;

        var row = new ParameterRowViewModel(
            new NodeParameter("SchemaName", ParameterKind.Text, DefaultValue: "public"),
            "public");
        panel.Parameters.Add(row);

        IDictionary<ParameterRowViewModel, PropertyChangedEventHandler> handlersAfter =
            GetParameterHandlers(panel);
        Assert.Equal(countBefore + 1, handlersAfter.Count);
        Assert.True(handlersAfter.ContainsKey(row));
    }

    private static PropertyPanelViewModel CreatePanelWithTableReferenceNode()
    {
        var canvas = new CanvasViewModel();
        var undo = new UndoRedoStack(canvas);
        var panel = new PropertyPanelViewModel(undo);
        var node = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.TableReference), new Point(0, 0));
        panel.ShowNode(node);
        return panel;
    }

    private static IDictionary<ParameterRowViewModel, PropertyChangedEventHandler> GetParameterHandlers(
        PropertyPanelViewModel panel)
    {
        FieldInfo field = typeof(PropertyPanelViewModel).GetField(
            "_parameterRowPropertyHandlers",
            BindingFlags.Instance | BindingFlags.NonPublic
        ) ?? throw new InvalidOperationException("Could not locate _parameterRowPropertyHandlers field.");

        object? value = field.GetValue(panel);
        Assert.NotNull(value);
        return Assert.IsAssignableFrom<IDictionary<ParameterRowViewModel, PropertyChangedEventHandler>>(value);
    }
}
