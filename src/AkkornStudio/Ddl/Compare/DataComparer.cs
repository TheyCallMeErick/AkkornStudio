using System.Globalization;
using AkkornStudio.Metadata;

namespace AkkornStudio.Ddl.Compare;

/// <summary>
/// Pure row-level comparison of two <see cref="DataRowSet"/>s for the same logical table.
/// Rows are matched by a key (the primary key when available, otherwise the whole row), and
/// classified as insert/update/delete/unchanged relative to the target. No SQL, no I/O, no UI.
/// </summary>
public sealed class DataComparer
{
    /// <summary>
    /// Resolves the key columns to match rows on: the primary key shared by both tables when
    /// present, otherwise empty (the caller should fall back to whole-row matching).
    /// </summary>
    public static IReadOnlyList<string> ResolveKeyColumns(TableMetadata source, TableMetadata target)
    {
        string[] sourcePk = TableComparer.ResolvePrimaryKeyColumns(source);
        if (sourcePk.Length == 0)
            return [];

        var targetPk = new HashSet<string>(TableComparer.ResolvePrimaryKeyColumns(target), StringComparer.OrdinalIgnoreCase);

        // Only use key columns the target also has as PK, so the WHERE clause is valid on the target.
        return sourcePk.Where(targetPk.Contains).ToArray();
    }

    /// <summary>
    /// Compares <paramref name="source"/> against <paramref name="target"/> over their shared columns.
    /// When <paramref name="keyColumns"/> is empty (or none survive on both sides), rows are matched
    /// by the whole row, so changes surface as insert + delete rather than update.
    /// </summary>
    public DataComparison Compare(
        DataRowSet source,
        DataRowSet target,
        IReadOnlyList<string> keyColumns)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(keyColumns);

        var warnings = new List<string>();

        var targetColumnSet = new HashSet<string>(target.Columns, StringComparer.OrdinalIgnoreCase);
        var sharedColumns = source.Columns.Where(targetColumnSet.Contains).ToArray();
        if (sharedColumns.Length == 0)
        {
            warnings.Add("As tabelas nao possuem colunas em comum; comparacao de dados nao e possivel.");
            return new DataComparison([], [], [], warnings);
        }

        foreach (string only in source.Columns.Where(c => !targetColumnSet.Contains(c)))
            warnings.Add($"Coluna so na origem, ignorada na comparacao: {only}.");
        var sourceColumnSet = new HashSet<string>(source.Columns, StringComparer.OrdinalIgnoreCase);
        foreach (string only in target.Columns.Where(c => !sourceColumnSet.Contains(c)))
            warnings.Add($"Coluna so no destino, ignorada na comparacao: {only}.");

        var sharedSet = new HashSet<string>(sharedColumns, StringComparer.OrdinalIgnoreCase);
        string[] effectiveKey = keyColumns.Where(sharedSet.Contains).ToArray();
        if (effectiveKey.Length == 0)
        {
            warnings.Add("Sem primary key comum: linhas casadas pela linha inteira (alteracoes aparecem como insert + delete).");
            effectiveKey = sharedColumns;
        }

        Dictionary<string, int> sourceIndex = BuildColumnIndex(source.Columns);
        Dictionary<string, int> targetIndex = BuildColumnIndex(target.Columns);

        Dictionary<string, IReadOnlyList<object?>> sourceRows =
            IndexRows(source.Rows, sourceIndex, effectiveKey, "origem", warnings);
        Dictionary<string, IReadOnlyList<object?>> targetRows =
            IndexRows(target.Rows, targetIndex, effectiveKey, "destino", warnings);

        var differences = new List<RowDifference>();
        var consumedTargetKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string key, IReadOnlyList<object?> sourceRow) in sourceRows)
        {
            IReadOnlyDictionary<string, object?> sourceCells = ProjectCells(sourceRow, sourceIndex, sharedColumns);
            if (!targetRows.TryGetValue(key, out IReadOnlyList<object?>? targetRow))
            {
                differences.Add(new RowDifference
                {
                    Kind = RowDifferenceKind.InsertIntoTarget,
                    KeyDisplay = DescribeKey(effectiveKey, sourceCells),
                    SourceValues = sourceCells,
                    TargetValues = EmptyCells,
                    ChangedColumns = [],
                });
                continue;
            }

            consumedTargetKeys.Add(key);
            IReadOnlyDictionary<string, object?> targetCells = ProjectCells(targetRow, targetIndex, sharedColumns);

            string[] changed = sharedColumns
                .Where(column => !ValuesEqual(sourceCells[column], targetCells[column]))
                .ToArray();

            differences.Add(new RowDifference
            {
                Kind = changed.Length == 0 ? RowDifferenceKind.Unchanged : RowDifferenceKind.UpdateInTarget,
                KeyDisplay = DescribeKey(effectiveKey, sourceCells),
                SourceValues = sourceCells,
                TargetValues = targetCells,
                ChangedColumns = changed,
            });
        }

        foreach ((string key, IReadOnlyList<object?> targetRow) in targetRows)
        {
            if (consumedTargetKeys.Contains(key))
                continue;

            IReadOnlyDictionary<string, object?> targetCells = ProjectCells(targetRow, targetIndex, sharedColumns);
            differences.Add(new RowDifference
            {
                Kind = RowDifferenceKind.DeleteFromTarget,
                KeyDisplay = DescribeKey(effectiveKey, targetCells),
                SourceValues = EmptyCells,
                TargetValues = targetCells,
                ChangedColumns = [],
            });
        }

        return new DataComparison(sharedColumns, effectiveKey, differences, warnings);
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyCells =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, int> BuildColumnIndex(IReadOnlyList<string> columns)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
            map[columns[i]] = i;

        return map;
    }

    private static Dictionary<string, IReadOnlyList<object?>> IndexRows(
        IReadOnlyList<IReadOnlyList<object?>> rows,
        Dictionary<string, int> columnIndex,
        IReadOnlyList<string> keyColumns,
        string sideLabel,
        List<string> warnings)
    {
        // Preserves first-seen order so insert/update output follows the source's natural order.
        var map = new Dictionary<string, IReadOnlyList<object?>>(StringComparer.Ordinal);
        var ordered = new List<KeyValuePair<string, IReadOnlyList<object?>>>();
        bool duplicateWarned = false;

        foreach (IReadOnlyList<object?> row in rows)
        {
            string key = BuildKeyToken(row, columnIndex, keyColumns);
            if (map.ContainsKey(key))
            {
                if (!duplicateWarned)
                {
                    warnings.Add($"Chave duplicada na {sideLabel}; apenas a primeira ocorrencia e comparada.");
                    duplicateWarned = true;
                }

                continue;
            }

            map[key] = row;
            ordered.Add(new KeyValuePair<string, IReadOnlyList<object?>>(key, row));
        }

        // Rebuild as an insertion-ordered dictionary view.
        var orderedMap = new Dictionary<string, IReadOnlyList<object?>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IReadOnlyList<object?>> pair in ordered)
            orderedMap[pair.Key] = pair.Value;

        return orderedMap;
    }

    private static string BuildKeyToken(
        IReadOnlyList<object?> row,
        Dictionary<string, int> columnIndex,
        IReadOnlyList<string> keyColumns)
    {
        return string.Join('', keyColumns.Select(column =>
            columnIndex.TryGetValue(column, out int idx) && idx < row.Count
                ? Canonical(row[idx])
                : Canonical(null)));
    }

    private static IReadOnlyDictionary<string, object?> ProjectCells(
        IReadOnlyList<object?> row,
        Dictionary<string, int> columnIndex,
        IReadOnlyList<string> sharedColumns)
    {
        var cells = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (string column in sharedColumns)
            cells[column] = columnIndex.TryGetValue(column, out int idx) && idx < row.Count
                ? NormalizeNull(row[idx])
                : null;

        return cells;
    }

    private static string DescribeKey(IReadOnlyList<string> keyColumns, IReadOnlyDictionary<string, object?> cells)
    {
        return string.Join(", ", keyColumns.Select(column =>
            $"{column}={DisplayValue(cells.TryGetValue(column, out object? value) ? value : null)}"));
    }

    private static string DisplayValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            string s => s,
            byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>Compares two cell values for logical equality (cross-type numeric aware, null aware).</summary>
    public static bool ValuesEqual(object? left, object? right) =>
        string.Equals(Canonical(left), Canonical(right), StringComparison.Ordinal);

    private static object? NormalizeNull(object? value) => value is DBNull ? null : value;

    /// <summary>Produces a stable canonical string used for both key matching and value equality.</summary>
    private static string Canonical(object? value)
    {
        object? v = NormalizeNull(value);
        return v switch
        {
            null => "∅",
            bool b => b ? "b:1" : "b:0",
            byte[] bytes => "x:" + Convert.ToHexString(bytes),
            DateTime dt => "d:" + dt.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => "o:" + dto.ToString("o", CultureInfo.InvariantCulture),
            Guid g => "g:" + g.ToString("D"),
            sbyte or byte or short or ushort or int or uint or long or ulong or decimal =>
                "n:" + Convert.ToDecimal(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            float or double =>
                "f:" + Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => "s:" + formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => "s:" + (v.ToString() ?? string.Empty),
        };
    }
}
