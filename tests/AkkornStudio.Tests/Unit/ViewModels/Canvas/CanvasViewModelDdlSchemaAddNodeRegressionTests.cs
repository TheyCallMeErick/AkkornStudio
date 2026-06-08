using Avalonia;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class CanvasViewModelDdlSchemaAddNodeRegressionTests
{
    [Fact]
    public void SchemaAddNode_InDdlDomain_ImportsTableIntoDdlGraph()
    {
        var canvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new DdlDomainStrategy());

        canvas.SetDatabaseContext(BuildMetadata(), config: null);

        SchemaViewModel schema = canvas.Schema;
        SchemaObjectViewModel tableItem = schema.Categories
            .First(category => category.Name == "Tables")
            .Items
            .First(item => item.Name == "orders");

        Assert.NotNull(tableItem.AddNodeCommand);

        tableItem.AddNodeCommand!.Execute(null);

        Assert.Contains(canvas.Nodes, node => node.Type == NodeType.TableDefinition);
        Assert.Contains(canvas.Nodes, node => node.Type == NodeType.CreateTableOutput);
        Assert.DoesNotContain(canvas.Nodes, node => node.Type == NodeType.TableSource);
    }

    private static DbMetadata BuildMetadata()
    {
        TableMetadata orders = new(
            Schema: "public",
            Name: "orders",
            Kind: TableKind.Table,
            EstimatedRowCount: null,
            Columns:
            [
                new ColumnMetadata("id", "int", "int", false, true, false, false, true, 1),
                new ColumnMetadata("customer_id", "int", "int", false, false, true, false, true, 2),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: []);

        return new DbMetadata(
            DatabaseName: "sample",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16.0",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [orders])],
            AllForeignKeys: []);
    }
}
