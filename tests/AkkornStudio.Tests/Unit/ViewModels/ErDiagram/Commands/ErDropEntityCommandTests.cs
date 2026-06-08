using System.Collections.Specialized;
using System.Reflection;
using AkkornStudio.Ddl;
using AkkornStudio.Metadata;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.ErDiagram;
using AkkornStudio.UI.ViewModels.ErDiagram.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.ErDiagram.Commands;

public sealed class ErDropEntityCommandTests
{
    [Fact]
    public void Constructor_WithNullCanvas_Throws()
    {
        ErEntityNodeViewModel entity = CreateEntity("public", "orders");

        Assert.Throws<ArgumentNullException>(() => new ErDropEntityCommand(null!, entity));
    }

    [Fact]
    public void Constructor_WithNullEntity_Throws()
    {
        var erCanvas = new ErCanvasViewModel();

        Assert.Throws<ArgumentNullException>(() => new ErDropEntityCommand(erCanvas, null!));
    }

    [Fact]
    public void ExecuteUndo_WhenSuccessful_RemovesAttachedEdgesAndRestoresState()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        ErEntityNodeViewModel parent = CreateEntity("public", "customers");
        ErEntityNodeViewModel other = CreateEntity("public", "products");
        ErRelationEdgeViewModel attachedVisible = CreateEdge("public.orders", "public.customers", "customer_id", "id");
        ErRelationEdgeViewModel attachedHidden = CreateEdge("public.customers", "public.orders", "id", "customer_id");
        attachedHidden.IsHidden = true;
        ErRelationEdgeViewModel unrelated = CreateEdge("public.products", "public.customers", "customer_id", "id");

        erCanvas.Entities.Add(target);
        erCanvas.Entities.Add(parent);
        erCanvas.Entities.Add(other);
        erCanvas.Edges.Add(attachedVisible);
        erCanvas.Edges.Add(attachedHidden);
        erCanvas.Edges.Add(unrelated);
        erCanvas.SelectedEntity = target;

        var sut = new ErDropEntityCommand(erCanvas, target);

        sut.Execute(canvas);

        Assert.DoesNotContain(target, erCanvas.Entities);
        Assert.DoesNotContain(attachedVisible, erCanvas.Edges);
        Assert.DoesNotContain(attachedHidden, erCanvas.Edges);
        Assert.Contains(unrelated, erCanvas.Edges);
        Assert.Null(erCanvas.SelectedEntity);

        sut.Undo(canvas);

        Assert.Equal(3, erCanvas.Entities.Count);
        Assert.Same(target, erCanvas.Entities[0]);
        Assert.Equal(3, erCanvas.Edges.Count);
        Assert.Same(attachedVisible, erCanvas.Edges[0]);
        Assert.Same(attachedHidden, erCanvas.Edges[1]);
        Assert.Same(unrelated, erCanvas.Edges[2]);
    }

    [Fact]
    public void Execute_WhenEdgeRemovalThrows_RollsBackAndPreservesOriginalState()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        ErEntityNodeViewModel parent = CreateEntity("public", "customers");
        ErRelationEdgeViewModel failingEdge = CreateEdge("public.orders", "public.customers", "customer_id", "id");
        ErRelationEdgeViewModel siblingEdge = CreateEdge("public.customers", "public.orders", "id", "customer_id");

        erCanvas.Entities.Add(target);
        erCanvas.Entities.Add(parent);
        erCanvas.Edges.Add(failingEdge);
        erCanvas.Edges.Add(siblingEdge);
        erCanvas.SelectedEntity = target;

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Remove || args.OldItems is null)
                return;

            if (args.OldItems.Cast<object>().Contains(failingEdge))
                throw new InvalidOperationException("Injected remove failure.");
        };

        erCanvas.Edges.CollectionChanged += handler;
        var sut = new ErDropEntityCommand(erCanvas, target);

        try
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
            Assert.Equal("Injected remove failure.", ex.Message);
        }
        finally
        {
            erCanvas.Edges.CollectionChanged -= handler;
        }

        Assert.Same(target, erCanvas.SelectedEntity);
        Assert.Contains(target, erCanvas.Entities);
        Assert.Equal(2, erCanvas.Edges.Count);
        Assert.Same(failingEdge, erCanvas.Edges[0]);
        Assert.Same(siblingEdge, erCanvas.Edges[1]);
    }

    [Fact]
    public void Execute_WhenEdgeRemovalThrowsWithoutSelection_RollsBackWithoutSelectingEntity()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        ErEntityNodeViewModel parent = CreateEntity("public", "customers");
        ErRelationEdgeViewModel failingEdge = CreateEdge("public.orders", "public.customers", "customer_id", "id");

        erCanvas.Entities.Add(target);
        erCanvas.Entities.Add(parent);
        erCanvas.Edges.Add(failingEdge);

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Remove && args.OldItems is not null)
                throw new InvalidOperationException("Injected remove failure.");
        };

        erCanvas.Edges.CollectionChanged += handler;
        var sut = new ErDropEntityCommand(erCanvas, target);

        try
        {
            Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        }
        finally
        {
            erCanvas.Edges.CollectionChanged -= handler;
        }

        Assert.Null(erCanvas.SelectedEntity);
        Assert.Contains(target, erCanvas.Entities);
        Assert.Contains(failingEdge, erCanvas.Edges);
    }

    [Fact]
    public void Execute_WhenClearSelectionThrows_RollsBackEntityAndEdgesAtOriginalPositions()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        ErEntityNodeViewModel parent = CreateEntity("public", "customers");
        ErRelationEdgeViewModel attached = CreateEdge("public.orders", "public.customers", "customer_id", "id");

        erCanvas.Entities.Add(parent);
        erCanvas.Entities.Add(target);
        erCanvas.Edges.Add(attached);
        FieldInfo selectedEntityField = typeof(ErCanvasViewModel).GetField("_selectedEntity", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Expected private field '_selectedEntity' was not found.");
        selectedEntityField.SetValue(erCanvas, target);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(ErCanvasViewModel.SelectedEntity), StringComparison.Ordinal))
                throw new InvalidOperationException("Injected selection clear failure.");
        };

        erCanvas.PropertyChanged += handler;
        var sut = new ErDropEntityCommand(erCanvas, target);

        try
        {
            Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        }
        finally
        {
            erCanvas.PropertyChanged -= handler;
        }

        Assert.Equal(2, erCanvas.Entities.Count);
        Assert.Same(parent, erCanvas.Entities[0]);
        Assert.Same(target, erCanvas.Entities[1]);
        Assert.Single(erCanvas.Edges);
        Assert.Same(attached, erCanvas.Edges[0]);
        Assert.Same(target, erCanvas.SelectedEntity);
    }

    [Fact]
    public void ToDdlExpression_WithSchemaAndWithoutSchema_ParsesQualifiedName()
    {
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel withSchema = CreateEntity("public", "orders");
        ErEntityNodeViewModel withoutSchema = CreateEntity(string.Empty, "events");

        var withSchemaCommand = new ErDropEntityCommand(erCanvas, withSchema);
        var withoutSchemaCommand = new ErDropEntityCommand(erCanvas, withoutSchema);

        var withSchemaDdl = Assert.IsType<DropTableExpr>(withSchemaCommand.ToDdlExpression());
        var withoutSchemaDdl = Assert.IsType<DropTableExpr>(withoutSchemaCommand.ToDdlExpression());

        Assert.Equal("public", withSchemaDdl.SchemaName);
        Assert.Equal("orders", withSchemaDdl.TableName);
        Assert.Equal(string.Empty, withoutSchemaDdl.SchemaName);
        Assert.Equal("events", withoutSchemaDdl.TableName);
        Assert.Equal("ER: drop entity", withSchemaCommand.Description);
    }

    [Fact]
    public void Undo_WhenEntityWasNotPresentDuringExecute_DoesNotInsertEntity()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        ErEntityNodeViewModel parent = CreateEntity("public", "customers");
        ErRelationEdgeViewModel attached = CreateEdge("public.orders", "public.customers", "customer_id", "id");

        erCanvas.Entities.Add(parent);
        erCanvas.Edges.Add(attached);

        var sut = new ErDropEntityCommand(erCanvas, target);

        sut.Execute(canvas);
        sut.Undo(canvas);

        Assert.DoesNotContain(target, erCanvas.Entities);
        Assert.Single(erCanvas.Entities);
    }

    [Fact]
    public void Undo_WhenEntityAlreadyExists_DoesNotDuplicateEntity()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        ErEntityNodeViewModel parent = CreateEntity("public", "customers");
        ErRelationEdgeViewModel attached = CreateEdge("public.orders", "public.customers", "customer_id", "id");

        erCanvas.Entities.Add(target);
        erCanvas.Entities.Add(parent);
        erCanvas.Edges.Add(attached);

        var sut = new ErDropEntityCommand(erCanvas, target);
        sut.Execute(canvas);
        erCanvas.Entities.Insert(0, target);

        sut.Undo(canvas);

        Assert.Equal(1, erCanvas.Entities.Count(entity => ReferenceEquals(entity, target)));
        Assert.Equal(2, erCanvas.Entities.Count);
    }

    private static ErEntityNodeViewModel CreateEntity(string schema, string name)
    {
        return new ErEntityNodeViewModel(
            schema: schema,
            name: name,
            isView: false,
            estimatedRowCount: null,
            columns:
            [
                new ErColumnRowViewModel("id", "int", false, true, false, true, null),
            ],
            dependencies: null,
            createStatementSql: null);
    }

    private static ErRelationEdgeViewModel CreateEdge(
        string childEntityId,
        string parentEntityId,
        string childColumn,
        string parentColumn)
    {
        return new ErRelationEdgeViewModel(
            constraintName: null,
            childEntityId: childEntityId,
            parentEntityId: parentEntityId,
            childColumn: childColumn,
            parentColumn: parentColumn,
            onDelete: ReferentialAction.NoAction,
            onUpdate: ReferentialAction.NoAction);
    }
}
