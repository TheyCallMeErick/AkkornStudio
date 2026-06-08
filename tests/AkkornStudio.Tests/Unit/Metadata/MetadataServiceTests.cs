using AkkornStudio.Core;
using AkkornStudio.Metadata;
using Xunit;

namespace AkkornStudio.Tests.Unit.Metadata;

// ─────────────────────────────────────────────────────────────────────────────
// Helpers — in-memory DbMetadata builder for tests
// ─────────────────────────────────────────────────────────────────────────────

internal static class MetadataFixtures
{
    // Build a minimal ColumnMetadata
    public static ColumnMetadata Col(
        string name,
        string type = "integer",
        bool isPk = false,
        bool isFk = false,
        bool isNullable = true,
        bool isUnique = false,
        bool isIndexed = false
    ) => new(name, type, type, isNullable, isPk, isFk, isUnique, isIndexed, 1);

    // Build a FK relation
    public static ForeignKeyRelation Fk(
        string childSchema,
        string childTable,
        string childCol,
        string parentSchema,
        string parentTable,
        string parentCol,
        ReferentialAction onDelete = ReferentialAction.NoAction,
        string constraint = "fk_test"
    ) =>
        new(
            constraint,
            childSchema,
            childTable,
            childCol,
            parentSchema,
            parentTable,
            parentCol,
            onDelete,
            ReferentialAction.NoAction
        );

    // Build a complete table
    public static TableMetadata Table(
        string schema,
        string name,
        IReadOnlyList<ColumnMetadata> columns,
        IReadOnlyList<ForeignKeyRelation>? outbound = null,
        IReadOnlyList<ForeignKeyRelation>? inbound = null
    ) => new(schema, name, TableKind.Table, 1000, columns, [], outbound ?? [], inbound ?? []);

    /// <summary>
    /// Builds a canonical e-commerce schema:
    /// customers ←── orders ←── order_items ──→ products
    /// </summary>
    public static DbMetadata EcommerceDb()
    {
        ForeignKeyRelation fk_orders_customers = Fk(
            "public",
            "orders",
            "customer_id",
            "public",
            "customers",
            "id",
            ReferentialAction.Restrict,
            "fk_orders_customer"
        );

        ForeignKeyRelation fk_items_orders = Fk(
            "public",
            "order_items",
            "order_id",
            "public",
            "orders",
            "id",
            ReferentialAction.Cascade,
            "fk_items_order"
        );

        ForeignKeyRelation fk_items_products = Fk(
            "public",
            "order_items",
            "product_id",
            "public",
            "products",
            "id",
            ReferentialAction.Restrict,
            "fk_items_product"
        );

        ForeignKeyRelation[] allFks = new[]
        {
            fk_orders_customers,
            fk_items_orders,
            fk_items_products,
        };

        TableMetadata customers = Table(
            "public",
            "customers",
            [
                Col("id", isPk: true, isNullable: false),
                Col("name", type: "varchar"),
                Col("email", type: "varchar", isUnique: true, isIndexed: true),
            ],
            inbound: [fk_orders_customers]
        );

        TableMetadata orders = Table(
            "public",
            "orders",
            [
                Col("id", isPk: true, isNullable: false),
                Col("customer_id", isFk: true, isNullable: false, isIndexed: true),
                Col("total", type: "decimal"),
                Col("status", type: "varchar"),
            ],
            outbound: [fk_orders_customers],
            inbound: [fk_items_orders]
        );

        TableMetadata orderItems = Table(
            "public",
            "order_items",
            [
                Col("id", isPk: true, isNullable: false),
                Col("order_id", isFk: true, isNullable: false, isIndexed: true),
                Col("product_id", isFk: true, isNullable: false, isIndexed: true),
                Col("qty", type: "integer"),
                Col("price", type: "decimal"),
            ],
            outbound: [fk_items_orders, fk_items_products]
        );

        TableMetadata products = Table(
            "public",
            "products",
            [
                Col("id", isPk: true, isNullable: false),
                Col("sku", type: "varchar", isUnique: true),
                Col("name", type: "varchar"),
                Col("price", type: "decimal"),
            ],
            inbound: [fk_items_products]
        );

        var schema = new SchemaMetadata("public", [customers, orders, orderItems, products]);

        return new DbMetadata(
            "ecommerce",
            DatabaseProvider.Postgres,
            "PostgreSQL 16",
            DateTimeOffset.UtcNow,
            [schema],
            allFks
        );
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DbMetadata model tests
// ─────────────────────────────────────────────────────────────────────────────

public class DbMetadataModelTests
{
    private readonly DbMetadata _db = MetadataFixtures.EcommerceDb();

    [Fact]
    public void FindTable_Postgres_UsesCaseSensitiveComparison()
    {
        Assert.Null(_db.FindTable("public.Orders"));

        TableMetadata? exact = _db.FindTable("public.orders");
        Assert.NotNull(exact);
        Assert.Equal("orders", exact!.Name);
    }

    [Fact]
    public void FindTable_BySchemaAndName_Works()
    {
        TableMetadata? t = _db.FindTable("public", "products");
        Assert.NotNull(t);
    }

    [Fact]
    public void FindTable_Missing_ReturnsNull() => Assert.Null(_db.FindTable("public.nonexistent"));

    [Fact]
    public void FindTable_NonPostgres_RemainsCaseInsensitive()
    {
        var table = MetadataFixtures.Table(
            "dbo",
            "Orders",
            [MetadataFixtures.Col("id", isPk: true, isNullable: false)]
        );
        var schema = new SchemaMetadata("dbo", [table]);
        var db = new DbMetadata(
            "sample",
            DatabaseProvider.SqlServer,
            "SQL Server",
            DateTimeOffset.UtcNow,
            [schema],
            []
        );

        TableMetadata? resolved = db.FindTable("dbo.orders");
        Assert.NotNull(resolved);
        Assert.Equal("Orders", resolved!.Name);
    }

    [Fact]
    public void AllTables_Count_IsCorrect() => Assert.Equal(4, _db.TotalTables);

    [Fact]
    public void AllForeignKeys_Count_IsCorrect() => Assert.Equal(3, _db.TotalForeignKeys);

    [Fact]
    public void GetRelationsBetween_DirectFK_Found()
    {
        IReadOnlyList<ForeignKeyRelation> rels = _db.GetRelationsBetween(
            "public.orders",
            "public.customers"
        );
        Assert.Single(rels);
        Assert.Equal("customer_id", rels[0].ChildColumn);
    }

    [Fact]
    public void GetRelationsBetween_ReverseDirection_AlsoFound()
    {
        // Same query, reversed argument order
        IReadOnlyList<ForeignKeyRelation> rels = _db.GetRelationsBetween(
            "public.customers",
            "public.orders"
        );
        Assert.Single(rels);
    }

    [Fact]
    public void GetRelationsToCanvas_ReturnsOnlyCanvasMatches()
    {
        string[] canvas = new[] { "public.customers", "public.products" };
        IReadOnlyList<ForeignKeyRelation> relOrders = _db.GetRelationsToCanvas(
            "public.orders",
            canvas
        );

        // orders→customers is a canvas match; orders→order_items is NOT (not on canvas)
        Assert.Single(relOrders);
        Assert.Equal("fk_orders_customer", relOrders[0].ConstraintName);
    }

    [Fact]
    public void TableMetadata_ReferencedTables_Correct()
    {
        TableMetadata orders = _db.FindTable("public", "orders")!;
        Assert.Contains(
            "public.customers",
            orders.ReferencedTables,
            StringComparer.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void TableMetadata_ReferencingTables_Correct()
    {
        TableMetadata customers = _db.FindTable("public", "customers")!;
        Assert.Contains(
            "public.orders",
            customers.ReferencingTables,
            StringComparer.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ColumnSemanticType_Inference_Correct()
    {
        ColumnMetadata intCol = MetadataFixtures.Col("qty", "integer");
        ColumnMetadata textCol = MetadataFixtures.Col("name", "varchar");
        ColumnMetadata dtCol = MetadataFixtures.Col("ts", "timestamp");
        ColumnMetadata boolCol = MetadataFixtures.Col("flag", "boolean");

        Assert.Equal(ColumnSemanticType.Numeric, intCol.SemanticType);
        Assert.Equal(ColumnSemanticType.Text, textCol.SemanticType);
        Assert.Equal(ColumnSemanticType.DateTime, dtCol.SemanticType);
        Assert.Equal(ColumnSemanticType.Boolean, boolCol.SemanticType);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ForeignKeyRelation tests
// ─────────────────────────────────────────────────────────────────────────────

public class ForeignKeyRelationTests
{
    [Fact]
    public void Involves_BothDirections_ReturnsTrue()
    {
        ForeignKeyRelation fk = MetadataFixtures.Fk(
            "public",
            "orders",
            "customer_id",
            "public",
            "customers",
            "id"
        );

        Assert.True(fk.Involves("public.orders", "public.customers"));
        Assert.True(fk.Involves("public.customers", "public.orders"));
    }

    [Fact]
    public void Involves_UnrelatedTable_ReturnsFalse()
    {
        ForeignKeyRelation fk = MetadataFixtures.Fk(
            "public",
            "orders",
            "customer_id",
            "public",
            "customers",
            "id"
        );

        Assert.False(fk.Involves("public.orders", "public.products"));
    }

    [Fact]
    public void ToJoinOnClause_FormatsCorrectly()
    {
        ForeignKeyRelation fk = MetadataFixtures.Fk(
            "public",
            "orders",
            "customer_id",
            "public",
            "customers",
            "id"
        );
        Assert.Equal("public.orders.customer_id = public.customers.id", fk.ToJoinOnClause());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AutoJoinDetector tests
// ─────────────────────────────────────────────────────────────────────────────

public class AutoJoinDetectorTests
{
    private readonly DbMetadata _db = MetadataFixtures.EcommerceDb();
    private readonly AutoJoinDetector _sut;

    public AutoJoinDetectorTests()
    {
        _sut = new AutoJoinDetector(_db);
    }

    // ── Catalog FK detection ──────────────────────────────────────────────────

    [Fact]
    public void Suggest_DirectFK_ReturnsHighConfidence()
    {
        // orders.customer_id → customers.id (FK defined in catalog)
        string[] canvas = new[] { "public.customers" };
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest("public.orders", canvas);

        Assert.NotEmpty(results);
        JoinSuggestion top = results[0];
        Assert.True(top.Score >= 0.9);
        Assert.Equal(JoinConfidence.CatalogDefinedFk, top.Confidence);
        Assert.Contains("customer_id", top.LeftColumn);
    }

    [Fact]
    public void Suggest_ReverseFK_ReturnsHighConfidence()
    {
        // Drop 'orders' onto canvas that already has 'order_items'
        // (order_items.order_id → orders.id — orders is the parent)
        string[] canvas = new[] { "public.order_items" };
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest("public.orders", canvas);

        Assert.NotEmpty(results);
        JoinSuggestion? catalogResult = results.FirstOrDefault(r =>
            r.Confidence >= JoinConfidence.CatalogDefinedReverse
        );
        Assert.NotNull(catalogResult);
    }

    [Fact]
    public void Suggest_CatalogFK_HasSourceFkPopulated()
    {
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest("public.orders", ["public.customers"]);
        Assert.NotNull(results[0].SourceFk);
        Assert.Equal("fk_orders_customer", results[0].SourceFk!.ConstraintName);
    }

    [Fact]
    public void Suggest_OnClause_IsWellFormed()
    {
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest("public.orders", ["public.customers"]);
        string clause = results[0].OnClause;

        Assert.Contains("=", clause);
        Assert.Contains("customer_id", clause);
        Assert.Contains("customers", clause);
    }

    [Fact]
    public void Suggest_CascadeDelete_RecommendsInnerJoin()
    {
        // fk_items_order has ON DELETE CASCADE → should suggest INNER
        string[] canvas = new[] { "public.orders" };
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest("public.order_items", canvas);
        JoinSuggestion catalogResult = results.First(r =>
            r.SourceFk?.ConstraintName == "fk_items_order"
        );
        Assert.Equal("INNER", catalogResult.JoinType);
    }

    [Fact]
    public void Suggest_NonCascadeDelete_RecommendsLeftJoin()
    {
        // fk_orders_customer has ON DELETE RESTRICT → should suggest LEFT
        string[] canvas = new[] { "public.customers" };
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest("public.orders", canvas);
        Assert.Equal("LEFT", results[0].JoinType);
    }

    // ── No canvas / unknown table ─────────────────────────────────────────────

    [Fact]
    public void Suggest_EmptyCanvas_ReturnsEmpty()
    {
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest("public.orders", []);
        Assert.Empty(results);
    }

    [Fact]
    public void Suggest_UnknownTable_ReturnsEmpty()
    {
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest(
            "public.ghost_table",
            ["public.orders"]
        );
        Assert.Empty(results);
    }

    // ── Deduplication ─────────────────────────────────────────────────────────

    [Fact]
    public void Suggest_MultipleCanvasTables_DeduplicatesColumnPairs()
    {
        string[] canvas = new[] { "public.customers", "public.products" };
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest("public.order_items", canvas);

        // Each (leftCol, rightCol) pair should appear at most once
        var keys = results.Select(r => $"{r.LeftColumn}|{r.RightColumn}").ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // ── Naming heuristic tests ────────────────────────────────────────────────

    [Fact]
    public void Suggest_NamingHeuristic_DetectsConvention()
    {
        // Build a schema where there is NO catalog FK, but column names imply a join
        _ = MetadataFixtures.Fk("app", "invoices", "client_id", "app", "clients", "id");

        // No FK defined — remove outbound FK from the table
        TableMetadata invoices = MetadataFixtures.Table(
            "app",
            "invoices",
            [
                MetadataFixtures.Col("id", isPk: true),
                MetadataFixtures.Col("client_id", isFk: false), // no FK in catalog
            ]
        );

        TableMetadata clients = MetadataFixtures.Table(
            "app",
            "clients",
            [MetadataFixtures.Col("id", isPk: true), MetadataFixtures.Col("name", type: "varchar")]
        );

        var schema = new SchemaMetadata("app", [invoices, clients]);
        var db = new DbMetadata(
            "test",
            DatabaseProvider.Postgres,
            "v16",
            DateTimeOffset.UtcNow,
            [schema],
            []
        ); // ← no catalog FKs

        var detector = new AutoJoinDetector(db);
        IReadOnlyList<JoinSuggestion> results = detector.Suggest("app.invoices", ["app.clients"]);

        // Should still detect via naming heuristic (invoices.client_id → clients.id)
        Assert.NotEmpty(results);
        Assert.True(results[0].Score >= 0.7);
        Assert.Equal(JoinConfidence.HeuristicStrong, results[0].Confidence);
    }

    // ── Sorted order ──────────────────────────────────────────────────────────

    [Fact]
    public void Suggest_ResultsAreSortedByScoreDescending()
    {
        string[] canvas = new[] { "public.customers", "public.products" };
        IReadOnlyList<JoinSuggestion> results = _sut.Suggest("public.order_items", canvas);

        for (int i = 0; i < results.Count - 1; i++)
            Assert.True(
                results[i].Score >= results[i + 1].Score,
                $"Suggestions not sorted: [{i}]={results[i].Score} < [{i + 1}]={results[i + 1].Score}"
            );
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Singulariser tests
// ─────────────────────────────────────────────────────────────────────────────

public class SingulariserTests
{
    [Theory]
    [InlineData("orders", "order")]
    [InlineData("customers", "customer")]
    [InlineData("products", "product")]
    [InlineData("categories", "category")]
    [InlineData("addresses", "address")]
    [InlineData("boxes", "box")]
    [InlineData("buses", "bus")]
    [InlineData("status", "status")] // no change — ends in 'ss' guard
    [InlineData("user", "user")] // already singular
    public void Singularize_ConvertsCorrectly(string plural, string expected) =>
        Assert.Equal(expected, AutoJoinDetector.Singularize(plural));
}

// ─────────────────────────────────────────────────────────────────────────────
// JoinSuggestion → JoinDefinition conversion
// ─────────────────────────────────────────────────────────────────────────────

public class JoinSuggestionConversionTests
{
    [Fact]
    public void ToJoinDefinition_MapsFieldsCorrectly()
    {
        DbMetadata db = MetadataFixtures.EcommerceDb();
        var sut = new AutoJoinDetector(db);
        IReadOnlyList<JoinSuggestion> results = sut.Suggest("public.orders", ["public.customers"]);

        Assert.NotEmpty(results);
        var def = results[0].ToJoinDefinition();

        Assert.Equal("public.orders", def.TargetTable);
        Assert.Equal(results[0].JoinType, def.Type);
    }
}
