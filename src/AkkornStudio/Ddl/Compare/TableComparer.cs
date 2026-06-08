using AkkornStudio.Metadata;

namespace AkkornStudio.Ddl.Compare;

/// <summary>
/// Pure, provider-agnostic comparison of two tables. Produces a typed <see cref="TableDiff"/>
/// with structured operations; contains no SQL emission and no UI concerns.
/// </summary>
public sealed class TableComparer
{
    public TableDiff Compare(TableMetadata source, TableMetadata target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var diffs = new List<SchemaDifference>();
        var warnings = new List<string>();
        int sequence = 1;

        CompareColumns(source, target, diffs, ref sequence);
        ComparePrimaryKey(source, target, diffs, warnings, ref sequence);
        CompareUnique(source, target, diffs, ref sequence);
        CompareIndexes(source, target, diffs, ref sequence);
        CompareChecks(source, target, diffs, ref sequence);
        CompareForeignKeys(source, target, diffs, ref sequence);
        CompareExternalImpact(source, target, diffs, ref sequence);

        return new TableDiff(diffs, warnings);
    }

    // ── columns ───────────────────────────────────────────────────────────────
    private static void CompareColumns(
        TableMetadata source,
        TableMetadata target,
        List<SchemaDifference> diffs,
        ref int sequence)
    {
        Dictionary<string, ColumnMetadata> targetColumns =
            target.Columns.ToDictionary(static c => c.Name, StringComparer.OrdinalIgnoreCase);
        var sourceNames = new HashSet<string>(source.Columns.Select(static c => c.Name), StringComparer.OrdinalIgnoreCase);

        foreach (ColumnMetadata sourceColumn in source.Columns.OrderBy(static c => c.OrdinalPosition))
        {
            if (!targetColumns.TryGetValue(sourceColumn.Name, out ColumnMetadata? targetColumn))
            {
                diffs.Add(new SchemaDifference
                {
                    Id = NextId(ref sequence),
                    Category = SchemaDiffCategory.Column,
                    ChangeKind = SchemaDiffChangeKind.AddToTarget,
                    ObjectName = sourceColumn.Name,
                    SourceDescription = DescribeColumn(sourceColumn),
                    TargetDescription = NotPresent,
                    Severity = SchemaDiffSeverity.Medium,
                    IsDestructive = false,
                    Operation = new AddColumnOperation(sourceColumn, ResolveColumnType(sourceColumn)),
                });
                continue;
            }

            bool typeDiff = !string.Equals(
                NormalizeColumnType(ResolveColumnType(sourceColumn)),
                NormalizeColumnType(ResolveColumnType(targetColumn)),
                StringComparison.OrdinalIgnoreCase);
            bool nullabilityDiff = sourceColumn.IsNullable != targetColumn.IsNullable;
            if (typeDiff || nullabilityDiff)
            {
                diffs.Add(new SchemaDifference
                {
                    Id = NextId(ref sequence),
                    Category = SchemaDiffCategory.Column,
                    ChangeKind = SchemaDiffChangeKind.AlterInTarget,
                    ObjectName = sourceColumn.Name,
                    SourceDescription = DescribeColumn(sourceColumn),
                    TargetDescription = DescribeColumn(targetColumn),
                    Severity = SchemaDiffSeverity.High,
                    IsDestructive = true,
                    Operation = new AlterColumnOperation(targetColumn.Name, ResolveColumnType(sourceColumn), sourceColumn.IsNullable),
                });
            }

            if (!string.Equals(NormalizeDefault(sourceColumn.DefaultValue), NormalizeDefault(targetColumn.DefaultValue), StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add(new SchemaDifference
                {
                    Id = NextId(ref sequence),
                    Category = SchemaDiffCategory.Column,
                    ChangeKind = SchemaDiffChangeKind.AlterInTarget,
                    ObjectName = sourceColumn.Name,
                    SourceDescription = $"default={sourceColumn.DefaultValue ?? string.Empty}",
                    TargetDescription = $"default={targetColumn.DefaultValue ?? string.Empty}",
                    Severity = SchemaDiffSeverity.Medium,
                    IsDestructive = false,
                    Operation = new SetColumnDefaultOperation(targetColumn.Name, sourceColumn.DefaultValue),
                });
            }

            if (!string.Equals((sourceColumn.Comment ?? string.Empty).Trim(), (targetColumn.Comment ?? string.Empty).Trim(), StringComparison.Ordinal))
            {
                diffs.Add(new SchemaDifference
                {
                    Id = NextId(ref sequence),
                    Category = SchemaDiffCategory.Column,
                    ChangeKind = SchemaDiffChangeKind.AlterInTarget,
                    ObjectName = sourceColumn.Name,
                    SourceDescription = $"comment={sourceColumn.Comment ?? string.Empty}",
                    TargetDescription = $"comment={targetColumn.Comment ?? string.Empty}",
                    Severity = SchemaDiffSeverity.Low,
                    IsDestructive = false,
                    Operation = new SetColumnCommentOperation(targetColumn.Name, sourceColumn.Comment),
                });
            }

            CompareColumnAttributes(sourceColumn, targetColumn, diffs, ref sequence);
        }

        foreach (ColumnMetadata targetColumn in target.Columns.OrderBy(static c => c.OrdinalPosition))
        {
            if (sourceNames.Contains(targetColumn.Name))
                continue;

            diffs.Add(new SchemaDifference
            {
                Id = NextId(ref sequence),
                Category = SchemaDiffCategory.Column,
                ChangeKind = SchemaDiffChangeKind.RemoveFromTarget,
                ObjectName = targetColumn.Name,
                SourceDescription = NotPresent,
                TargetDescription = DescribeColumn(targetColumn),
                Severity = SchemaDiffSeverity.High,
                IsDestructive = true,
                Operation = new DropColumnOperation(targetColumn.Name),
            });
        }
    }

    // Identity / generated / collation can't be safely auto-altered in most engines, so they are
    // surfaced as informational notes (no executable SQL) rather than risky ALTERs.
    private static void CompareColumnAttributes(
        ColumnMetadata source,
        ColumnMetadata target,
        List<SchemaDifference> diffs,
        ref int sequence)
    {
        if (source.IsAutoIncrement != target.IsAutoIncrement)
        {
            diffs.Add(BuildColumnAttributeNote(
                NextId(ref sequence), source.Name, "Identity",
                source.IsAutoIncrement ? "identity" : "nao-identity",
                target.IsAutoIncrement ? "identity" : "nao-identity"));
        }

        if (!string.Equals(
                NormalizeExpression(source.GeneratedExpression),
                NormalizeExpression(target.GeneratedExpression),
                StringComparison.Ordinal))
        {
            diffs.Add(BuildColumnAttributeNote(
                NextId(ref sequence), source.Name, "Generated",
                source.GeneratedExpression ?? "(nenhuma)",
                target.GeneratedExpression ?? "(nenhuma)"));
        }

        if (!string.Equals(
                (source.Collation ?? string.Empty).Trim(),
                (target.Collation ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            diffs.Add(BuildColumnAttributeNote(
                NextId(ref sequence), source.Name, "Collation",
                source.Collation ?? "(default)",
                target.Collation ?? "(default)"));
        }
    }

    private static SchemaDifference BuildColumnAttributeNote(
        string id,
        string columnName,
        string attribute,
        string sourceValue,
        string targetValue) =>
        new()
        {
            Id = id,
            Category = SchemaDiffCategory.Column,
            ChangeKind = SchemaDiffChangeKind.AlterInTarget,
            ObjectName = columnName,
            SourceDescription = $"{attribute.ToLowerInvariant()}={sourceValue}",
            TargetDescription = $"{attribute.ToLowerInvariant()}={targetValue}",
            Severity = SchemaDiffSeverity.Medium,
            IsDestructive = false,
            Operation = new ColumnAttributeNote(columnName, attribute),
        };

    private static string NormalizeExpression(string? expression) =>
        string.Concat((expression ?? string.Empty).Trim().ToLowerInvariant().Where(static ch => !char.IsWhiteSpace(ch)));

    // ── primary key ─────────────────────────────────────────────────────────────
    private static void ComparePrimaryKey(
        TableMetadata source,
        TableMetadata target,
        List<SchemaDifference> diffs,
        List<string> warnings,
        ref int sequence)
    {
        string[] sourcePk = ResolvePrimaryKeyColumns(source);
        string[] targetPk = ResolvePrimaryKeyColumns(target);

        // Column order is significant for a primary key, so compare in order.
        if (sourcePk.SequenceEqual(targetPk, StringComparer.OrdinalIgnoreCase))
            return;

        string? targetPkName = target.Indexes.FirstOrDefault(static i => i.IsPrimaryKey)?.Name;
        string? sourcePkName = source.Indexes.FirstOrDefault(static i => i.IsPrimaryKey)?.Name;

        if (targetPk.Length > 0 && string.IsNullOrWhiteSpace(targetPkName))
            warnings.Add("Primary key divergente sem nome resolvido para DROP automatico.");

        diffs.Add(new SchemaDifference
        {
            Id = NextId(ref sequence),
            Category = SchemaDiffCategory.PrimaryKey,
            ChangeKind = SchemaDiffChangeKind.AlterInTarget,
            ObjectName = "PK",
            SourceDescription = string.Join(", ", sourcePk),
            TargetDescription = string.Join(", ", targetPk),
            Severity = SchemaDiffSeverity.High,
            IsDestructive = targetPk.Length > 0,
            Operation = new RecreatePrimaryKeyOperation(
                DropExisting: targetPk.Length > 0,
                targetPk.Length > 0 ? targetPkName : null,
                sourcePk.Length > 0 ? sourcePkName : null,
                sourcePk),
        });
    }

    // ── unique constraints / indexes ─────────────────────────────────────────────
    private static void CompareUnique(
        TableMetadata source,
        TableMetadata target,
        List<SchemaDifference> diffs,
        ref int sequence)
    {
        Dictionary<string, IndexMetadata> sourceUnique = MapIndexes(source, unique: true);
        Dictionary<string, IndexMetadata> targetUnique = MapIndexes(target, unique: true);

        foreach ((string signature, IndexMetadata targetIndex) in targetUnique)
        {
            if (sourceUnique.ContainsKey(signature))
                continue;

            diffs.Add(new SchemaDifference
            {
                Id = NextId(ref sequence),
                Category = SchemaDiffCategory.Unique,
                ChangeKind = SchemaDiffChangeKind.RemoveFromTarget,
                ObjectName = targetIndex.Name,
                SourceDescription = NotPresent,
                TargetDescription = DescribeIndex(targetIndex),
                Severity = SchemaDiffSeverity.Medium,
                IsDestructive = true,
                Operation = new DropUniqueOperation(targetIndex.Name),
            });
        }

        foreach ((string signature, IndexMetadata sourceIndex) in sourceUnique)
        {
            if (targetUnique.ContainsKey(signature))
                continue;

            diffs.Add(new SchemaDifference
            {
                Id = NextId(ref sequence),
                Category = SchemaDiffCategory.Unique,
                ChangeKind = SchemaDiffChangeKind.AddToTarget,
                ObjectName = sourceIndex.Name,
                SourceDescription = DescribeIndex(sourceIndex),
                TargetDescription = NotPresent,
                Severity = SchemaDiffSeverity.Medium,
                IsDestructive = false,
                Operation = new AddUniqueOperation(sourceIndex.Name, sourceIndex.Columns),
            });
        }
    }

    // ── secondary indexes ────────────────────────────────────────────────────────
    private static void CompareIndexes(
        TableMetadata source,
        TableMetadata target,
        List<SchemaDifference> diffs,
        ref int sequence)
    {
        Dictionary<string, IndexMetadata> sourceIndexes = MapIndexes(source, unique: false);
        Dictionary<string, IndexMetadata> targetIndexes = MapIndexes(target, unique: false);

        foreach ((string signature, IndexMetadata targetIndex) in targetIndexes)
        {
            if (sourceIndexes.ContainsKey(signature))
                continue;

            diffs.Add(new SchemaDifference
            {
                Id = NextId(ref sequence),
                Category = SchemaDiffCategory.Index,
                ChangeKind = SchemaDiffChangeKind.RemoveFromTarget,
                ObjectName = targetIndex.Name,
                SourceDescription = NotPresent,
                TargetDescription = DescribeIndex(targetIndex),
                Severity = SchemaDiffSeverity.Medium,
                IsDestructive = true,
                Operation = new DropIndexOperation(targetIndex.Name),
            });
        }

        foreach ((string signature, IndexMetadata sourceIndex) in sourceIndexes)
        {
            if (targetIndexes.ContainsKey(signature))
                continue;

            diffs.Add(new SchemaDifference
            {
                Id = NextId(ref sequence),
                Category = SchemaDiffCategory.Index,
                ChangeKind = SchemaDiffChangeKind.AddToTarget,
                ObjectName = sourceIndex.Name,
                SourceDescription = DescribeIndex(sourceIndex),
                TargetDescription = NotPresent,
                Severity = SchemaDiffSeverity.Medium,
                IsDestructive = false,
                Operation = new CreateIndexOperation(sourceIndex.Name, sourceIndex.Columns),
            });
        }
    }

    // ── check constraints ────────────────────────────────────────────────────────
    private static void CompareChecks(
        TableMetadata source,
        TableMetadata target,
        List<SchemaDifference> diffs,
        ref int sequence)
    {
        Dictionary<string, CheckConstraintMetadata> sourceChecks = MapChecks(source);
        Dictionary<string, CheckConstraintMetadata> targetChecks = MapChecks(target);

        foreach ((string signature, CheckConstraintMetadata targetCheck) in targetChecks)
        {
            if (sourceChecks.ContainsKey(signature))
                continue;

            diffs.Add(new SchemaDifference
            {
                Id = NextId(ref sequence),
                Category = SchemaDiffCategory.Check,
                ChangeKind = SchemaDiffChangeKind.RemoveFromTarget,
                ObjectName = targetCheck.Name,
                SourceDescription = NotPresent,
                TargetDescription = DescribeCheck(targetCheck),
                Severity = SchemaDiffSeverity.Medium,
                IsDestructive = false,
                Operation = new DropCheckOperation(targetCheck.Name),
            });
        }

        foreach ((string signature, CheckConstraintMetadata sourceCheck) in sourceChecks)
        {
            if (targetChecks.ContainsKey(signature))
                continue;

            diffs.Add(new SchemaDifference
            {
                Id = NextId(ref sequence),
                Category = SchemaDiffCategory.Check,
                ChangeKind = SchemaDiffChangeKind.AddToTarget,
                ObjectName = sourceCheck.Name,
                SourceDescription = DescribeCheck(sourceCheck),
                TargetDescription = NotPresent,
                Severity = SchemaDiffSeverity.Medium,
                IsDestructive = false,
                Operation = new AddCheckOperation(sourceCheck.Name, sourceCheck.Expression),
            });
        }
    }

    // ── outbound foreign keys ────────────────────────────────────────────────────
    private static void CompareForeignKeys(
        TableMetadata source,
        TableMetadata target,
        List<SchemaDifference> diffs,
        ref int sequence)
    {
        Dictionary<string, CompositeForeignKey> sourceFks = MapForeignKeys(source.OutboundForeignKeys);
        Dictionary<string, CompositeForeignKey> targetFks = MapForeignKeys(target.OutboundForeignKeys);

        foreach ((string signature, CompositeForeignKey targetFk) in targetFks)
        {
            if (sourceFks.ContainsKey(signature))
                continue;

            diffs.Add(new SchemaDifference
            {
                Id = NextId(ref sequence),
                Category = SchemaDiffCategory.ForeignKey,
                ChangeKind = SchemaDiffChangeKind.RemoveFromTarget,
                ObjectName = targetFk.ConstraintName,
                SourceDescription = NotPresent,
                TargetDescription = DescribeForeignKey(targetFk),
                Severity = SchemaDiffSeverity.High,
                IsDestructive = true,
                Operation = new DropForeignKeyOperation(targetFk.ConstraintName),
            });
        }

        foreach ((string signature, CompositeForeignKey sourceFk) in sourceFks)
        {
            if (targetFks.ContainsKey(signature))
                continue;

            diffs.Add(new SchemaDifference
            {
                Id = NextId(ref sequence),
                Category = SchemaDiffCategory.ForeignKey,
                ChangeKind = SchemaDiffChangeKind.AddToTarget,
                ObjectName = sourceFk.ConstraintName,
                SourceDescription = DescribeForeignKey(sourceFk),
                TargetDescription = NotPresent,
                Severity = SchemaDiffSeverity.High,
                IsDestructive = false,
                Operation = new AddForeignKeyOperation(sourceFk),
            });
        }
    }

    // ── inbound foreign keys (external impact, informational only) ────────────────
    private static void CompareExternalImpact(
        TableMetadata source,
        TableMetadata target,
        List<SchemaDifference> diffs,
        ref int sequence)
    {
        Dictionary<string, CompositeForeignKey> sourceInbound = MapForeignKeys(source.InboundForeignKeys);
        Dictionary<string, CompositeForeignKey> targetInbound = MapForeignKeys(target.InboundForeignKeys);

        foreach ((string signature, CompositeForeignKey sourceFk) in sourceInbound)
        {
            if (targetInbound.ContainsKey(signature))
                continue;

            diffs.Add(BuildExternalImpact(NextId(ref sequence), sourceFk.ConstraintName, DescribeForeignKey(sourceFk), NotPresent));
        }

        foreach ((string signature, CompositeForeignKey targetFk) in targetInbound)
        {
            if (sourceInbound.ContainsKey(signature))
                continue;

            diffs.Add(BuildExternalImpact(NextId(ref sequence), targetFk.ConstraintName, NotPresent, DescribeForeignKey(targetFk)));
        }
    }

    private static SchemaDifference BuildExternalImpact(string id, string name, string sourceDescription, string targetDescription) =>
        new()
        {
            Id = id,
            Category = SchemaDiffCategory.ExternalDependency,
            ChangeKind = SchemaDiffChangeKind.AlterInTarget,
            ObjectName = name,
            SourceDescription = sourceDescription,
            TargetDescription = targetDescription,
            Severity = SchemaDiffSeverity.Medium,
            IsDestructive = false,
            Operation = new InformationalOperation(),
        };

    // ── shared helpers (moved from the ViewModel) ────────────────────────────────
    internal const string NotPresent = "(nao existe)";

    private static string NextId(ref int sequence) => $"diff_{sequence++}";

    internal static string ResolveColumnType(ColumnMetadata column)
    {
        string baseType = string.IsNullOrWhiteSpace(column.NativeType) ? column.DataType : column.NativeType;
        baseType = baseType.Trim();

        if (baseType.Contains('(', StringComparison.Ordinal))
            return baseType;

        string lower = baseType.ToLowerInvariant();

        if (column.MaxLength is int maxLength && maxLength > 0
            && (lower.Contains("char", StringComparison.Ordinal) || lower.Contains("binary", StringComparison.Ordinal)))
        {
            return $"{baseType}({maxLength})";
        }

        if (column.Precision is int precision && precision > 0
            && (lower.Contains("numeric", StringComparison.Ordinal) || lower.Contains("decimal", StringComparison.Ordinal)))
        {
            return column.Scale is int scale && scale > 0
                ? $"{baseType}({precision},{scale})"
                : $"{baseType}({precision})";
        }

        return baseType;
    }

    private static readonly (string From, string To)[] TypeAliases =
    [
        ("charactervarying", "varchar"),
        ("character", "char"),
        ("integer", "int"),
        ("int4", "int"),
        ("int8", "bigint"),
        ("int2", "smallint"),
        ("boolean", "bool"),
        ("timestampwithtimezone", "timestamptz"),
        ("timestampwithouttimezone", "timestamp"),
        ("doubleprecision", "double"),
    ];

    private static string NormalizeColumnType(string? type)
    {
        string normalized = (type ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        foreach ((string from, string to) in TypeAliases)
            normalized = normalized.Replace(from, to, StringComparison.Ordinal);

        return normalized;
    }

    private static string NormalizeDefault(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    internal static string[] ResolvePrimaryKeyColumns(TableMetadata table)
    {
        IndexMetadata? pkIndex = table.Indexes.FirstOrDefault(static i => i.IsPrimaryKey);
        if (pkIndex is not null && pkIndex.Columns.Count > 0)
            return pkIndex.Columns.Select(static c => c.Trim()).ToArray();

        return table.Columns
            .Where(static c => c.IsPrimaryKey)
            .OrderBy(static c => c.OrdinalPosition)
            .Select(static c => c.Name)
            .ToArray();
    }

    private static Dictionary<string, IndexMetadata> MapIndexes(TableMetadata table, bool unique)
    {
        var map = new Dictionary<string, IndexMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (IndexMetadata index in table.Indexes.Where(i => i.IsUnique == unique && !i.IsPrimaryKey))
            map[BuildIndexSignature(index.Columns)] = index;

        return map;
    }

    private static string BuildIndexSignature(IReadOnlyList<string> columns) =>
        string.Join("|", columns.Select(static c => c.Trim()).OrderBy(static c => c, StringComparer.OrdinalIgnoreCase));

    internal static Dictionary<string, CompositeForeignKey> MapForeignKeys(IEnumerable<ForeignKeyRelation> relations)
    {
        var map = new Dictionary<string, CompositeForeignKey>(StringComparer.OrdinalIgnoreCase);
        foreach (CompositeForeignKey fk in GroupForeignKeys(relations))
            map[BuildForeignKeySignature(fk)] = fk;

        return map;
    }

    private static IReadOnlyList<CompositeForeignKey> GroupForeignKeys(IEnumerable<ForeignKeyRelation> relations)
    {
        var groups = new List<CompositeForeignKey>();
        foreach (IGrouping<string, ForeignKeyRelation> group in relations.GroupBy(
                     fk => string.IsNullOrWhiteSpace(fk.ConstraintName)
                         ? $"{fk.ChildColumn}|{fk.ParentSchema}|{fk.ParentTable}|{fk.ParentColumn}"
                         : fk.ConstraintName,
                     StringComparer.OrdinalIgnoreCase))
        {
            ForeignKeyRelation[] ordered = group.OrderBy(static fk => fk.OrdinalPosition).ToArray();
            ForeignKeyRelation first = ordered[0];
            groups.Add(new CompositeForeignKey(
                first.ConstraintName,
                ordered.Select(static fk => fk.ChildColumn).ToArray(),
                first.ParentSchema,
                first.ParentTable,
                ordered.Select(static fk => fk.ParentColumn).ToArray(),
                first.OnDelete,
                first.OnUpdate));
        }

        return groups;
    }

    private static string BuildForeignKeySignature(CompositeForeignKey fk) =>
        string.Join("|",
            string.Join(",", fk.ChildColumns.Select(static c => c.Trim())),
            fk.ParentSchema.Trim(),
            fk.ParentTable.Trim(),
            string.Join(",", fk.ParentColumns.Select(static c => c.Trim())),
            fk.OnDelete,
            fk.OnUpdate);

    // Match checks by normalized expression so a logically identical check is not flagged
    // just because the two databases auto-named the constraint differently.
    private static Dictionary<string, CheckConstraintMetadata> MapChecks(TableMetadata table)
    {
        var map = new Dictionary<string, CheckConstraintMetadata>(StringComparer.Ordinal);
        foreach (CheckConstraintMetadata check in table.CheckConstraints)
        {
            string signature = NormalizeCheckExpression(check.Expression);
            if (signature.Length == 0)
                signature = check.Name.Trim().ToLowerInvariant();
            map[signature] = check;
        }

        return map;
    }

    private static string NormalizeCheckExpression(string? expression)
    {
        string normalized = (expression ?? string.Empty).Trim().ToLowerInvariant();
        while (normalized.StartsWith('(') && normalized.EndsWith(')'))
            normalized = normalized[1..^1].Trim();

        return string.Concat(normalized.Where(static ch => !char.IsWhiteSpace(ch)));
    }

    private static string DescribeCheck(CheckConstraintMetadata check) =>
        string.IsNullOrWhiteSpace(check.Expression) ? check.Name : $"{check.Name}: {check.Expression}";

    private static string DescribeColumn(ColumnMetadata column) =>
        $"{ResolveColumnType(column)} | {(column.IsNullable ? "NULL" : "NOT NULL")} | default={column.DefaultValue ?? string.Empty}";

    private static string DescribeIndex(IndexMetadata index) =>
        $"{index.Name}({string.Join(", ", index.Columns)})";

    private static string DescribeForeignKey(CompositeForeignKey fk)
    {
        string child = string.Join(", ", fk.ChildColumns);
        string parent = string.Join(", ", fk.ParentColumns);
        string parentTable = string.IsNullOrEmpty(fk.ParentSchema) ? fk.ParentTable : $"{fk.ParentSchema}.{fk.ParentTable}";
        return $"({child}) -> {parentTable} ({parent}) (on delete {fk.OnDelete}, on update {fk.OnUpdate})";
    }
}
