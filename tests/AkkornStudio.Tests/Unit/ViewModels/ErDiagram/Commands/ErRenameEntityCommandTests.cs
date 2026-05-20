using System.ComponentModel;
using AkkornStudio.Ddl;
using AkkornStudio.Metadata;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.ErDiagram;
using AkkornStudio.UI.ViewModels.ErDiagram.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.ErDiagram.Commands;

public sealed class ErRenameEntityCommandTests
{
    [Fact]
    public void Constructor_WithNullCanvas_Throws()
    {
        ErEntityNodeViewModel entity = CreateEntity("public", "orders");

        Assert.Throws<ArgumentNullException>(() => new ErRenameEntityCommand(
            null!,
            entity,
            "sales",
            "orders_archive"));
    }

    [Fact]
    public void Constructor_WithNullEntity_Throws()
    {
        var erCanvas = new ErCanvasViewModel();

        Assert.Throws<ArgumentNullException>(() => new ErRenameEntityCommand(
            erCanvas,
            null!,
            "sales",
            "orders_archive"));
    }

    [Fact]
    public void ExecuteUndo_WhenSuccessful_UpdatesAndRestoresAllEdgeEndpoints()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        ErEntityNodeViewModel customers = CreateEntity("public", "customers");
        ErEntityNodeViewModel suppliers = CreateEntity("public", "suppliers");
        ErRelationEdgeViewModel childEdge = CreateEdge("public.orders", "public.customers");
        ErRelationEdgeViewModel parentEdge = CreateEdge("public.suppliers", "public.orders");
        ErRelationEdgeViewModel selfEdge = CreateEdge("public.orders", "public.orders");
        ErRelationEdgeViewModel unrelated = CreateEdge("public.customers", "public.suppliers");

        erCanvas.Entities.Add(target);
        erCanvas.Entities.Add(customers);
        erCanvas.Entities.Add(suppliers);
        erCanvas.Edges.Add(childEdge);
        erCanvas.Edges.Add(parentEdge);
        erCanvas.Edges.Add(selfEdge);
        erCanvas.Edges.Add(unrelated);
        erCanvas.SelectedEntity = target;

        var sut = new ErRenameEntityCommand(erCanvas, target, "sales", "orders_archive");

        sut.Execute(canvas);

        Assert.Equal("sales.orders_archive", target.Id);
        Assert.Equal("sales", target.Schema);
        Assert.Equal("orders_archive", target.Name);
        Assert.Equal("sales.orders_archive", childEdge.ChildEntityId);
        Assert.Equal("sales.orders_archive", parentEdge.ParentEntityId);
        Assert.Equal("sales.orders_archive", selfEdge.ChildEntityId);
        Assert.Equal("sales.orders_archive", selfEdge.ParentEntityId);
        Assert.Equal("public.customers", unrelated.ChildEntityId);
        Assert.Same(target, erCanvas.SelectedEntity);

        sut.Undo(canvas);

        Assert.Equal("public.orders", target.Id);
        Assert.Equal("public.orders", childEdge.ChildEntityId);
        Assert.Equal("public.orders", parentEdge.ParentEntityId);
        Assert.Equal("public.orders", selfEdge.ChildEntityId);
        Assert.Equal("public.orders", selfEdge.ParentEntityId);
        Assert.Same(target, erCanvas.SelectedEntity);
        Assert.Equal("ER: rename entity", sut.Description);
    }

    [Fact]
    public void Execute_WhenEdgeUpdateThrows_RollsBackEntityAndEdgeState()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        ErEntityNodeViewModel customers = CreateEntity("public", "customers");
        ErEntityNodeViewModel suppliers = CreateEntity("public", "suppliers");
        ErRelationEdgeViewModel firstEdge = CreateEdge("public.orders", "public.customers");
        ErRelationEdgeViewModel throwingEdge = CreateEdge("public.suppliers", "public.orders");

        erCanvas.Entities.Add(target);
        erCanvas.Entities.Add(customers);
        erCanvas.Entities.Add(suppliers);
        erCanvas.Edges.Add(firstEdge);
        erCanvas.Edges.Add(throwingEdge);
        erCanvas.SelectedEntity = target;

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(ErRelationEdgeViewModel.ParentEntityId), StringComparison.Ordinal))
                return;

            if (string.Equals(throwingEdge.ParentEntityId, "sales.orders_archive", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected parent edge update failure.");
        };

        throwingEdge.PropertyChanged += handler;
        var sut = new ErRenameEntityCommand(erCanvas, target, "sales", "orders_archive");

        try
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
            Assert.Equal("Injected parent edge update failure.", ex.Message);
        }
        finally
        {
            throwingEdge.PropertyChanged -= handler;
        }

        Assert.Equal("public.orders", target.Id);
        Assert.Equal("public.orders", firstEdge.ChildEntityId);
        Assert.Equal("public.orders", throwingEdge.ParentEntityId);
        Assert.Same(target, erCanvas.SelectedEntity);
    }

    [Fact]
    public void Undo_WhenEdgeUpdateThrows_RestoresRenamedState()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        ErEntityNodeViewModel customers = CreateEntity("public", "customers");
        ErEntityNodeViewModel suppliers = CreateEntity("public", "suppliers");
        ErRelationEdgeViewModel firstEdge = CreateEdge("public.orders", "public.customers");
        ErRelationEdgeViewModel throwingEdge = CreateEdge("public.suppliers", "public.orders");

        erCanvas.Entities.Add(target);
        erCanvas.Entities.Add(customers);
        erCanvas.Entities.Add(suppliers);
        erCanvas.Edges.Add(firstEdge);
        erCanvas.Edges.Add(throwingEdge);
        erCanvas.SelectedEntity = target;

        var sut = new ErRenameEntityCommand(erCanvas, target, "sales", "orders_archive");
        sut.Execute(canvas);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(ErRelationEdgeViewModel.ParentEntityId), StringComparison.Ordinal))
                return;

            if (string.Equals(throwingEdge.ParentEntityId, "public.orders", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected undo edge update failure.");
        };

        throwingEdge.PropertyChanged += handler;
        try
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Undo(canvas));
            Assert.Equal("Injected undo edge update failure.", ex.Message);
        }
        finally
        {
            throwingEdge.PropertyChanged -= handler;
        }

        Assert.Equal("sales.orders_archive", target.Id);
        Assert.Equal("sales.orders_archive", firstEdge.ChildEntityId);
        Assert.Equal("sales.orders_archive", throwingEdge.ParentEntityId);
        Assert.Same(target, erCanvas.SelectedEntity);
    }

    [Fact]
    public void Undo_BeforeExecute_IsNoOp()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        erCanvas.Entities.Add(target);
        var sut = new ErRenameEntityCommand(erCanvas, target, "sales", "orders_archive");

        sut.Undo(canvas);

        Assert.Equal("public.orders", target.Id);
    }

    [Fact]
    public void ToDdlExpression_BeforeExecute_Throws()
    {
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        var sut = new ErRenameEntityCommand(erCanvas, target, "sales", "orders_archive");

        Assert.Throws<InvalidOperationException>(() => sut.ToDdlExpression());
    }

    [Fact]
    public void ToDdlExpression_AfterExecute_ReturnsExpectedRenameOperation()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        erCanvas.Entities.Add(target);
        var sut = new ErRenameEntityCommand(erCanvas, target, "sales", "orders_archive");

        sut.Execute(canvas);

        var ddl = Assert.IsType<AlterTableExpr>(sut.ToDdlExpression());
        var op = Assert.IsType<RenameTableOpExpr>(Assert.Single(ddl.Operations));

        Assert.Equal("public", ddl.SchemaName);
        Assert.Equal("orders", ddl.TableName);
        Assert.Equal("orders_archive", op.NewName);
        Assert.Equal("sales", op.NewSchema);
    }

    [Fact]
    public void Constructor_WithNullSchemaAndName_ExecuteThrowsFromInvalidName()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel target = CreateEntity("public", "orders");
        erCanvas.Entities.Add(target);
        var sut = new ErRenameEntityCommand(erCanvas, target, null!, null!);

        Assert.Throws<ArgumentException>(() => sut.Execute(canvas));
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

    private static ErRelationEdgeViewModel CreateEdge(string childEntityId, string parentEntityId)
    {
        return new ErRelationEdgeViewModel(
            constraintName: null,
            childEntityId: childEntityId,
            parentEntityId: parentEntityId,
            childColumn: "id",
            parentColumn: "id",
            onDelete: ReferentialAction.NoAction,
            onUpdate: ReferentialAction.NoAction);
    }
}
