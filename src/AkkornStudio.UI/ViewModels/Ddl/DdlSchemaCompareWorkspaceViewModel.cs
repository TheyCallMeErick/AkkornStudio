using System.Collections.ObjectModel;
using AkkornStudio.Core;
using AkkornStudio.Ddl.Compare;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.ConnectionManager.Models;

namespace AkkornStudio.UI.ViewModels;

public enum DdlSchemaCompareDirection
{
    LeftToRight,
    RightToLeft,
}

public sealed partial class DdlSchemaCompareWorkspaceViewModel : ViewModelBase, IDisposable
{
    // Pure comparison + per-dialect generation live in the core engine; this VM is an adapter.
    private readonly TableComparer _comparer = new();
    private readonly SchemaComparer _schemaComparer = new();
    private readonly SyncScriptGenerator _generator = new();

    // One difference plus the table it targets. Both single-table and whole-schema comparisons
    // flatten into this list so the wizard and SQL generation are mode-agnostic.
    internal readonly record struct FlatDifference(string TargetSchema, string TargetTable, SchemaDifference Difference);

    // Sentinel table selection that switches the comparison to whole-schema mode.
    internal const string AllTablesOption = "(todas as tabelas)";

    // Sentinel schema selection that switches the comparison to whole-database mode.
    internal const string AllSchemasOption = "(todos os schemas)";

    // Last comparison result + context, kept so generation options can recompute SQL live
    // without a new round-trip and without resetting the user's review state.
    private List<FlatDifference> _flatDifferences = [];
    private DatabaseProvider _compareProvider;

    private readonly ConnectionManagerViewModel _connectionManager;
    private readonly Dictionary<string, DbMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _databaseCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _leftLoadCts;
    private CancellationTokenSource? _rightLoadCts;

    private ConnectionProfile? _leftSelectedProfile;
    private ConnectionProfile? _rightSelectedProfile;
    private string? _leftSelectedDatabase;
    private string? _rightSelectedDatabase;
    private string? _leftSelectedSchema;
    private string? _rightSelectedSchema;
    private string? _leftSelectedTable;
    private string? _rightSelectedTable;
    private bool _isLeftLoading;
    private bool _isRightLoading;
    private string _leftStatus = "Selecione uma conexao.";
    private string _rightStatus = "Selecione uma conexao.";
    private string _compatibilityMessage = "Selecione os dois lados para comparar.";
    private bool _isCompatibilityBlocked = true;
    private DdlSchemaCompareDirection _selectedDirection = DdlSchemaCompareDirection.LeftToRight;
    private string _summary = "Sem comparacao executada.";
    private string _generatedSql = string.Empty;

    private DbMetadata? _leftMetadata;
    private DbMetadata? _rightMetadata;

    public DdlSchemaCompareWorkspaceViewModel(ConnectionManagerViewModel connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));

        CompareCommand = new RelayCommand(() => _ = CompareAsync(), () => !IsBusy && !IsCompatibilityBlocked);
        RefreshBothCommand = new RelayCommand(() => _ = RefreshBothAsync(), () => !IsBusy);
        CopySqlCommand = new RelayCommand(() => CopySqlRequested?.Invoke(GeneratedSql), () => HasGeneratedSql);
        OpenConnectionManagerCommand = new RelayCommand(OpenConnectionManager, () => !IsBusy);

        InitializeWizardState();
        InitializeDataCommands();
        _connectionManager.ProfilesChanged += HandleProfilesChanged;
        RefreshProfiles();
    }

    public event Action<string>? CopySqlRequested;

    public RelayCommand CompareCommand { get; }

    public RelayCommand RefreshBothCommand { get; }

    public RelayCommand CopySqlCommand { get; }

    public RelayCommand OpenConnectionManagerCommand { get; }

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    public ObservableCollection<string> LeftDatabases { get; } = [];

    public ObservableCollection<string> RightDatabases { get; } = [];

    public ObservableCollection<string> LeftSchemas { get; } = [];

    public ObservableCollection<string> RightSchemas { get; } = [];

    public ObservableCollection<string> LeftTables { get; } = [];

    public ObservableCollection<string> RightTables { get; } = [];

    public ObservableCollection<string> CompareWarnings { get; } = [];

    public bool HasAvailableProfiles => Profiles.Count > 0;

    public string NoConnectionsWarningText =>
        "Nenhuma conexao cadastrada. Adicione uma conexao para comparar tabelas.";

    public IReadOnlyList<DdlSchemaCompareDirection> DirectionOptions { get; } = [
        DdlSchemaCompareDirection.LeftToRight,
        DdlSchemaCompareDirection.RightToLeft,
    ];

    public ConnectionProfile? LeftSelectedProfile
    {
        get => _leftSelectedProfile;
        set
        {
            if (!Set(ref _leftSelectedProfile, value))
                return;

            _ = LoadLeftContextAsync(forceRefresh: false);
            RecomputeCompatibility();
        }
    }

    public ConnectionProfile? RightSelectedProfile
    {
        get => _rightSelectedProfile;
        set
        {
            if (!Set(ref _rightSelectedProfile, value))
                return;

            _ = LoadRightContextAsync(forceRefresh: false);
            RecomputeCompatibility();
        }
    }

    public string? LeftSelectedDatabase
    {
        get => _leftSelectedDatabase;
        set
        {
            if (!Set(ref _leftSelectedDatabase, value))
                return;

            _ = LoadLeftMetadataAsync(forceRefresh: false);
        }
    }

    public string? RightSelectedDatabase
    {
        get => _rightSelectedDatabase;
        set
        {
            if (!Set(ref _rightSelectedDatabase, value))
                return;

            _ = LoadRightMetadataAsync(forceRefresh: false);
        }
    }

    public string? LeftSelectedSchema
    {
        get => _leftSelectedSchema;
        set
        {
            if (!Set(ref _leftSelectedSchema, value))
                return;

            RebuildTables(EndpointSide.Left);
            RecomputeCompatibility();
        }
    }

    public string? RightSelectedSchema
    {
        get => _rightSelectedSchema;
        set
        {
            if (!Set(ref _rightSelectedSchema, value))
                return;

            RebuildTables(EndpointSide.Right);
            RecomputeCompatibility();
        }
    }

    public string? LeftSelectedTable
    {
        get => _leftSelectedTable;
        set
        {
            if (!Set(ref _leftSelectedTable, value))
                return;

            RecomputeCompatibility();
        }
    }

    public string? RightSelectedTable
    {
        get => _rightSelectedTable;
        set
        {
            if (!Set(ref _rightSelectedTable, value))
                return;

            RecomputeCompatibility();
        }
    }

    public bool IsLeftLoading
    {
        get => _isLeftLoading;
        private set
        {
            if (!Set(ref _isLeftLoading, value))
                return;

            RaisePropertyChanged(nameof(IsBusy));
            CompareCommand.NotifyCanExecuteChanged();
            RefreshBothCommand.NotifyCanExecuteChanged();
            CompareAndContinueCommand?.NotifyCanExecuteChanged();
            RefreshMetadataCommand?.NotifyCanExecuteChanged();
            SwapSourceTargetCommand?.NotifyCanExecuteChanged();
            OpenConnectionManagerCommand.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(CanRunComparison));
        }
    }

    public bool IsRightLoading
    {
        get => _isRightLoading;
        private set
        {
            if (!Set(ref _isRightLoading, value))
                return;

            RaisePropertyChanged(nameof(IsBusy));
            CompareCommand.NotifyCanExecuteChanged();
            RefreshBothCommand.NotifyCanExecuteChanged();
            CompareAndContinueCommand?.NotifyCanExecuteChanged();
            RefreshMetadataCommand?.NotifyCanExecuteChanged();
            SwapSourceTargetCommand?.NotifyCanExecuteChanged();
            OpenConnectionManagerCommand.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(CanRunComparison));
        }
    }

    public bool IsBusy => IsLeftLoading || IsRightLoading;

    public string LeftStatus
    {
        get => _leftStatus;
        private set => Set(ref _leftStatus, value);
    }

    public string RightStatus
    {
        get => _rightStatus;
        private set => Set(ref _rightStatus, value);
    }

    public DdlSchemaCompareDirection SelectedDirection
    {
        get => _selectedDirection;
        set
        {
            if (!Set(ref _selectedDirection, value))
                return;

            RaisePropertyChanged(nameof(SelectedDirectionIndex));
        }
    }

    public int SelectedDirectionIndex
    {
        get => SelectedDirection == DdlSchemaCompareDirection.LeftToRight ? 0 : 1;
        set
        {
            DdlSchemaCompareDirection nextDirection = value == 1
                ? DdlSchemaCompareDirection.RightToLeft
                : DdlSchemaCompareDirection.LeftToRight;

            SelectedDirection = nextDirection;
        }
    }

    public string CompatibilityMessage
    {
        get => _compatibilityMessage;
        private set => Set(ref _compatibilityMessage, value);
    }

    public bool IsCompatibilityBlocked
    {
        get => _isCompatibilityBlocked;
        private set
        {
            if (!Set(ref _isCompatibilityBlocked, value))
                return;

            CompareCommand.NotifyCanExecuteChanged();
            CompareAndContinueCommand?.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(CanRunComparison));
        }
    }

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public string GeneratedSql
    {
        get => _generatedSql;
        private set
        {
            if (!Set(ref _generatedSql, value))
                return;

            RaisePropertyChanged(nameof(HasGeneratedSql));
            CopySqlCommand.NotifyCanExecuteChanged();
            OnGeneratedSqlChanged();
        }
    }

    public bool HasGeneratedSql => !string.IsNullOrWhiteSpace(GeneratedSql);

    public string LeftProviderLabel => LeftSelectedProfile?.Provider.ToString() ?? "-";

    public string RightProviderLabel => RightSelectedProfile?.Provider.ToString() ?? "-";

    public void Dispose()
    {
        _connectionManager.ProfilesChanged -= HandleProfilesChanged;
        _leftLoadCts?.Cancel();
        _rightLoadCts?.Cancel();
        _leftLoadCts?.Dispose();
        _rightLoadCts?.Dispose();
    }

    private void HandleProfilesChanged()
    {
        RefreshProfiles();
    }

    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (ConnectionProfile profile in _connectionManager.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Profiles.Add(profile);

        RaisePropertyChanged(nameof(HasAvailableProfiles));

        LeftSelectedProfile ??= ResolveDefaultProfile();
        RightSelectedProfile ??= ResolveDefaultProfile(exceptProfileId: LeftSelectedProfile?.Id) ?? LeftSelectedProfile;

        RaisePropertyChanged(nameof(LeftProviderLabel));
        RaisePropertyChanged(nameof(RightProviderLabel));

        RecomputeCompatibility();
    }

    private ConnectionProfile? ResolveDefaultProfile(string? exceptProfileId = null)
    {
        if (!string.IsNullOrWhiteSpace(_connectionManager.ActiveProfileId))
        {
            ConnectionProfile? active = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, _connectionManager.ActiveProfileId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(profile.Id, exceptProfileId, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
                return active;
        }

        return Profiles.FirstOrDefault(profile =>
            !string.Equals(profile.Id, exceptProfileId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RefreshBothAsync()
    {
        await Task.WhenAll(
            LoadLeftContextAsync(forceRefresh: true),
            LoadRightContextAsync(forceRefresh: true));
    }

    private async Task LoadLeftContextAsync(bool forceRefresh)
    {
        await LoadDatabasesAsync(EndpointSide.Left, forceRefresh);
        await LoadLeftMetadataAsync(forceRefresh);
    }

    private async Task LoadRightContextAsync(bool forceRefresh)
    {
        await LoadDatabasesAsync(EndpointSide.Right, forceRefresh);
        await LoadRightMetadataAsync(forceRefresh);
    }

    private async Task LoadDatabasesAsync(EndpointSide side, bool forceRefresh)
    {
        ConnectionProfile? profile = side == EndpointSide.Left ? LeftSelectedProfile : RightSelectedProfile;
        if (profile is null)
        {
            SetDatabases(side, []);
            SetSelectedDatabase(side, null);
            return;
        }

        string profileKey = profile.Id;
        if (!forceRefresh && _databaseCache.TryGetValue(profileKey, out string[]? cached))
        {
            SetDatabases(side, cached);
            if (string.IsNullOrWhiteSpace(GetSelectedDatabase(side)))
                SetSelectedDatabase(side, profile.Database);
            return;
        }

        try
        {
            SetLoading(side, true);
            SetStatus(side, "Carregando bancos...");
            string[] databases = await ListDatabasesAsync(profile, CancellationToken.None);
            _databaseCache[profileKey] = databases;
            SetDatabases(side, databases);

            if (string.IsNullOrWhiteSpace(GetSelectedDatabase(side)))
                SetSelectedDatabase(side, profile.Database);

            SetStatus(side, "Banco carregado.");
        }
        catch (Exception ex)
        {
            SetStatus(side, $"Falha ao carregar bancos: {ex.Message}");
            SetDatabases(side, [profile.Database]);
            if (string.IsNullOrWhiteSpace(GetSelectedDatabase(side)))
                SetSelectedDatabase(side, profile.Database);
        }
        finally
        {
            SetLoading(side, false);
        }
    }

    private async Task LoadLeftMetadataAsync(bool forceRefresh)
    {
        await LoadMetadataAsync(EndpointSide.Left, forceRefresh);
    }

    private async Task LoadRightMetadataAsync(bool forceRefresh)
    {
        await LoadMetadataAsync(EndpointSide.Right, forceRefresh);
    }

    private async Task LoadMetadataAsync(EndpointSide side, bool forceRefresh)
    {
        ConnectionProfile? profile = side == EndpointSide.Left ? LeftSelectedProfile : RightSelectedProfile;
        if (profile is null)
        {
            SetMetadata(side, null);
            SetSchemas(side, []);
            SetTables(side, []);
            SetStatus(side, "Selecione uma conexao.");
            RecomputeCompatibility();
            return;
        }

        CancellationTokenSource cts = ReplaceSideCts(side);
        CancellationToken ct = cts.Token;

        string? databaseName = GetSelectedDatabase(side);
        if (string.IsNullOrWhiteSpace(databaseName))
            databaseName = profile.Database;

        ConnectionConfig config = profile.ToConnectionConfig() with { Database = databaseName };
        string cacheKey = BuildMetadataKey(profile.Id, databaseName);

        try
        {
            SetLoading(side, true);
            SetStatus(side, "Carregando metadata...");

            DbMetadata metadata;
            if (!forceRefresh && _metadataCache.TryGetValue(cacheKey, out DbMetadata? cachedMetadata))
            {
                metadata = cachedMetadata;
            }
            else
            {
                using var metadataService = MetadataService.Create(config);
                metadata = await metadataService.GetMetadataAsync(forceRefresh: true, ct);
                _metadataCache[cacheKey] = metadata;
            }

            if (ct.IsCancellationRequested)
                return;

            SetMetadata(side, metadata);
            SetSchemas(side, metadata.Schemas.Select(schema => schema.Name).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray());
            if (string.IsNullOrWhiteSpace(GetSelectedSchema(side)))
                SetSelectedSchema(side, metadata.Schemas.FirstOrDefault()?.Name);

            RebuildTables(side);
            SetStatus(side, $"Metadata: {metadata.TotalTables} tabelas, {metadata.TotalForeignKeys} FKs.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetMetadata(side, null);
            SetSchemas(side, []);
            SetTables(side, []);
            SetStatus(side, $"Falha ao carregar metadata: {ex.Message}");
        }
        finally
        {
            SetLoading(side, false);
            RecomputeCompatibility();
        }
    }

    private void RebuildTables(EndpointSide side)
    {
        DbMetadata? metadata = side == EndpointSide.Left ? _leftMetadata : _rightMetadata;
        if (metadata is null)
        {
            SetTables(side, []);
            SetSelectedTable(side, null);
            return;
        }

        string? selectedSchema = GetSelectedSchema(side);

        // Whole-database mode: the table picker collapses to "all tables".
        if (string.Equals(selectedSchema, AllSchemasOption, StringComparison.Ordinal))
        {
            SetTables(side, [AllTablesOption]);
            SetSelectedTable(side, AllTablesOption);
            return;
        }

        IEnumerable<TableMetadata> tables = metadata.AllTables;
        if (!string.IsNullOrWhiteSpace(selectedSchema))
            tables = tables.Where(table => string.Equals(table.Schema, selectedSchema, StringComparison.OrdinalIgnoreCase));

        string[] tableNames = tables
            .Where(table => table.Kind == TableKind.Table)
            .Select(table => table.FullName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // First option compares the whole schema; the rest are individual tables.
        string[] options = [AllTablesOption, .. tableNames];
        SetTables(side, options);

        string? selectedTable = GetSelectedTable(side);
        if (!string.IsNullOrWhiteSpace(selectedTable)
            && options.Any(table => string.Equals(table, selectedTable, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SetSelectedTable(side, tableNames.FirstOrDefault() ?? AllTablesOption);
    }

    private void RecomputeCompatibility()
    {
        RaisePropertyChanged(nameof(LeftProviderLabel));
        RaisePropertyChanged(nameof(RightProviderLabel));

        if (Profiles.Count == 0)
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = NoConnectionsWarningText;
            UpdateSelectionStepState();
            return;
        }

        ConnectionProfile? leftProfile = LeftSelectedProfile;
        ConnectionProfile? rightProfile = RightSelectedProfile;

        if (leftProfile is null || rightProfile is null)
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = "Selecione as duas conexoes.";
            UpdateSelectionStepState();
            return;
        }

        if (leftProfile.Provider != rightProfile.Provider)
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = "Comparacao bloqueada: adapters diferentes.";
            UpdateSelectionStepState();
            return;
        }

        // Data comparison only works table-to-table (it reads rows), so schema/database-wide is blocked.
        if (IsDataComparison)
        {
            bool leftConcrete = IsConcreteTableSelection(LeftSelectedSchema, LeftSelectedTable);
            bool rightConcrete = IsConcreteTableSelection(RightSelectedSchema, RightSelectedTable);
            if (!leftConcrete || !rightConcrete)
            {
                IsCompatibilityBlocked = true;
                CompatibilityMessage = "Comparacao de dados: selecione uma tabela especifica nos dois lados (sem banco/schema inteiro).";
                UpdateSelectionStepState();
                return;
            }

            RefreshDataKeyOptionsForSelection();
            IsCompatibilityBlocked = false;
            CompatibilityMessage = "Comparacao de dados pronta: os valores das duas tabelas serao comparados.";
            UpdateSelectionStepState();
            return;
        }

        bool leftAllSchemas = string.Equals(LeftSelectedSchema, AllSchemasOption, StringComparison.Ordinal);
        bool rightAllSchemas = string.Equals(RightSelectedSchema, AllSchemasOption, StringComparison.Ordinal);
        if (leftAllSchemas != rightAllSchemas)
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = $"Selecione \"{AllSchemasOption}\" dos dois lados para comparar o banco inteiro.";
            UpdateSelectionStepState();
            return;
        }

        if (leftAllSchemas && rightAllSchemas)
        {
            IsCompatibilityBlocked = false;
            CompatibilityMessage = "Banco inteiro: todas as tabelas de todos os schemas serao comparadas.";
            UpdateSelectionStepState();
            return;
        }

        if (string.IsNullOrWhiteSpace(LeftSelectedTable) || string.IsNullOrWhiteSpace(RightSelectedTable))
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = "Selecione as duas tabelas.";
            UpdateSelectionStepState();
            return;
        }

        bool leftAll = string.Equals(LeftSelectedTable, AllTablesOption, StringComparison.Ordinal);
        bool rightAll = string.Equals(RightSelectedTable, AllTablesOption, StringComparison.Ordinal);
        if (leftAll != rightAll)
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = $"Selecione \"{AllTablesOption}\" dos dois lados para comparar o schema inteiro.";
            UpdateSelectionStepState();
            return;
        }

        IsCompatibilityBlocked = false;
        CompatibilityMessage = IsSchemaWideComparison
            ? "Schema inteiro: todas as tabelas serao comparadas."
            : "Conexoes compativeis para comparacao.";
        UpdateSelectionStepState();
    }

    public bool IsDatabaseWideComparison =>
        string.Equals(LeftSelectedSchema, AllSchemasOption, StringComparison.Ordinal)
        && string.Equals(RightSelectedSchema, AllSchemasOption, StringComparison.Ordinal);

    public bool IsSchemaWideComparison =>
        !IsDatabaseWideComparison
        && string.Equals(LeftSelectedTable, AllTablesOption, StringComparison.Ordinal)
        && string.Equals(RightSelectedTable, AllTablesOption, StringComparison.Ordinal);

    private static bool IsConcreteTableSelection(string? schema, string? table) =>
        !string.Equals(schema, AllSchemasOption, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(table)
        && !string.Equals(table, AllTablesOption, StringComparison.Ordinal);

    private void OpenConnectionManager()
    {
        if (_connectionManager.ConnectOrOpenManagerCommand.CanExecute(null))
            _connectionManager.ConnectOrOpenManagerCommand.Execute(null);
    }

    /// <summary>
    /// Pre-selects the left (source) connection/database/schema, e.g. when launched from the
    /// schema analysis workspace. The async loaders preserve the pre-set database/schema.
    /// </summary>
    public void SeedSource(string? profileId, string? database, string? schema)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        ConnectionProfile? profile = Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
            return;

        if (!string.IsNullOrWhiteSpace(database))
            _leftSelectedDatabase = database;
        if (!string.IsNullOrWhiteSpace(schema))
            _leftSelectedSchema = schema;

        _leftSelectedProfile = profile;
        RaisePropertyChanged(nameof(LeftSelectedProfile));
        RaisePropertyChanged(nameof(LeftSelectedDatabase));
        RaisePropertyChanged(nameof(LeftSelectedSchema));
        RaisePropertyChanged(nameof(LeftProviderLabel));

        _ = LoadLeftContextAsync(forceRefresh: false);
        RecomputeCompatibility();
    }

    private async Task CompareAsync()
    {
        RecomputeCompatibility();
        if (IsCompatibilityBlocked)
            return;

        CompareWarnings.Clear();
        GeneratedSql = string.Empty;
        _flatDifferences = [];
        _compareProvider = LeftSelectedProfile!.Provider;

        if (IsDataComparison)
        {
            await CompareDataAsync();
            return;
        }

        ResetDataResults();

        if (IsDatabaseWideComparison)
        {
            BuildDatabaseWideDifferences();
        }
        else if (IsSchemaWideComparison)
        {
            if (!BuildSchemaWideDifferences())
                return;
        }
        else if (!BuildSingleTableDifferences())
        {
            return;
        }

        Summary = BuildComparisonSummary(_flatDifferences);
        OnComparisonCompleted();

        await Task.CompletedTask;
    }

    private bool BuildSingleTableDifferences()
    {
        TableMetadata? leftTable = ResolveSelectedTable(EndpointSide.Left);
        TableMetadata? rightTable = ResolveSelectedTable(EndpointSide.Right);
        if (leftTable is null || rightTable is null)
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = "Nao foi possivel resolver as tabelas selecionadas.";
            return false;
        }

        // Resolve source/target by direction FIRST so the grid's Origem/Destino columns and
        // the generated SQL describe the same thing (the SQL always alters the target).
        (TableMetadata source, TableMetadata target, string targetSchema, string targetTableName) =
            SelectedDirection == DdlSchemaCompareDirection.LeftToRight
                ? (leftTable, rightTable, rightTable.Schema, rightTable.Name)
                : (rightTable, leftTable, leftTable.Schema, leftTable.Name);

        TableDiff diff = _comparer.Compare(source, target);
        _flatDifferences = diff.Differences
            .Select(d => new FlatDifference(targetSchema, targetTableName, d))
            .ToList();

        foreach (string warning in diff.Warnings)
            CompareWarnings.Add(warning);

        return true;
    }

    private bool BuildSchemaWideDifferences()
    {
        bool leftIsSource = SelectedDirection == DdlSchemaCompareDirection.LeftToRight;
        IReadOnlyList<TableMetadata> sourceTables = ResolveSchemaTables(leftIsSource ? EndpointSide.Left : EndpointSide.Right);
        IReadOnlyList<TableMetadata> targetTables = ResolveSchemaTables(leftIsSource ? EndpointSide.Right : EndpointSide.Left);
        string targetSchema = (leftIsSource ? RightSelectedSchema : LeftSelectedSchema) ?? string.Empty;

        SchemaComparison comparison = _schemaComparer.Compare(sourceTables, targetTables, targetSchema);
        _flatDifferences = comparison.Tables
            .SelectMany(table => table.Diff.Differences
                .Select(d => new FlatDifference(table.TargetSchema, table.TableName, d)))
            .ToList();

        foreach (string warning in comparison.Tables.SelectMany(t => t.Diff.Warnings))
            CompareWarnings.Add(warning);

        return true;
    }

    private void BuildDatabaseWideDifferences()
    {
        bool leftIsSource = SelectedDirection == DdlSchemaCompareDirection.LeftToRight;
        IReadOnlyList<TableMetadata> sourceTables = ResolveAllTables(leftIsSource ? EndpointSide.Left : EndpointSide.Right);
        IReadOnlyList<TableMetadata> targetTables = ResolveAllTables(leftIsSource ? EndpointSide.Right : EndpointSide.Left);

        SchemaComparison comparison = _schemaComparer.CompareDatabase(sourceTables, targetTables);
        _flatDifferences = comparison.Tables
            .SelectMany(table => table.Diff.Differences
                .Select(d => new FlatDifference(table.TargetSchema, table.TableName, d)))
            .ToList();

        foreach (string warning in comparison.Tables.SelectMany(t => t.Diff.Warnings))
            CompareWarnings.Add(warning);
    }

    private IReadOnlyList<TableMetadata> ResolveSchemaTables(EndpointSide side)
    {
        DbMetadata? metadata = side == EndpointSide.Left ? _leftMetadata : _rightMetadata;
        string? schema = side == EndpointSide.Left ? LeftSelectedSchema : RightSelectedSchema;
        if (metadata is null)
            return [];

        return metadata.AllTables
            .Where(table => table.Kind == TableKind.Table
                && (string.IsNullOrWhiteSpace(schema) || string.Equals(table.Schema, schema, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private IReadOnlyList<TableMetadata> ResolveAllTables(EndpointSide side)
    {
        DbMetadata? metadata = side == EndpointSide.Left ? _leftMetadata : _rightMetadata;
        if (metadata is null)
            return [];

        return metadata.AllTables.Where(table => table.Kind == TableKind.Table).ToList();
    }

    private static string BuildComparisonSummary(IReadOnlyList<FlatDifference> flat)
    {
        int Count(SchemaDiffCategory category) => flat.Count(f => f.Difference.Category == category);
        int tables = Count(SchemaDiffCategory.Table);
        int columns = Count(SchemaDiffCategory.Column);
        int constraints = flat.Count(f => f.Difference.Category is SchemaDiffCategory.PrimaryKey or SchemaDiffCategory.Unique or SchemaDiffCategory.Check);
        int indexes = Count(SchemaDiffCategory.Index);
        int relationships = Count(SchemaDiffCategory.ForeignKey);

        return $"Diferencas: {flat.Count} (tabelas {tables}, colunas {columns}, constraints {constraints}, indices {indexes}, relacionamentos {relationships}).";
    }

    private TableMetadata? ResolveSelectedTable(EndpointSide side)
    {
        DbMetadata? metadata = side == EndpointSide.Left ? _leftMetadata : _rightMetadata;
        string? tableName = side == EndpointSide.Left ? LeftSelectedTable : RightSelectedTable;
        if (metadata is null || string.IsNullOrWhiteSpace(tableName))
            return null;

        return metadata.FindTable(tableName.Trim());
    }

    private CancellationTokenSource ReplaceSideCts(EndpointSide side)
    {
        if (side == EndpointSide.Left)
        {
            _leftLoadCts?.Cancel();
            _leftLoadCts?.Dispose();
            _leftLoadCts = new CancellationTokenSource();
            return _leftLoadCts;
        }

        _rightLoadCts?.Cancel();
        _rightLoadCts?.Dispose();
        _rightLoadCts = new CancellationTokenSource();
        return _rightLoadCts;
    }

    private void SetLoading(EndpointSide side, bool value)
    {
        if (side == EndpointSide.Left)
            IsLeftLoading = value;
        else
            IsRightLoading = value;
    }

    private void SetStatus(EndpointSide side, string value)
    {
        if (side == EndpointSide.Left)
            LeftStatus = value;
        else
            RightStatus = value;
    }

    private void SetMetadata(EndpointSide side, DbMetadata? metadata)
    {
        if (side == EndpointSide.Left)
            _leftMetadata = metadata;
        else
            _rightMetadata = metadata;
    }

    private static string BuildMetadataKey(string profileId, string database)
    {
        return $"{profileId}|{database}";
    }

    private string? GetSelectedDatabase(EndpointSide side)
    {
        return side == EndpointSide.Left ? LeftSelectedDatabase : RightSelectedDatabase;
    }

    private string? GetSelectedSchema(EndpointSide side)
    {
        return side == EndpointSide.Left ? LeftSelectedSchema : RightSelectedSchema;
    }

    private string? GetSelectedTable(EndpointSide side)
    {
        return side == EndpointSide.Left ? LeftSelectedTable : RightSelectedTable;
    }

    private void SetSelectedDatabase(EndpointSide side, string? value)
    {
        if (side == EndpointSide.Left)
            LeftSelectedDatabase = value;
        else
            RightSelectedDatabase = value;
    }

    private void SetSelectedSchema(EndpointSide side, string? value)
    {
        if (side == EndpointSide.Left)
            LeftSelectedSchema = value;
        else
            RightSelectedSchema = value;
    }

    private void SetSelectedTable(EndpointSide side, string? value)
    {
        if (side == EndpointSide.Left)
            LeftSelectedTable = value;
        else
            RightSelectedTable = value;
    }

    private void SetDatabases(EndpointSide side, IReadOnlyList<string> values)
    {
        ObservableCollection<string> target = side == EndpointSide.Left ? LeftDatabases : RightDatabases;
        target.Clear();
        foreach (string value in values.Where(static item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            target.Add(value);
    }

    private void SetSchemas(EndpointSide side, IReadOnlyList<string> values)
    {
        ObservableCollection<string> target = side == EndpointSide.Left ? LeftSchemas : RightSchemas;
        target.Clear();

        // First option compares every schema (whole database); the rest are individual schemas.
        if (values.Any(static item => !string.IsNullOrWhiteSpace(item)))
            target.Add(AllSchemasOption);

        foreach (string value in values.Where(static item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            target.Add(value);
    }

    private void SetTables(EndpointSide side, IReadOnlyList<string> values)
    {
        ObservableCollection<string> target = side == EndpointSide.Left ? LeftTables : RightTables;
        target.Clear();
        foreach (string value in values.Where(static item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            target.Add(value);
    }

    private static async Task<string[]> ListDatabasesAsync(ConnectionProfile profile, CancellationToken ct)
    {
        if (profile.Provider == DatabaseProvider.SQLite)
            return [profile.Database];

        string sql = profile.Provider switch
        {
            DatabaseProvider.Postgres => "SELECT datname FROM pg_database WHERE datallowconn = TRUE ORDER BY datname;",
            DatabaseProvider.MySql => "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA ORDER BY SCHEMA_NAME;",
            DatabaseProvider.SqlServer => "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name;",
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(sql))
            return [profile.Database];

        await using IDbOrchestrator orchestrator = DbOrchestratorFactory.CreateDefault().Create(profile.ToConnectionConfig());
        PreviewResult result = await orchestrator.ExecutePreviewAsync(sql, 10000, ct);
        if (!result.Success || result.Data is null || result.Data.Columns.Count == 0)
            return [profile.Database];

        return result.Data.Rows
            .Cast<System.Data.DataRow>()
            .Select(static row => row[0]?.ToString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private enum EndpointSide
    {
        Left,
        Right,
    }

    internal void LoadComparisonForTesting(TableMetadata source, TableMetadata target, DatabaseProvider provider)
    {
        _compareProvider = provider;
        TableDiff diff = _comparer.Compare(source, target);
        _flatDifferences = diff.Differences
            .Select(d => new FlatDifference(target.Schema, target.Name, d))
            .ToList();
        OnComparisonCompleted();
    }
}
