using System.Linq;
using AkkornStudio.Core;
using AkkornStudio.Metadata;

namespace AkkornStudio.Ddl;

/// <summary>
/// Contract for DDL expression nodes that can emit provider-specific SQL.
/// </summary>
public interface IDdlExpression
{
    IReadOnlySet<DatabaseProvider>? SupportedProviders => null;
    string Emit(DdlEmitContext context);
}

public enum DdlIdempotentMode
{
    None,
    IfNotExists,
    DropAndCreate,
}

public enum DdlDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record DdlCompileWarning(string Code, string Message);

public sealed record DdlCompileDiagnostic(
    string Code,
    DdlDiagnosticSeverity Severity,
    string Message,
    string? NodeId = null
);

public sealed record DdlCompileResult(
    IReadOnlyList<IDdlExpression> Statements,
    IReadOnlyList<DdlCompileDiagnostic> Diagnostics
)
{
    public IReadOnlyList<DdlCompileWarning> Warnings =>
    [
        .. Diagnostics
            .Where(d => d.Severity == DdlDiagnosticSeverity.Warning)
            .Select(d => new DdlCompileWarning(d.Code, d.Message)),
    ];

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DdlDiagnosticSeverity.Error);
}

public sealed record DdlColumnExpr(
    string ColumnName,
    string DataType,
    bool IsNullable,
    string? DefaultExpression = null,
    string? Comment = null
);

public sealed record DdlPrimaryKeyExpr(string? ConstraintName, IReadOnlyList<string> Columns);

public sealed record DdlUniqueExpr(string? ConstraintName, IReadOnlyList<string> Columns);

public sealed record DdlCheckExpr(string? ConstraintName, string Expression);

/// <summary>
/// Represents a foreign key constraint that can be emitted inline in CREATE TABLE.
/// Supports single and composite key shapes.
/// </summary>
public sealed record DdlForeignKeyExpr(
    string? ConstraintName,
    IReadOnlyList<string> ChildColumns,
    string ParentSchema,
    string ParentTable,
    IReadOnlyList<string> ParentColumns,
    ReferentialAction OnDelete,
    ReferentialAction OnUpdate
) : IDdlExpression
{
    public string Emit(DdlEmitContext context)
    {
        if (ChildColumns.Count == 0 || ParentColumns.Count == 0 || ChildColumns.Count != ParentColumns.Count)
            throw new InvalidOperationException("Foreign key requires child/parent columns with matching non-zero cardinality.");

        var constraintClause = string.IsNullOrWhiteSpace(ConstraintName)
            ? string.Empty
            : $"CONSTRAINT {ConstraintName} ";

        var parentRef = string.IsNullOrWhiteSpace(ParentSchema)
            ? ParentTable
            : $"{ParentSchema}.{ParentTable}";

        var onDeleteClause = $" ON DELETE {EmitAction(OnDelete)}";
        var onUpdateClause = context.Provider == DatabaseProvider.SQLite
            ? string.Empty
            : $" ON UPDATE {EmitAction(OnUpdate)}";

        string childColumnsSql = string.Join(", ", ChildColumns.Select(column => context.Dialect.QuoteIdentifier(column.Trim())));
        string parentColumnsSql = string.Join(", ", ParentColumns.Select(column => context.Dialect.QuoteIdentifier(column.Trim())));
        return $"{constraintClause}FOREIGN KEY ({childColumnsSql}) REFERENCES {parentRef} ({parentColumnsSql}){onDeleteClause}{onUpdateClause}";
    }

    private static string EmitAction(ReferentialAction action) =>
        action switch
        {
            ReferentialAction.Cascade => "CASCADE",
            ReferentialAction.SetNull => "SET NULL",
            ReferentialAction.SetDefault => "SET DEFAULT",
            ReferentialAction.Restrict => "RESTRICT",
            _ => "NO ACTION",
        };
}

public sealed class CreateEnumTypeExpr(
    string schemaName,
    string typeName,
    IReadOnlyList<string> values,
    DdlIdempotentMode mode = DdlIdempotentMode.None
) : IDdlExpression
{
    private static readonly IReadOnlySet<DatabaseProvider> _supportedProviders =
        new HashSet<DatabaseProvider> { DatabaseProvider.Postgres };

    public string SchemaName { get; } = schemaName;
    public string TypeName { get; } = typeName;
    public IReadOnlyList<string> Values { get; } = values;
    public DdlIdempotentMode Mode { get; } = mode;
    public IReadOnlySet<DatabaseProvider>? SupportedProviders => _supportedProviders;

    public string Emit(DdlEmitContext context)
    {
        if (Values.Count == 0)
            throw new InvalidOperationException("CREATE TYPE ENUM requires at least one value.");

        string schema = string.IsNullOrWhiteSpace(SchemaName) ? "public" : SchemaName.Trim();
        string type = TypeName.Trim();

        string qualifiedType = $"{context.Dialect.QuoteIdentifier(schema)}.{context.Dialect.QuoteIdentifier(type)}";
        string valueList = string.Join(", ", Values.Select(SqlStringUtility.QuoteLiteral));

        string createSql = Mode switch
        {
            DdlIdempotentMode.IfNotExists =>
                $"CREATE TYPE IF NOT EXISTS {qualifiedType} AS ENUM ({valueList});",
            _ => $"CREATE TYPE {qualifiedType} AS ENUM ({valueList});",
        };

        if (Mode == DdlIdempotentMode.DropAndCreate)
            return $"DROP TYPE IF EXISTS {qualifiedType};\n{createSql}";

        return createSql;
    }
}

public sealed class CreateIndexExpr(
    string schemaName,
    string tableName,
    string indexName,
    bool isUnique,
    IReadOnlyList<DdlIndexKeyExpr> keyColumns,
    IReadOnlyList<string> includeColumns,
    bool ifNotExists
) : IDdlExpression
{
    public string SchemaName { get; } = schemaName;
    public string TableName { get; } = tableName;
    public string IndexName { get; } = indexName;
    public bool IsUnique { get; } = isUnique;
    public IReadOnlyList<DdlIndexKeyExpr> KeyColumns { get; } = keyColumns;
    public IReadOnlyList<string> IncludeColumns { get; } = includeColumns;
    public bool IfNotExists { get; } = ifNotExists;

    public string Emit(DdlEmitContext context)
    {
        return context.Dialect.EmitCreateIndex(
            SchemaName,
            TableName,
            IndexName,
            IsUnique,
            KeyColumns,
            IncludeColumns,
            IfNotExists
        );
    }
}

public sealed record DdlIndexKeyExpr(string? ColumnName = null, string? ExpressionSql = null)
{
    public bool IsExpression => !string.IsNullOrWhiteSpace(ExpressionSql);
}

public sealed class CreateSequenceExpr(
    string schemaName,
    string sequenceName,
    long? startValue,
    long? increment,
    long? minValue,
    long? maxValue,
    bool cycle,
    int? cache,
    DdlIdempotentMode mode = DdlIdempotentMode.None
) : IDdlExpression
{
    public string SchemaName { get; } = schemaName;
    public string SequenceName { get; } = sequenceName;
    public long? StartValue { get; } = startValue;
    public long? Increment { get; } = increment;
    public long? MinValue { get; } = minValue;
    public long? MaxValue { get; } = maxValue;
    public bool Cycle { get; } = cycle;
    public int? Cache { get; } = cache;
    public DdlIdempotentMode Mode { get; } = mode;

    public string Emit(DdlEmitContext context)
    {
        string schema = string.IsNullOrWhiteSpace(SchemaName)
            ? (context.Provider == Core.DatabaseProvider.SqlServer ? "dbo" : "public")
            : SchemaName.Trim();
        string seq = SequenceName.Trim();
        string qualified = $"{context.Dialect.QuoteIdentifier(schema)}.{context.Dialect.QuoteIdentifier(seq)}";

        if (context.Provider is not Core.DatabaseProvider.Postgres and not Core.DatabaseProvider.SqlServer)
            return string.Empty;

        var parts = new List<string>();
        if (StartValue.HasValue)
            parts.Add($"START WITH {StartValue.Value}");
        if (Increment.HasValue)
            parts.Add($"INCREMENT BY {Increment.Value}");
        if (MinValue.HasValue)
            parts.Add($"MINVALUE {MinValue.Value}");
        if (MaxValue.HasValue)
            parts.Add($"MAXVALUE {MaxValue.Value}");
        if (Cache.HasValue)
            parts.Add($"CACHE {Cache.Value}");
        parts.Add(Cycle ? "CYCLE" : "NO CYCLE");

        string createKeyword = context.Provider == Core.DatabaseProvider.Postgres && Mode == DdlIdempotentMode.IfNotExists
            ? "CREATE SEQUENCE IF NOT EXISTS"
            : "CREATE SEQUENCE";

        string createSql = $"{createKeyword} {qualified} {(parts.Count > 0 ? string.Join(" ", parts) : string.Empty)};".Replace("  ", " ");

        if (Mode != DdlIdempotentMode.DropAndCreate)
            return createSql;

        string dropSql = context.Provider == Core.DatabaseProvider.SqlServer
            ? $"IF OBJECT_ID(N'{schema}.{seq}', N'SO') IS NOT NULL DROP SEQUENCE {qualified};"
            : $"DROP SEQUENCE IF EXISTS {qualified};";

        return dropSql + "\n" + createSql;
    }
}

public sealed class CreateTableAsExpr(
    string schemaName,
    string tableName,
    string? sourceTable,
    string? selectSql,
    bool includeData,
    DdlIdempotentMode mode = DdlIdempotentMode.None
) : IDdlExpression
{
    public string SchemaName { get; } = schemaName;
    public string TableName { get; } = tableName;
    public string? SourceTable { get; } = sourceTable;
    public string? SelectSql { get; } = selectSql;
    public bool IncludeData { get; } = includeData;
    public DdlIdempotentMode Mode { get; } = mode;

    public string Emit(DdlEmitContext context)
    {
        string schema = string.IsNullOrWhiteSpace(SchemaName)
            ? (context.Provider == Core.DatabaseProvider.SqlServer ? "dbo" : "public")
            : SchemaName.Trim();
        string target = $"{context.Dialect.QuoteIdentifier(schema)}.{context.Dialect.QuoteIdentifier(TableName.Trim())}";

        string baseSql;
        if (!string.IsNullOrWhiteSpace(SourceTable))
        {
            string source = SourceTable.Trim();
            baseSql = context.Provider switch
            {
                Core.DatabaseProvider.Postgres => $"CREATE TABLE {target} (LIKE {source} INCLUDING ALL);",
                Core.DatabaseProvider.MySql => $"CREATE TABLE {target} LIKE {source};",
                Core.DatabaseProvider.SqlServer => $"SELECT TOP 0 * INTO {target} FROM {source};",
                Core.DatabaseProvider.SQLite => $"CREATE TABLE {target} AS SELECT * FROM {source} LIMIT 0;",
                _ => $"CREATE TABLE {target} AS SELECT * FROM {source};",
            };
        }
        else
        {
            string select = (SelectSql ?? string.Empty).Trim().TrimEnd(';');
            baseSql = context.Provider switch
            {
                Core.DatabaseProvider.Postgres =>
                    $"CREATE TABLE {target} AS {select} {(IncludeData ? "WITH DATA" : "WITH NO DATA")};",
                Core.DatabaseProvider.SqlServer => $"SELECT * INTO {target} FROM ({select}) AS _src;",
                _ => $"CREATE TABLE {target} AS {select};",
            };
        }

        if (Mode != DdlIdempotentMode.DropAndCreate)
            return baseSql;

        string dropSql = context.Dialect.EmitAlterTableDropTable(schema, TableName.Trim(), ifExists: true);
        return dropSql + "\n" + baseSql;
    }
}

public interface IAlterOpExpr
{
    bool IsDestructive { get; }
    string Emit(DdlEmitContext context, string schemaName, string tableName);
}

/// <summary>
/// Adds a single-column foreign key constraint through ALTER TABLE.
/// </summary>
public sealed class AddForeignKeyOpExpr(
    string? constraintName,
    string childColumn,
    string parentSchema,
    string parentTable,
    string parentColumn,
    ReferentialAction onDelete,
    ReferentialAction onUpdate
) : IAlterOpExpr
{
    public string? ConstraintName { get; } = string.IsNullOrWhiteSpace(constraintName) ? null : constraintName.Trim();
    public string ChildColumn { get; } = childColumn;
    public string ParentSchema { get; } = parentSchema;
    public string ParentTable { get; } = parentTable;
    public string ParentColumn { get; } = parentColumn;
    public ReferentialAction OnDelete { get; } = onDelete;
    public ReferentialAction OnUpdate { get; } = onUpdate;
    public bool IsDestructive => false;

    public string Emit(DdlEmitContext context, string schemaName, string tableName)
    {
        string childTable = QualifyTable(context, schemaName, tableName);
        string parent = QualifyTable(context, ParentSchema, ParentTable);
        string childCol = context.Dialect.QuoteIdentifier(ChildColumn.Trim());
        string parentCol = context.Dialect.QuoteIdentifier(ParentColumn.Trim());

        string constraintClause = string.IsNullOrWhiteSpace(ConstraintName)
            ? string.Empty
            : $"CONSTRAINT {context.Dialect.QuoteIdentifier(ConstraintName.Trim())} ";

        string onDeleteClause = $" ON DELETE {MapAction(OnDelete)}";
        string onUpdateClause = context.Provider == DatabaseProvider.SQLite
            ? string.Empty
            : $" ON UPDATE {MapAction(OnUpdate)}";

        return $"ALTER TABLE {childTable} ADD {constraintClause}FOREIGN KEY ({childCol}) REFERENCES {parent} ({parentCol}){onDeleteClause}{onUpdateClause};";
    }

    private static string QualifyTable(DdlEmitContext context, string schemaName, string tableName)
    {
        string normalizedTable = tableName.Trim();
        string normalizedSchema = NormalizeSchemaForProvider(context.Provider, schemaName);
        if (context.Provider == DatabaseProvider.SQLite || string.IsNullOrWhiteSpace(normalizedSchema))
            return context.Dialect.QuoteIdentifier(normalizedTable);

        return $"{context.Dialect.QuoteIdentifier(normalizedSchema)}.{context.Dialect.QuoteIdentifier(normalizedTable)}";
    }

    private static string NormalizeSchemaForProvider(DatabaseProvider provider, string schema)
    {
        if (!string.IsNullOrWhiteSpace(schema))
            return schema.Trim();

        return provider switch
        {
            DatabaseProvider.SqlServer => "dbo",
            DatabaseProvider.Postgres => "public",
            _ => string.Empty,
        };
    }

    private static string MapAction(ReferentialAction action) =>
        action switch
        {
            ReferentialAction.Cascade => "CASCADE",
            ReferentialAction.SetNull => "SET NULL",
            ReferentialAction.SetDefault => "SET DEFAULT",
            ReferentialAction.Restrict => "RESTRICT",
            _ => "NO ACTION",
        };
}

/// <summary>
/// Drops a named table constraint through ALTER TABLE.
/// </summary>
public sealed class DropConstraintOpExpr(string constraintName) : IAlterOpExpr
{
    public string ConstraintName { get; } = constraintName;
    public bool IsDestructive => true;

    public string Emit(DdlEmitContext context, string schemaName, string tableName)
    {
        if (context.Provider == DatabaseProvider.SQLite)
            return "-- SQLite does not support DROP CONSTRAINT directly; table rebuild is required.";

        string schema = NormalizeSchemaForProvider(context.Provider, schemaName);
        string qualifiedTable = string.IsNullOrWhiteSpace(schema)
            ? context.Dialect.QuoteIdentifier(tableName.Trim())
            : $"{context.Dialect.QuoteIdentifier(schema)}.{context.Dialect.QuoteIdentifier(tableName.Trim())}";

        return $"ALTER TABLE {qualifiedTable} DROP CONSTRAINT {context.Dialect.QuoteIdentifier(ConstraintName.Trim())};";
    }

    private static string NormalizeSchemaForProvider(DatabaseProvider provider, string schema)
    {
        if (!string.IsNullOrWhiteSpace(schema))
            return schema.Trim();

        return provider switch
        {
            DatabaseProvider.SqlServer => "dbo",
            DatabaseProvider.Postgres => "public",
            _ => string.Empty,
        };
    }
}

public sealed class AddColumnOpExpr(DdlColumnExpr column) : IAlterOpExpr
{
    public DdlColumnExpr Column { get; } = column;
    public bool IsDestructive => false;

    public string Emit(DdlEmitContext context, string schemaName, string tableName)
    {
        string columnFragment = context.Dialect.EmitCreateTableColumn(
            Column.ColumnName,
            Column.DataType,
            Column.IsNullable,
            Column.DefaultExpression
        );

        return context.Dialect.EmitAlterTableAddColumn(schemaName, tableName, columnFragment);
    }
}

public sealed class DropColumnOpExpr(string columnName, bool ifExists) : IAlterOpExpr
{
    public string ColumnName { get; } = columnName;
    public bool IfExists { get; } = ifExists;
    public bool IsDestructive => false;

    public string Emit(DdlEmitContext context, string schemaName, string tableName)
        => context.Dialect.EmitAlterTableDropColumn(schemaName, tableName, ColumnName, IfExists);
}

public sealed class RenameColumnOpExpr(string oldName, string newName) : IAlterOpExpr
{
    public string OldName { get; } = oldName;
    public string NewName { get; } = newName;
    public bool IsDestructive => false;

    public string Emit(DdlEmitContext context, string schemaName, string tableName)
        => context.Dialect.EmitAlterTableRenameColumn(schemaName, tableName, OldName, NewName);
}

public sealed class RenameTableOpExpr(string newName, string? newSchema) : IAlterOpExpr
{
    public string NewName { get; } = newName;
    public string? NewSchema { get; } = string.IsNullOrWhiteSpace(newSchema) ? null : newSchema.Trim();
    public bool IsDestructive => false;

    public string Emit(DdlEmitContext context, string schemaName, string tableName)
        => context.Dialect.EmitAlterTableRenameTable(schemaName, tableName, NewName, NewSchema);
}

public sealed class DropTableOpExpr(bool ifExists) : IAlterOpExpr
{
    public bool IfExists { get; } = ifExists;
    public bool IsDestructive => true;

    public string Emit(DdlEmitContext context, string schemaName, string tableName)
        => context.Dialect.EmitAlterTableDropTable(schemaName, tableName, IfExists);
}

public sealed class AlterColumnTypeOpExpr(string columnName, string newDataType, bool isNullable) : IAlterOpExpr
{
    public string ColumnName { get; } = columnName;
    public string NewDataType { get; } = newDataType;
    public bool IsNullable { get; } = isNullable;
    public bool IsDestructive => false;

    public string Emit(DdlEmitContext context, string schemaName, string tableName)
        => context.Dialect.EmitAlterTableAlterColumnType(schemaName, tableName, ColumnName, NewDataType, IsNullable);
}

public sealed class AlterTableExpr(
    string schemaName,
    string tableName,
    IReadOnlyList<IAlterOpExpr> operations,
    bool emitSeparateStatements
) : IDdlExpression
{
    public string SchemaName { get; } = schemaName;
    public string TableName { get; } = tableName;
    public IReadOnlyList<IAlterOpExpr> Operations { get; } = operations;
    public bool EmitSeparateStatements { get; } = emitSeparateStatements;

    public string Emit(DdlEmitContext context)
    {
        IReadOnlyList<string> fragments =
        [
            .. Operations.Select(op => op.Emit(context, SchemaName, TableName))
                .Where(s => !string.IsNullOrWhiteSpace(s)),
        ];

        return context.Dialect.EmitAlterTable(SchemaName, TableName, fragments, EmitSeparateStatements);
    }
}

/// <summary>
/// Emits a direct DROP TABLE statement.
/// </summary>
public sealed class DropTableExpr(string schemaName, string tableName, bool ifExists = true) : IDdlExpression
{
    public string SchemaName { get; } = schemaName;
    public string TableName { get; } = tableName;
    public bool IfExists { get; } = ifExists;

    public string Emit(DdlEmitContext context)
    {
        string schema = string.IsNullOrWhiteSpace(SchemaName)
            ? context.Provider switch
            {
                DatabaseProvider.SqlServer => "dbo",
                DatabaseProvider.Postgres => "public",
                _ => string.Empty,
            }
            : SchemaName.Trim();

        return context.Dialect.EmitAlterTableDropTable(schema, TableName.Trim(), IfExists);
    }
}

public sealed class CreateTableExpr(
    string schemaName,
    string tableName,
    bool ifNotExists,
    IReadOnlyList<DdlColumnExpr> columns,
    IReadOnlyList<DdlPrimaryKeyExpr> primaryKeys,
    IReadOnlyList<DdlUniqueExpr> uniques,
    IReadOnlyList<DdlCheckExpr> checks,
    string? tableComment = null,
    DdlIdempotentMode mode = DdlIdempotentMode.None,
    IReadOnlyList<DdlForeignKeyExpr>? foreignKeys = null
) : IDdlExpression
{
    public string SchemaName { get; } = schemaName;
    public string TableName { get; } = tableName;
    public bool IfNotExists { get; } = ifNotExists;
    public IReadOnlyList<DdlColumnExpr> Columns { get; } = columns;
    public IReadOnlyList<DdlPrimaryKeyExpr> PrimaryKeys { get; } = primaryKeys;
    public IReadOnlyList<DdlUniqueExpr> Uniques { get; } = uniques;
    public IReadOnlyList<DdlCheckExpr> Checks { get; } = checks;
    public IReadOnlyList<DdlForeignKeyExpr> ForeignKeys { get; } = foreignKeys ?? [];
    public string? TableComment { get; } = string.IsNullOrWhiteSpace(tableComment) ? null : tableComment.Trim();
    public DdlIdempotentMode Mode { get; } = mode;

    public string Emit(DdlEmitContext context)
    {
        List<string> columnFragments =
        [
            .. Columns
                .Select(c => context.Dialect.EmitCreateTableColumn(c.ColumnName, c.DataType, c.IsNullable, c.DefaultExpression, c.Comment))
                .Where(static fragment => !string.IsNullOrWhiteSpace(fragment)),
        ];

        var constraintFragments = new List<string>();
        constraintFragments.AddRange(PrimaryKeys.Select(pk => context.Dialect.EmitPrimaryKeyConstraint(pk.ConstraintName, pk.Columns)));
        constraintFragments.AddRange(Uniques.Select(uq => context.Dialect.EmitUniqueConstraint(uq.ConstraintName, uq.Columns)));
        constraintFragments.AddRange(Checks.Select(ck => context.Dialect.EmitCheckConstraint(ck.ConstraintName, ck.Expression)));
        constraintFragments.AddRange(ForeignKeys.Select(fk => fk.Emit(context)));
        constraintFragments = [.. constraintFragments.Where(static fragment => !string.IsNullOrWhiteSpace(fragment))];

        bool effectiveIfNotExists = Mode == DdlIdempotentMode.IfNotExists || (Mode == DdlIdempotentMode.None && IfNotExists);

        string createTableSql = context.Dialect.EmitCreateTable(
            SchemaName,
            TableName,
            effectiveIfNotExists,
            columnFragments,
            constraintFragments,
            TableComment
        );

        var extras = new List<string>();
        string? tableCommentSql = context.Dialect.EmitTableComment(SchemaName, TableName, TableComment);
        if (!string.IsNullOrWhiteSpace(tableCommentSql))
            extras.Add(tableCommentSql);

        foreach (DdlColumnExpr col in Columns)
        {
            string? columnCommentSql = context.Dialect.EmitColumnComment(SchemaName, TableName, col.ColumnName, col.Comment);
            if (!string.IsNullOrWhiteSpace(columnCommentSql))
                extras.Add(columnCommentSql);
        }

        string createWithExtras = extras.Count == 0
            ? createTableSql
            : createTableSql + "\n" + string.Join("\n", extras);

        if (Mode != DdlIdempotentMode.DropAndCreate)
            return createWithExtras;

        string dropSql = context.Dialect.EmitAlterTableDropTable(SchemaName, TableName, ifExists: true);
        return dropSql + "\n" + createWithExtras;
    }
}

public sealed class CreateViewExpr(
    string schemaName,
    string viewName,
    bool orReplace,
    bool isMaterialized,
    string selectSql,
    DdlIdempotentMode mode = DdlIdempotentMode.None
) : IDdlExpression
{
    public string SchemaName { get; } = schemaName;
    public string ViewName { get; } = viewName;
    public bool OrReplace { get; } = orReplace;
    public bool IsMaterialized { get; } = isMaterialized;
    public string SelectSql { get; } = selectSql;
    public DdlIdempotentMode Mode { get; } = mode;

    private static string BuildQualifiedName(DdlEmitContext context, string schemaName, string objectName)
    {
        string name = context.Dialect.QuoteIdentifier(objectName);
        if (string.IsNullOrWhiteSpace(schemaName) || context.Provider == Core.DatabaseProvider.SQLite)
            return name;

        return $"{context.Dialect.QuoteIdentifier(schemaName)}.{name}";
    }

    public string Emit(DdlEmitContext context)
    {
        if (Mode == DdlIdempotentMode.DropAndCreate)
        {
            string qualified = BuildQualifiedName(context, SchemaName, ViewName);
            string normalizedSchema = string.IsNullOrWhiteSpace(SchemaName) ? "dbo" : SchemaName;
            string dropSql = context.Provider switch
            {
                Core.DatabaseProvider.SqlServer =>
                    $"IF OBJECT_ID(N'{normalizedSchema}.{ViewName}', N'V') IS NOT NULL DROP VIEW {qualified};",
                _ => $"DROP VIEW IF EXISTS {qualified};",
            };

            string createSql = context.Dialect.EmitCreateView(SchemaName, ViewName, false, IsMaterialized, SelectSql);
            return dropSql + "\n" + createSql;
        }

        if (Mode == DdlIdempotentMode.IfNotExists)
        {
            if (context.Provider == Core.DatabaseProvider.Postgres)
                return context.Dialect.EmitCreateView(SchemaName, ViewName, true, IsMaterialized, SelectSql);

            if (context.Provider == Core.DatabaseProvider.SQLite)
                return $"CREATE VIEW IF NOT EXISTS {context.Dialect.QuoteIdentifier(ViewName)} AS\n{SelectSql.Trim().TrimEnd(';')};";
        }

        return context.Dialect.EmitCreateView(SchemaName, ViewName, OrReplace, IsMaterialized, SelectSql);
    }
}

public sealed class AlterViewExpr(
    string schemaName,
    string viewName,
    string selectSql
) : IDdlExpression
{
    public string SchemaName { get; } = schemaName;
    public string ViewName { get; } = viewName;
    public string SelectSql { get; } = selectSql;

    public string Emit(DdlEmitContext context)
        => context.Dialect.EmitAlterView(SchemaName, ViewName, SelectSql);
}
