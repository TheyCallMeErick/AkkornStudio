using AkkornStudio.Core;
using AkkornStudio.Ddl.Compare;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Ddl.Compare;

public sealed class TableComparerTests
{
    private static readonly TableComparer Comparer = new();
    private static readonly SyncScriptGenerator Generator = new();

    // ── #1: length/precision are part of the compared type signature ──────────────
    [Fact]
    public void Compare_DetectsColumnLengthChange()
    {
        TableDiff diff = Comparer.Compare(
            Table("public", "t", Col("name", "varchar", maxLength: 200)),
            Table("public", "t", Col("name", "varchar", maxLength: 50)));

        SchemaDifference d = Assert.Single(diff.Differences);
        Assert.IsType<AlterColumnOperation>(d.Operation);

        string sql = Generator.Generate(d, DatabaseProvider.Postgres, "public", "t", SchemaSyncOptions.Default);
        Assert.Contains("200", sql, StringComparison.Ordinal);
    }

    // ── #9: type aliases must not produce false positives ─────────────────────────
    [Fact]
    public void Compare_TreatsTypeAliasesAsEqual()
    {
        TableDiff diff = Comparer.Compare(
            Table("public", "t", Col("id", "int4", "int4")),
            Table("public", "t", Col("id", "integer", "integer")));

        Assert.Empty(diff.Differences);
    }

    // ── #5: non-unique indexes are compared and emitted ───────────────────────────
    [Fact]
    public void Compare_EmitsCreateIndexForMissingSecondaryIndex()
    {
        TableDiff diff = Comparer.Compare(
            Table(
                "public",
                "t",
                new[] { Col("email", "varchar") },
                new[] { new IndexMetadata("ix_email", IsUnique: false, IsClustered: false, IsPrimaryKey: false, new[] { "email" }) }),
            Table("public", "t", Col("email", "varchar")));

        SchemaDifference d = Assert.Single(diff.Differences);
        Assert.IsType<CreateIndexOperation>(d.Operation);

        string sql = Generator.Generate(d, DatabaseProvider.Postgres, "public", "t", new SchemaSyncOptions(ExistenceSafe: true));
        Assert.Contains("CREATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_email", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── #7: composite FK is grouped and emitted as one multi-column constraint ────
    [Fact]
    public void Compare_EmitsCompositeForeignKeyAsSingleStatement()
    {
        var fk = new[]
        {
            new ForeignKeyRelation("fk_ab", "public", "child", "a", "public", "parent", "x", ReferentialAction.NoAction, ReferentialAction.NoAction, 1),
            new ForeignKeyRelation("fk_ab", "public", "child", "b", "public", "parent", "y", ReferentialAction.NoAction, ReferentialAction.NoAction, 2),
        };

        TableDiff diff = Comparer.Compare(
            Table("public", "child", new[] { Col("a", "int"), Col("b", "int") }, outbound: fk),
            Table("public", "child", new[] { Col("a", "int"), Col("b", "int") }));

        SchemaDifference d = Assert.Single(diff.Differences);
        AddForeignKeyOperation op = Assert.IsType<AddForeignKeyOperation>(d.Operation);
        Assert.Equal(new[] { "a", "b" }, op.ForeignKey.ChildColumns);

        string sql = Generator.Generate(d, DatabaseProvider.Postgres, "public", "child", SchemaSyncOptions.Default);
        Assert.Contains("\"a\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"b\"", sql, StringComparison.Ordinal);
    }

    // ── #7: composite PK column order is preserved (order-sensitive) ──────────────
    [Fact]
    public void Compare_DetectsReorderedPrimaryKey()
    {
        TableDiff diff = Comparer.Compare(TableWithPk("pk_s", "a", "b"), TableWithPk("pk_t", "b", "a"));

        SchemaDifference d = Assert.Single(diff.Differences);
        RecreatePrimaryKeyOperation op = Assert.IsType<RecreatePrimaryKeyOperation>(d.Operation);
        Assert.Equal(new[] { "a", "b" }, op.Columns);
    }

    // ── #8: source/target direction drives Add vs Drop ───────────────────────────
    [Fact]
    public void Compare_DirectionDeterminesAddVsDrop()
    {
        TableMetadata withCol = Table("public", "t", new[] { Col("id", "int"), Col("extra", "int") });
        TableMetadata withoutCol = Table("public", "t", Col("id", "int"));

        Assert.IsType<AddColumnOperation>(Assert.Single(Comparer.Compare(withCol, withoutCol).Differences).Operation);
        Assert.IsType<DropColumnOperation>(Assert.Single(Comparer.Compare(withoutCol, withCol).Differences).Operation);
    }

    // ── #10: SQLite must not throw and must not emit IF EXISTS on DROP COLUMN ──────
    [Fact]
    public void Generate_SqliteDropColumnHasNoIfExists()
    {
        TableDiff diff = Comparer.Compare(
            Table("main", "t", Col("id", "int")),
            Table("main", "t", new[] { Col("id", "int"), Col("legacy", "int") }));

        SchemaDifference d = Assert.Single(diff.Differences);
        string sql = Generator.Generate(d, DatabaseProvider.SQLite, "main", "t", new SchemaSyncOptions(ExistenceSafe: true));

        Assert.Contains("DROP COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── #4: default change is actionable on Postgres ─────────────────────────────
    [Fact]
    public void Generate_SetDefaultOnPostgres()
    {
        TableDiff diff = Comparer.Compare(
            Table("public", "t", Col("flag", "int", defaultValue: "1")),
            Table("public", "t", Col("flag", "int")));

        SchemaDifference d = Assert.Single(diff.Differences);
        Assert.IsType<SetColumnDefaultOperation>(d.Operation);

        string sql = Generator.Generate(d, DatabaseProvider.Postgres, "public", "t", SchemaSyncOptions.Default);
        Assert.Contains("SET DEFAULT 1", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── Phase 3: CHECK constraints ────────────────────────────────────────────────
    [Fact]
    public void Compare_EmitsAddCheckForMissingConstraint()
    {
        TableDiff diff = Comparer.Compare(
            TableWithChecks(new CheckConstraintMetadata("ck_amount", "amount > 0")),
            TableWithChecks());

        SchemaDifference d = Assert.Single(diff.Differences);
        Assert.Equal(SchemaDiffCategory.Check, d.Category);
        AddCheckOperation op = Assert.IsType<AddCheckOperation>(d.Operation);
        Assert.Equal("ck_amount", op.Name);

        string sql = Generator.Generate(d, DatabaseProvider.Postgres, "public", "t", SchemaSyncOptions.Default);
        Assert.Contains("ADD", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHECK", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("amount > 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_EmitsDropCheckForExtraConstraint()
    {
        TableDiff diff = Comparer.Compare(
            TableWithChecks(),
            TableWithChecks(new CheckConstraintMetadata("ck_amount", "amount > 0")));

        SchemaDifference d = Assert.Single(diff.Differences);
        Assert.IsType<DropCheckOperation>(d.Operation);

        string pg = Generator.Generate(d, DatabaseProvider.Postgres, "public", "t", SchemaSyncOptions.Default);
        Assert.Contains("DROP CONSTRAINT", pg, StringComparison.OrdinalIgnoreCase);

        string mysql = Generator.Generate(d, DatabaseProvider.MySql, "public", "t", SchemaSyncOptions.Default);
        Assert.Contains("DROP CHECK", mysql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_IgnoresIdenticalCheckWithDifferentName()
    {
        TableDiff diff = Comparer.Compare(
            TableWithChecks(new CheckConstraintMetadata("ck_a", "(amount > 0)")),
            TableWithChecks(new CheckConstraintMetadata("ck_b", "amount>0")));

        Assert.Empty(diff.Differences);
    }

    // ── Phase 3b: column attributes (informational) ───────────────────────────────
    [Fact]
    public void Compare_DetectsIdentityChangeAsInformationalNote()
    {
        TableDiff diff = Comparer.Compare(
            Table("public", "t", Col("id", "int") with { IsAutoIncrement = true }),
            Table("public", "t", Col("id", "int")));

        SchemaDifference d = Assert.Single(diff.Differences);
        ColumnAttributeNote note = Assert.IsType<ColumnAttributeNote>(d.Operation);
        Assert.Equal("Identity", note.Attribute);
        Assert.False(d.IsDestructive);

        // Informational: no executable SQL is produced.
        Assert.Equal(string.Empty, Generator.Generate(d, DatabaseProvider.Postgres, "public", "t", SchemaSyncOptions.Default));
    }

    [Fact]
    public void Compare_DetectsCollationAndGeneratedChanges()
    {
        TableDiff diff = Comparer.Compare(
            Table("public", "t", Col("name", "varchar") with { Collation = "C", GeneratedExpression = "upper(x)" }),
            Table("public", "t", Col("name", "varchar") with { Collation = "POSIX" }));

        Assert.Contains(diff.Differences, d => d.Operation is ColumnAttributeNote { Attribute: "Collation" });
        Assert.Contains(diff.Differences, d => d.Operation is ColumnAttributeNote { Attribute: "Generated" });
    }

    [Fact]
    public void Compare_IgnoresEqualColumnAttributes()
    {
        TableDiff diff = Comparer.Compare(
            Table("public", "t", Col("id", "int") with { IsAutoIncrement = true, Collation = "C" }),
            Table("public", "t", Col("id", "int") with { IsAutoIncrement = true, Collation = "c" }));

        Assert.Empty(diff.Differences);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────
    private static TableMetadata TableWithChecks(params CheckConstraintMetadata[] checks) =>
        Table("public", "t", Col("amount", "int")) with { CheckConstraints = checks };

    private static ColumnMetadata Col(
        string name,
        string nativeType,
        string? dataType = null,
        bool nullable = true,
        bool primaryKey = false,
        int? maxLength = null,
        string? defaultValue = null,
        int ordinal = 1) =>
        new(name, dataType ?? nativeType, nativeType, nullable, primaryKey, false, false, false, ordinal, DefaultValue: defaultValue, MaxLength: maxLength);

    private static TableMetadata Table(string schema, string name, params ColumnMetadata[] columns) =>
        Table(schema, name, columns, Array.Empty<IndexMetadata>());

    private static TableMetadata Table(
        string schema,
        string name,
        IReadOnlyList<ColumnMetadata> columns,
        IReadOnlyList<IndexMetadata>? indexes = null,
        IReadOnlyList<ForeignKeyRelation>? outbound = null) =>
        new(
            schema,
            name,
            TableKind.Table,
            null,
            columns,
            indexes ?? Array.Empty<IndexMetadata>(),
            outbound ?? Array.Empty<ForeignKeyRelation>(),
            Array.Empty<ForeignKeyRelation>());

    private static TableMetadata TableWithPk(string pkName, params string[] pkColumns)
    {
        ColumnMetadata[] columns = pkColumns
            .Select((c, i) => Col(c, "int", primaryKey: true, ordinal: i + 1))
            .ToArray();
        var pk = new IndexMetadata(pkName, IsUnique: true, IsClustered: false, IsPrimaryKey: true, pkColumns);
        return Table("public", "t", columns, new[] { pk });
    }
}
