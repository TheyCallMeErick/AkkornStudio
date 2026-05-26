namespace AkkornStudio.Providers.Dialects;

using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.QueryEngine;

/// <summary>
/// Implementação de ISqlDialect para SQL Server.
/// Usa INFORMATION_SCHEMA views compatível com SQL Server 2012+
/// </summary>
public sealed class SqlServerDialect : ISqlDialect
{
    public string GetTablesQuery() =>
        @"
            SELECT
                TABLE_SCHEMA,
                TABLE_NAME
            FROM
                INFORMATION_SCHEMA.TABLES
            WHERE
                TABLE_TYPE = 'BASE TABLE'
            ORDER BY
                TABLE_SCHEMA, TABLE_NAME
        ";

    public string GetColumnsQuery() =>
        @"
            SELECT
                c.COLUMN_NAME,
                c.DATA_TYPE,
                CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IS_NULLABLE,
                CASE
                    WHEN EXISTS
                    (
                        SELECT 1
                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                            ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                            AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                            AND tc.TABLE_NAME = kcu.TABLE_NAME
                        WHERE
                            tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                            AND kcu.TABLE_SCHEMA = c.TABLE_SCHEMA
                            AND kcu.TABLE_NAME = c.TABLE_NAME
                            AND kcu.COLUMN_NAME = c.COLUMN_NAME
                    )
                    THEN 1
                    ELSE 0
                END AS IS_PRIMARY_KEY
            FROM
                INFORMATION_SCHEMA.COLUMNS c
            WHERE
                c.TABLE_SCHEMA = @schema
                AND c.TABLE_NAME = @table
            ORDER BY
                c.ORDINAL_POSITION
        ";

    public string GetPrimaryKeysQuery() =>
        @"
            SELECT
                COLUMN_NAME
            FROM
                INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE
                TABLE_SCHEMA = @schema
                AND TABLE_NAME = @table
                AND CONSTRAINT_NAME LIKE 'PK%'
        ";

    public string GetForeignKeysQuery() =>
        @"
            SELECT
                KCU1.COLUMN_NAME,
                KCU2.TABLE_NAME AS REFERENCED_TABLE,
                KCU2.COLUMN_NAME AS REFERENCED_COLUMN
            FROM
                INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS RC
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE KCU1
                    ON RC.CONSTRAINT_NAME = KCU1.CONSTRAINT_NAME
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE KCU2
                    ON RC.UNIQUE_CONSTRAINT_NAME = KCU2.CONSTRAINT_NAME
            WHERE
                KCU1.TABLE_SCHEMA = @schema
                AND KCU1.TABLE_NAME = @table
        ";

    public string WrapWithPreviewLimit(string baseQuery, int maxRows) =>
        $"SELECT TOP {maxRows} * FROM (\n{TrimTrailingSemicolon(baseQuery)}\n) AS __preview";

    public string FormatPagination(int? limit, int? offset)
    {
        if (!offset.HasValue)
            return limit.HasValue ? $"OFFSET 0 ROWS FETCH NEXT {limit} ROWS ONLY" : "";

        if (limit.HasValue)
            return $"OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";

        return $"OFFSET {offset} ROWS FETCH NEXT {long.MaxValue} ROWS ONLY";
    }

    public string ApplyQueryHints(string sql, string? queryHints)
    {
        if (!QueryHintSyntax.TryNormalize(DatabaseProvider.SqlServer, queryHints, out string hints, out _)
            || string.IsNullOrWhiteSpace(hints))
            return TrimTrailingSemicolon(sql);

        string baseSql = TrimTrailingSemicolon(sql);
        if (baseSql.Contains(" OPTION (", StringComparison.OrdinalIgnoreCase))
            return baseSql;

        string normalized = hints.StartsWith("OPTION", StringComparison.OrdinalIgnoreCase)
            ? hints
            : $"OPTION ({hints})";

        return $"{baseSql}\n{normalized}";
    }

    public string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]")}]";

    public string EmitCreateTableColumn(
        string columnName,
        string dataType,
        bool isNullable,
        string? defaultExpression = null,
        string? columnComment = null
    )
    {
        _ = columnComment;
        string quotedName = QuoteIdentifier(columnName);
        string sqlType = NormalizeName(dataType, "data type");
        string nullability = isNullable ? "NULL" : "NOT NULL";

        if (string.IsNullOrWhiteSpace(defaultExpression))
            return $"{quotedName} {sqlType} {nullability}";

        return $"{quotedName} {sqlType} {nullability} DEFAULT {defaultExpression.Trim()}";
    }

    public string EmitPrimaryKeyConstraint(string? constraintName, IReadOnlyList<string> columns)
    {
        if (columns.Count == 0)
            throw new InvalidOperationException("PRIMARY KEY requires at least one column.");

        string columnList = string.Join(", ", columns.Select(QuoteIdentifier));
        string prefix = string.IsNullOrWhiteSpace(constraintName)
            ? "PRIMARY KEY"
            : $"CONSTRAINT {QuoteIdentifier(constraintName.Trim())} PRIMARY KEY";

        return $"{prefix} ({columnList})";
    }

    public string EmitUniqueConstraint(string? constraintName, IReadOnlyList<string> columns)
    {
        if (columns.Count == 0)
            throw new InvalidOperationException("UNIQUE constraint requires at least one column.");

        string columnList = string.Join(", ", columns.Select(QuoteIdentifier));
        string prefix = string.IsNullOrWhiteSpace(constraintName)
            ? "UNIQUE"
            : $"CONSTRAINT {QuoteIdentifier(constraintName.Trim())} UNIQUE";

        return $"{prefix} ({columnList})";
    }

    public string EmitCheckConstraint(string? constraintName, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new InvalidOperationException("CHECK constraint expression is required.");

        string prefix = string.IsNullOrWhiteSpace(constraintName)
            ? "CHECK"
            : $"CONSTRAINT {QuoteIdentifier(constraintName.Trim())} CHECK";

        return $"{prefix} ({expression.Trim()})";
    }

    public string EmitForeignKeyConstraint(
        string? constraintName,
        IReadOnlyList<string> childColumns,
        string parentSchema,
        string parentTable,
        IReadOnlyList<string> parentColumns,
        ReferentialAction onDelete,
        ReferentialAction onUpdate
    )
    {
        if (childColumns.Count == 0 || parentColumns.Count == 0 || childColumns.Count != parentColumns.Count)
            throw new InvalidOperationException("Foreign key requires child/parent columns with matching non-zero cardinality.");

        string[] normalizedChildColumns = NormalizeConstraintColumns(childColumns, "Foreign key child");
        string[] normalizedParentColumns = NormalizeConstraintColumns(parentColumns, "Foreign key parent");
        string normalizedParentTable = NormalizeName(parentTable, "foreign key parent table");
        string normalizedParentSchema = string.IsNullOrWhiteSpace(parentSchema) ? "dbo" : parentSchema.Trim();

        string constraintClause = string.IsNullOrWhiteSpace(constraintName)
            ? string.Empty
            : $"CONSTRAINT {QuoteIdentifier(constraintName.Trim())} ";
        string parentRef = $"{QuoteIdentifier(normalizedParentSchema)}.{QuoteIdentifier(normalizedParentTable)}";
        string childColumnsSql = string.Join(", ", normalizedChildColumns.Select(QuoteIdentifier));
        string parentColumnsSql = string.Join(", ", normalizedParentColumns.Select(QuoteIdentifier));

        return $"{constraintClause}FOREIGN KEY ({childColumnsSql}) REFERENCES {parentRef} ({parentColumnsSql}) ON DELETE {EmitReferentialAction(onDelete)} ON UPDATE {EmitReferentialAction(onUpdate)}";
    }

    public string EmitCreateTable(
        string schemaName,
        string tableName,
        bool ifNotExists,
        IReadOnlyList<string> columnFragments,
        IReadOnlyList<string> constraintFragments,
        string? tableComment = null
    )
    {
        _ = tableComment;
        if (columnFragments.Count == 0)
            throw new InvalidOperationException("CREATE TABLE requires at least one column.");

        string schema = string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName.Trim();
        string name = string.IsNullOrWhiteSpace(tableName)
            ? throw new InvalidOperationException("Table name is required.")
            : tableName.Trim();

        string qualifiedName = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(name)}";
        string body = string.Join(",\n    ", columnFragments.Concat(constraintFragments));
        string createSql = $"CREATE TABLE {qualifiedName}\n(\n    {body}\n);";

        if (!ifNotExists)
            return createSql;

        string tableObjectIdLiteral = BuildQualifiedNameLiteral(schema, name);
        return $"IF OBJECT_ID({tableObjectIdLiteral}, N'U') IS NULL\nBEGIN\n    {createSql.Replace("\n", "\n    ")}\nEND;";
    }

    public string? EmitTableComment(string schemaName, string tableName, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string escaped = EscapeSqlLiteral(comment);
        string escapedSchema = EscapeSqlLiteral(schema);
        string escapedTable = EscapeSqlLiteral(table);

        return
            "EXEC sys.sp_addextendedproperty @name=N'MS_Description', "
            + $"@value=N'{escaped}', "
            + "@level0type=N'Schema', "
            + $"@level0name=N'{escapedSchema}', "
            + "@level1type=N'Table', "
            + $"@level1name=N'{escapedTable}';";
    }

    public string? EmitColumnComment(string schemaName, string tableName, string columnName, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string column = NormalizeName(columnName, "column");
        string escaped = EscapeSqlLiteral(comment);
        string escapedSchema = EscapeSqlLiteral(schema);
        string escapedTable = EscapeSqlLiteral(table);
        string escapedColumn = EscapeSqlLiteral(column);

        return
            "EXEC sys.sp_addextendedproperty @name=N'MS_Description', "
            + $"@value=N'{escaped}', "
            + "@level0type=N'Schema', "
            + $"@level0name=N'{escapedSchema}', "
            + "@level1type=N'Table', "
            + $"@level1name=N'{escapedTable}', "
            + "@level2type=N'Column', "
            + $"@level2name=N'{escapedColumn}';";
    }

    public string EmitCreateIndex(
        string schemaName,
        string tableName,
        string indexName,
        bool isUnique,
        IReadOnlyList<Ddl.DdlIndexKeyExpr> keyColumns,
        IReadOnlyList<string> includeColumns,
        bool ifNotExists
    )
    {
        if (keyColumns.Count == 0)
            throw new InvalidOperationException("CREATE INDEX requires at least one key column.");
        if (keyColumns.Any(k => k.IsExpression))
        {
            throw new InvalidOperationException(
                "SQL Server CREATE INDEX does not support arbitrary expression keys. Use a persisted computed column and index that column."
            );
        }

        string schema = string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName.Trim();
        string table = string.IsNullOrWhiteSpace(tableName)
            ? throw new InvalidOperationException("Table name is required for CREATE INDEX.")
            : tableName.Trim();
        string idx = string.IsNullOrWhiteSpace(indexName)
            ? throw new InvalidOperationException("Index name is required.")
            : indexName.Trim();

        string qualifiedTable = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        string keyList = string.Join(", ", keyColumns.Select(k => QuoteIdentifier(k.ColumnName!)));
        string includeClause = includeColumns.Count == 0
            ? string.Empty
            : $" INCLUDE ({string.Join(", ", includeColumns.Select(QuoteIdentifier))})";

        string statement =
            $"CREATE {(isUnique ? "UNIQUE " : string.Empty)}INDEX {QuoteIdentifier(idx)} ON {qualifiedTable} ({keyList}){includeClause};";

        if (!ifNotExists)
            return statement;

        string escapedIndexName = EscapeSqlLiteral(idx);
        string tableObjectIdLiteral = BuildQualifiedNameLiteral(schema, table);
        return
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{escapedIndexName}' AND object_id = OBJECT_ID({tableObjectIdLiteral}))\nBEGIN\n    {statement}\nEND;";
    }

    public string EmitCreateView(
        string schemaName,
        string viewName,
        bool orReplace,
        bool isMaterialized,
        string selectSql
    )
    {
        _ = isMaterialized;
        string schema = NormalizeSchema(schemaName);
        string view = NormalizeName(viewName, "view");
        string body = NormalizeName(selectSql, "SELECT").Trim().TrimEnd(';');
        string qualified = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(view)}";

        if (!orReplace)
            return $"CREATE VIEW {qualified} AS\n{body};";

        string viewObjectIdLiteral = BuildQualifiedNameLiteral(schema, view);
        return $"IF OBJECT_ID({viewObjectIdLiteral}, N'V') IS NOT NULL DROP VIEW {qualified};\nCREATE VIEW {qualified} AS\n{body};";
    }

    public string EmitAlterView(
        string schemaName,
        string viewName,
        string selectSql
    )
    {
        string schema = NormalizeSchema(schemaName);
        string view = NormalizeName(viewName, "view");
        string body = NormalizeName(selectSql, "SELECT").Trim().TrimEnd(';');
        string qualified = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(view)}";
        return $"ALTER VIEW {qualified} AS\n{body};";
    }

    public string EmitAlterTableAddColumn(string schemaName, string tableName, string columnFragment)
    {
        string qualified = $"{QuoteIdentifier(NormalizeSchema(schemaName))}.{QuoteIdentifier(NormalizeName(tableName, "table"))}";
        return $"ALTER TABLE {qualified} ADD {columnFragment};";
    }

    public string EmitAlterTableDropColumn(string schemaName, string tableName, string columnName, bool ifExists)
    {
        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string column = NormalizeName(columnName, "column");
        string qualified = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        string drop = $"ALTER TABLE {qualified} DROP COLUMN {QuoteIdentifier(column)};";

        if (!ifExists)
            return drop;

        string escapedColumn = EscapeSqlLiteral(column);
        string tableObjectIdLiteral = BuildQualifiedNameLiteral(schema, table);
        return
            $"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'{escapedColumn}' AND Object_ID = Object_ID({tableObjectIdLiteral}))\nBEGIN\n    {drop}\nEND;";
    }

    public string EmitAlterTableRenameColumn(string schemaName, string tableName, string oldName, string newName)
    {
        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string oldColumn = NormalizeName(oldName, "old column");
        string newColumn = NormalizeName(newName, "new column");
        string oldColumnLiteral = BuildQualifiedNameLiteral(schema, table, oldColumn);
        string newColumnLiteral = EscapeSqlLiteral(newColumn);
        return $"EXEC sp_rename {oldColumnLiteral}, N'{newColumnLiteral}', 'COLUMN';";
    }

    public string EmitAlterTableRenameTable(string schemaName, string tableName, string newName, string? newSchema)
    {
        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string targetTable = NormalizeName(newName, "new table");
        string tableLiteral = BuildQualifiedNameLiteral(schema, table);
        string escapedTargetTable = EscapeSqlLiteral(targetTable);
        string renameSql = $"EXEC sp_rename {tableLiteral}, N'{escapedTargetTable}', 'OBJECT';";

        if (string.IsNullOrWhiteSpace(newSchema))
            return renameSql;

        string targetSchema = NormalizeSchema(newSchema);
        if (string.Equals(schema, targetSchema, StringComparison.OrdinalIgnoreCase))
            return renameSql;

        return $"{renameSql}\nALTER SCHEMA {QuoteIdentifier(targetSchema)} TRANSFER {QuoteIdentifier(schema)}.{QuoteIdentifier(targetTable)};";
    }

    public string EmitAlterTableDropTable(string schemaName, string tableName, bool ifExists)
    {
        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string qualified = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        if (!ifExists)
            return $"DROP TABLE {qualified};";

        string tableObjectIdLiteral = BuildQualifiedNameLiteral(schema, table);
        return $"IF OBJECT_ID({tableObjectIdLiteral}, N'U') IS NOT NULL DROP TABLE {qualified};";
    }

    public string EmitAlterTableAlterColumnType(
        string schemaName,
        string tableName,
        string columnName,
        string newDataType,
        bool isNullable
    )
    {
        string qualified = $"{QuoteIdentifier(NormalizeSchema(schemaName))}.{QuoteIdentifier(NormalizeName(tableName, "table"))}";
        string column = QuoteIdentifier(NormalizeName(columnName, "column"));
        string dataType = NormalizeName(newDataType, "data type");
        string nullability = isNullable ? "NULL" : "NOT NULL";
        return $"ALTER TABLE {qualified} ALTER COLUMN {column} {dataType} {nullability};";
    }

    public string EmitAlterTable(
        string schemaName,
        string tableName,
        IReadOnlyList<string> operationStatements,
        bool emitSeparateStatements
    )
    {
        _ = schemaName;
        _ = tableName;
        _ = emitSeparateStatements;
        return string.Join("\n", operationStatements.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string NormalizeSchema(string schemaName) =>
        string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName.Trim();

    private static string NormalizeName(string value, string label) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{label} is required.")
            : value.Trim();

    private static string[] NormalizeConstraintColumns(IReadOnlyList<string> columns, string label)
    {
        var normalized = new string[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            string value = columns[i];
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{label} columns must not contain blank names.");
            normalized[i] = value.Trim();
        }

        return normalized;
    }

    private static string EmitReferentialAction(ReferentialAction action) =>
        action switch
        {
            ReferentialAction.Cascade => "CASCADE",
            ReferentialAction.SetNull => "SET NULL",
            ReferentialAction.SetDefault => "SET DEFAULT",
            ReferentialAction.Restrict => "RESTRICT",
            _ => "NO ACTION",
        };

    private static string TrimTrailingSemicolon(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        return sql.Trim().TrimEnd(';').TrimEnd();
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

    private static string BuildQualifiedNameLiteral(params string[] parts) =>
        $"N'{EscapeSqlLiteral(string.Join('.', parts))}'";
}
