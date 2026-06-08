using AkkornStudio.Ddl;
using AkkornStudio.Core;
using AkkornStudio.Nodes;
using System.Text.Json;

namespace AkkornStudio.Tests.Unit.Ddl;

public class DdlGraphCompilerTests
{
    [Fact]
    public void Compile_CreateTableOutput_BuildsCreateTableExpression()
    {
        var graph = BuildCreateTableGraph();
        var compiler = new DdlGraphCompiler(graph);

        IReadOnlyList<IDdlExpression> statements = compiler.Compile();

        var create = Assert.IsType<CreateTableExpr>(Assert.Single(statements));
        Assert.Equal("dbo", create.SchemaName);
        Assert.Equal("orders", create.TableName);
        Assert.True(create.IfNotExists);
        Assert.Equal(2, create.Columns.Count);
        Assert.Contains(create.Columns, c => c.ColumnName == "id" && c.DataType == "INT" && !c.IsNullable);
        Assert.Contains(create.Columns, c => c.ColumnName == "status" && c.DefaultExpression == "('NEW')");
        Assert.Single(create.PrimaryKeys);
        Assert.Equal("PK_orders", create.PrimaryKeys[0].ConstraintName);
        Assert.Contains("id", create.PrimaryKeys[0].Columns);
    }

    [Fact]
    public void Compile_CreateIndexOutput_BuildsCreateIndexExpression()
    {
        var graph = BuildCreateIndexGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.Postgres);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        var index = Assert.IsType<CreateIndexExpr>(Assert.Single(result.Statements));
        Assert.Equal("public", index.SchemaName);
        Assert.Equal("orders", index.TableName);
        Assert.Equal("ix_orders_status", index.IndexName);
        Assert.Contains(index.KeyColumns, k => !k.IsExpression && k.ColumnName == "status");
        Assert.Contains("created_at", index.IncludeColumns);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Compile_CreateIndexWithInclude_OnMySql_FailsWithExplicitError()
    {
        var graph = BuildCreateIndexGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.MySql);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        Assert.Empty(result.Statements);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "E-DDL-COMPILE-CREATEINDEX"
            && d.Severity == DdlDiagnosticSeverity.Error
            && d.Message.Contains("does not support INCLUDE columns", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compile_CreateIndexWithExpressionColumn_BuildsExpressionKeyEntry()
    {
        var graph = BuildCreateIndexWithExpressionGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.Postgres);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        var index = Assert.IsType<CreateIndexExpr>(Assert.Single(result.Statements));
        Assert.Contains(index.KeyColumns, k => k.IsExpression && k.ExpressionSql == "lower(name)");
    }

    [Fact]
    public void Compile_AlterTableOutput_BuildsAlterTableExpression()
    {
        var graph = BuildAlterTableGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.Postgres);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        var alter = Assert.IsType<AlterTableExpr>(Assert.Single(result.Statements));
        Assert.Equal("public", alter.SchemaName);
        Assert.Equal("orders", alter.TableName);
        Assert.True(alter.EmitSeparateStatements);
        Assert.Equal(6, alter.Operations.Count);
        Assert.IsType<DropTableOpExpr>(alter.Operations[0]);
        Assert.Contains(alter.Operations, o => o is AddColumnOpExpr);
        Assert.Contains(alter.Operations, o => o is DropColumnOpExpr);
        Assert.Contains(alter.Operations, o => o is RenameColumnOpExpr);
        Assert.Contains(alter.Operations, o => o is RenameTableOpExpr);
        Assert.Contains(alter.Operations, o => o is DropTableOpExpr);
        Assert.Contains(alter.Operations, o => o is AlterColumnTypeOpExpr);
        Assert.Contains(alter.Operations, o => o.IsDestructive);
        Assert.All(alter.Operations.Where(o => o is not DropTableOpExpr), o => Assert.False(o.IsDestructive));
        Assert.Contains(result.Warnings, w => w.Code == "W-DDL-DROPTABLE-DESTRUCTIVE");
    }

    [Fact]
    public void Compile_AlterColumnType_OnSqlite_EmitsCompatibilityWarning()
    {
        var graph = BuildAlterTableGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.SQLite);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        Assert.Contains(result.Warnings, w => w.Code == "W-DDL-ALTERTYPE-SQLITE");
    }

    [Fact]
    public void Compile_PostgresEnumType_EmitsCreateTypeBeforeCreateTable()
    {
        var graph = BuildPostgresEnumTypeGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.Postgres);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        Assert.Equal(2, result.Statements.Count);
        Assert.IsType<CreateEnumTypeExpr>(result.Statements[0]);
        var createTable = Assert.IsType<CreateTableExpr>(result.Statements[1]);
        Assert.Contains(createTable.Columns, c => c.ColumnName == "status" && c.DataType.Contains("status_enum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compile_MySqlEnumType_UsesInlineEnumInColumnType()
    {
        var graph = BuildMySqlEnumTypeGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.MySql);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        var createTable = Assert.IsType<CreateTableExpr>(Assert.Single(result.Statements));
        Assert.Contains(createTable.Columns, c => c.ColumnName == "status" && c.DataType.StartsWith("ENUM(", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compile_ColumnWithUseNativeType_PassesThroughNativeTypeExpression()
    {
        var nodes = new List<NodeInstance>
        {
            new("table_out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "network_ranges",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "range",
                ["DataType"] = "TEXT",
                ["IsNullable"] = "false",
                ["UseNativeType"] = "true",
                ["NativeTypeExpression"] = "CIDR",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "table_out", "table"),
            new("col", "column", "tbl", "column"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();

        var createTable = Assert.IsType<CreateTableExpr>(Assert.Single(result.Statements));
        Assert.Contains(createTable.Columns, c => c.ColumnName == "range" && c.DataType == "CIDR");
        Assert.Contains(result.Diagnostics, d => d.Code == "W-DDL-NATIVE-TYPE-PORTABILITY");
    }

    [Fact]
    public void Compile_ColumnWithScalarTypeDefinition_UsesTypeNodeInsteadOfTextParameter()
    {
        var nodes = new List<NodeInstance>
        {
            new("table_out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "customers",
            }),
            new("type", NodeType.ScalarTypeDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["TypeKind"] = "VARCHAR",
                ["Length"] = "120",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "name",
                ["DataType"] = "INT",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("tbl", "table", "table_out", "table"),
            new("col", "column", "tbl", "column"),
            new("type", "type_def", "col", "type_def"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();

        var createTable = Assert.IsType<CreateTableExpr>(Assert.Single(result.Statements));
        Assert.Contains(createTable.Columns, c => c.ColumnName == "name" && c.DataType == "VARCHAR(120)");
    }

    [Fact]
    public void Compile_CreateViewOutput_BuildsCreateViewExpression()
    {
        var graph = BuildCreateViewGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.Postgres);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        var expr = Assert.IsType<CreateViewExpr>(Assert.Single(result.Statements));
        Assert.Equal("public", expr.SchemaName);
        Assert.Equal("v_orders", expr.ViewName);
        Assert.True(expr.OrReplace);
        Assert.False(expr.IsMaterialized);
        Assert.Contains("SELECT", expr.SelectSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_CreateOutputs_WithIdempotentMode_PropagatesToExpressions()
    {
        var baseGraph = BuildPostgresEnumTypeGraph();
        NodeGraph graph = new()
        {
            Nodes =
            [
                .. baseGraph.Nodes.Select(n => n.Id switch
                {
                    "type_out" => n with
                    {
                        Parameters = new Dictionary<string, string>
                        {
                            ["IdempotentMode"] = "IfNotExists",
                        },
                    },
                    "table_out" => n with
                    {
                        Parameters = new Dictionary<string, string>
                        {
                            ["IdempotentMode"] = "DropAndCreate",
                        },
                    },
                    _ => n,
                }),
            ],
            Connections = baseGraph.Connections,
        };

        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.Postgres);
        DdlCompileResult result = compiler.CompileWithDiagnostics();

        var typeExpr = Assert.IsType<CreateEnumTypeExpr>(result.Statements[0]);
        var tableExpr = Assert.IsType<CreateTableExpr>(result.Statements[1]);
        Assert.Equal(DdlIdempotentMode.IfNotExists, typeExpr.Mode);
        Assert.Equal(DdlIdempotentMode.DropAndCreate, tableExpr.Mode);
    }

    [Fact]
    public void Compile_CreateSequenceOutput_BuildsSequenceBeforeCreateTableWithSequenceDefault()
    {
        var graph = BuildPostgresSequenceGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.Postgres);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        Assert.Equal(2, result.Statements.Count);
        Assert.IsType<CreateSequenceExpr>(result.Statements[0]);
        var tableExpr = Assert.IsType<CreateTableExpr>(result.Statements[1]);
        DdlColumnExpr idColumn = Assert.Single(tableExpr.Columns, c => c.ColumnName == "id");
        Assert.Contains("nextval", idColumn.DefaultExpression ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_CreateTableAsOutput_WithSourceTable_BuildsLikeExpression()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableAsOutput, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["TableName"] = "orders_clone",
            }),
            new("src", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
        };

        var connections = new List<Connection>
        {
            new("src", "table", "out", "source_table"),
        };

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();
        var expr = Assert.IsType<CreateTableAsExpr>(Assert.Single(result.Statements));
        Assert.Contains("orders", expr.SourceTable ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_CreateTableAsOutput_WithSelectSqlFallback_BuildsAsSelectExpression()
    {
        var nodes = new List<NodeInstance>
        {
            new("out", NodeType.CreateTableAsOutput, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["TableName"] = "orders_copy",
                ["SelectSql"] = "SELECT id FROM orders",
            }),
        };

        var connections = new List<Connection>();

        DdlCompileResult result = new DdlGraphCompiler(new NodeGraph { Nodes = nodes, Connections = connections }, DatabaseProvider.Postgres).CompileWithDiagnostics();
        var expr = Assert.IsType<CreateTableAsExpr>(Assert.Single(result.Statements));
        Assert.Equal("SELECT id FROM orders", expr.SelectSql);
    }

    [Fact]
    public void Compile_AlterViewOutput_BuildsAlterViewExpression()
    {
        var graph = BuildAlterViewGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.SqlServer);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        var expr = Assert.IsType<AlterViewExpr>(Assert.Single(result.Statements));
        Assert.Equal("dbo", expr.SchemaName);
        Assert.Equal("v_orders", expr.ViewName);
        Assert.Contains("SELECT", expr.SelectSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_CreateMaterializedView_Postgres_BuildsExpression()
    {
        var graph = BuildCreateMaterializedViewGraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.Postgres);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        var expr = Assert.IsType<CreateViewExpr>(Assert.Single(result.Statements));
        Assert.True(expr.IsMaterialized);
        Assert.False(expr.OrReplace);
    }

    [Fact]
    public void Compile_CreateViewOutput_WithSubgraphJson_BuildsCreateViewExpression()
    {
        var graph = BuildCreateViewGraphWithSubgraph();
        var compiler = new DdlGraphCompiler(graph, DatabaseProvider.Postgres);

        DdlCompileResult result = compiler.CompileWithDiagnostics();

        var expr = Assert.IsType<CreateViewExpr>(Assert.Single(result.Statements));
        Assert.Equal("public", expr.SchemaName);
        Assert.Equal("v_orders_subgraph", expr.ViewName);
        Assert.Contains("SELECT", expr.SelectSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", expr.SelectSql, StringComparison.OrdinalIgnoreCase);
    }

    private static NodeGraph BuildCreateTableGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("out1", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("t1", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "dbo",
                ["TableName"] = "orders",
                ["IfNotExists"] = "true",
            }),
            new("c1", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "id",
                ["DataType"] = "INT",
                ["IsNullable"] = "false",
            }),
            new("c2", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "status",
                ["DataType"] = "NVARCHAR(32)",
                ["IsNullable"] = "false",
            }),
            new("pk1", NodeType.PrimaryKeyConstraint, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ConstraintName"] = "PK_orders",
            }),
            new("dc1", NodeType.DefaultConstraint, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["DefaultValue"] = "('NEW')",
            }),
        };

        var connections = new List<Connection>
        {
            new("t1", "table", "out1", "table"),
            new("c1", "column", "t1", "column"),
            new("c2", "column", "t1", "column"),
            new("c1", "column", "pk1", "column"),
            new("pk1", "pk", "t1", "constraint"),
            new("c2", "column", "dc1", "column"),
            new("dc1", "dc", "t1", "constraint"),
        };

        return new NodeGraph
        {
            Nodes = nodes,
            Connections = connections,
        };
    }

    private static NodeGraph BuildCreateIndexGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("out1", NodeType.CreateIndexOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("idx1", NodeType.IndexDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["IndexName"] = "ix_orders_status",
                ["IsUnique"] = "false",
            }),
            new("t1", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("c1", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "status",
                ["DataType"] = "TEXT",
                ["IsNullable"] = "false",
            }),
            new("c2", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "created_at",
                ["DataType"] = "TIMESTAMP",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("idx1", "idx", "out1", "index"),
            new("t1", "table", "idx1", "table"),
            new("c1", "column", "t1", "column"),
            new("c2", "column", "t1", "column"),
            new("c1", "column", "idx1", "column"),
            new("c2", "column", "idx1", "include_column"),
        };

        return new NodeGraph
        {
            Nodes = nodes,
            Connections = connections,
        };
    }

    private static NodeGraph BuildCreateIndexWithExpressionGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("out1", NodeType.CreateIndexOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("idx1", NodeType.IndexDefinition, new Dictionary<string, string>
            {
                ["expression_column"] = "lower(name)",
            }, new Dictionary<string, string>
            {
                ["IndexName"] = "ix_users_lower_name",
                ["IsUnique"] = "false",
            }),
            new("t1", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "users",
            }),
            new("c1", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "name",
                ["DataType"] = "TEXT",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("idx1", "idx", "out1", "index"),
            new("t1", "table", "idx1", "table"),
            new("c1", "column", "t1", "column"),
        };

        return new NodeGraph
        {
            Nodes = nodes,
            Connections = connections,
        };
    }

    private static NodeGraph BuildAlterTableGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("out1", NodeType.AlterTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["EmitSeparateStatements"] = "true",
            }),
            new("t1", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("add1", NodeType.AddColumnOp, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("drop1", NodeType.DropColumnOp, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "legacy_code",
                ["IfExists"] = "true",
            }),
            new("ren1", NodeType.RenameColumnOp, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["OldName"] = "old_name",
                ["NewName"] = "new_name",
            }),
            new("rent1", NodeType.RenameTableOp, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["NewName"] = "orders_v2",
            }),
            new("dropt1", NodeType.DropTableOp, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["IfExists"] = "true",
            }),
            new("alt1", NodeType.AlterColumnTypeOp, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("c_add", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "status",
                ["DataType"] = "TEXT",
                ["IsNullable"] = "false",
            }),
            new("c_alt", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "total",
                ["DataType"] = "NUMERIC(10,2)",
                ["IsNullable"] = "true",
            }),
        };

        var connections = new List<Connection>
        {
            new("t1", "table", "out1", "table"),
            new("add1", "op", "out1", "operation"),
            new("drop1", "op", "out1", "operation"),
            new("ren1", "op", "out1", "operation"),
            new("rent1", "op", "out1", "operation"),
            new("dropt1", "op", "out1", "operation"),
            new("alt1", "op", "out1", "operation"),
            new("c_add", "column", "add1", "column"),
            new("c_alt", "column", "alt1", "new_column"),
        };

        return new NodeGraph
        {
            Nodes = nodes,
            Connections = connections,
        };
    }

    private static NodeGraph BuildPostgresEnumTypeGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("type_out", NodeType.CreateTypeOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("table_out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("enum_def", NodeType.EnumTypeDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TypeName"] = "status_enum",
                ["EnumValues"] = "NEW,ACTIVE,DISABLED",
            }),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
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
            new("enum_def", "type_def", "type_out", "type_def"),
            new("tbl", "table", "table_out", "table"),
            new("col", "column", "tbl", "column"),
            new("enum_def", "type_def", "col", "type_def"),
        };

        return new NodeGraph
        {
            Nodes = nodes,
            Connections = connections,
        };
    }

    private static NodeGraph BuildMySqlEnumTypeGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("table_out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("enum_def", NodeType.EnumTypeDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TypeName"] = "status_enum",
                ["EnumValues"] = "NEW,ACTIVE,DISABLED",
            }),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
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
            new("tbl", "table", "table_out", "table"),
            new("col", "column", "tbl", "column"),
            new("enum_def", "type_def", "col", "type_def"),
        };

        return new NodeGraph
        {
            Nodes = nodes,
            Connections = connections,
        };
    }

    private static NodeGraph BuildPostgresSequenceGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("seq_out", NodeType.CreateSequenceOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("table_out", NodeType.CreateTableOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("seq", NodeType.SequenceDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["SequenceName"] = "orders_id_seq",
                ["StartValue"] = "1",
                ["Increment"] = "1",
                ["Cycle"] = "false",
            }),
            new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["SchemaName"] = "public",
                ["TableName"] = "orders",
            }),
            new("col", NodeType.ColumnDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["ColumnName"] = "id",
                ["DataType"] = "BIGINT",
                ["IsNullable"] = "false",
            }),
        };

        var connections = new List<Connection>
        {
            new("seq", "seq", "seq_out", "seq"),
            new("tbl", "table", "table_out", "table"),
            new("col", "column", "tbl", "column"),
            new("seq", "seq", "col", "sequence"),
        };

        return new NodeGraph
        {
            Nodes = nodes,
            Connections = connections,
        };
    }

    private static NodeGraph BuildCreateViewGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("out_view", NodeType.CreateViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("view_def", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["ViewName"] = "v_orders",
                ["OrReplace"] = "true",
                ["IsMaterialized"] = "false",
                ["SelectSql"] = "SELECT id, status FROM public.orders",
            }),
        };

        var connections = new List<Connection>
        {
            new("view_def", "view", "out_view", "view"),
        };

        return new NodeGraph { Nodes = nodes, Connections = connections };
    }

    private static NodeGraph BuildAlterViewGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("out_view", NodeType.AlterViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("view_def", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "dbo",
                ["ViewName"] = "v_orders",
                ["SelectSql"] = "SELECT id FROM dbo.orders",
            }),
        };

        var connections = new List<Connection>
        {
            new("view_def", "view", "out_view", "view"),
        };

        return new NodeGraph { Nodes = nodes, Connections = connections };
    }

    private static NodeGraph BuildCreateMaterializedViewGraph()
    {
        var nodes = new List<NodeInstance>
        {
            new("out_view", NodeType.CreateViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("view_def", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["ViewName"] = "mv_orders",
                ["OrReplace"] = "false",
                ["IsMaterialized"] = "true",
                ["SelectSql"] = "SELECT id, status FROM public.orders",
            }),
        };

        var connections = new List<Connection>
        {
            new("view_def", "view", "out_view", "view"),
        };

        return new NodeGraph { Nodes = nodes, Connections = connections };
    }

    private static NodeGraph BuildCreateViewGraphWithSubgraph()
    {
        var subgraph = new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    "tbl",
                    NodeType.TableSource,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    Alias: "orders",
                    ColumnPins: new Dictionary<string, string> { ["status"] = "status" },
                    ColumnPinTypes: new Dictionary<string, PinDataType> { ["status"] = PinDataType.Text },
                    TableFullName: "public.orders"
                ),
                new NodeInstance(
                    "out",
                    NodeType.ResultOutput,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()
                ),
            ],
            Connections = [new Connection("tbl", "status", "out", "column")],
        };

        string payload = JsonSerializer.Serialize(subgraph);

        var nodes = new List<NodeInstance>
        {
            new("out_view", NodeType.CreateViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>()),
            new("view_def", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>
            {
                ["Schema"] = "public",
                ["ViewName"] = "v_orders_subgraph",
                ["OrReplace"] = "false",
                ["IsMaterialized"] = "false",
                ["ViewFromTable"] = "public.orders",
                ["ViewSubgraphGraphJson"] = payload,
            }),
        };

        var connections = new List<Connection>
        {
            new("view_def", "view", "out_view", "view"),
        };

        return new NodeGraph { Nodes = nodes, Connections = connections };
    }
}
