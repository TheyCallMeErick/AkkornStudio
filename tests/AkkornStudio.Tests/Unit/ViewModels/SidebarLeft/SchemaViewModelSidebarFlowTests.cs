using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.SidebarLeft;

public class SchemaViewModelSidebarFlowTests
{
    [Fact]
    public void Metadata_SetWithoutSchemas_KeepsCategoriesEmpty()
    {
        var vm = new SchemaViewModel();

        vm.Metadata = new DbMetadata(
            "db",
            DatabaseProvider.Postgres,
            "16",
            DateTimeOffset.UtcNow,
            [],
            []
        );

        Assert.True(vm.HasConnection);
        Assert.Equal("db", vm.DatabaseName);
        Assert.Empty(vm.Categories);
        Assert.True(vm.ShowNoTablesState);
        Assert.False(vm.ShowFilterEmptyState);
    }

    [Fact]
    public void Metadata_WithTablesAndViews_PopulatesExpectedCategories()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildSampleMetadata();

        Assert.True(vm.HasConnection);
        Assert.Equal("sample_db", vm.DatabaseName);
        Assert.Equal(2, vm.Categories.Count);
        Assert.Equal("Tables", vm.Categories[0].Name);
        Assert.Equal("Views", vm.Categories[1].Name);

        Assert.Single(vm.Categories[0].Items);
        Assert.Single(vm.Categories[1].Items);

        SchemaObjectViewModel orders = vm.Categories[0].Items[0];
        Assert.Equal("orders", orders.Name);
        Assert.True(orders.IsExpandable);
        Assert.Equal(2, orders.Children.Count);
        Assert.Equal("KeyPlus", orders.Children[0].Icon);
        Assert.Equal("#FCD34D", orders.Children[0].BadgeColor);
        Assert.Equal("CircleOutline", orders.Children[1].Icon);
        Assert.Equal("#9CA3AF", orders.Children[1].BadgeColor);
    }

    [Fact]
    public void FilterQuery_FiltersSchemaObjects_ByTableOrColumnName()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildSampleMetadata();

        vm.FilterQuery = "customer";

        Assert.NotEmpty(vm.Categories);
        var tables = vm.Categories.First(c => c.Name == "Tables");
        Assert.Single(tables.Items);
        Assert.Equal("orders", tables.Items[0].Name);

        vm.FilterQuery = "nonexistent_filter";

        Assert.Empty(vm.Categories);
        Assert.True(vm.ShowFilterEmptyState);
        Assert.False(vm.ShowNoTablesState);
    }

    [Fact]
    public void SelectedSchema_FiltersVisibleObjectsToSchema()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildMultiSchemaMetadata();

        Assert.Equal(2, vm.Categories.SelectMany(category => category.Items).Count());

        vm.SelectedSchema = "sales";

        List<SchemaObjectViewModel> visible = vm.Categories.SelectMany(category => category.Items).ToList();
        Assert.Single(visible);
        Assert.Equal("invoices", visible[0].Name);
    }

    [Fact]
    public void FilterQuery_ReusesCachedSchemaObjectInstances()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildSampleMetadata();

        SchemaObjectViewModel originalOrders = vm.Categories
            .First(category => category.Name == "Tables")
            .Items
            .First(item => item.Name == "orders");

        vm.FilterQuery = "customer";

        SchemaObjectViewModel filteredOrders = vm.Categories
            .First(category => category.Name == "Tables")
            .Items
            .First(item => item.Name == "orders");

        Assert.Same(originalOrders, filteredOrders);
    }

    [Fact]
    public void FilterQuery_WithAtPrefix_FiltersTablesByName()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildSampleMetadata();

        vm.FilterQuery = "@orders";

        Assert.Single(vm.Categories);
        Assert.Equal("Tables", vm.Categories[0].Name);
        Assert.Single(vm.Categories[0].Items);
        Assert.Equal("orders", vm.Categories[0].Items[0].Name);
    }

    [Fact]
    public void FilterQuery_WithHashPrefix_FiltersViewsOnly()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildSampleMetadata();

        vm.FilterQuery = "#orders";

        Assert.Single(vm.Categories);
        Assert.Equal("Views", vm.Categories[0].Name);
        Assert.Single(vm.Categories[0].Items);
        Assert.Equal("orders_view", vm.Categories[0].Items[0].Name);
    }

    [Fact]
    public void FilterQuery_WithBangPrefix_SearchesColumnsOnly()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildSampleMetadata();

        vm.FilterQuery = "!customer";

        Assert.Single(vm.Categories);
        Assert.Equal("Tables", vm.Categories[0].Name);
        Assert.Single(vm.Categories[0].Items);
        Assert.Equal("orders", vm.Categories[0].Items[0].Name);
    }

    [Fact]
    public void FilterQuery_WithSchemaShortcut_FiltersBySchema()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildMultiSchemaMetadata();

        vm.FilterQuery = "schema:sales";

        Assert.Single(vm.Categories);
        Assert.Equal("Tables", vm.Categories[0].Name);
        Assert.Single(vm.Categories[0].Items);
        Assert.Equal("invoices", vm.Categories[0].Items[0].Name);
    }

    [Fact]
    public void FilterQuery_WithForeignKeyShortcut_ReturnsOnlyTablesWithForeignKeys()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildMetadataWithShortcutSignals();

        vm.FilterQuery = "fk";

        Assert.Single(vm.Categories);
        Assert.Equal("Tables", vm.Categories[0].Name);
        Assert.Single(vm.Categories[0].Items);
        Assert.Equal("order_items", vm.Categories[0].Items[0].Name);
    }

    [Fact]
    public void FilterQuery_WithIndexedShortcut_ReturnsOnlyTablesWithIndexedColumns()
    {
        var vm = new SchemaViewModel();
        vm.Metadata = BuildMetadataWithShortcutSignals();

        vm.FilterQuery = "idx:code";

        Assert.Single(vm.Categories);
        Assert.Equal("Tables", vm.Categories[0].Name);
        Assert.Single(vm.Categories[0].Items);
        Assert.Equal("indexed_table", vm.Categories[0].Items[0].Name);
    }

    private static DbMetadata BuildSampleMetadata()
    {
        var orders = new TableMetadata(
            "public",
            "orders",
            TableKind.Table,
            10,
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("customer_name", "varchar", "varchar", true, false, false, false, false, 2)
            ],
            [],
            [],
            []
        );

        var ordersView = new TableMetadata(
            "public",
            "orders_view",
            TableKind.View,
            null,
            [new ColumnMetadata("id", "int", "int", false, false, false, false, false, 1)],
            [],
            [],
            []
        );

        return new DbMetadata(
            "sample_db",
            DatabaseProvider.Postgres,
            "16",
            DateTimeOffset.UtcNow,
            [new SchemaMetadata("public", [orders, ordersView])],
            []
        );
    }

    private static DbMetadata BuildMultiSchemaMetadata()
    {
        var orders = new TableMetadata(
            "public",
            "orders",
            TableKind.Table,
            10,
            [new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1)],
            [],
            [],
            []
        );

        var invoices = new TableMetadata(
            "sales",
            "invoices",
            TableKind.Table,
            5,
            [new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1)],
            [],
            [],
            []
        );

        return new DbMetadata(
            "sample_db",
            DatabaseProvider.Postgres,
            "16",
            DateTimeOffset.UtcNow,
            [new SchemaMetadata("public", [orders]), new SchemaMetadata("sales", [invoices])],
            []
        );
    }

    private static DbMetadata BuildMetadataWithShortcutSignals()
    {
        var orderItems = new TableMetadata(
            "public",
            "order_items",
            TableKind.Table,
            10,
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("order_id", "int", "int", false, false, true, true, true, 2)
            ],
            [],
            [],
            []
        );

        var indexed = new TableMetadata(
            "public",
            "indexed_table",
            TableKind.Table,
            10,
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("code", "varchar", "varchar", false, false, false, false, true, 2)
            ],
            [],
            [],
            []
        );

        return new DbMetadata(
            "sample_db",
            DatabaseProvider.Postgres,
            "16",
            DateTimeOffset.UtcNow,
            [new SchemaMetadata("public", [orderItems, indexed])],
            []
        );
    }
}
