using AkkornStudio.Core;
using AkkornStudio.Ddl;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Ddl;

public class DdlGeneratorServiceTests
{
    [Fact]
    public void Generate_SqlServerCreateTable_EmitsExpectedSql()
    {
        var expr = new CreateTableExpr(
            "dbo",
            "orders",
            ifNotExists: true,
            columns:
            [
                new DdlColumnExpr("id", "INT", false),
                new DdlColumnExpr("status", "NVARCHAR(32)", false, "('NEW')"),
            ],
            primaryKeys:
            [
                new DdlPrimaryKeyExpr("PK_orders", ["id"]),
            ],
            uniques: [],
            checks: []
        );

        var generator = new DdlGeneratorService(DatabaseProvider.SqlServer);
        string sql = generator.Generate([expr]);

        Assert.Contains("CREATE TABLE [dbo].[orders]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[id] INT NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[status] NVARCHAR(32) NOT NULL DEFAULT ('NEW')", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CONSTRAINT [PK_orders] PRIMARY KEY ([id])", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF OBJECT_ID", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "CREATE TABLE IF NOT EXISTS \"public\".\"orders\"")]
    [InlineData(DatabaseProvider.MySql, "CREATE TABLE IF NOT EXISTS `public`.`orders`")]
    [InlineData(DatabaseProvider.SQLite, "CREATE TABLE IF NOT EXISTS \"orders\"")]
    public void Generate_CreateTable_MultiDialect_EmitsProviderSyntax(DatabaseProvider provider, string expectedPrefix)
    {
        var expr = new CreateTableExpr(
            "public",
            "orders",
            ifNotExists: true,
            columns:
            [
                new DdlColumnExpr("id", "INT", false),
            ],
            primaryKeys: [],
            uniques: [],
            checks: []
        );

        var generator = new DdlGeneratorService(provider);
        string sql = generator.Generate([expr]);

        Assert.Contains(expectedPrefix, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateIndex_Postgres_IncludesIncludeColumns()
    {
        var expr = new CreateIndexExpr(
            "public",
            "orders",
            "ix_orders_status",
            isUnique: false,
            keyColumns: [new DdlIndexKeyExpr(ColumnName: "status")],
            includeColumns: ["created_at"],
            ifNotExists: true
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("CREATE INDEX IF NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INCLUDE (\"created_at\")", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateIndex_WithExpression_Postgres_EmitsParenthesizedExpression()
    {
        var expr = new CreateIndexExpr(
            "public",
            "users",
            "ix_users_lower_name",
            isUnique: false,
            keyColumns: [new DdlIndexKeyExpr(ExpressionSql: "lower(name)")],
            includeColumns: [],
            ifNotExists: true
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("(lower(name))", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateIndex_WithExpression_MySql_EmitsDoubleParentheses()
    {
        var expr = new CreateIndexExpr(
            "public",
            "users",
            "ix_users_lower_name",
            isUnique: false,
            keyColumns: [new DdlIndexKeyExpr(ExpressionSql: "lower(name)")],
            includeColumns: [],
            ifNotExists: true
        );

        var generator = new DdlGeneratorService(DatabaseProvider.MySql);
        string sql = generator.Generate([expr]);

        Assert.Contains("((lower(name)))", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateIndex_WithExpression_SqlServer_ThrowsExplicitError()
    {
        var expr = new CreateIndexExpr(
            "dbo",
            "users",
            "ix_users_lower_name",
            isUnique: false,
            keyColumns: [new DdlIndexKeyExpr(ExpressionSql: "lower(name)")],
            includeColumns: [],
            ifNotExists: true
        );

        var generator = new DdlGeneratorService(DatabaseProvider.SqlServer);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => generator.Generate([expr]));
        Assert.Contains("does not support arbitrary expression keys", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "sp_rename")]
    [InlineData(DatabaseProvider.MySql, "MODIFY COLUMN")]
    [InlineData(DatabaseProvider.Postgres, "RENAME COLUMN")]
    public void Generate_AlterTable_UsesDialectSpecificSyntax(DatabaseProvider provider, string expectedToken)
    {
        var expr = new AlterTableExpr(
            "public",
            "orders",
            operations:
            [
                new AddColumnOpExpr(new DdlColumnExpr("status", "VARCHAR(32)", false)),
                new DropColumnOpExpr("legacy_code", true),
                new RenameColumnOpExpr("old_name", "new_name"),
                new AlterColumnTypeOpExpr("total", "DECIMAL(10,2)", true),
            ],
            emitSeparateStatements: true
        );

        var generator = new DdlGeneratorService(provider);
        string sql = generator.Generate([expr]);

        Assert.Contains(expectedToken, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_AlterTableAlterColumnType_OnSqlite_ThrowsExplicitError()
    {
        var expr = new AlterTableExpr(
            "main",
            "orders",
            operations: [new AlterColumnTypeOpExpr("total", "DECIMAL(10,2)", true)],
            emitSeparateStatements: true
        );

        var generator = new DdlGeneratorService(DatabaseProvider.SQLite);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => generator.Generate([expr]));
        Assert.Contains("does not support ALTER COLUMN TYPE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateEnumType_Postgres_EmitsExpectedSql()
    {
        var expr = new CreateEnumTypeExpr(
            "public",
            "status_enum",
            ["NEW", "ACTIVE", "DISABLED"]
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("CREATE TYPE \"public\".\"status_enum\" AS ENUM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'NEW'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'ACTIVE'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'DISABLED'", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateEnumType_NonPostgres_ThrowsNotSupportedException()
    {
        var expr = new CreateEnumTypeExpr(
            "public",
            "status_enum",
            ["NEW", "ACTIVE", "DISABLED"]
        );

        var generator = new DdlGeneratorService(DatabaseProvider.SqlServer);
        var ex = Assert.Throws<NotSupportedException>(() => generator.Generate([expr]));

        Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(CreateEnumTypeExpr), ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "CREATE OR REPLACE VIEW")]
    [InlineData(DatabaseProvider.MySql, "DROP VIEW IF EXISTS")]
    [InlineData(DatabaseProvider.SqlServer, "CREATE VIEW")]
    [InlineData(DatabaseProvider.SQLite, "CREATE VIEW")]
    public void Generate_CreateView_EmitsProviderSpecificSyntax(DatabaseProvider provider, string expectedToken)
    {
        var expr = new CreateViewExpr(
            "public",
            "v_orders",
            orReplace: true,
            isMaterialized: false,
            selectSql: "SELECT id, status FROM orders"
        );

        var generator = new DdlGeneratorService(provider);
        string sql = generator.Generate([expr]);

        Assert.Contains(expectedToken, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "CREATE OR REPLACE VIEW")]
    [InlineData(DatabaseProvider.MySql, "DROP VIEW IF EXISTS")]
    [InlineData(DatabaseProvider.SqlServer, "ALTER VIEW")]
    [InlineData(DatabaseProvider.SQLite, "DROP VIEW IF EXISTS")]
    public void Generate_AlterView_EmitsProviderSpecificSyntax(DatabaseProvider provider, string expectedToken)
    {
        var expr = new AlterViewExpr(
            "public",
            "v_orders",
            "SELECT id FROM orders"
        );

        var generator = new DdlGeneratorService(provider);
        string sql = generator.Generate([expr]);

        Assert.Contains(expectedToken, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "RENAME TO")]
    [InlineData(DatabaseProvider.MySql, "RENAME TABLE")]
    [InlineData(DatabaseProvider.SqlServer, "sp_rename")]
    [InlineData(DatabaseProvider.SQLite, "RENAME TO")]
    public void Generate_RenameTableOp_UsesDialectSpecificSyntax(DatabaseProvider provider, string expectedToken)
    {
        var expr = new AlterTableExpr(
            "public",
            "orders",
            operations:
            [
                new RenameTableOpExpr("orders_archive", null),
            ],
            emitSeparateStatements: true
        );

        var generator = new DdlGeneratorService(provider);
        string sql = generator.Generate([expr]);

        Assert.Contains(expectedToken, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "DROP TABLE IF EXISTS")]
    [InlineData(DatabaseProvider.MySql, "DROP TABLE IF EXISTS")]
    [InlineData(DatabaseProvider.SqlServer, "IF OBJECT_ID")]
    [InlineData(DatabaseProvider.SQLite, "DROP TABLE IF EXISTS")]
    public void Generate_DropTableOp_UsesDialectSpecificSyntax(DatabaseProvider provider, string expectedToken)
    {
        var expr = new AlterTableExpr(
            "public",
            "orders",
            operations:
            [
                new DropTableOpExpr(true),
            ],
            emitSeparateStatements: true
        );

        var generator = new DdlGeneratorService(provider);
        string sql = generator.Generate([expr]);

        Assert.Contains(expectedToken, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "COMMENT ON TABLE")]
    [InlineData(DatabaseProvider.MySql, "COMMENT='Orders table'")]
    [InlineData(DatabaseProvider.SqlServer, "sp_addextendedproperty")]
    public void Generate_CreateTableWithComments_EmitsProviderSpecificCommentSyntax(DatabaseProvider provider, string expectedToken)
    {
        var expr = new CreateTableExpr(
            "public",
            "orders",
            ifNotExists: true,
            columns:
            [
                new DdlColumnExpr("id", "INT", false, Comment: "Primary key"),
            ],
            primaryKeys: [],
            uniques: [],
            checks: [],
            tableComment: "Orders table"
        );

        var generator = new DdlGeneratorService(provider);
        string sql = generator.Generate([expr]);

        Assert.Contains(expectedToken, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateTable_DropAndCreate_PrependsDropStatement()
    {
        var expr = new CreateTableExpr(
            "public",
            "orders",
            ifNotExists: false,
            columns: [new DdlColumnExpr("id", "INT", false)],
            primaryKeys: [],
            uniques: [],
            checks: [],
            tableComment: null,
            mode: DdlIdempotentMode.DropAndCreate
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("DROP TABLE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateTable_DropAndCreate_Postgres_AppendsCascadeToDrop()
    {
        var expr = new CreateTableExpr(
            "public",
            "orders",
            ifNotExists: false,
            columns: [new DdlColumnExpr("id", "INT", false)],
            primaryKeys: [],
            uniques: [],
            checks: [],
            tableComment: null,
            mode: DdlIdempotentMode.DropAndCreate
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("DROP TABLE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASCADE;", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateTable_PrimaryKeyWithUnknownColumn_ThrowsExplicitError()
    {
        var expr = new CreateTableExpr(
            "public",
            "orders",
            ifNotExists: true,
            columns: [new DdlColumnExpr("id", "INT", false)],
            primaryKeys: [new DdlPrimaryKeyExpr("pk_orders", ["missing_col"])],
            uniques: [],
            checks: []
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => generator.Generate([expr]));
        Assert.Contains("PRIMARY KEY references unknown column 'missing_col'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateTable_UniqueWithDuplicateColumns_ThrowsExplicitError()
    {
        var expr = new CreateTableExpr(
            "public",
            "orders",
            ifNotExists: true,
            columns:
            [
                new DdlColumnExpr("id", "INT", false),
                new DdlColumnExpr("status", "TEXT", true),
            ],
            primaryKeys: [],
            uniques: [new DdlUniqueExpr("uq_orders_status", ["status", "STATUS"])],
            checks: []
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => generator.Generate([expr]));
        Assert.Contains("UNIQUE contains duplicate column 'STATUS'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateTable_WithForeignKey_QuotesConstraintAndParentReference()
    {
        var expr = new CreateTableExpr(
            "public",
            "orders",
            ifNotExists: true,
            columns:
            [
                new DdlColumnExpr("id", "INT", false),
                new DdlColumnExpr("customer_id", "INT", false),
            ],
            primaryKeys: [new DdlPrimaryKeyExpr("pk_orders", ["id"])],
            uniques: [],
            checks: [],
            foreignKeys:
            [
                new DdlForeignKeyExpr(
                    ConstraintName: "fk_orders_customer",
                    ChildColumns: ["customer_id"],
                    ParentSchema: "public",
                    ParentTable: "customers",
                    ParentColumns: ["id"],
                    OnDelete: ReferentialAction.Restrict,
                    OnUpdate: ReferentialAction.Cascade
                ),
            ]
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("CONSTRAINT \"fk_orders_customer\" FOREIGN KEY (\"customer_id\")", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REFERENCES \"public\".\"customers\" (\"id\")", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON UPDATE CASCADE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateTable_WithForeignKey_OnSqlite_OmitsOnUpdateClause()
    {
        var expr = new CreateTableExpr(
            "main",
            "orders",
            ifNotExists: true,
            columns:
            [
                new DdlColumnExpr("id", "INTEGER", false),
                new DdlColumnExpr("customer_id", "INTEGER", false),
            ],
            primaryKeys: [new DdlPrimaryKeyExpr("pk_orders", ["id"])],
            uniques: [],
            checks: [],
            foreignKeys:
            [
                new DdlForeignKeyExpr(
                    ConstraintName: "fk_orders_customer",
                    ChildColumns: ["customer_id"],
                    ParentSchema: "main",
                    ParentTable: "customers",
                    ParentColumns: ["id"],
                    OnDelete: ReferentialAction.Cascade,
                    OnUpdate: ReferentialAction.Cascade
                ),
            ]
        );

        var generator = new DdlGeneratorService(DatabaseProvider.SQLite);
        string sql = generator.Generate([expr]);

        Assert.Contains("FOREIGN KEY (\"customer_id\")", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DELETE CASCADE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ON UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateEnumType_IfNotExists_EmitsGuardedCreate()
    {
        var expr = new CreateEnumTypeExpr(
            "public",
            "status_enum",
            ["NEW", "ACTIVE"],
            DdlIdempotentMode.IfNotExists
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("CREATE TYPE IF NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateView_DropAndCreate_EmitsDropThenCreate()
    {
        var expr = new CreateViewExpr(
            "dbo",
            "v_orders",
            orReplace: false,
            isMaterialized: false,
            selectSql: "SELECT id FROM orders",
            mode: DdlIdempotentMode.DropAndCreate
        );

        var generator = new DdlGeneratorService(DatabaseProvider.SqlServer);
        string sql = generator.Generate([expr]);

        Assert.Contains("DROP VIEW", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE VIEW", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateView_DropAndCreate_Postgres_AppendsCascadeToDrop()
    {
        var expr = new CreateViewExpr(
            "public",
            "v_orders",
            orReplace: false,
            isMaterialized: false,
            selectSql: "SELECT id FROM orders",
            mode: DdlIdempotentMode.DropAndCreate
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("DROP VIEW IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASCADE;", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "CREATE SEQUENCE")]
    [InlineData(DatabaseProvider.SqlServer, "CREATE SEQUENCE")]
    public void Generate_CreateSequence_EmitsSupportedProviderSyntax(DatabaseProvider provider, string expectedToken)
    {
        var expr = new CreateSequenceExpr(
            "public",
            "seq_orders",
            startValue: 10,
            increment: 2,
            minValue: null,
            maxValue: null,
            cycle: false,
            cache: 20,
            mode: DdlIdempotentMode.None
        );

        var generator = new DdlGeneratorService(provider);
        string sql = generator.Generate([expr]);

        Assert.Contains(expectedToken, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("seq_orders", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateSequence_DropAndCreate_EmitsDropThenCreate()
    {
        var expr = new CreateSequenceExpr(
            "public",
            "seq_orders",
            startValue: null,
            increment: null,
            minValue: null,
            maxValue: null,
            cycle: false,
            cache: null,
            mode: DdlIdempotentMode.DropAndCreate
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("DROP SEQUENCE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE SEQUENCE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateSequence_DropAndCreate_Postgres_AppendsCascadeToDrop()
    {
        var expr = new CreateSequenceExpr(
            "public",
            "seq_orders",
            startValue: null,
            increment: null,
            minValue: null,
            maxValue: null,
            cycle: false,
            cache: null,
            mode: DdlIdempotentMode.DropAndCreate
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("DROP SEQUENCE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASCADE;", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateEnumType_DropAndCreate_Postgres_AppendsCascadeToDrop()
    {
        var expr = new CreateEnumTypeExpr(
            "public",
            "status_enum",
            ["NEW", "ACTIVE"],
            DdlIdempotentMode.DropAndCreate
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("DROP TYPE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASCADE;", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateTableAs_DropAndCreate_Postgres_AppendsCascadeToDrop()
    {
        var expr = new CreateTableAsExpr(
            "public",
            "orders_copy",
            sourceTable: null,
            selectSql: "SELECT * FROM orders",
            includeData: true,
            mode: DdlIdempotentMode.DropAndCreate
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("DROP TABLE IF EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASCADE;", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateTableAs_Postgres_WithNoData_EmitsAsSelectWithClause()
    {
        var expr = new CreateTableAsExpr(
            "public",
            "orders_copy",
            sourceTable: null,
            selectSql: "SELECT * FROM orders",
            includeData: false,
            mode: DdlIdempotentMode.None
        );

        var generator = new DdlGeneratorService(DatabaseProvider.Postgres);
        string sql = generator.Generate([expr]);

        Assert.Contains("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS SELECT * FROM orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WITH NO DATA", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CreateTableLike_MySql_EmitsLikeSyntax()
    {
        var expr = new CreateTableAsExpr(
            "public",
            "orders_clone",
            sourceTable: "`public`.`orders`",
            selectSql: null,
            includeData: true,
            mode: DdlIdempotentMode.None
        );

        var generator = new DdlGeneratorService(DatabaseProvider.MySql);
        string sql = generator.Generate([expr]);

        Assert.Contains("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
