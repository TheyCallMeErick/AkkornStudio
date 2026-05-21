using AkkornStudio.Core;
using AkkornStudio.Nodes;

namespace AkkornStudio.Ddl;

public sealed partial class DdlGraphCompiler
{
    private CreateIndexExpr CompileIndexDefinition(NodeInstance indexNode)
    {
        Connection? tableWire = _graph.GetSingleInputConnection(indexNode.Id, "table");
        if (tableWire is null)
            throw new InvalidOperationException("IndexDefinition requires a connected 'table' input.");

        NodeInstance tableNode = _graph.NodeMap[tableWire.FromNodeId];
        if (tableNode.Type != NodeType.TableDefinition)
            throw new InvalidOperationException("IndexDefinition.table must come from TableDefinition.");

        string schemaName = ReadParam(tableNode, "SchemaName", "dbo");
        string tableName = ReadParam(tableNode, "TableName", "");
        if (string.IsNullOrWhiteSpace(tableName))
            throw new InvalidOperationException("TableDefinition requires TableName for index generation.");

        string indexName = ReadParam(indexNode, "IndexName", "");
        if (string.IsNullOrWhiteSpace(indexName))
            throw new InvalidOperationException("IndexDefinition requires IndexName.");

        bool isUnique = ReadBoolParam(indexNode, "IsUnique", false);
        bool ifNotExists = true;

        var allColumnsByNodeId = BuildColumnsByNodeIdForTable(tableNode.Id);
        IReadOnlyList<string> keyColumns = ResolveConstraintColumnNames(indexNode.Id, "column", allColumnsByNodeId);
        IReadOnlyList<string> expressionColumns = ResolveExpressionColumns(indexNode);

        if (expressionColumns.Count > 0 && _provider == DatabaseProvider.SqlServer)
        {
            AddWarning(
                "W-DDL-INDEX-EXPR-UNSUPPORTED-SQLSERVER",
                "SQL Server nao suporta indice em expressao arbitraria; use coluna computada persistida.",
                indexNode.Id
            );
            expressionColumns = [];
        }

        if (expressionColumns.Count > 0 && _provider == DatabaseProvider.SQLite)
        {
            AddWarning(
                "W-DDL-INDEX-EXPR-SQLITE-PARTIAL",
                "SQLite suporta indice em expressao com limitacoes; valide a expressao no provider alvo.",
                indexNode.Id
            );
        }

        if (keyColumns.Count == 0 && expressionColumns.Count == 0)
            throw new InvalidOperationException("IndexDefinition requires at least one key column or expression column.");

        var keyEntries = new List<DdlIndexKeyExpr>();
        keyEntries.AddRange(keyColumns.Select(c => new DdlIndexKeyExpr(ColumnName: c)));
        keyEntries.AddRange(expressionColumns.Select(e => new DdlIndexKeyExpr(ExpressionSql: e)));

        IReadOnlyList<string> includeColumns = ResolveConstraintColumnNames(indexNode.Id, "include_column", allColumnsByNodeId);
        if (includeColumns.Count > 0 && _provider is not DatabaseProvider.SqlServer and not DatabaseProvider.Postgres)
        {
            throw new InvalidOperationException(
                $"Provider {_provider} does not support INCLUDE columns in CREATE INDEX."
            );
        }

        return new CreateIndexExpr(
            schemaName,
            tableName,
            indexName,
            isUnique,
            keyEntries,
            includeColumns,
            ifNotExists
        );
    }

    private IReadOnlyList<string> ResolveExpressionColumns(NodeInstance indexNode)
    {
        var expressions = new List<string>();

        if (indexNode.PinLiterals.TryGetValue("expression_column", out string? literal)
            && !string.IsNullOrWhiteSpace(literal))
        {
            expressions.Add(literal.Trim());
        }

        foreach (Connection wire in _graph.Connections.Where(c => c.ToNodeId == indexNode.Id && c.ToPinName == "expression_column"))
        {
            NodeInstance fromNode = _graph.NodeMap[wire.FromNodeId];

            string expr = fromNode.Type switch
            {
                NodeType.ValueString => ReadParam(fromNode, "value", ""),
                NodeType.CheckConstraint => ReadParam(fromNode, "Expression", ""),
                _ => string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(expr))
                expressions.Add(expr.Trim());
        }

        return expressions
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private AlterTableExpr CompileAlterTableOutput(NodeInstance outputNode, NodeInstance tableNode)
    {
        string schemaName = ReadParam(tableNode, "SchemaName", "dbo");
        string tableName = ReadParam(tableNode, "TableName", "");
        if (string.IsNullOrWhiteSpace(tableName))
            throw new InvalidOperationException("TableDefinition requires TableName for alter operations.");

        bool emitSeparateStatements = ReadBoolParam(outputNode, "EmitSeparateStatements", true);

        IReadOnlyList<Connection> operationWires =
        [
            .. _graph.Connections.Where(c => c.ToNodeId == outputNode.Id && c.ToPinName == "operation"),
        ];

        if (operationWires.Count == 0)
            throw new InvalidOperationException("AlterTableOutput requires at least one connected operation.");

        var operations = new List<IAlterOpExpr>();
        foreach (Connection wire in operationWires)
        {
            NodeInstance opNode = _graph.NodeMap[wire.FromNodeId];
            IAlterOpExpr opExpr = CompileAlterOperation(opNode);
            operations.Add(opExpr);
        }

        operations = operations
            .OrderByDescending(op => op.IsDestructive)
            .ToList();

        return new AlterTableExpr(schemaName, tableName, operations, emitSeparateStatements);
    }

    private IAlterOpExpr CompileAlterOperation(NodeInstance opNode)
    {
        switch (opNode.Type)
        {
            case NodeType.AddColumnOp:
                {
                    Connection? columnWire = _graph.GetSingleInputConnection(opNode.Id, "column");
                    if (columnWire is null)
                        throw new InvalidOperationException("AddColumnOp requires a connected 'column' input.");

                    NodeInstance columnNode = _graph.NodeMap[columnWire.FromNodeId];
                    if (columnNode.Type != NodeType.ColumnDefinition)
                        throw new InvalidOperationException("AddColumnOp.column must come from ColumnDefinition.");

                    DdlColumnExpr colExpr = BuildColumnFromDefinitionNode(columnNode);
                    return new AddColumnOpExpr(colExpr);
                }
            case NodeType.DropColumnOp:
                {
                    string columnName = ResolveColumnNameFromTargetOrParameter(
                        opNode,
                        targetPinName: "target_column",
                        legacyParameterName: "ColumnName");
                    if (string.IsNullOrWhiteSpace(columnName))
                        throw new InvalidOperationException("DropColumnOp requires ColumnName.");
                    bool ifExists = ReadBoolParam(opNode, "IfExists", false);
                    return new DropColumnOpExpr(columnName, ifExists);
                }
            case NodeType.RenameColumnOp:
                {
                    string oldName = ResolveColumnNameFromTargetOrParameter(
                        opNode,
                        targetPinName: "target_column",
                        legacyParameterName: "OldName");
                    string newName = ResolveTextFromInputOrParameter(
                        opNode,
                        inputPinName: "new_name",
                        legacyParameterName: "NewName");
                    if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                        throw new InvalidOperationException("RenameColumnOp requires OldName and NewName.");
                    return new RenameColumnOpExpr(oldName, newName);
                }
            case NodeType.RenameTableOp:
                {
                    string newName = ResolveTextFromInputOrParameter(
                        opNode,
                        inputPinName: "new_name",
                        legacyParameterName: "NewName");
                    string? newSchema = ResolveOptionalTextFromInputOrParameter(
                        opNode,
                        inputPinName: "new_schema",
                        legacyParameterName: "NewSchema");
                    if (string.IsNullOrWhiteSpace(newName))
                        throw new InvalidOperationException("RenameTableOp requires NewName.");

                    return new RenameTableOpExpr(newName, string.IsNullOrWhiteSpace(newSchema) ? null : newSchema);
                }
            case NodeType.DropTableOp:
                {
                    bool ifExists = ReadBoolParam(opNode, "IfExists", false);
                    return new DropTableOpExpr(ifExists);
                }
            case NodeType.AlterColumnTypeOp:
                {
                    string? targetColumnName = ResolveColumnNameFromTargetOrParameter(
                        opNode,
                        targetPinName: "target_column",
                        legacyParameterName: "ColumnName");

                    Connection? newColumnWire = _graph.GetSingleInputConnection(opNode.Id, "new_column");
                    if (newColumnWire is null)
                        throw new InvalidOperationException("AlterColumnTypeOp requires a connected 'new_column' input.");

                    NodeInstance columnNode = _graph.NodeMap[newColumnWire.FromNodeId];
                    if (columnNode.Type != NodeType.ColumnDefinition)
                        throw new InvalidOperationException("AlterColumnTypeOp.new_column must come from ColumnDefinition.");

                    DdlColumnExpr colExpr = BuildColumnFromDefinitionNode(columnNode);

                    string effectiveTargetColumn = string.IsNullOrWhiteSpace(targetColumnName)
                        ? colExpr.ColumnName
                        : targetColumnName;

                    return new AlterColumnTypeOpExpr(effectiveTargetColumn, colExpr.DataType, colExpr.IsNullable);
                }
            default:
                throw new InvalidOperationException($"Unsupported alter operation node type: {opNode.Type}.");
        }
    }

    private string ResolveColumnNameFromTargetOrParameter(
        NodeInstance opNode,
        string targetPinName,
        string legacyParameterName)
    {
        Connection? targetWire = _graph.GetSingleInputConnection(opNode.Id, targetPinName);
        if (targetWire is not null)
        {
            NodeInstance targetNode = _graph.NodeMap[targetWire.FromNodeId];
            if (targetNode.Type != NodeType.ColumnDefinition)
                throw new InvalidOperationException($"{opNode.Type}.{targetPinName} must come from ColumnDefinition.");

            string wiredColumnName = ReadParam(targetNode, "ColumnName", "");
            if (string.IsNullOrWhiteSpace(wiredColumnName))
                throw new InvalidOperationException("ColumnDefinition requires ColumnName.");

            return wiredColumnName;
        }

        return ReadParam(opNode, legacyParameterName, "");
    }

    private string ResolveTextFromInputOrParameter(
        NodeInstance opNode,
        string inputPinName,
        string legacyParameterName)
    {
        string? fromInput = ResolveOptionalTextFromInputOrParameter(opNode, inputPinName, legacyParameterName);
        return fromInput ?? string.Empty;
    }

    private string? ResolveOptionalTextFromInputOrParameter(
        NodeInstance opNode,
        string inputPinName,
        string legacyParameterName)
    {
        Connection? textWire = _graph.GetSingleInputConnection(opNode.Id, inputPinName);
        if (textWire is null)
            return ReadParam(opNode, legacyParameterName, "");

        NodeInstance valueNode = _graph.NodeMap[textWire.FromNodeId];
        if (valueNode.Type != NodeType.ValueString)
            throw new InvalidOperationException($"{opNode.Type}.{inputPinName} must come from ValueString.");

        return ReadParam(valueNode, "value", "");
    }

    private DdlColumnExpr BuildColumnFromDefinitionNode(NodeInstance columnNode)
    {
        string columnName = ReadParam(columnNode, "ColumnName", "");
        if (string.IsNullOrWhiteSpace(columnName))
            throw new InvalidOperationException("ColumnDefinition requires ColumnName.");

        string dataType;
        bool useNativeType = ReadBoolParam(columnNode, "UseNativeType", false);
        if (useNativeType)
        {
            string nativeTypeExpression = ReadParam(columnNode, "NativeTypeExpression", "");
            if (string.IsNullOrWhiteSpace(nativeTypeExpression))
                throw new InvalidOperationException("ColumnDefinition with UseNativeType=true requires NativeTypeExpression.");

            dataType = nativeTypeExpression;
            AddWarning(
                "W-DDL-NATIVE-TYPE-PORTABILITY",
                "NativeTypeExpression pode reduzir portabilidade entre providers.",
                columnNode.Id
            );
        }
        else
        {
            Connection? typeWire = _graph.GetSingleInputConnection(columnNode.Id, "type_def");
            if (typeWire is not null)
            {
                NodeInstance typeNode = _graph.NodeMap[typeWire.FromNodeId];
                dataType = typeNode.Type switch
                {
                    NodeType.EnumTypeDefinition => ResolveEnumColumnDataType(BuildEnumTypeSpec(typeNode)),
                    NodeType.ScalarTypeDefinition => ResolveScalarColumnDataType(BuildScalarTypeSpec(typeNode)),
                    _ => throw new InvalidOperationException("ColumnDefinition.type_def must come from EnumTypeDefinition or ScalarTypeDefinition."),
                };
            }
            else
            {
                dataType = ReadParam(columnNode, "DataType", "INT");
            }
        }

        bool isNullable = ReadBoolParam(columnNode, "IsNullable", false);
        string? sequenceDefaultExpression = null;
        Connection? sequenceWire = _graph.GetSingleInputConnection(columnNode.Id, "sequence");
        if (sequenceWire is not null)
        {
            NodeInstance sequenceNode = _graph.NodeMap[sequenceWire.FromNodeId];
            if (sequenceNode.Type != NodeType.SequenceDefinition)
                throw new InvalidOperationException("ColumnDefinition.sequence must come from SequenceDefinition.");

            SequenceSpec sequenceSpec = BuildSequenceSpec(sequenceNode);
            sequenceDefaultExpression = ResolveSequenceDefaultExpression(sequenceSpec);
            if (string.IsNullOrWhiteSpace(sequenceDefaultExpression))
            {
                AddWarning(
                    "W-DDL-SEQUENCE-DEFAULT-UNSUPPORTED",
                    $"Provider {_provider} não suporta DEFAULT baseado em sequence nativa.",
                    columnNode.Id
                );
            }
        }

        string comment = ReadParam(columnNode, "Comment", "");
        return new DdlColumnExpr(columnName, dataType, isNullable, sequenceDefaultExpression, comment);
    }

    private Dictionary<string, DdlColumnExpr> BuildColumnsByNodeIdForTable(string tableNodeId)
    {
        var columnWires = _graph.Connections
            .Where(c => c.ToNodeId == tableNodeId && c.ToPinName == "column")
            .ToList();

        var columnsByNodeId = new Dictionary<string, DdlColumnExpr>(StringComparer.Ordinal);
        foreach (Connection wire in columnWires)
        {
            NodeInstance columnNode = _graph.NodeMap[wire.FromNodeId];
            if (columnNode.Type != NodeType.ColumnDefinition)
                continue;

            string columnName = ReadParam(columnNode, "ColumnName", "");
            if (string.IsNullOrWhiteSpace(columnName))
                continue;

            columnsByNodeId[columnNode.Id] = BuildColumnFromDefinitionNode(columnNode);
        }

        return columnsByNodeId;
    }

    private IReadOnlyList<string> ResolveConstraintColumnNames(
        string constraintNodeId,
        string inputPinName,
        IReadOnlyDictionary<string, DdlColumnExpr> columnsByNodeId
    )
    {
        return _graph.Connections
            .Where(c => c.ToNodeId == constraintNodeId && c.ToPinName == inputPinName)
            .Select(c => columnsByNodeId.TryGetValue(c.FromNodeId, out DdlColumnExpr? col) ? col.ColumnName : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ApplyDefaultConstraints(Dictionary<string, DdlColumnExpr> columnsByNodeId, string tableNodeId)
    {
        var defaultConstraintNodes = _graph.Connections
            .Where(c => c.ToNodeId == tableNodeId && c.ToPinName == "constraint")
            .Select(c => _graph.NodeMap[c.FromNodeId])
            .Where(n => n.Type == NodeType.DefaultConstraint)
            .DistinctBy(n => n.Id)
            .ToList();

        foreach (NodeInstance defaultNode in defaultConstraintNodes)
        {
            Connection? columnWire = _graph.GetSingleInputConnection(defaultNode.Id, "column");
            if (columnWire is null || !columnsByNodeId.TryGetValue(columnWire.FromNodeId, out DdlColumnExpr? columnExpr))
                continue;

            string defaultExpr = ReadParam(defaultNode, "DefaultValue", "");
            if (string.IsNullOrWhiteSpace(defaultExpr))
                continue;

            columnsByNodeId[columnWire.FromNodeId] = columnExpr with { DefaultExpression = defaultExpr };
        }
    }
}
