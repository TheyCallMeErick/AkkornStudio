using AkkornStudio.Ddl.SchemaAnalysis.Application.Rules;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Normalization;
using AkkornStudio.Metadata;

namespace AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;

public sealed record SchemaObservedPatterns(
    NamingConvention DominantNamingConvention,
    string? DominantPkPattern,
    string? DominantFkPattern
);

public sealed class SchemaPatternDetector
{
    private readonly SchemaNamingConventionValidator _validator;

    public SchemaPatternDetector()
    {
        _validator = new SchemaNamingConventionValidator();
    }

    public SchemaObservedPatterns DetectPatterns(DbMetadata metadata)
    {
        Dictionary<NamingConvention, int> conventionCounts = new();
        Dictionary<string, int> pkPatternCounts = new();
        Dictionary<string, int> fkPatternCounts = new();

        var conventionsToTest = new[]
        {
            NamingConvention.SnakeCase,
            NamingConvention.CamelCase,
            NamingConvention.PascalCase,
            NamingConvention.KebabCase
        };

        foreach (var table in metadata.AllTables)
        {
            // Table name pattern
            CountConvention(table.Name, conventionCounts, conventionsToTest);

            foreach (var col in table.Columns)
            {
                CountConvention(col.Name, conventionCounts, conventionsToTest);
            }

            // Primary Key pattern
            var pkCols = table.PrimaryKeyColumns;
            if (pkCols.Count == 1)
            {
                var pkName = pkCols[0].Name;
                string pattern;
                if (pkName.Equals("id", StringComparison.OrdinalIgnoreCase))
                {
                    pattern = "id";
                }
                else if (pkName.Equals($"{table.Name}Id", StringComparison.OrdinalIgnoreCase) ||
                         pkName.Equals($"{table.Name}_id", StringComparison.OrdinalIgnoreCase))
                {
                    pattern = "[table]_id";
                }
                else if (pkName.Equals($"Id{table.Name}", StringComparison.OrdinalIgnoreCase) ||
                         pkName.Equals($"id_{table.Name}", StringComparison.OrdinalIgnoreCase))
                {
                    pattern = "id_[table]";
                }
                else
                {
                    pattern = "custom";
                }

                if (!pkPatternCounts.ContainsKey(pattern)) pkPatternCounts[pattern] = 0;
                pkPatternCounts[pattern]++;
            }

            // Foreign Key pattern
            foreach (var fk in table.OutboundForeignKeys)
            {
                var fkName = fk.ChildColumn;
                string pattern;
                if (fkName.Equals($"{fk.ParentTable}Id", StringComparison.OrdinalIgnoreCase) ||
                    fkName.Equals($"{fk.ParentTable}_id", StringComparison.OrdinalIgnoreCase))
                {
                    pattern = "[target]_id";
                }
                else if (fkName.Equals($"Id{fk.ParentTable}", StringComparison.OrdinalIgnoreCase) ||
                         fkName.Equals($"id_{fk.ParentTable}", StringComparison.OrdinalIgnoreCase))
                {
                    pattern = "id_[target]";
                }
                else
                {
                    pattern = "custom";
                }

                if (!fkPatternCounts.ContainsKey(pattern)) fkPatternCounts[pattern] = 0;
                fkPatternCounts[pattern]++;
            }
        }

        NamingConvention dominantNaming = NamingConvention.MixedAllowed;
        if (conventionCounts.Count > 0)
        {
            dominantNaming = conventionCounts.OrderByDescending(kv => kv.Value).First().Key;
        }

        string? dominantPk = pkPatternCounts.Count > 0
            ? pkPatternCounts.OrderByDescending(kv => kv.Value).First().Key
            : null;

        string? dominantFk = fkPatternCounts.Count > 0
            ? fkPatternCounts.OrderByDescending(kv => kv.Value).First().Key
            : null;

        return new SchemaObservedPatterns(dominantNaming, dominantPk, dominantFk);
    }

    private void CountConvention(string name, Dictionary<NamingConvention, int> counts, NamingConvention[] conventions)
    {
        foreach (var conv in conventions)
        {
            if (_validator.IsValid(name, conv))
            {
                if (!counts.ContainsKey(conv)) counts[conv] = 0;
                counts[conv]++;
                break; // stop at first match
            }
        }
    }
}
