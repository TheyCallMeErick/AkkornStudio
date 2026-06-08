using System.Globalization;
using System.Text;
using AkkornStudio.Core;
using AkkornStudio.Providers.Dialects;
using AkkornStudio.Registry;

namespace AkkornStudio.Ddl.Compare;

/// <summary>
/// Emits provider-specific DML (INSERT / UPDATE / DELETE) that converges a target table's data to
/// the source, from a <see cref="DataComparison"/>. Identifier quoting reuses <see cref="ISqlDialect"/>;
/// only value-literal formatting is added here.
/// </summary>
public sealed class DataSyncScriptGenerator
{
    private readonly IProviderRegistry _registry;

    public DataSyncScriptGenerator(IProviderRegistry? registry = null)
        => _registry = registry ?? ProviderRegistry.CreateDefault();

    public string Generate(
        DataComparison comparison,
        DatabaseProvider provider,
        string targetSchema,
        string targetTable,
        DataSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(options);

        if (comparison.IsInSync)
            return "-- Dados ja sincronizados: nenhuma diferenca de valores.";

        ISqlDialect dialect = _registry.GetDialect(provider);
        string qualified = Qualify(provider, dialect, targetSchema, targetTable);

        var builder = new StringBuilder();
        builder.Append("-- Sincronizacao de dados (destino <- origem): ")
            .Append(comparison.InsertCount).Append(" insert(s), ")
            .Append(comparison.UpdateCount).Append(" update(s), ")
            .Append(comparison.DeleteCount).AppendLine(" delete(s).");

        foreach (RowDifference difference in comparison.Differences)
        {
            switch (difference.Kind)
            {
                case RowDifferenceKind.InsertIntoTarget:
                    builder.AppendLine(BuildInsert(comparison.Columns, difference, provider, dialect, qualified));
                    break;
                case RowDifferenceKind.UpdateInTarget:
                    builder.AppendLine(BuildUpdate(comparison.KeyColumns, difference, provider, dialect, qualified));
                    break;
                case RowDifferenceKind.DeleteFromTarget:
                    builder.AppendLine(BuildDelete(comparison.KeyColumns, difference, provider, dialect, qualified, options));
                    break;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildInsert(
        IReadOnlyList<string> columns,
        RowDifference difference,
        DatabaseProvider provider,
        ISqlDialect dialect,
        string qualified)
    {
        string columnList = string.Join(", ", columns.Select(dialect.QuoteIdentifier));
        string valueList = string.Join(", ", columns.Select(column =>
            FormatLiteral(provider, difference.SourceValues.GetValueOrDefault(column))));

        return $"INSERT INTO {qualified} ({columnList}) VALUES ({valueList});";
    }

    private static string BuildUpdate(
        IReadOnlyList<string> keyColumns,
        RowDifference difference,
        DatabaseProvider provider,
        ISqlDialect dialect,
        string qualified)
    {
        string setClause = string.Join(", ", difference.ChangedColumns.Select(column =>
            $"{dialect.QuoteIdentifier(column)} = {FormatLiteral(provider, difference.SourceValues.GetValueOrDefault(column))}"));

        string whereClause = BuildWhere(keyColumns, difference.SourceValues, provider, dialect);
        return $"UPDATE {qualified} SET {setClause} WHERE {whereClause};";
    }

    private static string BuildDelete(
        IReadOnlyList<string> keyColumns,
        RowDifference difference,
        DatabaseProvider provider,
        ISqlDialect dialect,
        string qualified,
        DataSyncOptions options)
    {
        string whereClause = BuildWhere(keyColumns, difference.TargetValues, provider, dialect);
        string statement = $"DELETE FROM {qualified} WHERE {whereClause};";
        return options.CommentDestructive ? $"-- (destrutivo, revise) {statement}" : statement;
    }

    private static string BuildWhere(
        IReadOnlyList<string> keyColumns,
        IReadOnlyDictionary<string, object?> values,
        DatabaseProvider provider,
        ISqlDialect dialect)
    {
        return string.Join(" AND ", keyColumns.Select(column =>
        {
            object? value = values.GetValueOrDefault(column);
            return value is null
                ? $"{dialect.QuoteIdentifier(column)} IS NULL"
                : $"{dialect.QuoteIdentifier(column)} = {FormatLiteral(provider, value)}";
        }));
    }

    /// <summary>Formats a CLR value as a provider-specific SQL literal.</summary>
    public static string FormatLiteral(DatabaseProvider provider, object? value)
    {
        return value switch
        {
            null or DBNull => "NULL",
            bool b => provider == DatabaseProvider.Postgres ? (b ? "TRUE" : "FALSE") : (b ? "1" : "0"),
            byte[] bytes => FormatBytes(provider, bytes),
            DateTime dt => QuoteString(dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
            DateTimeOffset dto => QuoteString(dto.ToString("yyyy-MM-dd HH:mm:ss.fffzzz", CultureInfo.InvariantCulture)),
            Guid g => QuoteString(g.ToString("D")),
            sbyte or byte or short or ushort or int or uint or long or ulong or decimal =>
                Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            float or double =>
                Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            string s => QuoteString(s),
            IFormattable formattable => QuoteString(formattable.ToString(null, CultureInfo.InvariantCulture)),
            _ => QuoteString(value.ToString() ?? string.Empty),
        };
    }

    private static string FormatBytes(DatabaseProvider provider, byte[] bytes)
    {
        string hex = Convert.ToHexString(bytes);
        return provider switch
        {
            DatabaseProvider.SqlServer or DatabaseProvider.MySql => $"0x{hex}",
            DatabaseProvider.SQLite => $"x'{hex}'",
            DatabaseProvider.Postgres => $"'\\x{hex}'",
            _ => $"x'{hex}'",
        };
    }

    private static string QuoteString(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

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
