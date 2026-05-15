namespace AkkornStudio.Providers.Dialects;

/// <summary>
/// SQLite implementation of ISqlDialect.
/// SQLite uses sqlite_master system table for schema discovery and PRAGMA statements for metadata.
/// </summary>
public sealed class SqliteDialect : ISqlDialect
{
    public string GetTablesQuery() =>
        @"
            SELECT 'main' as TABLE_SCHEMA, name as TABLE_NAME
            FROM sqlite_master
            WHERE type='table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name
        ";

    public string GetColumnsQuery() =>
        @"
            SELECT
                name as COLUMN_NAME,
                type as DATA_TYPE,
                CASE WHEN notnull = 0 THEN 1 ELSE 0 END AS IS_NULLABLE,
                CASE WHEN pk > 0 THEN 1 ELSE 0 END AS IS_PRIMARY_KEY
            FROM pragma_table_info(@table)
            ORDER BY cid
        ";

    public string GetPrimaryKeysQuery() =>
        @"
            SELECT name as COLUMN_NAME
            FROM pragma_table_info(@table)
            WHERE pk > 0
            ORDER BY pk
        ";

    public string GetForeignKeysQuery() =>
        @"
            SELECT
                'id' as id,
                table as REFERENCED_TABLE,
                to as REFERENCED_COLUMN
            FROM pragma_foreign_key_list(@table)
        ";

    public string WrapWithPreviewLimit(string baseQuery, int maxRows)
    {
        return $"SELECT * FROM (\n{TrimTrailingSemicolon(baseQuery)}\n) AS __preview LIMIT {maxRows}";
    }

    public string FormatPagination(int? limit, int? offset)
    {
        var parts = new List<string>();
        if (limit.HasValue)
            parts.Add($"LIMIT {limit.Value}");
        if (offset.HasValue && offset.Value > 0)
            parts.Add($"OFFSET {offset.Value}");
        return string.Join(" ", parts);
    }

    public string ApplyQueryHints(string sql, string? queryHints)
    {
        _ = queryHints;
        return TrimTrailingSemicolon(sql);
    }

    public string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";

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
        string sqlType = string.IsNullOrWhiteSpace(dataType) ? "INTEGER" : dataType.Trim();
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
            throw new InvalidOperationException("UNIQUE requires at least one column.");

        string columnList = string.Join(", ", columns.Select(QuoteIdentifier));
        string prefix = string.IsNullOrWhiteSpace(constraintName)
            ? "UNIQUE"
            : $"CONSTRAINT {QuoteIdentifier(constraintName.Trim())} UNIQUE";

        return $"{prefix} ({columnList})";
    }

    public string EmitCheckConstraint(string? constraintName, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new InvalidOperationException("CHECK expression is required.");

        string prefix = string.IsNullOrWhiteSpace(constraintName)
            ? "CHECK"
            : $"CONSTRAINT {QuoteIdentifier(constraintName.Trim())} CHECK";

        return $"{prefix} ({expression.Trim()})";
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

        _ = schemaName; // SQLite does not support schemas in the same way as server RDBMS.
        string table = string.IsNullOrWhiteSpace(tableName)
            ? throw new InvalidOperationException("Table name is required.")
            : tableName.Trim();

        string body = string.Join(",\n    ", columnFragments.Concat(constraintFragments));
        return $"CREATE TABLE {(ifNotExists ? "IF NOT EXISTS " : string.Empty)}{QuoteIdentifier(table)}\n(\n    {body}\n);";
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

        _ = schemaName;
        string table = string.IsNullOrWhiteSpace(tableName)
            ? throw new InvalidOperationException("Table name is required for CREATE INDEX.")
            : tableName.Trim();
        string idx = string.IsNullOrWhiteSpace(indexName)
            ? throw new InvalidOperationException("Index name is required.")
            : indexName.Trim();

        string keyList = string.Join(", ", keyColumns.Select(k =>
            k.IsExpression
                ? $"({k.ExpressionSql!.Trim()})"
                : QuoteIdentifier(k.ColumnName!)));
        string includeComment = includeColumns.Count == 0
            ? string.Empty
            : $" -- INCLUDE columns ignored: {string.Join(", ", includeColumns.Select(QuoteIdentifier))}";

        return
            $"CREATE {(isUnique ? "UNIQUE " : string.Empty)}INDEX {(ifNotExists ? "IF NOT EXISTS " : string.Empty)}{QuoteIdentifier(idx)} ON {QuoteIdentifier(table)} ({keyList});{includeComment}";
    }

    public string EmitCreateView(
        string schemaName,
        string viewName,
        bool orReplace,
        bool isMaterialized,
        string selectSql
    )
    {
        _ = schemaName;
        _ = isMaterialized;
        string view = NormalizeName(viewName, "view");
        string body = NormalizeName(selectSql, "SELECT").Trim().TrimEnd(';');

        if (!orReplace)
            return $"CREATE VIEW {QuoteIdentifier(view)} AS\n{body};";

        return $"DROP VIEW IF EXISTS {QuoteIdentifier(view)};\nCREATE VIEW {QuoteIdentifier(view)} AS\n{body};";
    }

    public string EmitAlterView(
        string schemaName,
        string viewName,
        string selectSql
    )
    {
        _ = schemaName;
        string view = NormalizeName(viewName, "view");
        string body = NormalizeName(selectSql, "SELECT").Trim().TrimEnd(';');
        return $"DROP VIEW IF EXISTS {QuoteIdentifier(view)};\nCREATE VIEW {QuoteIdentifier(view)} AS\n{body};";
    }

    public string EmitAlterTableAddColumn(string schemaName, string tableName, string columnFragment)
    {
        _ = schemaName;
        string table = NormalizeName(tableName, "table");
        return $"ALTER TABLE {QuoteIdentifier(table)} ADD COLUMN {columnFragment};";
    }

    public string EmitAlterTableDropColumn(string schemaName, string tableName, string columnName, bool ifExists)
    {
        _ = schemaName;
        _ = ifExists;
        string table = NormalizeName(tableName, "table");
        string col = QuoteIdentifier(NormalizeName(columnName, "column"));
        return $"ALTER TABLE {QuoteIdentifier(table)} DROP COLUMN {col};";
    }

    public string EmitAlterTableRenameColumn(string schemaName, string tableName, string oldName, string newName)
    {
        _ = schemaName;
        string table = NormalizeName(tableName, "table");
        string oldCol = QuoteIdentifier(NormalizeName(oldName, "old column"));
        string newCol = QuoteIdentifier(NormalizeName(newName, "new column"));
        return $"ALTER TABLE {QuoteIdentifier(table)} RENAME COLUMN {oldCol} TO {newCol};";
    }

    public string EmitAlterTableRenameTable(string schemaName, string tableName, string newName, string? newSchema)
    {
        _ = schemaName;
        _ = newSchema;
        string table = NormalizeName(tableName, "table");
        string target = NormalizeName(newName, "new table");
        return $"ALTER TABLE {QuoteIdentifier(table)} RENAME TO {QuoteIdentifier(target)};";
    }

    public string EmitAlterTableDropTable(string schemaName, string tableName, bool ifExists)
    {
        _ = schemaName;
        string table = NormalizeName(tableName, "table");
        return $"DROP TABLE {(ifExists ? "IF EXISTS " : string.Empty)}{QuoteIdentifier(table)};";
    }

    public string EmitAlterTableAlterColumnType(
        string schemaName,
        string tableName,
        string columnName,
        string newDataType,
        bool isNullable
    )
    {
        _ = schemaName;
        _ = tableName;
        _ = columnName;
        _ = newDataType;
        _ = isNullable;
        return "-- SQLite does not support ALTER COLUMN TYPE directly; table rebuild is required.";
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

    private static string NormalizeName(string value, string label) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{label} is required.")
            : value.Trim();

    private static string TrimTrailingSemicolon(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        return sql.Trim().TrimEnd(';').TrimEnd();
    }
}
