using AkkornStudio.Metadata;
using AkkornStudio.UI.ViewModels.ErDiagram;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.ErDiagram;

public sealed class ErCanvasViewModelTests
{
    [Fact]
    public void BindSourceMetadata_WithoutViews_BuildsOnlyTables()
    {
        var vm = new ErCanvasViewModel();

        vm.BindSourceMetadata(CreateMetadata());

        Assert.Equal(2, vm.EntityCount);
        Assert.DoesNotContain(vm.Entities, entity => entity.IsView);
        Assert.False(vm.HasTechnicalWarnings);
    }

    [Fact]
    public void IncludeViews_WhenToggled_RebuildsCanvasFromBoundMetadata()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateMetadata());

        vm.IncludeViews = true;

        Assert.Equal(3, vm.EntityCount);
        Assert.Contains(vm.Entities, entity => entity.IsView);
        Assert.DoesNotContain("W-ER-NO-METADATA", vm.TechnicalWarnings);
    }

    [Fact]
    public void BindSourceMetadata_PopulatesEdgeGeometry()
    {
        var vm = new ErCanvasViewModel();

        vm.BindSourceMetadata(CreateMetadata());

        ErRelationEdgeViewModel edge = Assert.Single(vm.Edges);
        Assert.True(edge.StartX > 0);
        Assert.True(edge.EndX >= 0);
        Assert.True(edge.StartY > 0);
        Assert.True(edge.EndY > 0);
        Assert.NotEqual(edge.StartPoint, edge.EndPoint);
        Assert.Equal(4, edge.RoutePoints.Count);
    }

    [Fact]
    public void SetViewportSize_ThenBindSourceMetadata_FitsDiagramIntoViewport()
    {
        var vm = new ErCanvasViewModel();
        vm.SetViewportSize(1280, 720);

        vm.BindSourceMetadata(CreateMetadata());

        Assert.True(vm.Zoom > 0.15);
        Assert.True(vm.ViewportWidth > 0);
        Assert.True(vm.ViewportHeight > 0);
    }

    [Fact]
    public void PanOffset_UpdatesViewportCoordinates()
    {
        var vm = new ErCanvasViewModel
        {
            PanOffset = new Avalonia.Point(120, 340),
        };

        Assert.Equal(120, vm.ViewportX);
        Assert.Equal(340, vm.ViewportY);
        Assert.Equal(new Avalonia.Point(120, 340), vm.PanOffset);
    }

    [Fact]
    public void SelectedEdge_HighlightsParticipatingColumns()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateMetadata());

        ErRelationEdgeViewModel edge = Assert.Single(vm.Edges);
        vm.SelectedEdge = edge;

        ErEntityNodeViewModel orders = Assert.Single(vm.Entities.Where(entity => entity.Name == "orders"));
        ErEntityNodeViewModel customers = Assert.Single(vm.Entities.Where(entity => entity.Name == "customers"));

        Assert.Contains(orders.Columns, column => column.ColumnName == "customer_id" && column.IsRelationEndpointHighlighted);
        Assert.Contains(customers.Columns, column => column.ColumnName == "id" && column.IsRelationEndpointHighlighted);
        Assert.True(edge.IsSelected);
    }

    [Fact]
    public void SelectedEdge_ExposesJoinPredicateAndDetails()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateCompositeMetadata());

        ErRelationEdgeViewModel edge = Assert.Single(vm.Edges);
        vm.SelectedEdge = edge;

        Assert.True(vm.HasSelectionDetails);
        Assert.True(vm.HasSelectionJoinPredicate);
        Assert.Equal(edge.ConstraintLabel, vm.SelectionTitle);
        Assert.Contains("tenant_id -> tenant_id", vm.SelectionBody, StringComparison.Ordinal);
        Assert.Contains("public.orders.tenant_id = public.customers.tenant_id", vm.SelectionJoinPredicate, StringComparison.Ordinal);
        Assert.Contains("AND", vm.SelectionJoinPredicate, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedEntity_ExposesEntitySummary()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateMetadata());

        ErEntityNodeViewModel entity = Assert.Single(vm.Entities.Where(item => item.Name == "orders"));
        vm.SelectedEntity = entity;

        Assert.True(vm.HasSelectionDetails);
        Assert.False(vm.HasSelectionJoinPredicate);
        Assert.Equal(entity.DisplayName, vm.SelectionTitle);
        Assert.Contains("2 coluna(s)", vm.SelectionBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetSelectionFrame_ForEntity_ReturnsEntityBoundsWithPadding()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateMetadata());
        ErEntityNodeViewModel entity = Assert.Single(vm.Entities.Where(item => item.Name == "orders"));
        vm.SelectedEntity = entity;

        bool ok = vm.TryGetSelectionFrame(12, out Avalonia.Rect frame);

        Assert.True(ok);
        Assert.Equal(entity.X - 12, frame.X);
        Assert.Equal(entity.Y - 12, frame.Y);
        Assert.True(frame.Width > 220);
        Assert.True(frame.Height > 36);
    }

    [Fact]
    public void TryGetSelectionFrame_ForEdge_ReturnsRouteBoundsWithPadding()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateCompositeMetadata());
        ErRelationEdgeViewModel edge = Assert.Single(vm.Edges);
        vm.SelectedEdge = edge;

        bool ok = vm.TryGetSelectionFrame(10, out Avalonia.Rect frame);

        Assert.True(ok);
        Assert.True(frame.Width > 0);
        Assert.True(frame.Height > 0);
        Assert.True(frame.X <= edge.RoutePoints.Min(point => point.X));
        Assert.True(frame.Y <= edge.RoutePoints.Min(point => point.Y));
    }

    [Fact]
    public void TrySelectEntityInRegion_SelectsNearestIntersectingEntity()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateMetadata());

        bool selected = vm.TrySelectEntityInRegion(new Avalonia.Rect(340, 40, 260, 220));

        Assert.True(selected);
        Assert.Equal("orders", vm.SelectedEntity?.Name);
    }

    [Fact]
    public void OpenSelectionInQueryCommand_TracksSelectedEdgeAndNavigationBinding()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateMetadata());
        ErRelationEdgeViewModel edge = Assert.Single(vm.Edges);
        ErRelationEdgeViewModel? openedEdge = null;

        Assert.False(vm.OpenSelectionInQueryCommand.CanExecute(null));

        vm.BindQueryNavigation(selected => openedEdge = selected);
        vm.SelectedEdge = edge;

        Assert.True(vm.OpenSelectionInQueryCommand.CanExecute(null));

        vm.OpenSelectionInQueryCommand.Execute(null);

        Assert.Same(edge, openedEdge);
    }

    [Fact]
    public void TryFocusRelation_SelectsEdgeAndRequestsViewportFocus()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateCompositeMetadata());

        bool focused = vm.TryFocusRelation(
            "public.orders",
            "public.customers",
            ["tenant_id", "customer_id"],
            ["tenant_id", "id"]);

        Assert.True(focused);
        ErRelationEdgeViewModel edge = Assert.IsType<ErRelationEdgeViewModel>(vm.SelectedEdge);
        Assert.True(vm.FocusRequestVersion > 0);
        Assert.InRange(vm.FocusTargetX, Math.Min(edge.StartX, edge.EndX), Math.Max(edge.StartX, edge.EndX));
        Assert.InRange(vm.FocusTargetY, Math.Min(edge.StartY, edge.EndY), Math.Max(edge.StartY, edge.EndY));
    }

    [Fact]
    public void BindSourceMetadata_WithNullMetadata_EmitsNoMetadataWarning()
    {
        var vm = new ErCanvasViewModel();

        vm.BindSourceMetadata(null);

        Assert.True(vm.HasTechnicalWarnings);
        Assert.Contains("W-ER-NO-METADATA", vm.TechnicalWarnings);
    }

    [Fact]
    public void BindSourceMetadata_WithCompositeForeignKey_MaterializesSingleAggregatedEdge()
    {
        var vm = new ErCanvasViewModel();

        vm.BindSourceMetadata(CreateCompositeMetadata());

        ErRelationEdgeViewModel edge = Assert.Single(vm.Edges);
        Assert.Equal(["tenant_id", "customer_id"], edge.ChildColumns);
        Assert.Equal(["tenant_id", "id"], edge.ParentColumns);
        Assert.DoesNotContain("W-ER-READ-COMPOSITE-FK-IGNORED", vm.TechnicalWarnings);
    }

    [Fact]
    public void SelectedCompositeEdge_HighlightsAllParticipatingColumns()
    {
        var vm = new ErCanvasViewModel();
        vm.BindSourceMetadata(CreateCompositeMetadata());

        ErRelationEdgeViewModel edge = Assert.Single(vm.Edges);
        vm.SelectedEdge = edge;

        ErEntityNodeViewModel orders = Assert.Single(vm.Entities.Where(entity => entity.Name == "orders"));
        ErEntityNodeViewModel customers = Assert.Single(vm.Entities.Where(entity => entity.Name == "customers"));

        Assert.Contains(orders.Columns, column => column.ColumnName == "tenant_id" && column.IsRelationEndpointHighlighted);
        Assert.Contains(orders.Columns, column => column.ColumnName == "customer_id" && column.IsRelationEndpointHighlighted);
        Assert.Contains(customers.Columns, column => column.ColumnName == "tenant_id" && column.IsRelationEndpointHighlighted);
        Assert.Contains(customers.Columns, column => column.ColumnName == "id" && column.IsRelationEndpointHighlighted);
    }

    private static DbMetadata CreateMetadata()
    {
        TableMetadata customers = new(
            Schema: "public",
            Name: "customers",
            Kind: TableKind.Table,
            EstimatedRowCount: 10,
            Columns:
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: []);

        ForeignKeyRelation ordersToCustomers = new(
            ConstraintName: "fk_orders_customers",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "customer_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction);

        TableMetadata orders = new(
            Schema: "public",
            Name: "orders",
            Kind: TableKind.Table,
            EstimatedRowCount: 20,
            Columns:
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("customer_id", "int", "int", false, false, true, false, true, 2),
            ],
            Indexes: [],
            OutboundForeignKeys: [ordersToCustomers],
            InboundForeignKeys: []);

        TableMetadata ordersView = new(
            Schema: "reporting",
            Name: "vw_orders",
            Kind: TableKind.View,
            EstimatedRowCount: null,
            Columns:
            [
                new ColumnMetadata("id", "int", "int", false, false, false, false, false, 1),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: []);

        return new DbMetadata(
            DatabaseName: "akkorn",
            Provider: AkkornStudio.Core.DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas:
            [
                new SchemaMetadata("public", [customers, orders]),
                new SchemaMetadata("reporting", [ordersView]),
            ],
            AllForeignKeys: [ordersToCustomers]);
    }

    private static DbMetadata CreateCompositeMetadata()
    {
        ForeignKeyRelation ordersToCustomersTenant = new(
            ConstraintName: "fk_orders_customers_composite",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "tenant_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "tenant_id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction,
            OrdinalPosition: 1);

        ForeignKeyRelation ordersToCustomersCustomer = new(
            ConstraintName: "fk_orders_customers_composite",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "customer_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction,
            OrdinalPosition: 2);

        TableMetadata customers = new(
            Schema: "public",
            Name: "customers",
            Kind: TableKind.Table,
            EstimatedRowCount: 10,
            Columns:
            [
                new ColumnMetadata("tenant_id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 2),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: [ordersToCustomersTenant, ordersToCustomersCustomer]);

        TableMetadata orders = new(
            Schema: "public",
            Name: "orders",
            Kind: TableKind.Table,
            EstimatedRowCount: 20,
            Columns:
            [
                new ColumnMetadata("tenant_id", "int", "int", false, false, true, false, true, 1),
                new ColumnMetadata("customer_id", "int", "int", false, false, true, false, true, 2),
            ],
            Indexes: [],
            OutboundForeignKeys: [ordersToCustomersTenant, ordersToCustomersCustomer],
            InboundForeignKeys: []);

        return new DbMetadata(
            DatabaseName: "akkorn",
            Provider: AkkornStudio.Core.DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas:
            [
                new SchemaMetadata("public", [customers, orders]),
            ],
            AllForeignKeys: [ordersToCustomersTenant, ordersToCustomersCustomer]);
    }
}
