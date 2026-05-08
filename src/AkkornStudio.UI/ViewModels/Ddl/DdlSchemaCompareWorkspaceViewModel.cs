using System.Collections.ObjectModel;
using AkkornStudio.Core;
using AkkornStudio.Ddl;
using AkkornStudio.Metadata;
using AkkornStudio.Registry;
using AkkornStudio.UI.Services.ConnectionManager.Models;

namespace AkkornStudio.UI.ViewModels;

public enum DdlSchemaCompareDirection
{
    LeftToRight,
    RightToLeft,
}

public sealed partial class DdlSchemaCompareWorkspaceViewModel : ViewModelBase, IDisposable
{
    internal readonly record struct DdlSchemaCompareSqlOperation(
        string Category,
        string Item,
        string Action,
        string Sql,
        bool IsDestructive);

    private readonly ConnectionManagerViewModel _connectionManager;
    private readonly Dictionary<string, DbMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _databaseCache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<DdlSchemaCompareSqlOperation> _comparisonSqlOperations = [];
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

        InitializeWizardState();
        _connectionManager.ProfilesChanged += HandleProfilesChanged;
        RefreshProfiles();
    }

    public event Action<string>? CopySqlRequested;

    public RelayCommand CompareCommand { get; }

    public RelayCommand RefreshBothCommand { get; }

    public RelayCommand CopySqlCommand { get; }

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    public ObservableCollection<string> LeftDatabases { get; } = [];

    public ObservableCollection<string> RightDatabases { get; } = [];

    public ObservableCollection<string> LeftSchemas { get; } = [];

    public ObservableCollection<string> RightSchemas { get; } = [];

    public ObservableCollection<string> LeftTables { get; } = [];

    public ObservableCollection<string> RightTables { get; } = [];

    public ObservableCollection<DdlSchemaCompareDiffRowViewModel> ColumnDiffs { get; } = [];

    public ObservableCollection<DdlSchemaCompareDiffRowViewModel> ConstraintDiffs { get; } = [];

    public ObservableCollection<DdlSchemaCompareDiffRowViewModel> RelationshipDiffs { get; } = [];

    public ObservableCollection<DdlSchemaCompareDiffRowViewModel> ExternalImpactDiffs { get; } = [];

    public ObservableCollection<string> CompareWarnings { get; } = [];

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
        IEnumerable<TableMetadata> tables = metadata.AllTables;
        if (!string.IsNullOrWhiteSpace(selectedSchema))
            tables = tables.Where(table => string.Equals(table.Schema, selectedSchema, StringComparison.OrdinalIgnoreCase));

        string[] options = tables
            .Where(table => table.Kind == TableKind.Table)
            .Select(table => table.FullName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SetTables(side, options);

        string? selectedTable = GetSelectedTable(side);
        if (!string.IsNullOrWhiteSpace(selectedTable)
            && options.Any(table => string.Equals(table, selectedTable, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SetSelectedTable(side, options.FirstOrDefault());
    }

    private void RecomputeCompatibility()
    {
        RaisePropertyChanged(nameof(LeftProviderLabel));
        RaisePropertyChanged(nameof(RightProviderLabel));

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

        if (string.IsNullOrWhiteSpace(LeftSelectedTable) || string.IsNullOrWhiteSpace(RightSelectedTable))
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = "Selecione as duas tabelas.";
            UpdateSelectionStepState();
            return;
        }

        IsCompatibilityBlocked = false;
        CompatibilityMessage = "Conexoes compativeis para comparacao.";
        UpdateSelectionStepState();
    }

    private async Task CompareAsync()
    {
        RecomputeCompatibility();
        if (IsCompatibilityBlocked)
            return;

        ColumnDiffs.Clear();
        ConstraintDiffs.Clear();
        RelationshipDiffs.Clear();
        ExternalImpactDiffs.Clear();
        CompareWarnings.Clear();
        GeneratedSql = string.Empty;
        _comparisonSqlOperations = [];

        TableMetadata? leftTable = ResolveSelectedTable(EndpointSide.Left);
        TableMetadata? rightTable = ResolveSelectedTable(EndpointSide.Right);
        if (leftTable is null || rightTable is null)
        {
            IsCompatibilityBlocked = true;
            CompatibilityMessage = "Nao foi possivel resolver as tabelas selecionadas.";
            return;
        }

        BuildColumnDiffs(leftTable, rightTable);
        BuildConstraintDiffs(leftTable, rightTable);
        BuildRelationshipDiffs(leftTable, rightTable);
        BuildExternalImpactDiffs(leftTable, rightTable);

        (TableMetadata source, TableMetadata target, string targetSchema, string targetTableName) =
            SelectedDirection == DdlSchemaCompareDirection.LeftToRight
                ? (leftTable, rightTable, rightTable.Schema, rightTable.Name)
                : (rightTable, leftTable, leftTable.Schema, leftTable.Name);

        DatabaseProvider provider = LeftSelectedProfile!.Provider;
        IReadOnlyList<DdlSchemaCompareSqlOperation> sqlOperations = BuildSynchronizationOperations(
            source,
            target,
            provider,
            targetSchema,
            targetTableName,
            CompareWarnings);
        _comparisonSqlOperations = sqlOperations;
        GeneratedSql = string.Join("\n", sqlOperations
            .Select(static operation => operation.Sql)
            .Where(static statement => !string.IsNullOrWhiteSpace(statement)));

        int totalDiffs = ColumnDiffs.Count + ConstraintDiffs.Count + RelationshipDiffs.Count + ExternalImpactDiffs.Count;
        Summary = $"Diferencas: {totalDiffs} (colunas {ColumnDiffs.Count}, constraints {ConstraintDiffs.Count}, relacionamentos {RelationshipDiffs.Count}, impacto externo {ExternalImpactDiffs.Count}).";
        OnComparisonCompleted();

        await Task.CompletedTask;
    }

    private static IReadOnlyList<DdlSchemaCompareSqlOperation> BuildSynchronizationOperations(
        TableMetadata source,
        TableMetadata target,
        DatabaseProvider provider,
        string targetSchema,
        string targetTableName,
        ICollection<string> warnings)
    {
        IProviderRegistry registry = ProviderRegistry.CreateDefault();
        var dialect = registry.GetDialect(provider);

        var operations = new List<DdlSchemaCompareSqlOperation>();

        Dictionary<string, ColumnMetadata> sourceColumns = source.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ColumnMetadata> targetColumns = target.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        foreach ((string columnName, ColumnMetadata sourceColumn) in sourceColumns)
        {
            if (!targetColumns.TryGetValue(columnName, out ColumnMetadata? targetColumn))
            {
                string fragment = dialect.EmitCreateTableColumn(
                    sourceColumn.Name,
                    ResolveColumnType(sourceColumn),
                    sourceColumn.IsNullable,
                    sourceColumn.DefaultValue,
                    sourceColumn.Comment);
                operations.Add(new DdlSchemaCompareSqlOperation(
                    "Coluna ausente",
                    sourceColumn.Name,
                    "Adicionar no destino",
                    dialect.EmitAlterTableAddColumn(targetSchema, targetTableName, fragment),
                    IsDestructive: false));
                continue;
            }

            bool typeDiff = !string.Equals(
                NormalizeColumnType(ResolveColumnType(sourceColumn)),
                NormalizeColumnType(ResolveColumnType(targetColumn)),
                StringComparison.OrdinalIgnoreCase);
            bool nullabilityDiff = sourceColumn.IsNullable != targetColumn.IsNullable;
            if (typeDiff || nullabilityDiff)
            {
                operations.Add(new DdlSchemaCompareSqlOperation(
                    "Tipo",
                    targetColumn.Name,
                    "ALTER COLUMN",
                    dialect.EmitAlterTableAlterColumnType(
                        targetSchema,
                        targetTableName,
                        targetColumn.Name,
                        ResolveColumnType(sourceColumn),
                        sourceColumn.IsNullable),
                    IsDestructive: true));
            }

            if (!string.Equals(NormalizeDefault(sourceColumn.DefaultValue), NormalizeDefault(targetColumn.DefaultValue), StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Default divergente em {targetColumn.Name}: ajuste manual necessario.");
            }

            if (!string.Equals((sourceColumn.Comment ?? string.Empty).Trim(), (targetColumn.Comment ?? string.Empty).Trim(), StringComparison.Ordinal))
            {
                warnings.Add($"Comentario divergente em {targetColumn.Name}: ajuste manual necessario.");
            }
        }

        foreach ((string columnName, ColumnMetadata targetColumn) in targetColumns)
        {
            if (sourceColumns.ContainsKey(columnName))
                continue;

            operations.Add(new DdlSchemaCompareSqlOperation(
                "Coluna extra",
                targetColumn.Name,
                "Remover do destino",
                dialect.EmitAlterTableDropColumn(targetSchema, targetTableName, targetColumn.Name, ifExists: true),
                IsDestructive: true));
        }

        operations.AddRange(BuildPrimaryKeyOperations(source, target, provider, targetSchema, targetTableName, dialect, warnings));
        operations.AddRange(BuildUniqueOperations(source, target, provider, targetSchema, targetTableName, dialect, warnings));
        operations.AddRange(BuildForeignKeyOperations(source, target, provider, targetSchema, targetTableName, dialect));

        return operations;
    }

    private static IReadOnlyList<DdlSchemaCompareSqlOperation> BuildPrimaryKeyOperations(
        TableMetadata source,
        TableMetadata target,
        DatabaseProvider provider,
        string targetSchema,
        string targetTableName,
        Providers.Dialects.ISqlDialect dialect,
        ICollection<string> warnings)
    {
        var operations = new List<DdlSchemaCompareSqlOperation>();

        string[] sourcePk = source.Columns.Where(static column => column.IsPrimaryKey).Select(column => column.Name).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] targetPk = target.Columns.Where(static column => column.IsPrimaryKey).Select(column => column.Name).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();

        if (sourcePk.SequenceEqual(targetPk, StringComparer.OrdinalIgnoreCase))
            return operations;

        if (targetPk.Length > 0)
        {
            string? targetPkName = target.Indexes.FirstOrDefault(static index => index.IsPrimaryKey)?.Name;
            string? dropPk = BuildDropPrimaryKeySql(provider, targetSchema, targetTableName, targetPkName, dialect);
            if (!string.IsNullOrWhiteSpace(dropPk))
            {
                operations.Add(new DdlSchemaCompareSqlOperation(
                    "Primary Key",
                    "PK",
                    "Recriar PK",
                    dropPk,
                    IsDestructive: true));
            }
            else
                warnings.Add("Primary key divergente sem nome resolvido para DROP automatico.");
        }

        if (sourcePk.Length > 0)
        {
            string? sourcePkName = source.Indexes.FirstOrDefault(static index => index.IsPrimaryKey)?.Name;
            string fragment = dialect.EmitPrimaryKeyConstraint(sourcePkName, sourcePk);
            operations.Add(new DdlSchemaCompareSqlOperation(
                "Primary Key",
                "PK",
                "Recriar PK",
                $"ALTER TABLE {QualifyIdentifier(provider, dialect, targetSchema, targetTableName)} ADD {fragment};",
                IsDestructive: false));
        }

        return operations;
    }

    private static IReadOnlyList<DdlSchemaCompareSqlOperation> BuildUniqueOperations(
        TableMetadata source,
        TableMetadata target,
        DatabaseProvider provider,
        string targetSchema,
        string targetTableName,
        Providers.Dialects.ISqlDialect dialect,
        ICollection<string> warnings)
    {
        var operations = new List<DdlSchemaCompareSqlOperation>();

        Dictionary<string, IndexMetadata> sourceUnique = source.Indexes
            .Where(static index => index.IsUnique && !index.IsPrimaryKey)
            .ToDictionary(index => BuildIndexSignature(index.Columns), static index => index, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, IndexMetadata> targetUnique = target.Indexes
            .Where(static index => index.IsUnique && !index.IsPrimaryKey)
            .ToDictionary(index => BuildIndexSignature(index.Columns), static index => index, StringComparer.OrdinalIgnoreCase);

        foreach ((string signature, IndexMetadata targetIndex) in targetUnique)
        {
            if (sourceUnique.ContainsKey(signature))
                continue;

            string? dropUnique = BuildDropUniqueSql(provider, targetSchema, targetTableName, targetIndex.Name, dialect);
            if (!string.IsNullOrWhiteSpace(dropUnique))
            {
                operations.Add(new DdlSchemaCompareSqlOperation(
                    "Unique",
                    "UQ/Indices unicos",
                    "Sincronizar UNIQUE",
                    dropUnique,
                    IsDestructive: true));
            }
            else
                warnings.Add($"Unique index {targetIndex.Name} divergente sem suporte para DROP automatico.");
        }

        foreach ((string signature, IndexMetadata sourceIndex) in sourceUnique)
        {
            if (targetUnique.ContainsKey(signature))
                continue;

            string fragment = dialect.EmitUniqueConstraint(sourceIndex.Name, sourceIndex.Columns);
            operations.Add(new DdlSchemaCompareSqlOperation(
                "Unique",
                "UQ/Indices unicos",
                "Sincronizar UNIQUE",
                $"ALTER TABLE {QualifyIdentifier(provider, dialect, targetSchema, targetTableName)} ADD {fragment};",
                IsDestructive: false));
        }

        return operations;
    }

    private static IReadOnlyList<DdlSchemaCompareSqlOperation> BuildForeignKeyOperations(
        TableMetadata source,
        TableMetadata target,
        DatabaseProvider provider,
        string targetSchema,
        string targetTableName,
        Providers.Dialects.ISqlDialect dialect)
    {
        var operations = new List<DdlSchemaCompareSqlOperation>();

        Dictionary<string, ForeignKeyRelation> sourceFks = source.OutboundForeignKeys
            .ToDictionary(BuildForeignKeySignature, static fk => fk, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ForeignKeyRelation> targetFks = target.OutboundForeignKeys
            .ToDictionary(BuildForeignKeySignature, static fk => fk, StringComparer.OrdinalIgnoreCase);

        foreach ((string signature, ForeignKeyRelation targetFk) in targetFks)
        {
            if (sourceFks.ContainsKey(signature))
                continue;

            string? dropFk = BuildDropForeignKeySql(provider, dialect, targetSchema, targetTableName, targetFk.ConstraintName);
            if (!string.IsNullOrWhiteSpace(dropFk))
            {
                operations.Add(new DdlSchemaCompareSqlOperation(
                    "FK extra",
                    targetFk.ConstraintName,
                    "Remover FK",
                    dropFk,
                    IsDestructive: true));
            }
        }

        foreach ((string signature, ForeignKeyRelation sourceFk) in sourceFks)
        {
            if (targetFks.ContainsKey(signature))
                continue;

            var addFk = new AddForeignKeyOpExpr(
                sourceFk.ConstraintName,
                sourceFk.ChildColumn,
                sourceFk.ParentSchema,
                sourceFk.ParentTable,
                sourceFk.ParentColumn,
                sourceFk.OnDelete,
                sourceFk.OnUpdate);

            operations.Add(new DdlSchemaCompareSqlOperation(
                "FK ausente",
                sourceFk.ConstraintName,
                "Adicionar FK",
                addFk.Emit(new DdlEmitContext(provider), targetSchema, targetTableName),
                IsDestructive: false));
        }

        return operations;
    }

    private static string? BuildDropPrimaryKeySql(
        DatabaseProvider provider,
        string schema,
        string table,
        string? constraintName,
        Providers.Dialects.ISqlDialect dialect)
    {
        return provider switch
        {
            DatabaseProvider.MySql => $"ALTER TABLE {QualifyIdentifier(provider, dialect, schema, table)} DROP PRIMARY KEY;",
            DatabaseProvider.SqlServer or DatabaseProvider.Postgres => string.IsNullOrWhiteSpace(constraintName)
                ? null
                : $"ALTER TABLE {QualifyIdentifier(provider, dialect, schema, table)} DROP CONSTRAINT {dialect.QuoteIdentifier(constraintName)};",
            _ => null,
        };
    }

    private static string? BuildDropUniqueSql(
        DatabaseProvider provider,
        string schema,
        string table,
        string indexName,
        Providers.Dialects.ISqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(indexName))
            return null;

        return provider switch
        {
            DatabaseProvider.MySql => $"ALTER TABLE {QualifyIdentifier(provider, dialect, schema, table)} DROP INDEX {dialect.QuoteIdentifier(indexName)};",
            DatabaseProvider.SqlServer or DatabaseProvider.Postgres => $"ALTER TABLE {QualifyIdentifier(provider, dialect, schema, table)} DROP CONSTRAINT {dialect.QuoteIdentifier(indexName)};",
            _ => null,
        };
    }

    private static string? BuildDropForeignKeySql(
        DatabaseProvider provider,
        Providers.Dialects.ISqlDialect dialect,
        string schema,
        string table,
        string constraintName)
    {
        if (string.IsNullOrWhiteSpace(constraintName))
            return null;

        return provider switch
        {
            DatabaseProvider.MySql => $"ALTER TABLE {QualifyIdentifier(provider, dialect, schema, table)} DROP FOREIGN KEY {dialect.QuoteIdentifier(constraintName)};",
            DatabaseProvider.SqlServer or DatabaseProvider.Postgres => $"ALTER TABLE {QualifyIdentifier(provider, dialect, schema, table)} DROP CONSTRAINT {dialect.QuoteIdentifier(constraintName)};",
            _ => null,
        };
    }

    private static string BuildIndexSignature(IReadOnlyList<string> columns)
    {
        return string.Join("|", columns.Select(static name => name.Trim()).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildForeignKeySignature(ForeignKeyRelation fk)
    {
        return string.Join("|",
            fk.ChildColumn.Trim(),
            fk.ParentSchema.Trim(),
            fk.ParentTable.Trim(),
            fk.ParentColumn.Trim(),
            fk.OnDelete,
            fk.OnUpdate);
    }

    private static string ResolveColumnType(ColumnMetadata column)
    {
        return string.IsNullOrWhiteSpace(column.NativeType) ? column.DataType : column.NativeType;
    }

    private static string NormalizeColumnType(string? type)
    {
        return (type ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
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

    private static string QualifyIdentifier(
        DatabaseProvider provider,
        Providers.Dialects.ISqlDialect dialect,
        string schema,
        string table)
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

    private void BuildColumnDiffs(TableMetadata leftTable, TableMetadata rightTable)
    {
        Dictionary<string, ColumnMetadata> leftColumns = leftTable.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ColumnMetadata> rightColumns = rightTable.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        foreach ((string name, ColumnMetadata leftColumn) in leftColumns.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!rightColumns.TryGetValue(name, out ColumnMetadata? rightColumn))
            {
                ColumnDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
                    "Coluna ausente",
                    name,
                    DescribeColumn(leftColumn),
                    "(nao existe)",
                    "Medio",
                    "Adicionar no destino"));
                continue;
            }

            CompareColumnProperty(name, "Tipo", ResolveColumnType(leftColumn), ResolveColumnType(rightColumn));
            CompareColumnProperty(name, "Nullable", leftColumn.IsNullable ? "YES" : "NO", rightColumn.IsNullable ? "YES" : "NO");
            CompareColumnProperty(name, "Default", leftColumn.DefaultValue ?? string.Empty, rightColumn.DefaultValue ?? string.Empty);
            CompareColumnProperty(name, "Comment", leftColumn.Comment ?? string.Empty, rightColumn.Comment ?? string.Empty);
        }

        foreach ((string name, ColumnMetadata rightColumn) in rightColumns.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (leftColumns.ContainsKey(name))
                continue;

            ColumnDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
                "Coluna extra",
                name,
                "(nao existe)",
                DescribeColumn(rightColumn),
                "Alto",
                "Remover do destino"));
        }
    }

    private void CompareColumnProperty(string columnName, string propertyName, string leftValue, string rightValue)
    {
        if (string.Equals(NormalizeComparable(leftValue), NormalizeComparable(rightValue), StringComparison.Ordinal))
            return;

        ColumnDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
            propertyName,
            columnName,
            leftValue,
            rightValue,
            propertyName is "Tipo" or "Nullable" ? "Alto" : "Medio",
            propertyName is "Tipo" or "Nullable" ? "ALTER COLUMN" : "Ajuste manual/compatibilidade"));
    }

    private void BuildConstraintDiffs(TableMetadata leftTable, TableMetadata rightTable)
    {
        string leftPk = string.Join(", ", leftTable.Columns.Where(static column => column.IsPrimaryKey).Select(static column => column.Name).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
        string rightPk = string.Join(", ", rightTable.Columns.Where(static column => column.IsPrimaryKey).Select(static column => column.Name).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
        if (!string.Equals(NormalizeComparable(leftPk), NormalizeComparable(rightPk), StringComparison.Ordinal))
        {
            ConstraintDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
                "Primary Key",
                "PK",
                leftPk,
                rightPk,
                "Alto",
                "Recriar PK"));
        }

        string leftUnique = string.Join(" | ", leftTable.Indexes
            .Where(static index => index.IsUnique && !index.IsPrimaryKey)
            .Select(index => $"{index.Name}({string.Join(",", index.Columns)})")
            .OrderBy(static text => text, StringComparer.OrdinalIgnoreCase));
        string rightUnique = string.Join(" | ", rightTable.Indexes
            .Where(static index => index.IsUnique && !index.IsPrimaryKey)
            .Select(index => $"{index.Name}({string.Join(",", index.Columns)})")
            .OrderBy(static text => text, StringComparer.OrdinalIgnoreCase));

        if (!string.Equals(NormalizeComparable(leftUnique), NormalizeComparable(rightUnique), StringComparison.Ordinal))
        {
            ConstraintDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
                "Unique",
                "UQ/Indices unicos",
                leftUnique,
                rightUnique,
                "Medio",
                "Sincronizar UNIQUE"));
        }
    }

    private void BuildRelationshipDiffs(TableMetadata leftTable, TableMetadata rightTable)
    {
        Dictionary<string, ForeignKeyRelation> leftFks = leftTable.OutboundForeignKeys
            .ToDictionary(BuildForeignKeySignature, static fk => fk, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ForeignKeyRelation> rightFks = rightTable.OutboundForeignKeys
            .ToDictionary(BuildForeignKeySignature, static fk => fk, StringComparer.OrdinalIgnoreCase);

        foreach ((string signature, ForeignKeyRelation leftFk) in leftFks)
        {
            if (rightFks.ContainsKey(signature))
                continue;

            RelationshipDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
                "FK ausente",
                leftFk.ConstraintName,
                DescribeForeignKey(leftFk),
                "(nao existe)",
                "Alto",
                "Adicionar FK"));
        }

        foreach ((string signature, ForeignKeyRelation rightFk) in rightFks)
        {
            if (leftFks.ContainsKey(signature))
                continue;

            RelationshipDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
                "FK extra",
                rightFk.ConstraintName,
                "(nao existe)",
                DescribeForeignKey(rightFk),
                "Alto",
                "Remover FK"));
        }
    }

    private void BuildExternalImpactDiffs(TableMetadata leftTable, TableMetadata rightTable)
    {
        Dictionary<string, ForeignKeyRelation> leftInbound = leftTable.InboundForeignKeys
            .ToDictionary(BuildForeignKeySignature, static fk => fk, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ForeignKeyRelation> rightInbound = rightTable.InboundForeignKeys
            .ToDictionary(BuildForeignKeySignature, static fk => fk, StringComparer.OrdinalIgnoreCase);

        foreach ((string signature, ForeignKeyRelation leftFk) in leftInbound)
        {
            if (rightInbound.ContainsKey(signature))
                continue;

            ExternalImpactDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
                "Impacto externo",
                leftFk.ConstraintName,
                DescribeForeignKey(leftFk),
                "(nao existe)",
                "Medio",
                "Informativo: ajuste manual fora da tabela alvo"));
        }

        foreach ((string signature, ForeignKeyRelation rightFk) in rightInbound)
        {
            if (leftInbound.ContainsKey(signature))
                continue;

            ExternalImpactDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
                "Impacto externo",
                rightFk.ConstraintName,
                "(nao existe)",
                DescribeForeignKey(rightFk),
                "Medio",
                "Informativo: ajuste manual fora da tabela alvo"));
        }
    }

    private static string DescribeColumn(ColumnMetadata column)
    {
        return $"{ResolveColumnType(column)} | {(column.IsNullable ? "NULL" : "NOT NULL")} | default={column.DefaultValue ?? string.Empty}";
    }

    private static string DescribeForeignKey(ForeignKeyRelation fk)
    {
        return $"{fk.ChildFullTable}.{fk.ChildColumn} -> {fk.ParentFullTable}.{fk.ParentColumn} (on delete {fk.OnDelete}, on update {fk.OnUpdate})";
    }

    private static string NormalizeComparable(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
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

    internal void SetComparisonSqlOperationsForTesting(IEnumerable<DdlSchemaCompareSqlOperation> operations)
    {
        _comparisonSqlOperations = operations?.ToArray() ?? [];
    }
}

public sealed class DdlSchemaCompareDiffRowViewModel
{
    public DdlSchemaCompareDiffRowViewModel(
        string category,
        string item,
        string leftValue,
        string rightValue,
        string severity,
        string action)
    {
        Category = category;
        Item = item;
        LeftValue = leftValue;
        RightValue = rightValue;
        Severity = severity;
        Action = action;
    }

    public string Category { get; }

    public string Item { get; }

    public string LeftValue { get; }

    public string RightValue { get; }

    public string Severity { get; }

    public string Action { get; }
}
