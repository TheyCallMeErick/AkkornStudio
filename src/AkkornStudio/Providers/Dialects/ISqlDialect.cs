namespace AkkornStudio.Providers.Dialects;

/// <summary>
/// Abstração de dialeto SQL específico de cada provider.
/// Centraliza queries de metadata e operações de dialeto específicas.
/// </summary>
public interface IIdentifierDialect
{
    /// <summary>
    /// Quoteia um identificador (table name, column name) segundo dialeto.
    /// Ex SQL Server: [tableName]
    /// Ex MySQL: `tableName`
    /// Ex Postgres: "tableName"
    /// </summary>
    string QuoteIdentifier(string identifier);
}

public interface IQueryDialect
{
    /// <summary>
    /// Query para listar todas as tabelas/views do banco.
    /// Deve retornar: TABLE_SCHEMA, TABLE_NAME
    /// </summary>
    string GetTablesQuery();

    /// <summary>
    /// Query para listar colunas de uma tabela específica.
    /// Parâmetros: @schema, @table
    /// Deve retornar: COLUMN_NAME, DATA_TYPE, IS_NULLABLE, IS_PRIMARY_KEY
    /// </summary>
    string GetColumnsQuery();

    /// <summary>
    /// Query para descobrir chaves primárias.
    /// Parâmetros: @schema, @table
    /// Deve retornar: COLUMN_NAME
    /// </summary>
    string GetPrimaryKeysQuery();

    /// <summary>
    /// Query para descobrir chaves estrangeiras.
    /// Parâmetros: @schema, @table
    /// Deve retornar: COLUMN_NAME, REFERENCED_TABLE, REFERENCED_COLUMN
    /// </summary>
    string GetForeignKeysQuery();

    /// <summary>
    /// Envolve uma query em SELECT TOP N (SQL Server style).
    /// Ex: "SELECT TOP 100 * FROM (SELECT * FROM users WHERE ...) AS __preview"
    /// </summary>
    string WrapWithPreviewLimit(string baseQuery, int maxRows);

    /// <summary>
    /// Obtém a sintaxe de LIMIT/OFFSET específica do dialeto.
    /// Ex SQL Server: "OFFSET 10 ROWS FETCH NEXT 20 ROWS ONLY"
    /// Ex MySQL: "LIMIT 20 OFFSET 10"
    /// </summary>
    string FormatPagination(int? limit, int? offset);

    /// <summary>
    /// Applies provider-specific query hints to the SQL statement.
    /// Returns the original SQL when hints are invalid or unsupported.
    /// </summary>
    string ApplyQueryHints(string sql, string? queryHints);
}

public interface IDdlDialect
{
    /// <summary>
    /// Emits a single column definition fragment used inside CREATE TABLE.
    /// </summary>
    string EmitCreateTableColumn(
        string columnName,
        string dataType,
        bool isNullable,
        string? defaultExpression = null,
        string? columnComment = null
    ) => throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits a PRIMARY KEY table constraint fragment.
    /// </summary>
    string EmitPrimaryKeyConstraint(string? constraintName, IReadOnlyList<string> columns) =>
        throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits a UNIQUE table constraint fragment.
    /// </summary>
    string EmitUniqueConstraint(string? constraintName, IReadOnlyList<string> columns) =>
        throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits a CHECK table constraint fragment.
    /// </summary>
    string EmitCheckConstraint(string? constraintName, string expression) =>
        throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits a FOREIGN KEY table constraint fragment.
    /// </summary>
    string EmitForeignKeyConstraint(
        string? constraintName,
        IReadOnlyList<string> childColumns,
        string parentSchema,
        string parentTable,
        IReadOnlyList<string> parentColumns,
        AkkornStudio.Metadata.ReferentialAction onDelete,
        AkkornStudio.Metadata.ReferentialAction onUpdate
    ) => throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits a CREATE TABLE statement.
    /// </summary>
    string EmitCreateTable(
        string schemaName,
        string tableName,
        bool ifNotExists,
        IReadOnlyList<string> columnFragments,
        IReadOnlyList<string> constraintFragments,
        string? tableComment = null
    ) => throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits a provider-specific table comment statement when comments are supported as separate DDL.
    /// Return null/empty when not supported or inlined in CREATE TABLE.
    /// </summary>
    string? EmitTableComment(string schemaName, string tableName, string? comment) => null;

    /// <summary>
    /// Emits a provider-specific column comment statement when comments are supported as separate DDL.
    /// Return null/empty when not supported or inlined in CREATE TABLE.
    /// </summary>
    string? EmitColumnComment(string schemaName, string tableName, string columnName, string? comment) => null;

    /// <summary>
    /// Emits a CREATE INDEX statement.
    /// </summary>
    string EmitCreateIndex(
        string schemaName,
        string tableName,
        string indexName,
        bool isUnique,
        IReadOnlyList<Ddl.DdlIndexKeyExpr> keyColumns,
        IReadOnlyList<string> includeColumns,
        bool ifNotExists
    ) => throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits CREATE VIEW (or provider equivalent).
    /// </summary>
    string EmitCreateView(
        string schemaName,
        string viewName,
        bool orReplace,
        bool isMaterialized,
        string selectSql
    ) => throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits ALTER VIEW (or provider equivalent).
    /// </summary>
    string EmitAlterView(
        string schemaName,
        string viewName,
        string selectSql
    ) => throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits ALTER TABLE ... ADD COLUMN ...
    /// </summary>
    string EmitAlterTableAddColumn(string schemaName, string tableName, string columnFragment) =>
        throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits ALTER TABLE ... DROP COLUMN ...
    /// </summary>
    string EmitAlterTableDropColumn(string schemaName, string tableName, string columnName, bool ifExists) =>
        throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits provider-specific rename-column statement.
    /// </summary>
    string EmitAlterTableRenameColumn(string schemaName, string tableName, string oldName, string newName) =>
        throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits provider-specific rename-table statement.
    /// </summary>
    string EmitAlterTableRenameTable(string schemaName, string tableName, string newName, string? newSchema) =>
        throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits provider-specific drop-table statement.
    /// </summary>
    string EmitAlterTableDropTable(string schemaName, string tableName, bool ifExists) =>
        throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits provider-specific alter-column-type statement.
    /// </summary>
    string EmitAlterTableAlterColumnType(
        string schemaName,
        string tableName,
        string columnName,
        string newDataType,
        bool isNullable
    ) => throw new NotSupportedException("DDL emission is not implemented for this dialect.");

    /// <summary>
    /// Emits final ALTER TABLE script from operation statements.
    /// </summary>
    string EmitAlterTable(
        string schemaName,
        string tableName,
        IReadOnlyList<string> operationStatements,
        bool emitSeparateStatements
    ) => string.Join("\n", operationStatements);
}

public interface ISqlDialect : IIdentifierDialect, IQueryDialect, IDdlDialect
{
}
