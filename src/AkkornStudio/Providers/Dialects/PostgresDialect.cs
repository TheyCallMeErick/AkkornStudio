namespace AkkornStudio.Providers.Dialects;

using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.QueryEngine;

/// <summary>
/// Implementação de ISqlDialect para PostgreSQL.
/// Usa Information Schema views compatível com PostgreSQL 9.6+
/// </summary>
public sealed class PostgresDialect : ISqlDialect
{
    public string GetTablesQuery() =>
        @"
            SELECT
                table_schema,
                table_name
            FROM
                information_schema.tables
            WHERE
                table_type = 'BASE TABLE'
                AND table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY
                table_schema, table_name
        ";

    public string GetColumnsQuery() =>
        @"
            SELECT
                column_name,
                udt_name AS data_type,
                is_nullable::boolean,
                CASE
                    WHEN column_name IN (
                        SELECT a.attname
                        FROM pg_index i
                        JOIN pg_attribute a ON a.attrelid = i.indrelid
                            AND a.attnum = ANY(i.indkey)
                        WHERE i.indisprimary
                            AND i.indrelid = (@schema || '.' || @table)::regclass
                    ) THEN true
                    ELSE false
                END AS is_primary_key
            FROM
                information_schema.columns
            WHERE
                table_schema = @schema
                AND table_name = @table
            ORDER BY
                ordinal_position
        ";

    public string GetPrimaryKeysQuery() =>
        @"
            SELECT
                a.attname AS column_name
            FROM
                pg_index i
                JOIN pg_attribute a ON a.attrelid = i.indrelid
                    AND a.attnum = ANY(i.indkey)
            WHERE
                i.indisprimary
                AND i.indrelid = (@schema || '.' || @table)::regclass
        ";

    public string GetForeignKeysQuery() =>
        @"
            SELECT
                kcu.column_name,
                ccu.table_name AS referenced_table,
                ccu.column_name AS referenced_column
            FROM
                information_schema.table_constraints AS tc
                JOIN information_schema.key_column_usage AS kcu
                    ON tc.constraint_name = kcu.constraint_name
                    AND tc.table_schema = kcu.table_schema
                JOIN information_schema.constraint_column_usage AS ccu
                    ON ccu.constraint_name = tc.constraint_name
                    AND ccu.table_schema = tc.table_schema
            WHERE
                tc.constraint_type = 'FOREIGN KEY'
                AND tc.table_schema = @schema
                AND tc.table_name = @table
        ";

    public string WrapWithPreviewLimit(string baseQuery, int maxRows) =>
        $"{TrimTrailingSemicolon(baseQuery)}\nLIMIT {maxRows}";

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
        if (!QueryHintSyntax.TryNormalize(DatabaseProvider.Postgres, queryHints, out string hints, out _)
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

        string parentRef = normalizedParentSchema is null
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
        _ = tableComment;
        if (columnFragments.Count == 0)
            throw new InvalidOperationException("CREATE TABLE requires at least one column.");

        string schema = string.IsNullOrWhiteSpace(schemaName) ? "public" : schemaName.Trim();
        string table = string.IsNullOrWhiteSpace(tableName)
            ? throw new InvalidOperationException("Table name is required.")
            : tableName.Trim();

        string qualifiedName = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        string body = string.Join(",\n    ", columnFragments.Concat(constraintFragments));
        return $"CREATE TABLE {(ifNotExists ? "IF NOT EXISTS " : string.Empty)}{qualifiedName}\n(\n    {body}\n);";
    }

    public string? EmitTableComment(string schemaName, string tableName, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        string qualified = $"{QuoteIdentifier(NormalizeSchema(schemaName))}.{QuoteIdentifier(NormalizeName(tableName, "table"))}";
        return $"COMMENT ON TABLE {qualified} IS {QuoteLiteral(comment)};";
    }

    public string? EmitColumnComment(string schemaName, string tableName, string columnName, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        string qualified = $"{QuoteIdentifier(NormalizeSchema(schemaName))}.{QuoteIdentifier(NormalizeName(tableName, "table"))}";
        string col = QuoteIdentifier(NormalizeName(columnName, "column"));
        return $"COMMENT ON COLUMN {qualified}.{col} IS {QuoteLiteral(comment)};";
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

        string schema = string.IsNullOrWhiteSpace(schemaName) ? "public" : schemaName.Trim();
        string table = string.IsNullOrWhiteSpace(tableName)
            ? throw new InvalidOperationException("Table name is required for CREATE INDEX.")
            : tableName.Trim();
        string idx = string.IsNullOrWhiteSpace(indexName)
            ? throw new InvalidOperationException("Index name is required.")
            : indexName.Trim();

        string qualifiedTable = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        string keyList = string.Join(", ", keyColumns.Select(k =>
            k.IsExpression
                ? $"({k.ExpressionSql!.Trim()})"
                : QuoteIdentifier(k.ColumnName!)));
        string includeClause = includeColumns.Count == 0
            ? string.Empty
            : $" INCLUDE ({string.Join(", ", includeColumns.Select(QuoteIdentifier))})";

        return
            $"CREATE {(isUnique ? "UNIQUE " : string.Empty)}INDEX {(ifNotExists ? "IF NOT EXISTS " : string.Empty)}{QuoteIdentifier(idx)} ON {qualifiedTable} ({keyList}){includeClause};";
    }

    public string EmitCreateView(
        string schemaName,
        string viewName,
        bool orReplace,
        bool isMaterialized,
        string selectSql
    )
    {
        string schema = string.IsNullOrWhiteSpace(schemaName) ? "public" : schemaName.Trim();
        string view = NormalizeName(viewName, "view");
        string body = NormalizeName(selectSql, "SELECT").Trim().TrimEnd(';');

        string createKeyword = isMaterialized ? "CREATE MATERIALIZED VIEW" : "CREATE VIEW";
        if (orReplace && !isMaterialized)
            createKeyword = "CREATE OR REPLACE VIEW";

        string qualified = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(view)}";
        return $"{createKeyword} {qualified} AS\n{body};";
    }

    public string EmitAlterView(
        string schemaName,
        string viewName,
        string selectSql
    )
    {
        string schema = string.IsNullOrWhiteSpace(schemaName) ? "public" : schemaName.Trim();
        string view = NormalizeName(viewName, "view");
        string body = NormalizeName(selectSql, "SELECT").Trim().TrimEnd(';');
        string qualified = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(view)}";
        return $"CREATE OR REPLACE VIEW {qualified} AS\n{body};";
    }

    public string EmitAlterTableAddColumn(string schemaName, string tableName, string columnFragment)
    {
        string qualified = $"{QuoteIdentifier(NormalizeSchema(schemaName))}.{QuoteIdentifier(NormalizeName(tableName, "table"))}";
        return $"ALTER TABLE {qualified} ADD COLUMN {columnFragment};";
    }

    public string EmitAlterTableDropColumn(string schemaName, string tableName, string columnName, bool ifExists)
    {
        string qualified = $"{QuoteIdentifier(NormalizeSchema(schemaName))}.{QuoteIdentifier(NormalizeName(tableName, "table"))}";
        string col = QuoteIdentifier(NormalizeName(columnName, "column"));
        return $"ALTER TABLE {qualified} DROP COLUMN {(ifExists ? "IF EXISTS " : string.Empty)}{col};";
    }

    public string EmitAlterTableRenameColumn(string schemaName, string tableName, string oldName, string newName)
    {
        string qualified = $"{QuoteIdentifier(NormalizeSchema(schemaName))}.{QuoteIdentifier(NormalizeName(tableName, "table"))}";
        string oldCol = QuoteIdentifier(NormalizeName(oldName, "old column"));
        string newCol = QuoteIdentifier(NormalizeName(newName, "new column"));
        return $"ALTER TABLE {qualified} RENAME COLUMN {oldCol} TO {newCol};";
    }

    public string EmitAlterTableRenameTable(string schemaName, string tableName, string newName, string? newSchema)
    {
        string oldSchema = NormalizeSchema(schemaName);
        string oldTable = NormalizeName(tableName, "table");
        string targetTable = NormalizeName(newName, "new table");

        string qualifiedCurrent = $"{QuoteIdentifier(oldSchema)}.{QuoteIdentifier(oldTable)}";
        string renameSql = $"ALTER TABLE {qualifiedCurrent} RENAME TO {QuoteIdentifier(targetTable)};";

        if (string.IsNullOrWhiteSpace(newSchema))
            return renameSql;

        string targetSchema = NormalizeSchema(newSchema);
        if (string.Equals(oldSchema, targetSchema, StringComparison.OrdinalIgnoreCase))
            return renameSql;

        string qualifiedRenamed = $"{QuoteIdentifier(oldSchema)}.{QuoteIdentifier(targetTable)}";
        string moveSql = $"ALTER TABLE {qualifiedRenamed} SET SCHEMA {QuoteIdentifier(targetSchema)};";
        return $"{renameSql}\n{moveSql}";
    }

    public string EmitAlterTableDropTable(string schemaName, string tableName, bool ifExists)
    {
        string qualified = $"{QuoteIdentifier(NormalizeSchema(schemaName))}.{QuoteIdentifier(NormalizeName(tableName, "table"))}";
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
        string qualified = $"{QuoteIdentifier(NormalizeSchema(schemaName))}.{QuoteIdentifier(NormalizeName(tableName, "table"))}";
        string col = QuoteIdentifier(NormalizeName(columnName, "column"));
        string dataType = NormalizeName(newDataType, "data type");
        string setNullability = isNullable
            ? $"ALTER TABLE {qualified} ALTER COLUMN {col} DROP NOT NULL;"
            : $"ALTER TABLE {qualified} ALTER COLUMN {col} SET NOT NULL;";

        return $"ALTER TABLE {qualified} ALTER COLUMN {col} TYPE {dataType};\n{setNullability}";
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
        string.IsNullOrWhiteSpace(schemaName) ? "public" : schemaName.Trim();

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
