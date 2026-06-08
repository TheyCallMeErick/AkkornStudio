using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Ddl;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;
using System.Text.Json;

namespace AkkornStudio.Tests.Unit.Services.Ddl;

public class DdlSchemaImporterTests
{
    [Fact]
    public void Import_BuildsDdlGraph_AndProducesValidPreview()
    {
        DbMetadata metadata = BuildMetadata();
        var canvas = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());
        var importer = new DdlSchemaImporter();

        DdlImportResult result = importer.Import(metadata, canvas);

        Assert.Equal(2, result.TableCount);
        Assert.Equal(4, result.ColumnCount);
        Assert.Equal(1, result.ForeignKeyCount);
        Assert.Equal(1, result.IndexCount);

        Assert.Equal(2, canvas.Nodes.Count(n => n.Type == NodeType.TableDefinition));
        Assert.Equal(4, canvas.Nodes.Count(n => n.Type == NodeType.ColumnDefinition));
        Assert.Equal(2, canvas.Nodes.Count(n => n.Type == NodeType.CreateTableOutput));
        Assert.Single(canvas.Nodes, n => n.Type == NodeType.PrimaryKeyConstraint);
        Assert.Single(canvas.Nodes, n => n.Type == NodeType.UniqueConstraint);
        Assert.Single(canvas.Nodes, n => n.Type == NodeType.ForeignKeyConstraint);

        Assert.NotNull(canvas.LiveDdl);
        canvas.LiveDdl!.Recompile();

        Assert.True(canvas.LiveDdl.IsValid);
        Assert.Contains("CREATE TABLE", canvas.LiveDdl.RawSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportTable_AddsOnlyMissingTable_AndSkipsDuplicates()
    {
        DbMetadata metadata = BuildMetadata();
        var canvas = new CanvasViewModel();
        var importer = new DdlSchemaImporter();

        DdlPartialImportResult first = importer.ImportTable(metadata, "public.customers", canvas);
        DdlPartialImportResult duplicate = importer.ImportTable(metadata, "public.customers", canvas);
        DdlPartialImportResult second = importer.ImportTable(metadata, "public.orders", canvas);

        Assert.True(first.TableAdded);
        Assert.False(duplicate.TableAdded);
        Assert.True(second.TableAdded);

        Assert.Equal(2, canvas.Nodes.Count(n => n.Type == NodeType.TableDefinition));
        Assert.Contains(canvas.Nodes, n => n.Type == NodeType.ForeignKeyConstraint);
    }

    [Fact]
    public void Import_MySqlEnumColumn_CreatesEnumTypeDefinitionAndTypeConnection()
    {
        DbMetadata metadata = BuildMySqlEnumMetadata();
        var canvas = new CanvasViewModel();
        var importer = new DdlSchemaImporter();

        importer.Import(metadata, canvas);

        NodeViewModel enumType = Assert.Single(canvas.Nodes, n => n.Type == NodeType.EnumTypeDefinition);
        NodeViewModel column = Assert.Single(canvas.Nodes, n => n.Type == NodeType.ColumnDefinition);
        Assert.Equal("ENUM", column.Parameters["DataType"]);

        Assert.Contains(canvas.Connections, c =>
            c.FromPin.Owner == enumType
            && c.FromPin.Name == "type_def"
            && c.ToPin?.Owner == column
            && c.ToPin.Name == "type_def");
    }

    [Fact]
    public void Import_WithView_MetadataCreatesViewDefinitionAndWarning()
    {
        DbMetadata metadata = BuildMetadataWithView();
        var canvas = new CanvasViewModel();
        var importer = new DdlSchemaImporter();

        DdlImportResult result = importer.Import(metadata, canvas);

        NodeViewModel viewNode = Assert.Single(canvas.Nodes, n => n.Type == NodeType.ViewDefinition);
        Assert.Single(canvas.Nodes, n => n.Type == NodeType.CreateViewOutput);
        Assert.Equal("v_orders", viewNode.Parameters["ViewName"]);
        Assert.Equal("SELECT NULL AS \"id\"", viewNode.Parameters["SelectSql"]);
        Assert.True(viewNode.Parameters.TryGetValue(CanvasSerializer.ViewSubgraphParameterKey, out string? subgraphJson));
        Assert.False(string.IsNullOrWhiteSpace(subgraphJson));
        NodeGraph? subgraph = JsonSerializer.Deserialize<NodeGraph>(subgraphJson!);
        Assert.NotNull(subgraph);
        NodeInstance subquery = Assert.Single(subgraph!.Nodes, n => n.Type == NodeType.Subquery);
        Assert.Equal("SELECT NULL AS \"id\"", subquery.Parameters["query"]);
        Assert.Contains(subgraph.Nodes, n => n.Type == NodeType.ResultOutput);
        Assert.Equal("(SELECT NULL AS \"id\") view_src", viewNode.Parameters[CanvasSerializer.ViewFromTableParameterKey]);
        Assert.Contains(result.Warnings ?? [], w => w.Contains("v_orders", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportTable_WithView_AddsViewNodesAndSkipsDuplicate()
    {
        DbMetadata metadata = BuildMetadataWithView();
        var canvas = new CanvasViewModel();
        var importer = new DdlSchemaImporter();

        DdlPartialImportResult first = importer.ImportTable(metadata, "public.v_orders", canvas);
        DdlPartialImportResult duplicate = importer.ImportTable(metadata, "public.v_orders", canvas);

        Assert.True(first.TableAdded);
        Assert.False(duplicate.TableAdded);
        Assert.Single(canvas.Nodes, n => n.Type == NodeType.ViewDefinition);
        Assert.Single(canvas.Nodes, n => n.Type == NodeType.CreateViewOutput);
        Assert.DoesNotContain(canvas.Nodes, n => n.Type == NodeType.TableDefinition);

        NodeViewModel viewNode = Assert.Single(canvas.Nodes, n => n.Type == NodeType.ViewDefinition);
        string payload = viewNode.Parameters[CanvasSerializer.ViewSubgraphParameterKey];
        NodeGraph? subgraph = JsonSerializer.Deserialize<NodeGraph>(payload);
        Assert.NotNull(subgraph);
        NodeInstance subquery = Assert.Single(subgraph!.Nodes, n => n.Type == NodeType.Subquery);
        Assert.Equal("SELECT NULL AS \"id\"", subquery.Parameters["query"]);
    }

    [Fact]
    public void Import_WithViewColumns_PreservesColumnShapeInEditableSubcanvas()
    {
        DbMetadata metadata = BuildMetadataWithView(
            new ColumnMetadata("id", "int", "int", false, false, false, false, false, 1),
            new ColumnMetadata("order_status", "text", "text", true, false, false, false, false, 2)
        );
        var canvas = new CanvasViewModel();
        var importer = new DdlSchemaImporter();

        importer.Import(metadata, canvas);

        NodeViewModel viewNode = Assert.Single(canvas.Nodes, n => n.Type == NodeType.ViewDefinition);
        Assert.Equal("SELECT NULL AS \"id\", NULL AS \"order_status\"", viewNode.Parameters["SelectSql"]);

        string payload = viewNode.Parameters[CanvasSerializer.ViewSubgraphParameterKey];
        NodeGraph? subgraph = JsonSerializer.Deserialize<NodeGraph>(payload);
        Assert.NotNull(subgraph);
        NodeInstance subquery = Assert.Single(subgraph!.Nodes, n => n.Type == NodeType.Subquery);
        Assert.Equal("SELECT NULL AS \"id\", NULL AS \"order_status\"", subquery.Parameters["query"]);
    }

    [Fact]
    public void Import_WithTableAndColumnComments_PopulatesCommentParameters()
    {
        DbMetadata metadata = BuildMetadataWithComments();
        var canvas = new CanvasViewModel();
        var importer = new DdlSchemaImporter();

        importer.Import(metadata, canvas);

        NodeViewModel tableNode = Assert.Single(canvas.Nodes, n => n.Type == NodeType.TableDefinition);
        NodeViewModel colNode = Assert.Single(canvas.Nodes, n => n.Type == NodeType.ColumnDefinition);

        Assert.Equal("Customer master data", tableNode.Parameters["Comment"]);
        Assert.Equal("Unique customer id", colNode.Parameters["Comment"]);
    }

    [Fact]
    public void Import_WithPostgresSequence_CreatesSequenceNodesAndConnectsColumn()
    {
        DbMetadata metadata = BuildMetadataWithSequence();
        var canvas = new CanvasViewModel();
        var importer = new DdlSchemaImporter();

        importer.Import(metadata, canvas);

        NodeViewModel sequenceNode = Assert.Single(canvas.Nodes, n => n.Type == NodeType.SequenceDefinition);
        NodeViewModel sequenceOutput = Assert.Single(canvas.Nodes, n => n.Type == NodeType.CreateSequenceOutput);
        NodeViewModel columnNode = Assert.Single(canvas.Nodes, n => n.Type == NodeType.ColumnDefinition);

        Assert.Equal("orders_id_seq", sequenceNode.Parameters["SequenceName"]);
        Assert.Contains(canvas.Connections, c =>
            c.FromPin.Owner == sequenceNode
            && c.FromPin.Name == "seq"
            && c.ToPin?.Owner == sequenceOutput
            && c.ToPin.Name == "seq");

        Assert.Contains(canvas.Connections, c =>
            c.FromPin.Owner == sequenceNode
            && c.FromPin.Name == "seq"
            && c.ToPin?.Owner == columnNode
            && c.ToPin.Name == "sequence");
    }

    private static DbMetadata BuildMetadata()
    {
        var customersColumns = new List<ColumnMetadata>
        {
            new("id", "int", "int", false, true, false, false, true, 1),
            new("name", "varchar", "varchar", false, false, false, true, false, 2, MaxLength: 120),
        };

        var ordersColumns = new List<ColumnMetadata>
        {
            new("id", "int", "int", false, false, false, false, true, 1),
            new("customer_id", "int", "int", false, false, true, false, true, 2),
        };

        var fk = new ForeignKeyRelation(
            ConstraintName: "FK_orders_customers",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "customer_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction,
            OrdinalPosition: 1
        );

        TableMetadata customers = new(
            Schema: "public",
            Name: "customers",
            Kind: TableKind.Table,
            EstimatedRowCount: null,
            Columns: customersColumns,
            Indexes: [new IndexMetadata("UQ_customers_name", true, false, false, ["name"])],
            OutboundForeignKeys: [],
            InboundForeignKeys: [fk]
        );

        TableMetadata orders = new(
            Schema: "public",
            Name: "orders",
            Kind: TableKind.Table,
            EstimatedRowCount: null,
            Columns: ordersColumns,
            Indexes: [],
            OutboundForeignKeys: [fk],
            InboundForeignKeys: []
        );

        return new DbMetadata(
            DatabaseName: "sample",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16.0",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [customers, orders])],
            AllForeignKeys: [fk]
        );
    }

    private static DbMetadata BuildMySqlEnumMetadata()
    {
        var statusColumns = new List<ColumnMetadata>
        {
            new("status", "enum", "enum('NEW','ACTIVE')", false, false, false, false, false, 1),
        };

        TableMetadata statusTable = new(
            Schema: "public",
            Name: "order_status",
            Kind: TableKind.Table,
            EstimatedRowCount: null,
            Columns: statusColumns,
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: []
        );

        return new DbMetadata(
            DatabaseName: "sample",
            Provider: DatabaseProvider.MySql,
            ServerVersion: "8.0",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [statusTable])],
            AllForeignKeys: []
        );
    }

    private static DbMetadata BuildMetadataWithView(params ColumnMetadata[]? columns)
    {
        TableMetadata view = new(
            Schema: "public",
            Name: "v_orders",
            Kind: TableKind.View,
            EstimatedRowCount: null,
            Columns: columns is { Length: > 0 }
                ? columns
                : [new ColumnMetadata("id", "int", "int", false, false, false, false, false, 1)],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: []
        );

        return new DbMetadata(
            DatabaseName: "sample",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16.0",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [view])],
            AllForeignKeys: []
        );
    }

    private static DbMetadata BuildMetadataWithComments()
    {
        TableMetadata table = new(
            Schema: "public",
            Name: "customers",
            Kind: TableKind.Table,
            EstimatedRowCount: null,
            Columns:
            [
                new ColumnMetadata(
                    "id",
                    "int",
                    "int",
                    false,
                    false,
                    false,
                    false,
                    false,
                    1,
                    Comment: "Unique customer id"
                ),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: [],
            Comment: "Customer master data"
        );

        return new DbMetadata(
            DatabaseName: "sample",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16.0",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [table])],
            AllForeignKeys: []
        );
    }

    private static DbMetadata BuildMetadataWithSequence()
    {
        TableMetadata table = new(
            Schema: "public",
            Name: "orders",
            Kind: TableKind.Table,
            EstimatedRowCount: null,
            Columns:
            [
                new ColumnMetadata(
                    "id",
                    "bigint",
                    "bigint",
                    false,
                    false,
                    false,
                    false,
                    false,
                    1,
                    DefaultValue: "nextval('public.orders_id_seq'::regclass)"
                ),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: []
        );

        return new DbMetadata(
            DatabaseName: "sample",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16.0",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [table])],
            AllForeignKeys: [],
            Sequences:
            [
                new SequenceMetadata(
                    Schema: "public",
                    Name: "orders_id_seq",
                    StartValue: 1,
                    Increment: 1,
                    MinValue: null,
                    MaxValue: null,
                    Cycle: false,
                    Cache: null
                ),
            ]
        );
    }
}
