using AkkornStudio.Core;
using AkkornStudio.Ddl;
using AkkornStudio.Nodes;

namespace AkkornStudio.Tests.Unit.Ddl;

public class DdlGraphCompilerValidationTests
{
    [Fact]
    public void CompileWithDiagnostics_CreateTableOutputWithoutTable_ReturnsError()
    {
        NodeGraph graph = new()
        {
            Nodes = [new NodeInstance("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>())],
            Connections = [],
        };

        DdlCompileResult result = new DdlGraphCompiler(graph).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-OUTPUT-TABLE-NOT-CONNECTED" && d.Severity == DdlDiagnosticSeverity.Error);
        Assert.Empty(result.Statements);
    }

    [Fact]
    public void CompileWithDiagnostics_TableWithoutColumns_ReturnsError()
    {
        NodeGraph graph = new()
        {
            Nodes =
            [
                new NodeInstance("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
                new NodeInstance("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
                {
                    ["SchemaName"] = "public",
                    ["TableName"] = "orders",
                }),
            ],
            Connections = [new Connection("tbl", "table", "out", "table")],
        };

        DdlCompileResult result = new DdlGraphCompiler(graph).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-TABLE-NO-COLUMNS");
    }

    [Fact]
    public void CompileWithDiagnostics_ForeignKeyAsymmetry_ReturnsError()
    {
        NodeGraph graph = BuildFkGraph(
            childColumns: ["c_child_a", "c_child_b"],
            parentColumns: ["c_parent_a"]
        );

        DdlCompileResult result = new DdlGraphCompiler(graph).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-FK-COLUMN-COUNT");
    }

    [Fact]
    public void CompileWithDiagnostics_DuplicatePrimaryKey_ReturnsError()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "id",
                ["DataType"] = "INT",
                ["IsNullable"] = "false",
            }),
            new("pk1", NodeType.PrimaryKeyConstraint, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("pk2", NodeType.PrimaryKeyConstraint, new Dictionary<string, string>(), new Dictionary<string, string>()),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("col", "column", "tbl", "column"),
            new("col", "column", "pk1", "column"),
            new("col", "column", "pk2", "column"),
            new("pk1", "pk", "tbl", "constraint"),
            new("pk2", "pk", "tbl", "constraint"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-PK-DUPLICATE");
    }

    [Fact]
    public void CompileWithDiagnostics_IdentityOnNonInteger_ReturnsError()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "code",
                ["DataType"] = "VARCHAR(10)",
                ["IsNullable"] = "false",
                ["IsIdentity"] = "true",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("col", "column", "tbl", "column"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-IDENTITY-NONINTEGER");
    }

    [Fact]
    public void CompileWithDiagnostics_VarcharWithoutLength_Warns()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "name",
                ["DataType"] = "VARCHAR",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("col", "column", "tbl", "column"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-TYPE-LENGTH-MISSING" && d.Severity == DdlDiagnosticSeverity.Warning);
    }

    [Fact]
    public void CompileWithDiagnostics_SqlServerDropColumnIfExists_Warns()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.AlterTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "dbo",
                ["TableName"] = "orders",
            }),
            new("drop", NodeType.DropColumnOp, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "legacy",
                ["IfExists"] = "true",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("drop", "op", "out", "operation"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.SqlServer).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-DROPCOLUMN-IFEXISTS-SQLSERVER");
    }

    [Fact]
    public void CompileWithDiagnostics_EnumOnSqlServer_EmitsWarning()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "dbo",
                ["TableName"] = "orders",
            }),
            new("enum_def", NodeType.EnumTypeDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["TypeName"] = "status_enum",
                ["EnumValues"] = "NEW,ACTIVE",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "status",
                ["DataType"] = "ENUM",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("col", "column", "tbl", "column"),
            new("enum_def", "type_def", "col", "type_def"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.SqlServer).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-ENUM-UNSUPPORTED-PROVIDER");
    }

    [Fact]
    public void CompileWithDiagnostics_PostgresEnumWithoutCreateTypeOutput_ReturnsError()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("enum_def", NodeType.EnumTypeDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TypeName"] = "status_enum",
                ["EnumValues"] = "NEW,ACTIVE",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "status",
                ["DataType"] = "ENUM",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("col", "column", "tbl", "column"),
            new("enum_def", "type_def", "col", "type_def"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-ENUM-TYPE-NOT-EMITTED");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void CompileWithDiagnostics_AlterViewOutputWithoutViewInput_ReturnsError()
    {
        NodeGraph graph = new()
        {
            Nodes =
            [
                new NodeInstance("out", NodeType.AlterViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
                new NodeInstance("view", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
                {
                    ["Schema"] = "public",
                    ["ViewName"] = "v_orders",
                    ["SelectSql"] = "SELECT id FROM public.orders",
                }),
            ],
            Connections = [],
        };

        DdlCompileResult result = new DdlGraphCompiler(graph, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-ALTERVIEW-OUTPUT-NOT-CONNECTED" && d.Severity == DdlDiagnosticSeverity.Error);
        Assert.True(result.HasErrors);
        Assert.Empty(result.Statements);
    }

    [Fact]
    public void CompileWithDiagnostics_AlterViewOutputWithViewInput_ReturnsStatementWithoutErrors()
    {
        NodeGraph graph = new()
        {
            Nodes =
            [
                new NodeInstance("out", NodeType.AlterViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
                new NodeInstance("view", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
                {
                    ["Schema"] = "public",
                    ["ViewName"] = "v_orders",
                    ["SelectSql"] = "SELECT id FROM public.orders",
                }),
            ],
            Connections = [new Connection("view", "view", "out", "view")],
        };

        DdlCompileResult result = new DdlGraphCompiler(graph, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.False(result.HasErrors);
        Assert.Single(result.Statements);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "E-DDL-ALTERVIEW-OUTPUT-VIEW");
    }

    [Fact]
    public void CompileWithDiagnostics_AlterViewOutputWithWrongInputType_ReturnsError()
    {
        NodeGraph graph = new()
        {
            Nodes =
            [
                new NodeInstance("out", NodeType.AlterViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
                new NodeInstance("table", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
                {
                    ["SchemaName"] = "public",
                    ["TableName"] = "orders",
                }),
            ],
            Connections = [new Connection("table", "table", "out", "view")],
        };

        DdlCompileResult result = new DdlGraphCompiler(graph, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-ALTERVIEW-OUTPUT-TYPE");
        Assert.True(result.HasErrors);
        Assert.Empty(result.Statements);
    }

    [Fact]
    public void CompileWithDiagnostics_AlterViewOutputWithMissingSourceNode_ReturnsCompileError()
    {
        NodeGraph graph = new()
        {
            Nodes = [new NodeInstance("out", NodeType.AlterViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>())],
            Connections = [new Connection("missing", "view", "out", "view")],
        };

        DdlCompileResult result = new DdlGraphCompiler(graph, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-COMPILE-ALTERVIEW");
        Assert.True(result.HasErrors);
        Assert.Empty(result.Statements);
    }

    [Fact]
    public void CompileWithDiagnostics_ViewDefinitionWithoutSelect_ReturnsError()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("view", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["ViewName"] = "v_orders",
                ["SelectSql"] = "",
            }),
        };

        var connections = new List<Connection>
        {
            new("view", "view", "out", "view"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-VIEW-SELECT-BLANK");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void CompileWithDiagnostics_MaterializedViewOnSqlServer_ReturnsError()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("view", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "dbo",
                ["ViewName"] = "mv_orders",
                ["IsMaterialized"] = "true",
                ["SelectSql"] = "SELECT id FROM orders",
            }),
        };

        var connections = new List<Connection>
        {
            new("view", "view", "out", "view"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.SqlServer).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-VIEW-MATERIALIZED-UNSUPPORTED");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void CompileWithDiagnostics_ViewDefinitionWithInvalidSubgraphJson_ReturnsError()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("view", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["ViewName"] = "v_orders",
                ["ViewSubgraphGraphJson"] = "{not-json}",
                ["SelectSql"] = "",
            }),
        };

        var connections = new List<Connection>
        {
            new("view", "view", "out", "view"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-VIEW-SUBGRAPH-JSON");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void CompileWithDiagnostics_DropTableOp_EmitsDestructiveWarning()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.AlterTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("drop_table", NodeType.DropTableOp, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["IfExists"] = "true",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("drop_table", "op", "out", "operation"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-DROPTABLE-DESTRUCTIVE");
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)]
    [InlineData(DatabaseProvider.SQLite)]
    public void CompileWithDiagnostics_RenameTableNewSchema_OnUnsupportedProviders_Warns(DatabaseProvider provider)
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.AlterTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("rename_table", NodeType.RenameTableOp, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["NewName"] = "orders_v2",
                ["NewSchema"] = "archive",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("rename_table", "op", "out", "operation"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, provider).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-RENAMETABLE-NEWSCHEMA-UNSUPPORTED");
    }

    [Fact]
    public void CompileWithDiagnostics_SqliteComments_EmitsWarning()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "main",
                ["TableName"] = "orders",
                ["Comment"] = "Orders table",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "id",
                ["DataType"] = "INTEGER",
                ["IsNullable"] = "false",
                ["Comment"] = "PK",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("col", "column", "tbl", "column"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.SQLite).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-COMMENT-SQLITE");
    }

    [Fact]
    public void CompileWithDiagnostics_CreateViewIfNotExists_OnMySql_WarnsDegradation()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["IdempotentMode"] = "IfNotExists",
            }),
            new("view", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["ViewName"] = "v_orders",
                ["SelectSql"] = "SELECT id FROM orders",
            }),
        };

        var connections = new List<Connection>
        {
            new("view", "view", "out", "view"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.MySql).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-IDEMPOTENT-IFNOTEXISTS-VIEW-UNSUPPORTED");
    }

    [Fact]
    public void CompileWithDiagnostics_DropAndCreate_OnCreateTable_WarnsDestructiveScript()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["IdempotentMode"] = "DropAndCreate",
            }),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "id",
                ["DataType"] = "INT",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "out", "table"),
            new("col", "column", "tbl", "column"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-IDEMPOTENT-DROPANDCREATE");
    }

    [Fact]
    public void CompileWithDiagnostics_DropAndCreate_OnCreateType_WarnsTypeCascadeRisk()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTypeOutput, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["IdempotentMode"] = "DropAndCreate",
            }),
            new("enum", NodeType.EnumTypeDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TypeName"] = "status_enum",
                ["EnumValues"] = "NEW,ACTIVE",
            }),
        };

        var connections = new List<Connection>
        {
            new("enum", "type_def", "out", "type_def"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-IDEMPOTENT-DROPTYPE-CASCADE");
    }

    [Fact]
    public void CompileWithDiagnostics_CreateSequenceOutputWithoutSequence_ReturnsError()
    {
        NodeGraph graph = new()
        {
            Nodes = [new NodeInstance("out", NodeType.CreateSequenceOutput, new Dictionary<string, string>(), new Dictionary<string, string>())],
            Connections = [],
        };

        DdlCompileResult result = new DdlGraphCompiler(graph, DatabaseProvider.Postgres).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-OUTPUT-SEQUENCE-NOT-CONNECTED");
    }

    [Theory]
    [InlineData(DatabaseProvider.MySql)]
    [InlineData(DatabaseProvider.SQLite)]
    public void CompileWithDiagnostics_SequenceOnUnsupportedProvider_Warns(DatabaseProvider provider)
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateSequenceOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("seq", NodeType.SequenceDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["SequenceName"] = "seq_orders",
            }),
        };

        var connections = new List<Connection>
        {
            new("seq", "seq", "out", "seq"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, provider).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-SEQUENCE-UNSUPPORTED-PROVIDER");
    }

    [Fact]
    public void CompileWithDiagnostics_ExpressionIndexOnSqlServer_WarnsUnsupported()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateIndexOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("idx", NodeType.IndexDefinition, new Dictionary<string, string>
            {
                ["expression_column"] = "lower(name)",
            }, new Dictionary<string, string>
            {
                ["IndexName"] = "ix_users_lower_name",
            }),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "dbo",
                ["TableName"] = "users",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "name",
                ["DataType"] = "NVARCHAR(100)",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("idx", "idx", "out", "index"),
            new("tbl", "table", "idx", "table"),
            new("col", "column", "tbl", "column"),
            new("col", "column", "idx", "column"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.SqlServer).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-INDEX-EXPR-UNSUPPORTED-SQLSERVER");
    }

    [Fact]
    public void CompileWithDiagnostics_ExpressionIndexOnSqlite_WarnsPartialSupport()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateIndexOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("idx", NodeType.IndexDefinition, new Dictionary<string, string>
            {
                ["expression_column"] = "lower(name)",
            }, new Dictionary<string, string>
            {
                ["IndexName"] = "ix_users_lower_name",
            }),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "main",
                ["TableName"] = "users",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "name",
                ["DataType"] = "TEXT",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("idx", "idx", "out", "index"),
            new("tbl", "table", "idx", "table"),
            new("col", "column", "tbl", "column"),
            new("col", "column", "idx", "column"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.SQLite).CompileWithDiagnostics();

        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-INDEX-EXPR-SQLITE-PARTIAL");
    }

    [Fact]
    public void CompileWithDiagnostics_CreateTableAsOutput_SourceQueryConnection_ReturnsIsolationError()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableAsOutput, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["TableName"] = "orders_copy",
            }),
            new("src_q", NodeType.TableSource, new Dictionary<string, string>(), new Dictionary<string, string>(), TableFullName: "public.orders"),
        };

        var connections = new List<Connection>
        {
            new("src_q", "result", "out", "source_query"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();
        Assert.Contains(result.Diagnostics, d => d.Code == "E-DDL-CREATETABLEAS-SOURCEQUERY-FORBIDDEN");
    }

    [Fact]
    public void CompileWithDiagnostics_CreateTableAsOutput_IncludeDataOnMySql_WarnsIgnored()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableAsOutput, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["TableName"] = "orders_copy",
                ["IncludeData"] = "false",
                ["SelectSql"] = "SELECT * FROM orders",
            }),
        };

        var connections = new List<Connection>();

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.MySql).CompileWithDiagnostics();
        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-CREATETABLEAS-INCLUDEDATA-IGNORED");
    }

    private static NodeGraph BuildFkGraph(IReadOnlyList<string> childColumns, IReadOnlyList<string> parentColumns)
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl_child", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "child",
            }),
            new("tbl_parent", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "parent",
            }),
            new("fk", NodeType.ForeignKeyConstraint, new Dictionary<string, string>(), new Dictionary<string, string>()),
        };

        var connections = new List<Connection>
        {
            new("tbl_child", "table", "out", "table"),
            new("fk", "fk", "tbl_child", "constraint"),
        };

        foreach (string col in childColumns)
        {
            nodes.Add(new NodeInstance(col, NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = col,
                ["DataType"] = "INT",
                ["IsNullable"] = "false",
            }));
            connections.Add(new Connection(col, "column", "tbl_child", "column"));
            connections.Add(new Connection(col, "column", "fk", "child_column"));
        }

        foreach (string col in parentColumns)
        {
            nodes.Add(new NodeInstance(col, NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = col,
                ["DataType"] = "INT",
                ["IsNullable"] = "false",
            }));
            connections.Add(new Connection(col, "column", "tbl_parent", "column"));
            connections.Add(new Connection(col, "column", "fk", "parent_column"));
        }

        return new NodeGraph
        {
            Nodes = nodes,
            Connections = connections,
        };
    }
}
