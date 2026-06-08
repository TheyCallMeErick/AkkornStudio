using System.Collections.ObjectModel;
using System.Text;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Normalization;
using AkkornStudio.Metadata;

namespace AkkornStudio.UI.ViewModels;

public sealed class SchemaTrendPatternItemViewModel : ViewModelBase
{
    private bool _isSelected;

    public string Label { get; init; } = string.Empty;
    public string CategoryKey { get; init; } = string.Empty;
    public string CategoryLabel { get; init; } = string.Empty;
    public int Count { get; init; }
    public double Percent { get; init; }
    public bool IsUnidentified { get; init; }
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public string CountText => Count.ToString();
    public string PercentText => $"{Percent:0.#}%";
}

public sealed class SchemaTrendContributionEntryViewModel
{
    public string ObjectType { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed partial class DdlSchemaAnalysisWorkspaceViewModel
{
    private const string TrendCategoryTableNaming = "table_naming";
    private const string TrendCategoryColumnNaming = "column_naming";
    private const string TrendCategoryConstraintNaming = "constraint_naming";
    private const string TrendCategoryIndexNaming = "index_naming";
    private const string TrendCategoryPkColumnPattern = "pk_column_pattern";
    private const string TrendCategoryPkConstraintPattern = "pk_constraint_pattern";
    private const string TrendCategoryFkColumnPattern = "fk_column_pattern";
    private const string TrendCategoryFkConstraintPattern = "fk_constraint_pattern";

    private readonly Dictionary<string, List<SchemaTrendContributionEntryViewModel>> _trendContributionLookup =
        new(StringComparer.OrdinalIgnoreCase);
    private SchemaTrendPatternItemViewModel? _selectedTrendPattern;

    public ObservableCollection<SchemaTrendPatternItemViewModel> TableNamingTrendItems { get; } = [];
    public ObservableCollection<SchemaTrendPatternItemViewModel> ColumnNamingTrendItems { get; } = [];
    public ObservableCollection<SchemaTrendPatternItemViewModel> ConstraintNamingTrendItems { get; } = [];
    public ObservableCollection<SchemaTrendPatternItemViewModel> IndexNamingTrendItems { get; } = [];
    public ObservableCollection<SchemaTrendPatternItemViewModel> PrimaryKeyColumnPatternItems { get; } = [];
    public ObservableCollection<SchemaTrendPatternItemViewModel> PrimaryKeyConstraintPatternItems { get; } = [];
    public ObservableCollection<SchemaTrendPatternItemViewModel> ForeignKeyColumnPatternItems { get; } = [];
    public ObservableCollection<SchemaTrendPatternItemViewModel> ForeignKeyConstraintPatternItems { get; } = [];
    public ObservableCollection<SchemaTrendContributionEntryViewModel> SelectedTrendContributionItems { get; } = [];

    public SchemaTrendPatternItemViewModel? SelectedTrendPattern
    {
        get => _selectedTrendPattern;
        private set
        {
            if (ReferenceEquals(_selectedTrendPattern, value))
                return;

            _selectedTrendPattern = value;
            RaisePropertyChanged(nameof(SelectedTrendPattern));
            RaisePropertyChanged(nameof(HasSelectedTrendContribution));
            RaisePropertyChanged(nameof(SelectedTrendContributionTitle));
            RaisePropertyChanged(nameof(SelectedTrendContributionSummary));
            ClearSelectedTrendContributionCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasSelectedTrendContribution => SelectedTrendPattern is not null;

    public string SelectedTrendContributionTitle => SelectedTrendPattern is null
        ? "Sem predominancia selecionada"
        : $"{SelectedTrendPattern.CategoryLabel}: {SelectedTrendPattern.Label}";

    public string SelectedTrendContributionSummary => SelectedTrendPattern is null
        ? "Selecione uma predominancia para listar os elementos que contribuiram para a estatistica."
        : $"{SelectedTrendContributionItems.Count} elemento(s) contribuinte(s).";

    public int UnidentifiedTableNamingCount => GetUnidentifiedCount(TableNamingTrendItems);
    public int UnidentifiedColumnNamingCount => GetUnidentifiedCount(ColumnNamingTrendItems);
    public int UnidentifiedConstraintNamingCount => GetUnidentifiedCount(ConstraintNamingTrendItems);
    public int UnidentifiedIndexNamingCount => GetUnidentifiedCount(IndexNamingTrendItems);

    public string DominantTableNamingPattern => GetDominantLabel(TableNamingTrendItems);
    public string DominantColumnNamingPattern => GetDominantLabel(ColumnNamingTrendItems);
    public string DominantConstraintNamingPattern => GetDominantLabel(ConstraintNamingTrendItems);
    public string DominantIndexNamingPattern => GetDominantLabel(IndexNamingTrendItems);
    public string DominantPkColumnPattern => GetDominantLabel(PrimaryKeyColumnPatternItems);
    public string DominantPkConstraintPattern => GetDominantLabel(PrimaryKeyConstraintPatternItems);
    public string DominantFkColumnPattern => GetDominantLabel(ForeignKeyColumnPatternItems);
    public string DominantFkConstraintPattern => GetDominantLabel(ForeignKeyConstraintPatternItems);

    public string BuildTrendsExportMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Tendencias de Estrutura");
        sb.AppendLine();
        sb.AppendLine($"- Metadata: {TotalTables} tabelas, {TotalColumns} colunas, {TotalForeignKeys} FKs, {TotalViews} views, {TotalSchemas} schemas");
        sb.AppendLine($"- Naming dominante (geral): {DominantNaming}");
        sb.AppendLine();

        AppendTrendSection(sb, "Naming de tabelas", TableNamingTrendItems);
        AppendTrendSection(sb, "Naming de colunas", ColumnNamingTrendItems);
        AppendTrendSection(sb, "Naming de constraints", ConstraintNamingTrendItems);
        AppendTrendSection(sb, "Naming de indices", IndexNamingTrendItems);
        AppendTrendSection(sb, "Padrao de PK (coluna)", PrimaryKeyColumnPatternItems);
        AppendTrendSection(sb, "Padrao de PK (constraint)", PrimaryKeyConstraintPatternItems);
        AppendTrendSection(sb, "Padrao de FK (coluna)", ForeignKeyColumnPatternItems);
        AppendTrendSection(sb, "Padrao de FK (constraint)", ForeignKeyConstraintPatternItems);
        AppendSelectedTrendContributionSection(sb);
        return sb.ToString();
    }

    public string BuildIssuesExportMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Issues de Estrutura");
        sb.AppendLine();
        sb.AppendLine($"- Total filtrado: {SchemaAnalysisPanel.FilteredTotalIssues}");
        sb.AppendLine($"- Criticas: {SchemaAnalysisPanel.FilteredCriticalCount}");
        sb.AppendLine($"- Alertas: {SchemaAnalysisPanel.FilteredWarningCount}");
        sb.AppendLine($"- Info: {SchemaAnalysisPanel.FilteredInfoCount}");
        sb.AppendLine();

        foreach (SchemaIssue issue in SchemaAnalysisPanel.VisibleIssues)
        {
            sb.AppendLine($"## {issue.Title}");
            sb.AppendLine($"- Severidade: {issue.Severity}");
            sb.AppendLine($"- Regra: {issue.RuleCode}");
            sb.AppendLine($"- Confianca: {issue.Confidence:P0}");
            sb.AppendLine($"- Alvo: {issue.TargetType}");
            sb.AppendLine($"- Schema: {issue.SchemaName ?? "-"}");
            sb.AppendLine($"- Tabela: {issue.TableName ?? "-"}");
            sb.AppendLine($"- Coluna: {issue.ColumnName ?? "-"}");
            sb.AppendLine($"- Constraint: {issue.ConstraintName ?? "-"}");
            sb.AppendLine($"- Mensagem: {issue.Message}");
            if (issue.Suggestions.Count > 0)
            {
                sb.AppendLine("- Sugestoes:");
                foreach (SchemaSuggestion suggestion in issue.Suggestions)
                    sb.AppendLine($"  - {suggestion.Title}: {suggestion.Description}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void RebuildTrendPatternInsights(IReadOnlyList<SchemaMetadata> schemas)
    {
        var tableConventionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var columnConventionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var constraintConventionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var indexConventionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pkColumnPatternCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pkConstraintPatternCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fkColumnPatternCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fkConstraintPatternCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var validator = new SchemaNamingConventionValidator();
        _trendContributionLookup.Clear();

        foreach (SchemaMetadata schema in schemas)
        {
            foreach (TableMetadata table in schema.Tables)
            {
                string tablePath = BuildPath(schema.Name, table.Name);
                string tableConvention = DetectConvention(table.Name, validator) switch
                {
                    NamingConvention.SnakeCase => "snake_case",
                    NamingConvention.CamelCase => "camelCase",
                    NamingConvention.PascalCase => "PascalCase",
                    NamingConvention.KebabCase => "kebab-case",
                    _ => "nao_identificado",
                };
                CountPattern(tableConvention, tableConventionCounts);
                AddTrendContribution(
                    TrendCategoryTableNaming,
                    tableConvention,
                    new SchemaTrendContributionEntryViewModel
                    {
                        ObjectType = "Tabela",
                        Path = tablePath,
                        Detail = table.Name
                    });

                foreach (ColumnMetadata column in table.Columns)
                {
                    string columnConvention = DetectConvention(column.Name, validator) switch
                    {
                        NamingConvention.SnakeCase => "snake_case",
                        NamingConvention.CamelCase => "camelCase",
                        NamingConvention.PascalCase => "PascalCase",
                        NamingConvention.KebabCase => "kebab-case",
                        _ => "nao_identificado",
                    };
                    CountPattern(columnConvention, columnConventionCounts);
                    AddTrendContribution(
                        TrendCategoryColumnNaming,
                        columnConvention,
                        new SchemaTrendContributionEntryViewModel
                        {
                            ObjectType = "Coluna",
                            Path = BuildPath(schema.Name, table.Name, column.Name),
                            Detail = column.Name
                        });
                }

                foreach (IndexMetadata index in table.Indexes)
                {
                    string indexConvention = DetectConvention(index.Name, validator) switch
                    {
                        NamingConvention.SnakeCase => "snake_case",
                        NamingConvention.CamelCase => "camelCase",
                        NamingConvention.PascalCase => "PascalCase",
                        NamingConvention.KebabCase => "kebab-case",
                        _ => "nao_identificado",
                    };
                    CountPattern(indexConvention, indexConventionCounts);
                    AddTrendContribution(
                        TrendCategoryIndexNaming,
                        indexConvention,
                        new SchemaTrendContributionEntryViewModel
                        {
                            ObjectType = "Indice",
                            Path = BuildPath(schema.Name, table.Name),
                            Detail = index.Name
                        });

                    if (index.IsPrimaryKey)
                    {
                        string pkConstraintPattern = ClassifyPkConstraintPattern(index.Name, table.Name);
                        CountPattern(pkConstraintPattern, pkConstraintPatternCounts);
                        AddTrendContribution(
                            TrendCategoryPkConstraintPattern,
                            pkConstraintPattern,
                            new SchemaTrendContributionEntryViewModel
                            {
                                ObjectType = "PK Constraint",
                                Path = BuildPath(schema.Name, table.Name),
                                Detail = index.Name
                            });

                        string constraintConvention = DetectConvention(index.Name, validator) switch
                        {
                            NamingConvention.SnakeCase => "snake_case",
                            NamingConvention.CamelCase => "camelCase",
                            NamingConvention.PascalCase => "PascalCase",
                            NamingConvention.KebabCase => "kebab-case",
                            _ => "nao_identificado",
                        };
                        CountPattern(constraintConvention, constraintConventionCounts);
                        AddTrendContribution(
                            TrendCategoryConstraintNaming,
                            constraintConvention,
                            new SchemaTrendContributionEntryViewModel
                            {
                                ObjectType = "Constraint",
                                Path = BuildPath(schema.Name, table.Name),
                                Detail = index.Name
                            });
                    }
                }

                IReadOnlyList<ColumnMetadata> pkColumns = table.PrimaryKeyColumns;
                if (pkColumns.Count == 1)
                {
                    string pkPattern = ClassifyPkColumnPattern(pkColumns[0].Name, table.Name);
                    CountPattern(pkPattern, pkColumnPatternCounts);
                    AddTrendContribution(
                        TrendCategoryPkColumnPattern,
                        pkPattern,
                        new SchemaTrendContributionEntryViewModel
                        {
                            ObjectType = "PK Coluna",
                            Path = BuildPath(schema.Name, table.Name, pkColumns[0].Name),
                            Detail = pkColumns[0].Name
                        });
                }
                else if (pkColumns.Count > 1)
                {
                    CountPattern("composite_pk", pkColumnPatternCounts);
                    AddTrendContribution(
                        TrendCategoryPkColumnPattern,
                        "composite_pk",
                        new SchemaTrendContributionEntryViewModel
                        {
                            ObjectType = "PK Coluna",
                            Path = BuildPath(schema.Name, table.Name),
                            Detail = "chave composta"
                        });
                }

                foreach (ForeignKeyRelation fk in table.OutboundForeignKeys)
                {
                    string fkColumnPattern = ClassifyFkColumnPattern(fk.ChildColumn, fk.ParentTable);
                    CountPattern(fkColumnPattern, fkColumnPatternCounts);
                    AddTrendContribution(
                        TrendCategoryFkColumnPattern,
                        fkColumnPattern,
                        new SchemaTrendContributionEntryViewModel
                        {
                            ObjectType = "FK Coluna",
                            Path = BuildPath(schema.Name, fk.ChildTable, fk.ChildColumn),
                            Detail = $"{fk.ChildColumn} -> {fk.ParentTable}"
                        });

                    string fkConstraintPattern = ClassifyFkConstraintPattern(fk.ConstraintName, fk.ChildTable, fk.ParentTable);
                    CountPattern(fkConstraintPattern, fkConstraintPatternCounts);
                    AddTrendContribution(
                        TrendCategoryFkConstraintPattern,
                        fkConstraintPattern,
                        new SchemaTrendContributionEntryViewModel
                        {
                            ObjectType = "FK Constraint",
                            Path = BuildPath(schema.Name, fk.ChildTable),
                            Detail = fk.ConstraintName
                        });

                    string constraintConvention = DetectConvention(fk.ConstraintName, validator) switch
                    {
                        NamingConvention.SnakeCase => "snake_case",
                        NamingConvention.CamelCase => "camelCase",
                        NamingConvention.PascalCase => "PascalCase",
                        NamingConvention.KebabCase => "kebab-case",
                        _ => "nao_identificado",
                    };
                    CountPattern(constraintConvention, constraintConventionCounts);
                    AddTrendContribution(
                        TrendCategoryConstraintNaming,
                        constraintConvention,
                        new SchemaTrendContributionEntryViewModel
                        {
                            ObjectType = "Constraint",
                            Path = BuildPath(schema.Name, fk.ChildTable),
                            Detail = fk.ConstraintName
                        });
                }
            }
        }

        FillTrendItems(TableNamingTrendItems, tableConventionCounts, TrendCategoryTableNaming, "Naming de tabelas");
        FillTrendItems(ColumnNamingTrendItems, columnConventionCounts, TrendCategoryColumnNaming, "Naming de colunas");
        FillTrendItems(ConstraintNamingTrendItems, constraintConventionCounts, TrendCategoryConstraintNaming, "Naming de constraints");
        FillTrendItems(IndexNamingTrendItems, indexConventionCounts, TrendCategoryIndexNaming, "Naming de indices");
        FillTrendItems(PrimaryKeyColumnPatternItems, pkColumnPatternCounts, TrendCategoryPkColumnPattern, "Padrao de PK (coluna)");
        FillTrendItems(PrimaryKeyConstraintPatternItems, pkConstraintPatternCounts, TrendCategoryPkConstraintPattern, "Padrao de PK (constraint)");
        FillTrendItems(ForeignKeyColumnPatternItems, fkColumnPatternCounts, TrendCategoryFkColumnPattern, "Padrao de FK (coluna)");
        FillTrendItems(ForeignKeyConstraintPatternItems, fkConstraintPatternCounts, TrendCategoryFkConstraintPattern, "Padrao de FK (constraint)");

        if (SelectedTrendPattern is not null)
            SelectTrendContribution(FindMatchingTrendItem(SelectedTrendPattern.CategoryKey, SelectedTrendPattern.Label));
        else
            ClearSelectedTrendContribution();

        RaiseTrendInsightsChanged();
    }

    private void ClearTrendPatternInsights()
    {
        _trendContributionLookup.Clear();
        TableNamingTrendItems.Clear();
        ColumnNamingTrendItems.Clear();
        ConstraintNamingTrendItems.Clear();
        IndexNamingTrendItems.Clear();
        PrimaryKeyColumnPatternItems.Clear();
        PrimaryKeyConstraintPatternItems.Clear();
        ForeignKeyColumnPatternItems.Clear();
        ForeignKeyConstraintPatternItems.Clear();
        ClearSelectedTrendContribution();
        RaiseTrendInsightsChanged();
    }

    private void RaiseTrendInsightsChanged()
    {
        RaisePropertyChanged(nameof(DominantTableNamingPattern));
        RaisePropertyChanged(nameof(DominantColumnNamingPattern));
        RaisePropertyChanged(nameof(DominantConstraintNamingPattern));
        RaisePropertyChanged(nameof(DominantIndexNamingPattern));
        RaisePropertyChanged(nameof(DominantPkColumnPattern));
        RaisePropertyChanged(nameof(DominantPkConstraintPattern));
        RaisePropertyChanged(nameof(DominantFkColumnPattern));
        RaisePropertyChanged(nameof(DominantFkConstraintPattern));
        RaisePropertyChanged(nameof(UnidentifiedTableNamingCount));
        RaisePropertyChanged(nameof(UnidentifiedColumnNamingCount));
        RaisePropertyChanged(nameof(UnidentifiedConstraintNamingCount));
        RaisePropertyChanged(nameof(UnidentifiedIndexNamingCount));
    }

    private static void CountConvention(string name, IDictionary<string, int> target, SchemaNamingConventionValidator validator)
    {
        string key = DetectConvention(name, validator) switch
        {
            NamingConvention.SnakeCase => "snake_case",
            NamingConvention.CamelCase => "camelCase",
            NamingConvention.PascalCase => "PascalCase",
            NamingConvention.KebabCase => "kebab-case",
            _ => "nao_identificado",
        };
        CountPattern(key, target);
    }

    private static NamingConvention? DetectConvention(string name, SchemaNamingConventionValidator validator)
    {
        NamingConvention[] options =
        [
            NamingConvention.SnakeCase,
            NamingConvention.CamelCase,
            NamingConvention.PascalCase,
            NamingConvention.KebabCase,
        ];

        foreach (NamingConvention option in options)
        {
            if (validator.IsValid(name, option))
                return option;
        }

        return null;
    }

    private static void CountPattern(string key, IDictionary<string, int> target)
    {
        if (!target.TryAdd(key, 1))
            target[key]++;
    }

    private static void FillTrendItems(
        ObservableCollection<SchemaTrendPatternItemViewModel> collection,
        IDictionary<string, int> counts,
        string categoryKey,
        string categoryLabel)
    {
        collection.Clear();
        int total = counts.Values.Sum();
        if (total <= 0)
            return;

        foreach ((string key, int value) in counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            double percent = (value * 100d) / total;
            collection.Add(new SchemaTrendPatternItemViewModel
            {
                Label = key,
                CategoryKey = categoryKey,
                CategoryLabel = categoryLabel,
                Count = value,
                Percent = percent,
                IsUnidentified = key.Contains("nao_identificado", StringComparison.OrdinalIgnoreCase) ||
                                 key.Contains("custom", StringComparison.OrdinalIgnoreCase),
                IsSelected = false,
            });
        }
    }

    private static string ClassifyPkColumnPattern(string pkColumnName, string tableName)
    {
        string pk = Canonicalize(pkColumnName);
        string table = Canonicalize(tableName);
        if (pk == "id")
            return "id";
        if (pk == table + "id")
            return "[table]_id";
        if (pk == "id" + table)
            return "id_[table]";
        return "custom";
    }

    private static string ClassifyPkConstraintPattern(string constraintName, string tableName)
    {
        string raw = Canonicalize(constraintName);
        string table = Canonicalize(tableName);
        if (raw == "pk" + table)
            return "pk_[table]";
        if (raw == table + "pk")
            return "[table]_pk";
        return "custom";
    }

    private static string ClassifyFkColumnPattern(string fkColumnName, string targetTableName)
    {
        string fkColumn = Canonicalize(fkColumnName);
        string target = Canonicalize(targetTableName);
        if (fkColumn == target + "id")
            return "[target]_id";
        if (fkColumn == "id" + target)
            return "id_[target]";
        return "custom";
    }

    private static string ClassifyFkConstraintPattern(string constraintName, string tableName, string targetTableName)
    {
        string value = Canonicalize(constraintName);
        string table = Canonicalize(tableName);
        string target = Canonicalize(targetTableName);
        if (value == "fk" + table + target)
            return "fk_[table]_[target]";
        if (value == "fk" + target + table)
            return "fk_[target]_[table]";
        return "custom";
    }

    private static string Canonicalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string GetDominantLabel(IEnumerable<SchemaTrendPatternItemViewModel> items)
    {
        SchemaTrendPatternItemViewModel? dominant = items
            .Where(item => !item.IsUnidentified)
            .OrderByDescending(item => item.Count)
            .FirstOrDefault();

        if (dominant is null)
            dominant = items.OrderByDescending(item => item.Count).FirstOrDefault();

        return dominant?.Label ?? "-";
    }

    private static int GetUnidentifiedCount(IEnumerable<SchemaTrendPatternItemViewModel> items)
    {
        return items.Where(item => item.IsUnidentified).Sum(item => item.Count);
    }

    private static void AppendTrendSection(
        StringBuilder sb,
        string sectionTitle,
        IEnumerable<SchemaTrendPatternItemViewModel> items)
    {
        sb.AppendLine($"## {sectionTitle}");
        IReadOnlyList<SchemaTrendPatternItemViewModel> list = items.ToList();
        if (list.Count == 0)
        {
            sb.AppendLine("- Sem dados.");
            sb.AppendLine();
            return;
        }

        foreach (SchemaTrendPatternItemViewModel item in list)
            sb.AppendLine($"- {item.Label}: {item.Count} ({item.PercentText})");
        sb.AppendLine();
    }

    private void AppendSelectedTrendContributionSection(StringBuilder sb)
    {
        if (SelectedTrendPattern is null || SelectedTrendContributionItems.Count == 0)
            return;

        sb.AppendLine("## Estado Atual Filtrado Pela Predominancia");
        sb.AppendLine($"- Categoria: {SelectedTrendPattern.CategoryLabel}");
        sb.AppendLine($"- Padrao: {SelectedTrendPattern.Label}");
        sb.AppendLine($"- Participacao: {SelectedTrendPattern.PercentText} ({SelectedTrendPattern.Count} ocorrencias)");
        sb.AppendLine($"- Elementos contribuintes: {SelectedTrendContributionItems.Count}");
        sb.AppendLine();

        foreach (SchemaTrendContributionEntryViewModel item in SelectedTrendContributionItems)
        {
            if (string.IsNullOrWhiteSpace(item.Detail))
                sb.AppendLine($"- [{item.ObjectType}] {item.Path}");
            else
                sb.AppendLine($"- [{item.ObjectType}] {item.Path} :: {item.Detail}");
        }

        sb.AppendLine();
    }

    public void SelectTrendContribution(SchemaTrendPatternItemViewModel? item)
    {
        if (item is null)
        {
            ClearSelectedTrendContribution();
            return;
        }

        foreach (SchemaTrendPatternItemViewModel trendItem in EnumerateAllTrendItems())
            trendItem.IsSelected = false;

        item.IsSelected = true;
        SelectedTrendPattern = item;
        SelectedTrendContributionItems.Clear();

        string key = BuildTrendContributionLookupKey(item.CategoryKey, item.Label);
        if (_trendContributionLookup.TryGetValue(key, out List<SchemaTrendContributionEntryViewModel>? entries))
        {
            var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SchemaTrendContributionEntryViewModel entry in entries
                         .OrderBy(e => e.ObjectType, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(e => e.Detail, StringComparer.OrdinalIgnoreCase))
            {
                string dedupKey = $"{entry.ObjectType}|{entry.Path}|{entry.Detail}";
                if (!dedup.Add(dedupKey))
                    continue;

                SelectedTrendContributionItems.Add(entry);
            }
        }

        RaisePropertyChanged(nameof(SelectedTrendContributionSummary));
    }

    public void ClearSelectedTrendContribution()
    {
        foreach (SchemaTrendPatternItemViewModel trendItem in EnumerateAllTrendItems())
            trendItem.IsSelected = false;

        SelectedTrendContributionItems.Clear();
        SelectedTrendPattern = null;
    }

    private void AddTrendContribution(string categoryKey, string label, SchemaTrendContributionEntryViewModel entry)
    {
        string lookupKey = BuildTrendContributionLookupKey(categoryKey, label);
        if (!_trendContributionLookup.TryGetValue(lookupKey, out List<SchemaTrendContributionEntryViewModel>? list))
        {
            list = [];
            _trendContributionLookup[lookupKey] = list;
        }

        list.Add(entry);
    }

    private SchemaTrendPatternItemViewModel? FindMatchingTrendItem(string categoryKey, string label)
    {
        return EnumerateAllTrendItems()
            .FirstOrDefault(item =>
                string.Equals(item.CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Label, label, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<SchemaTrendPatternItemViewModel> EnumerateAllTrendItems()
    {
        foreach (SchemaTrendPatternItemViewModel item in TableNamingTrendItems)
            yield return item;
        foreach (SchemaTrendPatternItemViewModel item in ColumnNamingTrendItems)
            yield return item;
        foreach (SchemaTrendPatternItemViewModel item in ConstraintNamingTrendItems)
            yield return item;
        foreach (SchemaTrendPatternItemViewModel item in IndexNamingTrendItems)
            yield return item;
        foreach (SchemaTrendPatternItemViewModel item in PrimaryKeyColumnPatternItems)
            yield return item;
        foreach (SchemaTrendPatternItemViewModel item in PrimaryKeyConstraintPatternItems)
            yield return item;
        foreach (SchemaTrendPatternItemViewModel item in ForeignKeyColumnPatternItems)
            yield return item;
        foreach (SchemaTrendPatternItemViewModel item in ForeignKeyConstraintPatternItems)
            yield return item;
    }

    private static string BuildTrendContributionLookupKey(string categoryKey, string label) =>
        $"{categoryKey}::{label}";

    private static string BuildPath(string? schema, string? table, string? column = null)
    {
        string safeSchema = string.IsNullOrWhiteSpace(schema) ? "-" : schema;
        string safeTable = string.IsNullOrWhiteSpace(table) ? "-" : table;
        if (string.IsNullOrWhiteSpace(column))
            return $"{safeSchema}.{safeTable}";

        return $"{safeSchema}.{safeTable}.{column}";
    }
}
