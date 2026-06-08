namespace AkkornStudio.Providers.Dialects;

using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.QueryEngine;

/// <summary>
/// Implementação de ISqlDialect para MySQL.
/// Usa INFORMATION_SCHEMA views compatível com MySQL 5.7+
/// </summary>
public sealed class MySqlDialect : ISqlDialect
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
                AND TABLE_SCHEMA NOT IN ('mysql', 'information_schema', 'performance_schema', 'sys')
            ORDER BY
                TABLE_SCHEMA, TABLE_NAME
        ";

    public string GetColumnsQuery() =>
        @"
            SELECT
                COLUMN_NAME,
                COLUMN_TYPE AS DATA_TYPE,
                CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IS_NULLABLE,
                CASE WHEN COLUMN_KEY = 'PRI' THEN 1 ELSE 0 END AS IS_PRIMARY_KEY
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
                INFORMATION_SCHEMA.COLUMNS
            WHERE
                TABLE_SCHEMA = @schema
                AND TABLE_NAME = @table
                AND COLUMN_KEY = 'PRI'
        ";

    public string GetForeignKeysQuery() =>
        @"
            SELECT
                COLUMN_NAME,
                REFERENCED_TABLE_NAME AS REFERENCED_TABLE,
                REFERENCED_COLUMN_NAME AS REFERENCED_COLUMN
            FROM
                INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE
                TABLE_SCHEMA = @schema
                AND TABLE_NAME = @table
                AND REFERENCED_TABLE_NAME IS NOT NULL
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
        if (!QueryHintSyntax.TryNormalize(DatabaseProvider.MySql, queryHints, out string hints, out _)
            || string.IsNullOrWhiteSpace(hints))
            return TrimTrailingSemicolon(sql);

        string baseSql = TrimTrailingSemicolon(sql);
        int selectIndex = baseSql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
        if (selectIndex < 0)
            return baseSql;

        int insertAt = selectIndex + 6;
        return baseSql.Insert(insertAt, $" /*+ {hints} */");
    }

    public string QuoteIdentifier(string identifier) =>
        $"`{identifier.Replace("`", "``")}`";

    public string EmitCreateTableColumn(
        string columnName,
        string dataType,
        bool isNullable,
        string? defaultExpression = null,
        string? columnComment = null
    )
    {
        string quotedName = QuoteIdentifier(columnName);
        string sqlType = NormalizeName(dataType, "data type");
        string nullability = isNullable ? "NULL" : "NOT NULL";
        string commentSql = string.IsNullOrWhiteSpace(columnComment)
            ? string.Empty
            : $" COMMENT {QuoteLiteral(columnComment)}";

        if (string.IsNullOrWhiteSpace(defaultExpression))
            return $"{quotedName} {sqlType} {nullability}{commentSql}";

        return $"{quotedName} {sqlType} {nullability} DEFAULT {defaultExpression.Trim()}{commentSql}";
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
        string? normalizedParentSchema = string.IsNullOrWhiteSpace(parentSchema) ? null : parentSchema.Trim();

        string constraintClause = string.IsNullOrWhiteSpace(constraintName)
            ? string.Empty
            : $"CONSTRAINT {QuoteIdentifier(constraintName.Trim())} ";

        string parentRef = string.IsNullOrWhiteSpace(normalizedParentSchema)
            ? QuoteIdentifier(normalizedParentTable)
            : $"{QuoteIdentifier(normalizedParentSchema)}.{QuoteIdentifier(normalizedParentTable)}";
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
        if (columnFragments.Count == 0)
            throw new InvalidOperationException("CREATE TABLE requires at least one column.");

        string schema = string.IsNullOrWhiteSpace(schemaName) ? "" : schemaName.Trim();
        string table = string.IsNullOrWhiteSpace(tableName)
            ? throw new InvalidOperationException("Table name is required.")
            : tableName.Trim();

        string qualifiedName = string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(table)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        string body = string.Join(",\n    ", columnFragments.Concat(constraintFragments));
        string tableCommentClause = string.IsNullOrWhiteSpace(tableComment)
            ? string.Empty
            : $" COMMENT={QuoteLiteral(tableComment)}";

        return $"CREATE TABLE {(ifNotExists ? "IF NOT EXISTS " : string.Empty)}{qualifiedName}\n(\n    {body}\n){tableCommentClause};";
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

        string schema = string.IsNullOrWhiteSpace(schemaName) ? "" : schemaName.Trim();
        string table = string.IsNullOrWhiteSpace(tableName)
            ? throw new InvalidOperationException("Table name is required for CREATE INDEX.")
            : tableName.Trim();
        string idx = string.IsNullOrWhiteSpace(indexName)
            ? throw new InvalidOperationException("Index name is required.")
            : indexName.Trim();

        string qualifiedTable = string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(table)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        string keyList = string.Join(", ", keyColumns.Select(k =>
            k.IsExpression
                ? $"({k.ExpressionSql!.Trim()})"
                : QuoteIdentifier(k.ColumnName!)));

        // MySQL does not support INCLUDE columns in CREATE INDEX.
        string commentHint = includeColumns.Count == 0
            ? string.Empty
            : $" /* INCLUDE columns ignored: {string.Join(", ", includeColumns.Select(QuoteIdentifier))} */";

        return
            $"CREATE {(isUnique ? "UNIQUE " : string.Empty)}INDEX {QuoteIdentifier(idx)} ON {qualifiedTable} ({keyList}){commentHint};";
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
        string schema = string.IsNullOrWhiteSpace(schemaName) ? string.Empty : schemaName.Trim();
        string view = NormalizeName(viewName, "view");
        string body = NormalizeName(selectSql, "SELECT").Trim().TrimEnd(';');
        string qualified = string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(view)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(view)}";

        if (!orReplace)
            return $"CREATE VIEW {qualified} AS\n{body};";

        return $"DROP VIEW IF EXISTS {qualified};\nCREATE VIEW {qualified} AS\n{body};";
    }

    public string EmitAlterView(
        string schemaName,
        string viewName,
        string selectSql
    )
    {
        string schema = string.IsNullOrWhiteSpace(schemaName) ? string.Empty : schemaName.Trim();
        string view = NormalizeName(viewName, "view");
        string body = NormalizeName(selectSql, "SELECT").Trim().TrimEnd(';');
        string qualified = string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(view)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(view)}";

        return $"DROP VIEW IF EXISTS {qualified};\nCREATE VIEW {qualified} AS\n{body};";
    }

    public string EmitAlterTableAddColumn(string schemaName, string tableName, string columnFragment)
    {
        string qualified = BuildQualifiedTable(schemaName, tableName);
        return $"ALTER TABLE {qualified} ADD COLUMN {columnFragment};";
    }

    public string EmitAlterTableDropColumn(string schemaName, string tableName, string columnName, bool ifExists)
    {
        string qualified = BuildQualifiedTable(schemaName, tableName);
        string column = QuoteIdentifier(NormalizeName(columnName, "column"));
        string ifExistsClause = ifExists ? "IF EXISTS " : string.Empty;
        return $"ALTER TABLE {qualified} DROP COLUMN {ifExistsClause}{column};";
    }

    public string EmitAlterTableRenameColumn(string schemaName, string tableName, string oldName, string newName)
    {
        string qualified = BuildQualifiedTable(schemaName, tableName);
        string oldCol = QuoteIdentifier(NormalizeName(oldName, "old column"));
        string newCol = QuoteIdentifier(NormalizeName(newName, "new column"));
        return $"ALTER TABLE {qualified} RENAME COLUMN {oldCol} TO {newCol};";
    }

    public string EmitAlterTableRenameTable(string schemaName, string tableName, string newName, string? newSchema)
    {
        string current = BuildQualifiedTable(schemaName, tableName);
        string targetSchema = string.IsNullOrWhiteSpace(newSchema) ? schemaName : newSchema;
        string target = BuildQualifiedTable(targetSchema, newName);
        return $"RENAME TABLE {current} TO {target};";
    }

    public string EmitAlterTableDropTable(string schemaName, string tableName, bool ifExists)
    {
        string qualified = BuildQualifiedTable(schemaName, tableName);
        return $"DROP TABLE {(ifExists ? "IF EXISTS " : string.Empty)}{qualified};";
    }

    public string EmitAlterTableAlterColumnType(
        string schemaName,
        string tableName,
        string columnName,
        string newDataType,
        bool isNullable
    )
    {
        string qualified = BuildQualifiedTable(schemaName, tableName);
        string column = QuoteIdentifier(NormalizeName(columnName, "column"));
        string dataType = NormalizeName(newDataType, "data type");
        string nullability = isNullable ? "NULL" : "NOT NULL";
        return $"ALTER TABLE {qualified} MODIFY COLUMN {column} {dataType} {nullability};";
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

    private string BuildQualifiedTable(string schemaName, string tableName)
    {
        string schema = string.IsNullOrWhiteSpace(schemaName) ? "" : schemaName.Trim();
        string table = NormalizeName(tableName, "table");
        return string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(table)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
    }

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

    private static string QuoteLiteral(string value)
        => $"'{value.Replace("'", "''")}'";
}
