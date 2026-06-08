using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Providers.Dialects;
using AkkornStudio.Registry;

namespace AkkornStudio.Ddl.Compare;

/// <summary>
/// Emits provider-specific DDL for a single <see cref="SchemaDifference"/>. All dialect
/// emission lives here, so the comparison engine stays free of SQL strings and the SQL for
/// a difference can be (re)generated lazily as options change.
/// </summary>
public sealed class SyncScriptGenerator
{
    private readonly IProviderRegistry _registry;

    public SyncScriptGenerator(IProviderRegistry? registry = null)
        => _registry = registry ?? ProviderRegistry.CreateDefault();

    public string Generate(
        SchemaDifference difference,
        DatabaseProvider provider,
        string targetSchema,
        string targetTable,
        SchemaSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(difference);
        ISqlDialect dialect = _registry.GetDialect(provider);
        return Generate(difference.Operation, provider, dialect, targetSchema, targetTable, options);
    }

    public string Generate(
        ISchemaSyncOperation operation,
        DatabaseProvider provider,
        ISqlDialect dialect,
        string targetSchema,
        string targetTable,
        SchemaSyncOptions options)
    {
        return operation switch
        {
            AddColumnOperation add => dialect.EmitAlterTableAddColumn(
                targetSchema,
                targetTable,
                dialect.EmitCreateTableColumn(add.Column.Name, add.ResolvedType, add.Column.IsNullable, add.Column.DefaultValue, add.Column.Comment)),

            // SQLite rejects IF EXISTS on ALTER TABLE DROP COLUMN.
            DropColumnOperation drop => dialect.EmitAlterTableDropColumn(
                targetSchema,
                targetTable,
                drop.ColumnName,
                ifExists: options.ExistenceSafe && provider != DatabaseProvider.SQLite),

            AlterColumnOperation alter => dialect.EmitAlterTableAlterColumnType(
                targetSchema,
                targetTable,
                alter.ColumnName,
                alter.DataType,
                alter.IsNullable),

            SetColumnDefaultOperation def =>
                BuildSetColumnDefaultSql(provider, dialect, targetSchema, targetTable, def.ColumnName, def.DefaultValue) ?? string.Empty,

            SetColumnCommentOperation comment =>
                dialect.EmitColumnComment(targetSchema, targetTable, comment.ColumnName, comment.Comment) ?? string.Empty,

            RecreatePrimaryKeyOperation pk =>
                BuildRecreatePrimaryKey(pk, provider, dialect, targetSchema, targetTable),

            AddUniqueOperation unique =>
                $"ALTER TABLE {Qualify(provider, dialect, targetSchema, targetTable)} ADD {dialect.EmitUniqueConstraint(unique.Name, unique.Columns)};",

            DropUniqueOperation unique =>
                BuildDropUniqueSql(provider, dialect, targetSchema, targetTable, unique.Name) ?? string.Empty,

            CreateIndexOperation index => dialect.EmitCreateIndex(
                targetSchema,
                targetTable,
                index.Name,
                isUnique: false,
                index.Columns.Select(static c => new DdlIndexKeyExpr(c.Trim())).ToArray(),
                includeColumns: [],
                ifNotExists: options.ExistenceSafe),

            DropIndexOperation index =>
                BuildDropIndexSql(provider, dialect, targetSchema, targetTable, index.Name, options.ExistenceSafe),

            AddCheckOperation check =>
                $"ALTER TABLE {Qualify(provider, dialect, targetSchema, targetTable)} ADD {dialect.EmitCheckConstraint(check.Name, check.Expression)};",

            DropCheckOperation check =>
                BuildDropCheckSql(provider, dialect, targetSchema, targetTable, check.Name),

            AddForeignKeyOperation fk =>
                $"ALTER TABLE {Qualify(provider, dialect, targetSchema, targetTable)} ADD {dialect.EmitForeignKeyConstraint(fk.ForeignKey.ConstraintName, fk.ForeignKey.ChildColumns, fk.ForeignKey.ParentSchema, fk.ForeignKey.ParentTable, fk.ForeignKey.ParentColumns, fk.ForeignKey.OnDelete, fk.ForeignKey.OnUpdate)};",

            DropForeignKeyOperation fk =>
                BuildDropForeignKeySql(provider, dialect, targetSchema, targetTable, fk.ConstraintName) ?? string.Empty,

            CreateTableOperation create =>
                BuildCreateTable(create.Table, dialect, targetSchema, options),

            DropTableOperation drop =>
                $"DROP TABLE {(options.ExistenceSafe ? "IF EXISTS " : string.Empty)}{Qualify(provider, dialect, drop.Schema, drop.Table)};",

            InformationalOperation => string.Empty,
            _ => string.Empty,
        };
    }

    private static string BuildCreateTable(
        TableMetadata table,
        ISqlDialect dialect,
        string targetSchema,
        SchemaSyncOptions options)
    {
        var columnFragments = table.Columns
            .OrderBy(static c => c.OrdinalPosition)
            .Select(c => dialect.EmitCreateTableColumn(c.Name, TableComparer.ResolveColumnType(c), c.IsNullable, c.DefaultValue, c.Comment))
            .ToList();

        var constraintFragments = new List<string>();

        string[] pk = TableComparer.ResolvePrimaryKeyColumns(table);
        if (pk.Length > 0)
            constraintFragments.Add(dialect.EmitPrimaryKeyConstraint(table.Indexes.FirstOrDefault(static i => i.IsPrimaryKey)?.Name, pk));

        foreach (IndexMetadata unique in table.Indexes.Where(static i => i.IsUnique && !i.IsPrimaryKey))
            constraintFragments.Add(dialect.EmitUniqueConstraint(unique.Name, unique.Columns));

        foreach (CompositeForeignKey fk in TableComparer.MapForeignKeys(table.OutboundForeignKeys).Values)
            constraintFragments.Add(dialect.EmitForeignKeyConstraint(fk.ConstraintName, fk.ChildColumns, fk.ParentSchema, fk.ParentTable, fk.ParentColumns, fk.OnDelete, fk.OnUpdate));

        foreach (CheckConstraintMetadata check in table.CheckConstraints)
            constraintFragments.Add(dialect.EmitCheckConstraint(check.Name, check.Expression));

        string createTable = dialect.EmitCreateTable(targetSchema, table.Name, options.ExistenceSafe, columnFragments, constraintFragments, table.Comment);

        // Secondary indexes are not part of CREATE TABLE; emit them as follow-up CREATE INDEX.
        var statements = new List<string> { createTable };
        foreach (IndexMetadata index in table.Indexes.Where(static i => !i.IsUnique && !i.IsPrimaryKey))
        {
            statements.Add(dialect.EmitCreateIndex(
                targetSchema,
                table.Name,
                index.Name,
                isUnique: false,
                index.Columns.Select(static c => new DdlIndexKeyExpr(c.Trim())).ToArray(),
                includeColumns: [],
                ifNotExists: options.ExistenceSafe));
        }

        return string.Join(Environment.NewLine, statements);
    }

    private static string BuildRecreatePrimaryKey(
        RecreatePrimaryKeyOperation pk,
        DatabaseProvider provider,
        ISqlDialect dialect,
        string schema,
        string table)
    {
        var statements = new List<string>();

        if (pk.DropExisting)
        {
            string? drop = BuildDropPrimaryKeySql(provider, schema, table, pk.DropConstraintName, dialect);
            if (!string.IsNullOrWhiteSpace(drop))
                statements.Add(drop);
        }

        if (pk.Columns.Count > 0)
            statements.Add($"ALTER TABLE {Qualify(provider, dialect, schema, table)} ADD {dialect.EmitPrimaryKeyConstraint(pk.AddConstraintName, pk.Columns)};");

        return string.Join(Environment.NewLine, statements);
    }

    private static string? BuildDropPrimaryKeySql(
        DatabaseProvider provider,
        string schema,
        string table,
        string? constraintName,
        ISqlDialect dialect)
    {
        return provider switch
        {
            DatabaseProvider.MySql => $"ALTER TABLE {Qualify(provider, dialect, schema, table)} DROP PRIMARY KEY;",
            DatabaseProvider.SqlServer or DatabaseProvider.Postgres => string.IsNullOrWhiteSpace(constraintName)
                ? null
                : $"ALTER TABLE {Qualify(provider, dialect, schema, table)} DROP CONSTRAINT {dialect.QuoteIdentifier(constraintName)};",
            _ => null,
        };
    }

    private static string? BuildDropUniqueSql(
        DatabaseProvider provider,
        ISqlDialect dialect,
        string schema,
        string table,
        string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
            return null;

        return provider switch
        {
            DatabaseProvider.MySql => $"ALTER TABLE {Qualify(provider, dialect, schema, table)} DROP INDEX {dialect.QuoteIdentifier(indexName)};",
            DatabaseProvider.SqlServer or DatabaseProvider.Postgres => $"ALTER TABLE {Qualify(provider, dialect, schema, table)} DROP CONSTRAINT {dialect.QuoteIdentifier(indexName)};",
            _ => null,
        };
    }

    private static string? BuildDropForeignKeySql(
        DatabaseProvider provider,
        ISqlDialect dialect,
        string schema,
        string table,
        string constraintName)
    {
        if (string.IsNullOrWhiteSpace(constraintName))
            return null;

        return provider switch
        {
            DatabaseProvider.MySql => $"ALTER TABLE {Qualify(provider, dialect, schema, table)} DROP FOREIGN KEY {dialect.QuoteIdentifier(constraintName)};",
            DatabaseProvider.SqlServer or DatabaseProvider.Postgres => $"ALTER TABLE {Qualify(provider, dialect, schema, table)} DROP CONSTRAINT {dialect.QuoteIdentifier(constraintName)};",
            _ => null,
        };
    }

    private static string BuildDropIndexSql(
        DatabaseProvider provider,
        ISqlDialect dialect,
        string schema,
        string table,
        string indexName,
        bool ifExists)
    {
        string idx = dialect.QuoteIdentifier(indexName.Trim());
        string qualifiedTable = Qualify(provider, dialect, schema, table);
        string ifExistsClause = ifExists ? "IF EXISTS " : string.Empty;

        return provider switch
        {
            // Postgres indexes are schema-scoped objects (not bound to the table in syntax).
            DatabaseProvider.Postgres => string.IsNullOrWhiteSpace(NormalizeSchema(provider, schema))
                ? $"DROP INDEX {ifExistsClause}{idx};"
                : $"DROP INDEX {ifExistsClause}{dialect.QuoteIdentifier(NormalizeSchema(provider, schema))}.{idx};",
            DatabaseProvider.SqlServer => $"DROP INDEX {ifExistsClause}{idx} ON {qualifiedTable};",
            // MySQL has no IF EXISTS on DROP INDEX.
            DatabaseProvider.MySql => $"ALTER TABLE {qualifiedTable} DROP INDEX {idx};",
            DatabaseProvider.SQLite => $"DROP INDEX {ifExistsClause}{idx};",
            _ => $"DROP INDEX {idx};",
        };
    }

    private static string? BuildSetColumnDefaultSql(
        DatabaseProvider provider,
        ISqlDialect dialect,
        string schema,
        string table,
        string columnName,
        string? sourceDefault)
    {
        string qualified = Qualify(provider, dialect, schema, table);
        string column = dialect.QuoteIdentifier(columnName.Trim());

        // SQL Server stores defaults as named constraints; dropping/recreating safely requires
        // resolving the existing constraint name, so it is left for manual handling.
        return provider switch
        {
            DatabaseProvider.Postgres or DatabaseProvider.MySql => string.IsNullOrWhiteSpace(sourceDefault)
                ? $"ALTER TABLE {qualified} ALTER COLUMN {column} DROP DEFAULT;"
                : $"ALTER TABLE {qualified} ALTER COLUMN {column} SET DEFAULT {sourceDefault.Trim()};",
            _ => null,
        };
    }

    private static string BuildDropCheckSql(
        DatabaseProvider provider,
        ISqlDialect dialect,
        string schema,
        string table,
        string constraintName)
    {
        string qualified = Qualify(provider, dialect, schema, table);
        string name = dialect.QuoteIdentifier(constraintName.Trim());

        // MySQL uses DROP CHECK; the others use the standard DROP CONSTRAINT.
        return provider == DatabaseProvider.MySql
            ? $"ALTER TABLE {qualified} DROP CHECK {name};"
            : $"ALTER TABLE {qualified} DROP CONSTRAINT {name};";
    }

    private static string Qualify(DatabaseProvider provider, ISqlDialect dialect, string schema, string table)
    {
        string normalizedSchema = NormalizeSchema(provider, schema);
        if (string.IsNullOrWhiteSpace(normalizedSchema))
            return dialect.QuoteIdentifier(table.Trim());

        return $"{dialect.QuoteIdentifier(normalizedSchema)}.{dialect.QuoteIdentifier(table.Trim())}";
    }

    private static string NormalizeSchema(DatabaseProvider provider, string schema)
    {
        if (!string.IsNullOrWhiteSpace(schema))
            return schema.Trim();

        return provider switch
        {
            DatabaseProvider.Postgres => "public",
            DatabaseProvider.SqlServer => "dbo",
            DatabaseProvider.SQLite => "main",
            _ => string.Empty,
        };
    }
}
