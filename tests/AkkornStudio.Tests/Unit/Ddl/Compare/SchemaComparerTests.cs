using AkkornStudio.Core;
using AkkornStudio.Ddl.Compare;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Ddl.Compare;

public sealed class SchemaComparerTests
{
    private static readonly SchemaComparer Comparer = new();
    private static readonly SyncScriptGenerator Generator = new();

    [Fact]
    public void Compare_DetectsAddedTable()
    {
        SchemaComparison result = Comparer.Compare(
            new[] { Table("orders", Col("id", "int")) },
            Array.Empty<TableMetadata>(),
            "public");

        TableComparison table = Assert.Single(result.Tables);
        Assert.Equal(TableChangeKind.Added, table.Kind);
        SchemaDifference d = Assert.Single(table.Diff.Differences);
        Assert.IsType<CreateTableOperation>(d.Operation);

        string sql = Generator.Generate(d, DatabaseProvider.Postgres, "public", "orders", SchemaSyncOptions.Default);
        Assert.Contains("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_DetectsRemovedTable()
    {
        SchemaComparison result = Comparer.Compare(
            Array.Empty<TableMetadata>(),
            new[] { Table("legacy", Col("id", "int")) },
            "public");

        TableComparison table = Assert.Single(result.Tables);
        Assert.Equal(TableChangeKind.Removed, table.Kind);
        SchemaDifference d = Assert.Single(table.Diff.Differences);
        Assert.IsType<DropTableOperation>(d.Operation);
        Assert.True(d.IsDestructive);

        string sql = Generator.Generate(d, DatabaseProvider.Postgres, "public", "legacy", new SchemaSyncOptions(ExistenceSafe: true));
        Assert.Contains("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_DetectsModifiedTable()
    {
        SchemaComparison result = Comparer.Compare(
            new[] { Table("orders", Col("id", "int"), Col("total", "int")) },
            new[] { Table("orders", Col("id", "int")) },
            "public");

        TableComparison table = Assert.Single(result.Tables);
        Assert.Equal(TableChangeKind.Modified, table.Kind);
        Assert.Contains(table.Diff.Differences, d => d.Operation is AddColumnOperation);
    }

    [Fact]
    public void Compare_SkipsIdenticalTables()
    {
        SchemaComparison result = Comparer.Compare(
            new[] { Table("orders", Col("id", "int")) },
            new[] { Table("orders", Col("id", "int")) },
            "public");

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Generator_CreateTableIncludesColumnsAndPrimaryKey()
    {
        TableMetadata table = TableWithPk("orders", "id");
        SchemaComparison result = Comparer.Compare(new[] { table }, Array.Empty<TableMetadata>(), "public");
        SchemaDifference d = Assert.Single(result.Tables[0].Diff.Differences);

        string sql = Generator.Generate(d, DatabaseProvider.Postgres, "public", "orders", SchemaSyncOptions.Default);
        Assert.Contains("\"id\"", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generator_CreateTableEmitsSecondaryIndexes()
    {
        var index = new IndexMetadata("ix_email", IsUnique: false, IsClustered: false, IsPrimaryKey: false, new[] { "email" });
        TableMetadata table = new(
            "public",
            "users",
            TableKind.Table,
            null,
            new[] { Col("id", "int", primaryKey: true), Col("email", "varchar") },
            new[] { index },
            Array.Empty<ForeignKeyRelation>(),
            Array.Empty<ForeignKeyRelation>());

        SchemaComparison result = Comparer.Compare(new[] { table }, Array.Empty<TableMetadata>(), "public");
        SchemaDifference d = Assert.Single(result.Tables[0].Diff.Differences);

        string sql = Generator.Generate(d, DatabaseProvider.Postgres, "public", "users", SchemaSyncOptions.Default);
        Assert.Contains("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE", sql.Replace("CREATE TABLE", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_email", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompareDatabase_MatchesByQualifiedNameAcrossSchemas()
    {
        TableMetadata salesOrders = TableIn("sales", "orders", Col("id", "int"));
        TableMetadata hrOrders = TableIn("hr", "orders", Col("id", "int"));

        // Same-named "orders" in two schemas: only hr.orders is missing in the target.
        SchemaComparison result = Comparer.CompareDatabase(
            new[] { salesOrders, hrOrders },
            new[] { salesOrders });

        TableComparison added = Assert.Single(result.Tables);
        Assert.Equal(TableChangeKind.Added, added.Kind);
        Assert.Equal("hr", added.TargetSchema);
        Assert.Equal("orders", added.TableName);
        Assert.IsType<CreateTableOperation>(Assert.Single(added.Diff.Differences).Operation);
    }

    [Fact]
    public void CompareDatabase_DropsTargetOnlyTableInItsOwnSchema()
    {
        SchemaComparison result = Comparer.CompareDatabase(
            Array.Empty<TableMetadata>(),
            new[] { TableIn("audit", "log", Col("id", "int")) });

        TableComparison removed = Assert.Single(result.Tables);
        Assert.Equal(TableChangeKind.Removed, removed.Kind);
        DropTableOperation drop = Assert.IsType<DropTableOperation>(Assert.Single(removed.Diff.Differences).Operation);
        Assert.Equal("audit", drop.Schema);
        Assert.Equal("log", drop.Table);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────
    private static TableMetadata TableIn(string schema, string name, params ColumnMetadata[] columns) =>
        new(
            schema,
            name,
            TableKind.Table,
            null,
            columns,
            Array.Empty<IndexMetadata>(),
            Array.Empty<ForeignKeyRelation>(),
            Array.Empty<ForeignKeyRelation>());

    private static ColumnMetadata Col(string name, string nativeType, bool primaryKey = false, int ordinal = 1) =>
        new(name, nativeType, nativeType, true, primaryKey, false, false, false, ordinal);

    private static TableMetadata Table(string name, params ColumnMetadata[] columns) =>
        new(
            "public",
            name,
            TableKind.Table,
            null,
            columns,
            Array.Empty<IndexMetadata>(),
            Array.Empty<ForeignKeyRelation>(),
            Array.Empty<ForeignKeyRelation>());

    private static TableMetadata TableWithPk(string name, params string[] pkColumns)
    {
        ColumnMetadata[] columns = pkColumns
            .Select((c, i) => Col(c, "int", primaryKey: true, ordinal: i + 1))
            .ToArray();
        var pk = new IndexMetadata($"pk_{name}", IsUnique: true, IsClustered: false, IsPrimaryKey: true, pkColumns);
        return new TableMetadata(
            "public",
            name,
            TableKind.Table,
            null,
            columns,
            new[] { pk },
            Array.Empty<ForeignKeyRelation>(),
            Array.Empty<ForeignKeyRelation>());
    }
}
