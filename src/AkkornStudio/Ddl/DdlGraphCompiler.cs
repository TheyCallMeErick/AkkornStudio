using AkkornStudio.Nodes;
using AkkornStudio.Core;
using AkkornStudio.Ddl.Compilers;
using AkkornStudio.Metadata;
using AkkornStudio.QueryEngine;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AkkornStudio.Ddl;

/// <summary>
/// Compiles a DDL-oriented NodeGraph into DDL expression trees.
/// Phase 4 scope: CreateTableOutput only.
/// </summary>
public sealed partial class DdlGraphCompiler(NodeGraph graph, DatabaseProvider provider = DatabaseProvider.SqlServer)
{
    private readonly NodeGraph _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    private readonly DatabaseProvider _provider = provider;
    private readonly List<DdlCompileDiagnostic> _diagnostics = [];

    public IReadOnlyList<IDdlExpression> Compile()
        => CompileWithDiagnostics().Statements;

    public DdlCompileResult CompileWithDiagnostics()
    {
        _diagnostics.Clear();

        var createTableOutputs = _graph.Nodes.Where(n => n.Type == NodeType.CreateTableOutput).ToList();
        var createTypeOutputs = _graph.Nodes.Where(n => n.Type == NodeType.CreateTypeOutput).ToList();
        var createSequenceOutputs = _graph.Nodes.Where(n => n.Type == NodeType.CreateSequenceOutput).ToList();
        var createTableAsOutputs = _graph.Nodes.Where(n => n.Type == NodeType.CreateTableAsOutput).ToList();
        var createViewOutputs = _graph.Nodes.Where(n => n.Type == NodeType.CreateViewOutput).ToList();
        var alterViewOutputs = _graph.Nodes.Where(n => n.Type == NodeType.AlterViewOutput).ToList();
        var createIndexOutputs = _graph.Nodes.Where(n => n.Type == NodeType.CreateIndexOutput).ToList();
        var alterTableOutputs = _graph.Nodes.Where(n => n.Type == NodeType.AlterTableOutput).ToList();

        ValidateStaticRules(createTableOutputs, createTypeOutputs, createSequenceOutputs, createTableAsOutputs, createViewOutputs, alterViewOutputs);

        if (createTableOutputs.Count == 0 && createTypeOutputs.Count == 0 && createSequenceOutputs.Count == 0 && createTableAsOutputs.Count == 0 && createViewOutputs.Count == 0 && alterViewOutputs.Count == 0 && createIndexOutputs.Count == 0 && alterTableOutputs.Count == 0)
            return new DdlCompileResult([], [.. _diagnostics]);

        if (_diagnostics.Any(d => d.Severity == DdlDiagnosticSeverity.Error))
            return new DdlCompileResult([], [.. _diagnostics]);

        var statements = new List<IDdlExpression>();
        var createSequenceOutputCompiler = new CreateSequenceOutputCompiler();
        createSequenceOutputCompiler.Compile(
            createSequenceOutputs,
            CreateOutputCompilationContext(),
            statements
        );

        var createTypeOutputCompiler = new CreateTypeOutputCompiler();
        createTypeOutputCompiler.Compile(
            createTypeOutputs,
            CreateOutputCompilationContext(),
            statements
        );

        var createTableOutputCompiler = new CreateTableOutputCompiler();
        createTableOutputCompiler.Compile(
            createTableOutputs,
            CreateOutputCompilationContext(),
            statements
        );

        var createTableAsOutputCompiler = new CreateTableAsOutputCompiler();
        createTableAsOutputCompiler.Compile(
            createTableAsOutputs,
            CreateOutputCompilationContext(),
            statements
        );

        var createViewOutputCompiler = new CreateViewOutputCompiler();
        createViewOutputCompiler.Compile(
            createViewOutputs,
            CreateOutputCompilationContext(),
            statements
        );

        var alterViewOutputCompiler = new AlterViewOutputCompiler();
        alterViewOutputCompiler.Compile(
            alterViewOutputs,
            CreateOutputCompilationContext(),
            statements
        );

        var createIndexOutputCompiler = new CreateIndexOutputCompiler();
        createIndexOutputCompiler.Compile(
            createIndexOutputs,
            CreateOutputCompilationContext(),
            statements
        );

        var alterTableOutputCompiler = new AlterTableOutputCompiler();
        alterTableOutputCompiler.Compile(
            alterTableOutputs,
            CreateOutputCompilationContext(),
            statements
        );

        return new DdlCompileResult(statements, [.. _diagnostics]);
    }

    private CreateTableExpr CompileTableDefinition(NodeInstance tableNode, DdlIdempotentMode idempotentMode)
    {
        string schemaName = ReadParam(tableNode, "SchemaName", "dbo");
        string tableName = ReadParam(tableNode, "TableName", "");
        bool ifNotExists = ReadBoolParam(tableNode, "IfNotExists", false);
        string tableComment = ReadParam(tableNode, "Comment", "");

        if (string.IsNullOrWhiteSpace(tableName))
            throw new InvalidOperationException("TableDefinition requires TableName.");

        var columnWires = _graph.Connections
            .Where(c => c.ToNodeId == tableNode.Id && c.ToPinName == "column")
            .ToList();

        var columnsByNodeId = new Dictionary<string, DdlColumnExpr>(StringComparer.Ordinal);
        foreach (Connection wire in columnWires)
        {
            NodeInstance columnNode = _graph.NodeMap[wire.FromNodeId];
            if (columnNode.Type != NodeType.ColumnDefinition)
                continue;

            columnsByNodeId[columnNode.Id] = BuildColumnFromDefinitionNode(columnNode);
        }

        // Default constraints are translated into inline column DEFAULT for phase 4.
        ApplyDefaultConstraints(columnsByNodeId, tableNode.Id);

        var constraintNodes = _graph.Connections
            .Where(c => c.ToNodeId == tableNode.Id && c.ToPinName == "constraint")
            .Select(c => _graph.NodeMap[c.FromNodeId])
            .DistinctBy(n => n.Id)
            .ToList();

        var pks = new List<DdlPrimaryKeyExpr>();
        var uqs = new List<DdlUniqueExpr>();
        var checks = new List<DdlCheckExpr>();
        var foreignKeys = new List<DdlForeignKeyExpr>();

        foreach (NodeInstance constraintNode in constraintNodes)
        {
            switch (constraintNode.Type)
            {
                case NodeType.PrimaryKeyConstraint:
                    {
                        IReadOnlyList<string> cols = ResolveConstraintColumnNames(constraintNode.Id, "column", columnsByNodeId);
                        if (cols.Count > 0)
                            pks.Add(new DdlPrimaryKeyExpr(ReadParam(constraintNode, "ConstraintName", ""), cols));
                        break;
                    }
                case NodeType.UniqueConstraint:
                    {
                        IReadOnlyList<string> cols = ResolveConstraintColumnNames(constraintNode.Id, "column", columnsByNodeId);
                        if (cols.Count > 0)
                            uqs.Add(new DdlUniqueExpr(ReadParam(constraintNode, "ConstraintName", ""), cols));
                        break;
                    }
                case NodeType.CheckConstraint:
                    {
                        string expression = ReadParam(constraintNode, "Expression", "");
                        if (!string.IsNullOrWhiteSpace(expression))
                            checks.Add(new DdlCheckExpr(ReadParam(constraintNode, "ConstraintName", ""), expression));
                        break;
                    }
                case NodeType.ForeignKeyConstraint:
                    {
                        IReadOnlyList<NodeInstance> childColumnNodes = GetConnectedInputNodes(constraintNode.Id, "child_column")
                            .Where(static node => node.Type == NodeType.ColumnDefinition)
                            .ToList();
                        IReadOnlyList<NodeInstance> parentColumnNodes = GetConnectedInputNodes(constraintNode.Id, "parent_column")
                            .Where(static node => node.Type == NodeType.ColumnDefinition)
                            .ToList();

                        if (childColumnNodes.Count == 0
                            || parentColumnNodes.Count == 0
                            || childColumnNodes.Count != parentColumnNodes.Count)
                        {
                            AddWarning(
                                "W-DDL-FOREIGNKEY-UNSUPPORTED-SHAPE",
                                "ForeignKeyConstraint requires same non-zero number of child and parent columns.",
                                constraintNode.Id
                            );
                            break;
                        }

                        NodeInstance? childTableNode = ResolveOwnerTableDefinition(childColumnNodes[0]);
                        NodeInstance? parentTableNode = ResolveOwnerTableDefinition(parentColumnNodes[0]);
                        if (childTableNode is null
                            || parentTableNode is null
                            || !string.Equals(childTableNode.Id, tableNode.Id, StringComparison.Ordinal))
                        {
                            AddWarning(
                                "W-DDL-FOREIGNKEY-UNSUPPORTED-SHAPE",
                                "ForeignKeyConstraint requires resolvable child/parent table ownership for single-column FK emission.",
                                constraintNode.Id
                            );
                            break;
                        }

                        var childColumns = new List<string>(childColumnNodes.Count);
                        var parentColumns = new List<string>(parentColumnNodes.Count);
                        for (int pairIndex = 0; pairIndex < childColumnNodes.Count; pairIndex++)
                        {
                            NodeInstance childNode = childColumnNodes[pairIndex];
                            NodeInstance parentNode = parentColumnNodes[pairIndex];
                            NodeInstance? resolvedChildOwner = ResolveOwnerTableDefinition(childNode);
                            NodeInstance? resolvedParentOwner = ResolveOwnerTableDefinition(parentNode);
                            if (resolvedChildOwner is null
                                || resolvedParentOwner is null
                                || !string.Equals(resolvedChildOwner.Id, childTableNode.Id, StringComparison.Ordinal)
                                || !string.Equals(resolvedParentOwner.Id, parentTableNode.Id, StringComparison.Ordinal))
                            {
                                AddWarning(
                                    "W-DDL-FOREIGNKEY-UNSUPPORTED-SHAPE",
                                    "ForeignKeyConstraint could not resolve stable ownership for all composite pairs.",
                                    constraintNode.Id
                                );
                                childColumns.Clear();
                                parentColumns.Clear();
                                break;
                            }

                            string childColumnName = ReadParam(childNode, "ColumnName", "");
                            string parentColumnName = ReadParam(parentNode, "ColumnName", "");
                            if (string.IsNullOrWhiteSpace(childColumnName) || string.IsNullOrWhiteSpace(parentColumnName))
                            {
                                AddWarning(
                                    "W-DDL-FOREIGNKEY-UNSUPPORTED-SHAPE",
                                    "ForeignKeyConstraint could not resolve child/parent column names for composite FK emission.",
                                    constraintNode.Id
                                );
                                childColumns.Clear();
                                parentColumns.Clear();
                                break;
                            }

                            childColumns.Add(childColumnName);
                            parentColumns.Add(parentColumnName);
                        }

                        if (childColumns.Count == 0 || parentColumns.Count == 0)
                            break;

                        string parentSchema = ReadParam(parentTableNode, "SchemaName", "dbo");
                        string parentTable = ReadParam(parentTableNode, "TableName", "");
                        if (string.IsNullOrWhiteSpace(parentTable))
                        {
                            AddWarning(
                                "W-DDL-FOREIGNKEY-UNSUPPORTED-SHAPE",
                                "ForeignKeyConstraint could not resolve parent table name.",
                                constraintNode.Id
                            );
                            break;
                        }

                        string? constraintName = ReadParam(constraintNode, "ConstraintName", "");
                        foreignKeys.Add(new DdlForeignKeyExpr(
                            ConstraintName: string.IsNullOrWhiteSpace(constraintName) ? null : constraintName,
                            ChildColumns: childColumns,
                            ParentSchema: parentSchema,
                            ParentTable: parentTable,
                            ParentColumns: parentColumns,
                            OnDelete: ParseReferentialAction(ReadParam(constraintNode, "OnDelete", "NO ACTION")),
                            OnUpdate: ParseReferentialAction(ReadParam(constraintNode, "OnUpdate", "NO ACTION"))
                        ));
                        break;
                    }
            }
        }

        return new CreateTableExpr(
            schemaName,
            tableName,
            ifNotExists,
            [.. columnsByNodeId.Values],
            pks,
            uqs,
            checks,
            tableComment,
            idempotentMode,
            foreignKeys
        );
    }

    private IReadOnlyList<NodeInstance> GetConnectedInputNodes(string targetNodeId, string toPinName) =>
        _graph.Connections
            .Where(connection => connection.ToNodeId == targetNodeId
                && connection.ToPinName == toPinName
                && _graph.NodeMap.ContainsKey(connection.FromNodeId))
            .Select(connection => _graph.NodeMap[connection.FromNodeId])
            .DistinctBy(static node => node.Id)
            .ToList();

    private NodeInstance? ResolveOwnerTableDefinition(NodeInstance columnNode)
    {
        if (columnNode.Type != NodeType.ColumnDefinition)
            return null;

        Connection? ownerConnection = _graph.Connections.FirstOrDefault(connection =>
            connection.FromNodeId == columnNode.Id
            && connection.ToPinName == "column"
            && _graph.NodeMap.TryGetValue(connection.ToNodeId, out NodeInstance? owner)
            && owner.Type == NodeType.TableDefinition);

        if (ownerConnection is null)
            return null;

        return _graph.NodeMap[ownerConnection.ToNodeId];
    }

    private static ReferentialAction ParseReferentialAction(string value)
    {
        string normalized = value.Trim().Replace("_", " ", StringComparison.Ordinal).ToUpperInvariant();
        return normalized switch
        {
            "CASCADE" => ReferentialAction.Cascade,
            "SET NULL" => ReferentialAction.SetNull,
            "SET DEFAULT" => ReferentialAction.SetDefault,
            "RESTRICT" => ReferentialAction.Restrict,
            _ => ReferentialAction.NoAction,
        };
    }

    private CreateTableAsExpr CompileCreateTableAsOutput(NodeInstance outputNode)
    {
        DdlIdempotentMode mode = ReadIdempotentMode(outputNode);
        string schema = ReadParam(outputNode, "Schema", _provider == DatabaseProvider.SqlServer ? "dbo" : "public");
        string tableName = ReadParam(outputNode, "TableName", "");
        if (string.IsNullOrWhiteSpace(tableName))
            throw new InvalidOperationException("CreateTableAsOutput requires TableName.");

        bool includeData = ReadBoolParam(outputNode, "IncludeData", true);
        Connection? queryConn = _graph.GetSingleInputConnection(outputNode.Id, "source_query");
        Connection? tableConn = _graph.GetSingleInputConnection(outputNode.Id, "source_table");
        string fallbackSelectSql = ReadParam(outputNode, "SelectSql", "");

        if (queryConn is not null)
            throw new InvalidOperationException("CreateTableAsOutput.source_query nao e permitido no canvas DDL isolado.");

        if (tableConn is null && string.IsNullOrWhiteSpace(fallbackSelectSql))
            throw new InvalidOperationException("CreateTableAsOutput requires source_table or SelectSql.");

        if (_provider != DatabaseProvider.Postgres && !includeData)
        {
            AddWarning(
                "W-DDL-CREATETABLEAS-INCLUDEDATA-IGNORED",
                "IncludeData afeta apenas PostgreSQL; sera ignorado neste provider.",
                outputNode.Id
            );
        }

        if (mode == DdlIdempotentMode.IfNotExists)
        {
            AddWarning(
                "W-DDL-CREATETABLEAS-IFNOTEXISTS-UNSUPPORTED",
                "IfNotExists nao e suportado de forma portavel para CREATE TABLE AS; modo sera degradado.",
                outputNode.Id
            );
        }

        if (tableConn is null)
            return new CreateTableAsExpr(schema, tableName, null, fallbackSelectSql, includeData, mode);

        NodeInstance tableNode = _graph.NodeMap[tableConn.FromNodeId];
        if (tableNode.Type != NodeType.TableDefinition)
            throw new InvalidOperationException("CreateTableAsOutput.source_table must come from TableDefinition.");

        string srcSchema = ReadParam(tableNode, "SchemaName", _provider == DatabaseProvider.SqlServer ? "dbo" : "public");
        string srcTable = ReadParam(tableNode, "TableName", "");
        if (string.IsNullOrWhiteSpace(srcTable))
            throw new InvalidOperationException("source_table TableDefinition requires TableName.");

        string sourceQualified = BuildQualifiedName(srcSchema, srcTable);
        return new CreateTableAsExpr(schema, tableName, sourceQualified, null, includeData, mode);
    }

    private string BuildQualifiedName(string schema, string name)
    {
        string Quote(string value) => _provider switch
        {
            DatabaseProvider.SqlServer => $"[{value.Replace("]", "]]" )}]",
            DatabaseProvider.MySql => $"`{value.Replace("`", "``")}`",
            DatabaseProvider.Postgres or DatabaseProvider.SQLite => $"\"{value.Replace("\"", "\"\"")}\"",
            _ => value,
        };

        return $"{Quote(schema)}.{Quote(name)}";
    }

    private static string ReadParam(NodeInstance node, string name, string fallback)
    {
        if (!node.Parameters.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim();
    }

    private static bool ReadBoolParam(NodeInstance node, string name, bool fallback)
    {
        if (!node.Parameters.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => fallback,
        };
    }

    private static DdlIdempotentMode ReadIdempotentMode(NodeInstance node)
    {
        string mode = ReadParam(node, "IdempotentMode", "None");
        return mode.Trim() switch
        {
            "IfNotExists" => DdlIdempotentMode.IfNotExists,
            "DropAndCreate" => DdlIdempotentMode.DropAndCreate,
            _ => DdlIdempotentMode.None,
        };
    }

    private DdlOutputCompilationContext CreateOutputCompilationContext() =>
        new(
            _graph,
            ReadIdempotentMode,
            CompileEnumTypeDefinition,
            CompileSequenceDefinition,
            CompileTableDefinition,
            CompileCreateTableAsOutput,
            CompileCreateViewDefinition,
            CompileAlterViewDefinition,
            CompileIndexDefinition,
            CompileAlterTableOutput,
            AddError
        );

    private static long? ReadLongParam(NodeInstance node, string name)
    {
        if (!node.Parameters.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
            return null;

        return long.TryParse(value.Trim(), out long parsed) ? parsed : null;
    }

    private static int? ReadIntParam(NodeInstance node, string name)
    {
        if (!node.Parameters.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
            return null;

        return int.TryParse(value.Trim(), out int parsed) ? parsed : null;
    }

}
