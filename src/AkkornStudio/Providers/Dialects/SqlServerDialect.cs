namespace AkkornStudio.Providers.Dialects;

using AkkornStudio.Core;
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
                COLUMN_NAME,
                DATA_TYPE,
                CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IS_NULLABLE,
                CASE
                    WHEN COLUMNPROPERTY(OBJECT_ID(TABLE_SCHEMA + '.' + TABLE_NAME), COLUMN_NAME, 'IsIdentity') = 1
                    THEN 1
                    ELSE 0
                END AS IS_PRIMARY_KEY
            FROM
                INFORMATION_SCHEMA.COLUMNS
            WHERE
                TABLE_SCHEMA = @schema
                AND TABLE_NAME = @table
            ORDER BY
                ORDINAL_POSITION
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

        var parts = new List<string> { $"OFFSET {offset} ROWS" };
        if (limit.HasValue)
            parts.Add($"FETCH NEXT {limit} ROWS ONLY");

        return string.Join(" ", parts);
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
        string sqlType = string.IsNullOrWhiteSpace(dataType) ? "INT" : dataType.Trim();
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

        return $"IF OBJECT_ID(N'{schema}.{name}', N'U') IS NULL\nBEGIN\n    {createSql.Replace("\n", "\n    ")}\nEND;";
    }

    public string? EmitTableComment(string schemaName, string tableName, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string escaped = EscapeSqlLiteral(comment);

        return
            "EXEC sys.sp_addextendedproperty @name=N'MS_Description', "
            + $"@value=N'{escaped}', "
            + "@level0type=N'Schema', "
            + $"@level0name=N'{schema}', "
            + "@level1type=N'Table', "
            + $"@level1name=N'{table}';";
    }

    public string? EmitColumnComment(string schemaName, string tableName, string columnName, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string column = NormalizeName(columnName, "column");
        string escaped = EscapeSqlLiteral(comment);

        return
            "EXEC sys.sp_addextendedproperty @name=N'MS_Description', "
            + $"@value=N'{escaped}', "
            + "@level0type=N'Schema', "
            + $"@level0name=N'{schema}', "
            + "@level1type=N'Table', "
            + $"@level1name=N'{table}', "
            + "@level2type=N'Column', "
            + $"@level2name=N'{column}';";
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

        string schema = string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName.Trim();
        string table = string.IsNullOrWhiteSpace(tableName)
            ? throw new InvalidOperationException("Table name is required for CREATE INDEX.")
            : tableName.Trim();
        string idx = string.IsNullOrWhiteSpace(indexName)
            ? throw new InvalidOperationException("Index name is required.")
            : indexName.Trim();

        string qualifiedTable = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        string keyList = string.Join(", ", keyColumns
            .Where(k => !k.IsExpression)
            .Select(k => QuoteIdentifier(k.ColumnName!)));
        string includeClause = includeColumns.Count == 0
            ? string.Empty
            : $" INCLUDE ({string.Join(", ", includeColumns.Select(QuoteIdentifier))})";

        string statement =
            $"CREATE {(isUnique ? "UNIQUE " : string.Empty)}INDEX {QuoteIdentifier(idx)} ON {qualifiedTable} ({keyList}){includeClause};";

        if (!ifNotExists)
            return statement;

        return
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{idx}' AND object_id = OBJECT_ID(N'{schema}.{table}'))\nBEGIN\n    {statement}\nEND;";
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

        return $"IF OBJECT_ID(N'{schema}.{view}', N'V') IS NOT NULL DROP VIEW {qualified};\nCREATE VIEW {qualified} AS\n{body};";
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

        return
            $"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'{column}' AND Object_ID = Object_ID(N'{schema}.{table}'))\nBEGIN\n    {drop}\nEND;";
    }

    public string EmitAlterTableRenameColumn(string schemaName, string tableName, string oldName, string newName)
    {
        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string oldColumn = NormalizeName(oldName, "old column");
        string newColumn = NormalizeName(newName, "new column");
        return $"EXEC sp_rename N'{schema}.{table}.{oldColumn}', N'{newColumn}', 'COLUMN';";
    }

    public string EmitAlterTableRenameTable(string schemaName, string tableName, string newName, string? newSchema)
    {
        string schema = NormalizeSchema(schemaName);
        string table = NormalizeName(tableName, "table");
        string targetTable = NormalizeName(newName, "new table");
        string renameSql = $"EXEC sp_rename N'{schema}.{table}', N'{targetTable}', 'OBJECT';";

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

        return $"IF OBJECT_ID(N'{schema}.{table}', N'U') IS NOT NULL DROP TABLE {qualified};";
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

    private static string TrimTrailingSemicolon(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        return sql.Trim().TrimEnd(';').TrimEnd();
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''");
}
