using System.Collections.ObjectModel;
using System.Data;
using Avalonia.Media;
using AkkornStudio.Core;
using AkkornStudio.Ddl.Compare;
using AkkornStudio.Metadata;
using AkkornStudio.Providers.Dialects;
using AkkornStudio.Registry;
using AkkornStudio.UI.Services.ConnectionManager.Models;

namespace AkkornStudio.UI.ViewModels;

/// <summary>Whether the workspace compares table structure (DDL) or row data (values).</summary>
public enum DdlSchemaCompareComparisonKind
{
    Structure,
    Data,
}

/// <summary>One row-level difference projected for the data-compare grid.</summary>
public sealed class DdlSchemaCompareRowDiffItemViewModel
{
    public DdlSchemaCompareRowDiffItemViewModel(RowDifference difference)
    {
        Kind = difference.Kind;
        KeyDisplay = difference.KeyDisplay;

        Detail = difference.Kind switch
        {
            RowDifferenceKind.InsertIntoTarget => "Inserir no destino",
            RowDifferenceKind.DeleteFromTarget => "Remover do destino",
            RowDifferenceKind.UpdateInTarget => "Alterar: " + string.Join(", ", difference.ChangedColumns),
            _ => "Igual",
        };

        KindLabel = difference.Kind switch
        {
            RowDifferenceKind.InsertIntoTarget => "INSERT",
            RowDifferenceKind.DeleteFromTarget => "DELETE",
            RowDifferenceKind.UpdateInTarget => "UPDATE",
            _ => "=",
        };
    }

    public RowDifferenceKind Kind { get; }

    public string KindLabel { get; }

    public string KeyDisplay { get; }

    public string Detail { get; }

    public bool IsInsert => Kind == RowDifferenceKind.InsertIntoTarget;

    public bool IsUpdate => Kind == RowDifferenceKind.UpdateInTarget;

    public bool IsDelete => Kind == RowDifferenceKind.DeleteFromTarget;
}

/// <summary>A shared column the user can toggle in/out of the data-matching key.</summary>
public sealed class DataKeyColumnOption : ViewModelBase
{
    private bool _isKey;

    public DataKeyColumnOption(string name, bool isKey)
    {
        Name = name;
        _isKey = isKey;
    }

    public string Name { get; }

    public bool IsKey
    {
        get => _isKey;
        set => Set(ref _isKey, value);
    }
}

public sealed partial class DdlSchemaCompareWorkspaceViewModel
{
    // "Small tables with few values" guardrail: never pull more than this per side.
    private const int DataRowCap = 5000;

    private readonly DataComparer _dataComparer = new();
    private readonly DataSyncScriptGenerator _dataGenerator = new();
    private readonly IProviderRegistry _dialectRegistry = ProviderRegistry.CreateDefault();

    private DataComparison? _lastDataComparison;
    private string _lastDataTargetSchema = string.Empty;
    private string _lastDataTargetTable = string.Empty;
    private string _dataKeySignature = string.Empty;

    private DdlSchemaCompareComparisonKind _comparisonKind = DdlSchemaCompareComparisonKind.Structure;
    private string _dataSummary = "Sem comparacao de dados executada.";
    private string _generatedDataSql = string.Empty;
    private bool _commentDestructiveDeletes = true;
    private bool _hasDataResult;
    private bool _wrapDataSql;

    public ObservableCollection<DdlSchemaCompareRowDiffItemViewModel> DataDifferences { get; } = [];

    public ObservableCollection<DataKeyColumnOption> DataKeyColumns { get; } = [];

    public bool HasDataKeyOptions => DataKeyColumns.Count > 0;

    public RelayCommand CopyDataSqlCommand { get; private set; } = null!;

    public DdlSchemaCompareComparisonKind ComparisonKind
    {
        get => _comparisonKind;
        set
        {
            if (!Set(ref _comparisonKind, value))
                return;

            RaisePropertyChanged(nameof(ComparisonKindIndex));
            RaisePropertyChanged(nameof(IsDataComparison));
            RaisePropertyChanged(nameof(IsStructureComparison));
            RecomputeCompatibility();
        }
    }

    public int ComparisonKindIndex
    {
        get => ComparisonKind == DdlSchemaCompareComparisonKind.Data ? 1 : 0;
        set => ComparisonKind = value == 1
            ? DdlSchemaCompareComparisonKind.Data
            : DdlSchemaCompareComparisonKind.Structure;
    }

    public bool IsDataComparison => ComparisonKind == DdlSchemaCompareComparisonKind.Data;

    public bool IsStructureComparison => ComparisonKind == DdlSchemaCompareComparisonKind.Structure;

    public string DataSummary
    {
        get => _dataSummary;
        private set => Set(ref _dataSummary, value);
    }

    public string GeneratedDataSql
    {
        get => _generatedDataSql;
        private set
        {
            if (!Set(ref _generatedDataSql, value))
                return;

            RaisePropertyChanged(nameof(HasGeneratedDataSql));
            CopyDataSqlCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasGeneratedDataSql => !string.IsNullOrWhiteSpace(GeneratedDataSql);

    public bool HasDataResult
    {
        get => _hasDataResult;
        private set
        {
            if (!Set(ref _hasDataResult, value))
                return;

            RaisePropertyChanged(nameof(HasDataStatements));
            RaisePropertyChanged(nameof(DataIsInSync));
        }
    }

    public bool HasDataStatements => DataStatementCount > 0;

    public bool DataIsInSync => HasDataResult && DataStatementCount == 0;

    public bool WrapDataSql
    {
        get => _wrapDataSql;
        set
        {
            if (!Set(ref _wrapDataSql, value))
                return;

            RaisePropertyChanged(nameof(DataSqlWrapping));
        }
    }

    public TextWrapping DataSqlWrapping => WrapDataSql ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public bool CommentDestructiveDeletes
    {
        get => _commentDestructiveDeletes;
        set
        {
            if (!Set(ref _commentDestructiveDeletes, value))
                return;

            RegenerateDataSql();
        }
    }

    public int DataInsertCount => _lastDataComparison?.InsertCount ?? 0;
    public int DataUpdateCount => _lastDataComparison?.UpdateCount ?? 0;
    public int DataDeleteCount => _lastDataComparison?.DeleteCount ?? 0;
    public int DataUnchangedCount => _lastDataComparison?.UnchangedCount ?? 0;
    public int DataStatementCount => DataInsertCount + DataUpdateCount + DataDeleteCount;

    private void InitializeDataCommands()
    {
        CopyDataSqlCommand = new RelayCommand(
            () => CopySqlRequested?.Invoke(GeneratedDataSql),
            () => HasGeneratedDataSql);
    }

    /// <summary>
    /// Data-mode comparison: fetch rows from both endpoints, diff them by key and build sync DML.
    /// Only supports concrete table-to-table selections (no schema/database-wide).
    /// </summary>
    private async Task CompareDataAsync()
    {
        TableMetadata? leftTable = ResolveSelectedTable(EndpointSide.Left);
        TableMetadata? rightTable = ResolveSelectedTable(EndpointSide.Right);
        if (leftTable is null || rightTable is null)
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = "Selecione as duas tabelas para comparar dados.";
            return;
        }

        bool leftIsSource = SelectedDirection == DdlSchemaCompareDirection.LeftToRight;
        EndpointSide sourceSide = leftIsSource ? EndpointSide.Left : EndpointSide.Right;
        EndpointSide targetSide = leftIsSource ? EndpointSide.Right : EndpointSide.Left;
        TableMetadata sourceTable = leftIsSource ? leftTable : rightTable;
        TableMetadata targetTable = leftIsSource ? rightTable : leftTable;

        ConnectionProfile? sourceProfile = sourceSide == EndpointSide.Left ? LeftSelectedProfile : RightSelectedProfile;
        ConnectionProfile? targetProfile = targetSide == EndpointSide.Left ? LeftSelectedProfile : RightSelectedProfile;
        if (sourceProfile is null || targetProfile is null)
            return;

        string sourceDb = GetSelectedDatabase(sourceSide) ?? sourceProfile.Database;
        string targetDb = GetSelectedDatabase(targetSide) ?? targetProfile.Database;

        try
        {
            SetLoading(EndpointSide.Left, true);
            SetLoading(EndpointSide.Right, true);

            RefreshDataKeyOptionsForSelection();

            (DataRowSet sourceRows, bool sourceCapped) = await FetchRowsAsync(sourceProfile, sourceDb, sourceTable);
            (DataRowSet targetRows, bool targetCapped) = await FetchRowsAsync(targetProfile, targetDb, targetTable);

            // User-chosen matching key (defaults to the shared PK); empty falls back to whole-row matching.
            IReadOnlyList<string> keyColumns = ResolveSelectedKeyColumns();
            DataComparison comparison = _dataComparer.Compare(sourceRows, targetRows, keyColumns);

            _lastDataComparison = comparison;
            _lastDataTargetSchema = targetTable.Schema;
            _lastDataTargetTable = targetTable.Name;

            foreach (string warning in comparison.Warnings)
                CompareWarnings.Add(warning);
            if (sourceCapped)
                CompareWarnings.Add($"Origem truncada em {DataRowCap} linhas; resultado parcial.");
            if (targetCapped)
                CompareWarnings.Add($"Destino truncado em {DataRowCap} linhas; resultado parcial.");

            DataDifferences.Clear();
            foreach (RowDifference difference in comparison.Differences.Where(static d => d.Kind != RowDifferenceKind.Unchanged))
                DataDifferences.Add(new DdlSchemaCompareRowDiffItemViewModel(difference));

            DataSummary = comparison.IsInSync
                ? "Dados sincronizados: nenhuma diferenca de valores."
                : $"Dados: {comparison.InsertCount} insert(s), {comparison.UpdateCount} update(s), {comparison.DeleteCount} delete(s), {comparison.UnchangedCount} iguais.";

            RegenerateDataSql();
            RaiseDataCountsChanged();
            HasDataResult = true;
        }
        catch (Exception ex)
        {
            HasDataResult = false;
            DataSummary = $"Falha na comparacao de dados: {ex.Message}";
        }
        finally
        {
            SetLoading(EndpointSide.Left, false);
            SetLoading(EndpointSide.Right, false);
        }
    }

    private void RegenerateDataSql()
    {
        if (_lastDataComparison is null)
        {
            GeneratedDataSql = string.Empty;
            return;
        }

        GeneratedDataSql = _dataGenerator.Generate(
            _lastDataComparison,
            _compareProvider,
            _lastDataTargetSchema,
            _lastDataTargetTable,
            new DataSyncOptions(CommentDestructive: CommentDestructiveDeletes));
    }

    private void RaiseDataCountsChanged()
    {
        RaisePropertyChanged(nameof(DataInsertCount));
        RaisePropertyChanged(nameof(DataUpdateCount));
        RaisePropertyChanged(nameof(DataDeleteCount));
        RaisePropertyChanged(nameof(DataUnchangedCount));
        RaisePropertyChanged(nameof(DataStatementCount));
        RaisePropertyChanged(nameof(HasDataStatements));
        RaisePropertyChanged(nameof(DataIsInSync));
    }

    /// <summary>
    /// Rebuilds the available matching-key columns (the shared columns) whenever the selected
    /// source/target table pair changes, defaulting the checked set to the shared primary key.
    /// The signature guard preserves the user's manual choice while the same tables stay selected.
    /// </summary>
    internal void RefreshDataKeyOptionsForSelection()
    {
        TableMetadata? leftTable = ResolveSelectedTable(EndpointSide.Left);
        TableMetadata? rightTable = ResolveSelectedTable(EndpointSide.Right);
        if (leftTable is null || rightTable is null)
        {
            if (DataKeyColumns.Count == 0)
                return;

            _dataKeySignature = string.Empty;
            DataKeyColumns.Clear();
            RaisePropertyChanged(nameof(HasDataKeyOptions));
            return;
        }

        bool leftIsSource = SelectedDirection == DdlSchemaCompareDirection.LeftToRight;
        TableMetadata source = leftIsSource ? leftTable : rightTable;
        TableMetadata target = leftIsSource ? rightTable : leftTable;

        string signature = $"{source.FullName}->{target.FullName}";
        if (string.Equals(signature, _dataKeySignature, StringComparison.Ordinal))
            return;

        _dataKeySignature = signature;

        var targetColumns = new HashSet<string>(target.Columns.Select(static c => c.Name), StringComparer.OrdinalIgnoreCase);
        var defaultKey = new HashSet<string>(DataComparer.ResolveKeyColumns(source, target), StringComparer.OrdinalIgnoreCase);

        DataKeyColumns.Clear();
        foreach (ColumnMetadata column in source.Columns.Where(c => targetColumns.Contains(c.Name)))
            DataKeyColumns.Add(new DataKeyColumnOption(column.Name, defaultKey.Contains(column.Name)));

        RaisePropertyChanged(nameof(HasDataKeyOptions));
    }

    private IReadOnlyList<string> ResolveSelectedKeyColumns()
    {
        return DataKeyColumns.Where(static option => option.IsKey).Select(static option => option.Name).ToArray();
    }

    private async Task<(DataRowSet Rows, bool Capped)> FetchRowsAsync(
        ConnectionProfile profile,
        string database,
        TableMetadata table)
    {
        ConnectionConfig config = profile.ToConnectionConfig() with { Database = database };
        ISqlDialect dialect = _dialectRegistry.GetDialect(profile.Provider);
        string qualified = QualifyForSelect(profile.Provider, dialect, table.Schema, table.Name);
        string sql = $"SELECT * FROM {qualified}";

        await using IDbOrchestrator orchestrator = DbOrchestratorFactory.CreateDefault().Create(config);
        PreviewResult result = await orchestrator.ExecutePreviewAsync(sql, DataRowCap + 1, CancellationToken.None);

        if (!result.Success || result.Data is null)
            throw new InvalidOperationException(result.ErrorMessage ?? "Falha ao ler linhas.");

        return ConvertToRowSet(result.Data);
    }

    private static (DataRowSet Rows, bool Capped) ConvertToRowSet(DataTable table)
    {
        string[] columns = table.Columns.Cast<DataColumn>().Select(static c => c.ColumnName).ToArray();

        bool capped = table.Rows.Count > DataRowCap;
        IEnumerable<DataRow> rows = capped ? table.Rows.Cast<DataRow>().Take(DataRowCap) : table.Rows.Cast<DataRow>();

        var projected = rows
            .Select(static row => (IReadOnlyList<object?>)row.ItemArray)
            .ToList();

        return (new DataRowSet(columns, projected), capped);
    }

    private static string QualifyForSelect(DatabaseProvider provider, ISqlDialect dialect, string schema, string table)
    {
        string resolvedSchema = string.IsNullOrWhiteSpace(schema)
            ? provider switch
            {
                DatabaseProvider.Postgres => "public",
                DatabaseProvider.SqlServer => "dbo",
                DatabaseProvider.SQLite => "main",
                _ => string.Empty,
            }
            : schema.Trim();

        return string.IsNullOrWhiteSpace(resolvedSchema)
            ? dialect.QuoteIdentifier(table.Trim())
            : $"{dialect.QuoteIdentifier(resolvedSchema)}.{dialect.QuoteIdentifier(table.Trim())}";
    }

    /// <summary>Test hook: runs the data comparison from in-memory row sets, bypassing the DB fetch.</summary>
    internal void LoadDataComparisonForTesting(
        DataRowSet source,
        DataRowSet target,
        IReadOnlyList<string> keyColumns,
        DatabaseProvider provider,
        string targetSchema,
        string targetTable)
    {
        _compareProvider = provider;
        DataComparison comparison = _dataComparer.Compare(source, target, keyColumns);
        _lastDataComparison = comparison;
        _lastDataTargetSchema = targetSchema;
        _lastDataTargetTable = targetTable;

        DataDifferences.Clear();
        foreach (RowDifference difference in comparison.Differences.Where(static d => d.Kind != RowDifferenceKind.Unchanged))
            DataDifferences.Add(new DdlSchemaCompareRowDiffItemViewModel(difference));

        RegenerateDataSql();
        RaiseDataCountsChanged();
        HasDataResult = true;
    }

    private void ResetDataResults()
    {
        _lastDataComparison = null;
        DataDifferences.Clear();
        GeneratedDataSql = string.Empty;
        HasDataResult = false;
        DataSummary = "Sem comparacao de dados executada.";
        RaiseDataCountsChanged();
    }
}
