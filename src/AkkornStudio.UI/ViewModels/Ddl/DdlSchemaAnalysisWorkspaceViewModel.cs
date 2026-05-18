using System.Collections.ObjectModel;
using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Application;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Validation;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.ConnectionManager.Models;

namespace AkkornStudio.UI.ViewModels;

public enum DdlSchemaAnalysisWizardStep
{
    Connection = 1,
    Trends = 2,
    DesiredNaming = 3,
    Issues = 4,
}

public sealed record DdlSchemaAnalysisOpenDdlRequest(
    DbMetadata Metadata,
    string? ProfileId,
    string? DatabaseName,
    string? SchemaName);

public sealed partial class DdlSchemaAnalysisWorkspaceViewModel : ViewModelBase, IDisposable
{
    private readonly ConnectionManagerViewModel _connectionManager;
    private readonly Action<DdlSchemaAnalysisOpenDdlRequest>? _openDdlRequested;
    private readonly SchemaAnalysisService _schemaAnalysisService;
    private readonly Dictionary<string, string[]> _databaseCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DbMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _analysisCts;
    private ConnectionProfile? _selectedProfile;
    private string? _selectedDatabase;
    private string? _selectedSchema;
    private bool _isLoadingMetadata;
    private bool _isRunningAnalysis;
    private string _statusMessage = "Selecione uma conexão para iniciar.";
    private DbMetadata? _currentMetadata;
    private int _totalTables;
    private int _totalColumns;
    private int _totalForeignKeys;
    private int _totalViews;
    private int _totalSchemas;
    private string _dominantNaming = "-";
    private string _dominantPkPattern = "-";
    private string _dominantFkPattern = "-";
    private DdlSchemaAnalysisWizardStep _wizardStep = DdlSchemaAnalysisWizardStep.Connection;
    private NamingConvention _tableNamingConvention = NamingConvention.SnakeCase;
    private NamingConvention _columnNamingConvention = NamingConvention.SnakeCase;
    private NamingConvention _indexNamingConvention = NamingConvention.SnakeCase;
    private NamingConvention _constraintNamingConvention = NamingConvention.SnakeCase;
    private NamingConvention _viewNamingConvention = NamingConvention.SnakeCase;
    private NamingConvention _viewColumnNamingConvention = NamingConvention.SnakeCase;
    private string _primaryKeyPattern = "id";
    private string _primaryKeyConstraintPattern = "pk_[table]";
    private string _foreignKeyPattern = "[target]_id";
    private string _indexPattern = "idx_[table]_[column]";
    private string _constraintPattern = "fk_[table]_[target]";

    public DdlSchemaAnalysisWorkspaceViewModel(
        ConnectionManagerViewModel connectionManager,
        Action<DdlSchemaAnalysisOpenDdlRequest>? openDdlRequested = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _openDdlRequested = openDdlRequested;
        _schemaAnalysisService = SchemaAnalysisServiceFactory.CreateDefault();

        SchemaAnalysisPanel = new SchemaAnalysisPanelViewModel(
            copySql: _ => { });

        OpenConnectionManagerCommand = new RelayCommand(OpenConnectionManager, () => !IsBusy);
        RefreshMetadataCommand = new RelayCommand(() => _ = RefreshMetadataAsync(true), () => !IsBusy && SelectedProfile is not null);
        RunAnalysisCommand = new RelayCommand(() => _ = RunAnalysisAsync(), () => !IsBusy && CurrentMetadata is not null);
        CancelAnalysisCommand = new RelayCommand(CancelAnalysis, () => IsRunningAnalysis);
        OpenInDdlCommand = new RelayCommand(OpenInDdl, () => CurrentMetadata is not null);
        NextStepCommand = new RelayCommand(() => _ = MoveToNextStepAsync(), CanMoveToNextStep);
        PreviousStepCommand = new RelayCommand(MoveToPreviousStep, CanMoveToPreviousStep);
        OpenConnectionStepCommand = new RelayCommand(() => WizardStep = DdlSchemaAnalysisWizardStep.Connection, () => WizardStep != DdlSchemaAnalysisWizardStep.Connection);
        OpenTrendsStepCommand = new RelayCommand(() => WizardStep = DdlSchemaAnalysisWizardStep.Trends, () => HasMetadata && WizardStep != DdlSchemaAnalysisWizardStep.Trends);
        OpenDesiredNamingStepCommand = new RelayCommand(() => WizardStep = DdlSchemaAnalysisWizardStep.DesiredNaming, () => HasMetadata && WizardStep != DdlSchemaAnalysisWizardStep.DesiredNaming);
        OpenIssuesStepCommand = new RelayCommand(() => WizardStep = DdlSchemaAnalysisWizardStep.Issues, () => HasMetadata && WizardStep != DdlSchemaAnalysisWizardStep.Issues);
        GenerateIssuesCommand = new RelayCommand(() => _ = GenerateIssuesAndOpenStepAsync(), () => !IsBusy && HasMetadata && !HasTemplateValidationErrors);
        SelectTrendContributionCommand = new RelayCommand<SchemaTrendPatternItemViewModel>(
            SelectTrendContribution,
            item => item is not null);
        ClearSelectedTrendContributionCommand = new RelayCommand(
            ClearSelectedTrendContribution,
            () => HasSelectedTrendContribution);

        _connectionManager.ProfilesChanged += HandleProfilesChanged;
        RefreshProfiles();
        InitializePlayground();
    }

    public event Action? OpenConnectionManagerRequested;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    public ObservableCollection<string> Databases { get; } = [];

    public ObservableCollection<string> Schemas { get; } = [];

    public SchemaAnalysisPanelViewModel SchemaAnalysisPanel { get; }

    public RelayCommand OpenConnectionManagerCommand { get; }

    public RelayCommand RefreshMetadataCommand { get; }

    public RelayCommand RunAnalysisCommand { get; }

    public RelayCommand CancelAnalysisCommand { get; }

    public RelayCommand OpenInDdlCommand { get; }

    public RelayCommand NextStepCommand { get; }

    public RelayCommand PreviousStepCommand { get; }

    public RelayCommand OpenConnectionStepCommand { get; }

    public RelayCommand OpenTrendsStepCommand { get; }

    public RelayCommand OpenDesiredNamingStepCommand { get; }

    public RelayCommand OpenIssuesStepCommand { get; }

    public RelayCommand GenerateIssuesCommand { get; }

    public RelayCommand<SchemaTrendPatternItemViewModel> SelectTrendContributionCommand { get; }

    public RelayCommand ClearSelectedTrendContributionCommand { get; }

    public IReadOnlyList<NamingConvention> NamingConventionOptions { get; } =
    [
        NamingConvention.SnakeCase,
        NamingConvention.CamelCase,
        NamingConvention.PascalCase,
        NamingConvention.KebabCase,
        NamingConvention.MixedAllowed,
    ];

    public bool HasAvailableProfiles => Profiles.Count > 0;

    public DdlSchemaAnalysisWizardStep WizardStep
    {
        get => _wizardStep;
        private set
        {
            if (!Set(ref _wizardStep, value))
                return;

            RaisePropertyChanged(nameof(IsConnectionStep));
            RaisePropertyChanged(nameof(IsTrendsStep));
            RaisePropertyChanged(nameof(IsDesiredNamingStep));
            RaisePropertyChanged(nameof(IsIssuesStep));
            RaisePropertyChanged(nameof(IsConnectionStepCurrent));
            RaisePropertyChanged(nameof(IsTrendsStepCurrent));
            RaisePropertyChanged(nameof(IsDesiredNamingStepCurrent));
            RaisePropertyChanged(nameof(IsIssuesStepCurrent));
            RaisePropertyChanged(nameof(IsConnectionStepCompleted));
            RaisePropertyChanged(nameof(IsTrendsStepCompleted));
            RaisePropertyChanged(nameof(IsDesiredNamingStepCompleted));
            RaisePropertyChanged(nameof(IsIssuesStepCompleted));
            NextStepCommand.NotifyCanExecuteChanged();
            PreviousStepCommand.NotifyCanExecuteChanged();
            OpenConnectionStepCommand.NotifyCanExecuteChanged();
            OpenTrendsStepCommand.NotifyCanExecuteChanged();
            OpenDesiredNamingStepCommand.NotifyCanExecuteChanged();
            OpenIssuesStepCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsConnectionStep => WizardStep == DdlSchemaAnalysisWizardStep.Connection;
    public bool IsTrendsStep => WizardStep == DdlSchemaAnalysisWizardStep.Trends;
    public bool IsDesiredNamingStep => WizardStep == DdlSchemaAnalysisWizardStep.DesiredNaming;
    public bool IsIssuesStep => WizardStep == DdlSchemaAnalysisWizardStep.Issues;

    public bool IsConnectionStepCurrent => IsConnectionStep;
    public bool IsTrendsStepCurrent => IsTrendsStep;
    public bool IsDesiredNamingStepCurrent => IsDesiredNamingStep;
    public bool IsIssuesStepCurrent => IsIssuesStep;

    public bool IsConnectionStepCompleted => (int)WizardStep > (int)DdlSchemaAnalysisWizardStep.Connection;
    public bool IsTrendsStepCompleted => (int)WizardStep > (int)DdlSchemaAnalysisWizardStep.Trends;
    public bool IsDesiredNamingStepCompleted => (int)WizardStep > (int)DdlSchemaAnalysisWizardStep.DesiredNaming;
    public bool IsIssuesStepCompleted => (int)WizardStep > (int)DdlSchemaAnalysisWizardStep.Issues;

    public ConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value))
                return;

            RaisePropertyChanged(nameof(SelectedProviderLabel));
            _ = LoadDatabasesAndMetadataAsync(forceRefresh: false);
            NextStepCommand.NotifyCanExecuteChanged();
            OpenTrendsStepCommand.NotifyCanExecuteChanged();
            OpenDesiredNamingStepCommand.NotifyCanExecuteChanged();
            OpenIssuesStepCommand.NotifyCanExecuteChanged();
        }
    }

    public string? SelectedDatabase
    {
        get => _selectedDatabase;
        set
        {
            if (!Set(ref _selectedDatabase, value))
                return;

            _ = RefreshMetadataAsync(forceRefresh: false);
            NextStepCommand.NotifyCanExecuteChanged();
        }
    }

    public string? SelectedSchema
    {
        get => _selectedSchema;
        set
        {
            if (!Set(ref _selectedSchema, value))
                return;

            RebuildTrendMetrics();
        }
    }

    private bool CanMoveToNextStep()
    {
        if (IsBusy)
            return false;

        return WizardStep switch
        {
            DdlSchemaAnalysisWizardStep.Connection => HasMetadata,
            DdlSchemaAnalysisWizardStep.Trends => HasMetadata,
            DdlSchemaAnalysisWizardStep.DesiredNaming => HasMetadata,
            _ => false,
        };
    }

    private bool CanMoveToPreviousStep()
    {
        return !IsBusy && WizardStep != DdlSchemaAnalysisWizardStep.Connection;
    }

    public bool IsLoadingMetadata
    {
        get => _isLoadingMetadata;
        private set
        {
            if (!Set(ref _isLoadingMetadata, value))
                return;

            RaisePropertyChanged(nameof(IsBusy));
            OpenConnectionManagerCommand.NotifyCanExecuteChanged();
            RefreshMetadataCommand.NotifyCanExecuteChanged();
            RunAnalysisCommand.NotifyCanExecuteChanged();
            OpenInDdlCommand.NotifyCanExecuteChanged();
            NextStepCommand.NotifyCanExecuteChanged();
            PreviousStepCommand.NotifyCanExecuteChanged();
            GenerateIssuesCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsRunningAnalysis
    {
        get => _isRunningAnalysis;
        private set
        {
            if (!Set(ref _isRunningAnalysis, value))
                return;

            RaisePropertyChanged(nameof(IsBusy));
            RunAnalysisCommand.NotifyCanExecuteChanged();
            CancelAnalysisCommand.NotifyCanExecuteChanged();
            RefreshMetadataCommand.NotifyCanExecuteChanged();
            OpenInDdlCommand.NotifyCanExecuteChanged();
            NextStepCommand.NotifyCanExecuteChanged();
            PreviousStepCommand.NotifyCanExecuteChanged();
            GenerateIssuesCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsBusy => IsLoadingMetadata || IsRunningAnalysis;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public DbMetadata? CurrentMetadata
    {
        get => _currentMetadata;
        private set
        {
            if (!Set(ref _currentMetadata, value))
                return;

            OpenInDdlCommand.NotifyCanExecuteChanged();
            RunAnalysisCommand.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(HasMetadata));
            NextStepCommand.NotifyCanExecuteChanged();
            OpenTrendsStepCommand.NotifyCanExecuteChanged();
            OpenDesiredNamingStepCommand.NotifyCanExecuteChanged();
            OpenIssuesStepCommand.NotifyCanExecuteChanged();
            GenerateIssuesCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasMetadata => CurrentMetadata is not null;

    public int TotalTables
    {
        get => _totalTables;
        private set => Set(ref _totalTables, value);
    }

    public int TotalColumns
    {
        get => _totalColumns;
        private set => Set(ref _totalColumns, value);
    }

    public int TotalForeignKeys
    {
        get => _totalForeignKeys;
        private set => Set(ref _totalForeignKeys, value);
    }

    public int TotalViews
    {
        get => _totalViews;
        private set => Set(ref _totalViews, value);
    }

    public int TotalSchemas
    {
        get => _totalSchemas;
        private set => Set(ref _totalSchemas, value);
    }

    public string DominantNaming
    {
        get => _dominantNaming;
        private set => Set(ref _dominantNaming, value);
    }

    public string DominantPkPattern
    {
        get => _dominantPkPattern;
        private set => Set(ref _dominantPkPattern, value);
    }

    public string DominantFkPattern
    {
        get => _dominantFkPattern;
        private set => Set(ref _dominantFkPattern, value);
    }

    public NamingConvention TableNamingConvention
    {
        get => _tableNamingConvention;
        set
        {
            if (!Set(ref _tableNamingConvention, value))
                return;

            OnPlaygroundPatternChanged("tables");
        }
    }

    public NamingConvention ColumnNamingConvention
    {
        get => _columnNamingConvention;
        set
        {
            if (!Set(ref _columnNamingConvention, value))
                return;

            OnPlaygroundPatternChanged("columns");
        }
    }

    public NamingConvention IndexNamingConvention
    {
        get => _indexNamingConvention;
        set
        {
            if (!Set(ref _indexNamingConvention, value))
                return;

            OnPlaygroundPatternChanged("indexes");
        }
    }

    public NamingConvention ConstraintNamingConvention
    {
        get => _constraintNamingConvention;
        set
        {
            if (!Set(ref _constraintNamingConvention, value))
                return;

            OnPlaygroundPatternChanged("constraints");
        }
    }

    public NamingConvention ViewNamingConvention
    {
        get => _viewNamingConvention;
        set
        {
            if (!Set(ref _viewNamingConvention, value))
                return;

            OnPlaygroundPatternChanged("views");
        }
    }

    public NamingConvention ViewColumnNamingConvention
    {
        get => _viewColumnNamingConvention;
        set
        {
            if (!Set(ref _viewColumnNamingConvention, value))
                return;

            OnPlaygroundPatternChanged("views");
        }
    }

    public string PrimaryKeyPattern
    {
        get => _primaryKeyPattern;
        set
        {
            if (!Set(ref _primaryKeyPattern, value?.Trim() ?? string.Empty))
                return;

            OnPlaygroundPatternChanged("templates");
        }
    }

    public string PrimaryKeyConstraintPattern
    {
        get => _primaryKeyConstraintPattern;
        set
        {
            if (!Set(ref _primaryKeyConstraintPattern, value?.Trim() ?? string.Empty))
                return;

            OnPlaygroundPatternChanged("templates");
        }
    }

    public string ForeignKeyPattern
    {
        get => _foreignKeyPattern;
        set
        {
            if (!Set(ref _foreignKeyPattern, value?.Trim() ?? string.Empty))
                return;

            OnPlaygroundPatternChanged("templates");
        }
    }

    public string IndexPattern
    {
        get => _indexPattern;
        set
        {
            if (!Set(ref _indexPattern, value?.Trim() ?? string.Empty))
                return;

            OnPlaygroundPatternChanged("templates");
        }
    }

    public string ConstraintPattern
    {
        get => _constraintPattern;
        set
        {
            if (!Set(ref _constraintPattern, value?.Trim() ?? string.Empty))
                return;

            OnPlaygroundPatternChanged("templates");
        }
    }

    public string SelectedProviderLabel => SelectedProfile?.Provider.ToString() ?? "-";

    public void Dispose()
    {
        _connectionManager.ProfilesChanged -= HandleProfilesChanged;
        _loadCts?.Cancel();
        _analysisCts?.Cancel();
        _playgroundFocusPulseCts?.Cancel();
        _loadCts?.Dispose();
        _analysisCts?.Dispose();
        _playgroundFocusPulseCts?.Dispose();
    }

    private void HandleProfilesChanged() => RefreshProfiles();

    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (ConnectionProfile profile in _connectionManager.Profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
            Profiles.Add(profile);

        RaisePropertyChanged(nameof(HasAvailableProfiles));

        if (SelectedProfile is null || Profiles.All(profile => !string.Equals(profile.Id, SelectedProfile.Id, StringComparison.OrdinalIgnoreCase)))
            SelectedProfile = ResolveDefaultProfile();
    }

    private ConnectionProfile? ResolveDefaultProfile()
    {
        if (!string.IsNullOrWhiteSpace(_connectionManager.ActiveProfileId))
        {
            ConnectionProfile? active = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, _connectionManager.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
                return active;
        }

        return Profiles.FirstOrDefault();
    }

    private async Task LoadDatabasesAndMetadataAsync(bool forceRefresh)
    {
        await LoadDatabasesAsync(forceRefresh);
        await RefreshMetadataAsync(forceRefresh);
    }

    private async Task LoadDatabasesAsync(bool forceRefresh)
    {
        ConnectionProfile? profile = SelectedProfile;
        if (profile is null)
        {
            Databases.Clear();
            SelectedDatabase = null;
            return;
        }

        string cacheKey = profile.Id;
        if (!forceRefresh && _databaseCache.TryGetValue(cacheKey, out string[]? cached))
        {
            ApplyDatabases(cached, profile.Database);
            return;
        }

        try
        {
            IsLoadingMetadata = true;
            StatusMessage = "Carregando bancos...";
            string[] databases = await ListDatabasesAsync(profile, CancellationToken.None);
            _databaseCache[cacheKey] = databases;
            ApplyDatabases(databases, profile.Database);
        }
        catch (Exception ex)
        {
            ApplyDatabases([profile.Database], profile.Database);
            StatusMessage = $"Falha ao carregar bancos: {ex.Message}";
        }
        finally
        {
            IsLoadingMetadata = false;
        }
    }

    private void ApplyDatabases(IReadOnlyList<string> databases, string fallbackDatabase)
    {
        Databases.Clear();
        foreach (string item in databases
                     .Where(static item => !string.IsNullOrWhiteSpace(item))
                     .Select(static item => item.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            Databases.Add(item);
        }

        if (Databases.Count == 0)
            Databases.Add(fallbackDatabase);

        if (string.IsNullOrWhiteSpace(SelectedDatabase) || Databases.All(db => !string.Equals(db, SelectedDatabase, StringComparison.OrdinalIgnoreCase)))
            SelectedDatabase = Databases.FirstOrDefault();
    }

    private async Task RefreshMetadataAsync(bool forceRefresh)
    {
        ConnectionProfile? profile = SelectedProfile;
        if (profile is null)
        {
            CurrentMetadata = null;
            SchemaAnalysisPanel.SetMetadataUnavailable();
            StatusMessage = "Selecione uma conexão.";
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        CancellationToken ct = _loadCts.Token;

        string databaseName = string.IsNullOrWhiteSpace(SelectedDatabase)
            ? profile.Database
            : SelectedDatabase!;
        string metadataCacheKey = $"{profile.Id}::{databaseName}";
        ConnectionConfig config = profile.ToConnectionConfig() with { Database = databaseName };

        try
        {
            IsLoadingMetadata = true;
            StatusMessage = "Carregando metadata...";

            DbMetadata metadata;
            if (!forceRefresh && _metadataCache.TryGetValue(metadataCacheKey, out DbMetadata? cached))
            {
                metadata = cached;
            }
            else
            {
                using var metadataService = MetadataService.Create(config);
                metadata = await metadataService.GetMetadataAsync(forceRefresh: true, ct);
                _metadataCache[metadataCacheKey] = metadata;
            }

            if (ct.IsCancellationRequested)
                return;

            CurrentMetadata = metadata;
            RebuildSchemaOptions(metadata);
            RebuildTrendMetrics();
            StatusMessage = $"Metadata carregada: {metadata.TotalTables} tabelas · {metadata.TotalForeignKeys} FKs.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CurrentMetadata = null;
            Schemas.Clear();
            SelectedSchema = null;
            StatusMessage = $"Falha ao carregar metadata: {ex.Message}";
            SchemaAnalysisPanel.SetMetadataUnavailable();
        }
        finally
        {
            IsLoadingMetadata = false;
        }
    }

    private void RebuildSchemaOptions(DbMetadata metadata)
    {
        string? previous = SelectedSchema;
        Schemas.Clear();
        foreach (string schemaName in metadata.Schemas
                     .Select(schema => schema.Name)
                     .Where(static name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            Schemas.Add(schemaName);
        }

        if (!string.IsNullOrWhiteSpace(previous)
            && Schemas.Any(schema => string.Equals(schema, previous, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedSchema = previous;
            return;
        }

        SelectedSchema = Schemas.FirstOrDefault();
    }

    private void RebuildTrendMetrics()
    {
        DbMetadata? metadata = CurrentMetadata;
        if (metadata is null)
        {
            TotalTables = 0;
            TotalColumns = 0;
            TotalForeignKeys = 0;
            TotalViews = 0;
            TotalSchemas = 0;
            DominantNaming = "-";
            DominantPkPattern = "-";
            DominantFkPattern = "-";
            ClearTrendPatternInsights();
            RaisePropertyChanged(nameof(MetadataLoadedSummary));
            return;
        }

        string? schemaFilter = SelectedSchema;
        IReadOnlyList<SchemaMetadata> schemas = string.IsNullOrWhiteSpace(schemaFilter)
            ? metadata.Schemas
            : metadata.Schemas.Where(schema => string.Equals(schema.Name, schemaFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        IReadOnlyList<TableMetadata> tables = schemas.SelectMany(schema => schema.Tables)
            .Where(table => table.Kind == TableKind.Table)
            .ToList();

        var filteredMetadata = metadata with { Schemas = schemas };
        var detector = new SchemaPatternDetector();
        SchemaObservedPatterns patterns = detector.DetectPatterns(filteredMetadata);

        TotalTables = tables.Count;
        TotalColumns = tables.Sum(table => table.Columns.Count);
        TotalForeignKeys = filteredMetadata.AllForeignKeys.Count(foreignKey =>
            MatchesSchemaFilter(schemaFilter, foreignKey.ChildSchema) || MatchesSchemaFilter(schemaFilter, foreignKey.ParentSchema));
        TotalViews = schemas.Sum(schema => schema.Tables.Count(table => table.Kind != TableKind.Table));
        TotalSchemas = schemas.Count;

        DominantNaming = patterns.DominantNamingConvention.ToString();
        DominantPkPattern = string.IsNullOrWhiteSpace(patterns.DominantPkPattern) ? "-" : patterns.DominantPkPattern!;
        DominantFkPattern = string.IsNullOrWhiteSpace(patterns.DominantFkPattern) ? "-" : patterns.DominantFkPattern!;
        RebuildTrendPatternInsights(schemas);
        RaisePropertyChanged(nameof(MetadataLoadedSummary));
    }

    private async Task<bool> RunAnalysisAsync()
    {
        if (IsRunningAnalysis || CurrentMetadata is null)
            return false;

        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();

        try
        {
            IsRunningAnalysis = true;
            SchemaAnalysisPanel.SetLoading();
            StatusMessage = "Executando analise de estrutura...";

            DbMetadata analysisMetadata = BuildAnalysisMetadata(CurrentMetadata, SelectedSchema, SchemaAnalysisPanel);
            SchemaAnalysisProfile profile = BuildProfileFromDesiredPatterns();
            SchemaAnalysisResult result = await _schemaAnalysisService.AnalyzeAsync(analysisMetadata, profile, _analysisCts.Token);
            SchemaAnalysisPanel.ApplyResult(result);
            StatusMessage = $"Analise concluida: {SchemaAnalysisPanel.FilteredTotalIssues} issue(s).";
            return true;
        }
        catch (OperationCanceledException)
        {
            SchemaAnalysisPanel.SetCancelled();
            StatusMessage = "Analise cancelada.";
            return false;
        }
        catch (Exception ex)
        {
            SchemaAnalysisPanel.SetMetadataUnavailable();
            StatusMessage = $"Falha na analise: {ex.Message}";
            return false;
        }
        finally
        {
            IsRunningAnalysis = false;
        }
    }

    private async Task MoveToNextStepAsync()
    {
        if (!CanMoveToNextStep())
            return;

        switch (WizardStep)
        {
            case DdlSchemaAnalysisWizardStep.Connection:
                WizardStep = DdlSchemaAnalysisWizardStep.Trends;
                return;
            case DdlSchemaAnalysisWizardStep.Trends:
                WizardStep = DdlSchemaAnalysisWizardStep.DesiredNaming;
                return;
            case DdlSchemaAnalysisWizardStep.DesiredNaming:
                await GenerateIssuesAndOpenStepAsync();
                return;
            default:
                return;
        }
    }

    private void MoveToPreviousStep()
    {
        if (!CanMoveToPreviousStep())
            return;

        WizardStep = WizardStep switch
        {
            DdlSchemaAnalysisWizardStep.Trends => DdlSchemaAnalysisWizardStep.Connection,
            DdlSchemaAnalysisWizardStep.DesiredNaming => DdlSchemaAnalysisWizardStep.Trends,
            DdlSchemaAnalysisWizardStep.Issues => DdlSchemaAnalysisWizardStep.DesiredNaming,
            _ => DdlSchemaAnalysisWizardStep.Connection,
        };
    }

    private async Task GenerateIssuesAndOpenStepAsync()
    {
        if (HasTemplateValidationErrors)
        {
            StatusMessage = "Existem templates invalidos no playground. Ajuste antes de gerar issues.";
            return;
        }

        bool analysisSucceeded = await RunAnalysisAsync();
        if (analysisSucceeded)
            WizardStep = DdlSchemaAnalysisWizardStep.Issues;
    }

    private void CancelAnalysis()
    {
        _analysisCts?.Cancel();
    }

    private void OpenInDdl()
    {
        if (CurrentMetadata is null)
            return;

        var request = new DdlSchemaAnalysisOpenDdlRequest(
            CurrentMetadata,
            SelectedProfile?.Id,
            SelectedDatabase,
            SelectedSchema);
        _openDdlRequested?.Invoke(request);
    }

    private void OpenConnectionManager()
    {
        OpenConnectionManagerRequested?.Invoke();
    }

    private SchemaAnalysisProfile BuildProfileFromDesiredPatterns()
    {
        // The current analysis engine accepts one naming convention across all targets.
        // We still persist intent using allowlist/synonyms so downstream rules can consume it.
        NamingConvention globalConvention = ResolveGlobalNamingConvention();
        SchemaAnalysisProfile profile = SchemaAnalysisProfileNormalizer.CreateDefaultProfile();
        List<string> desiredTokens =
        [
            PrimaryKeyPattern,
            PrimaryKeyConstraintPattern,
            ForeignKeyPattern,
            IndexPattern,
            ConstraintPattern,
        ];

        IReadOnlyList<string> normalizedAllowlist = desiredTokens
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Select(static token => token.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return profile with
        {
            NamingConvention = globalConvention,
            NameAllowlist = normalizedAllowlist,
        };
    }

    private NamingConvention ResolveGlobalNamingConvention()
    {
        HashSet<NamingConvention> conventions =
        [
            TableNamingConvention,
            ColumnNamingConvention,
            IndexNamingConvention,
            ConstraintNamingConvention,
            ViewNamingConvention,
            ViewColumnNamingConvention,
        ];

        if (conventions.Count == 1)
            return conventions.First();

        return NamingConvention.MixedAllowed;
    }

    private static bool MatchesSchemaFilter(string? selectedSchema, string? schemaName)
    {
        if (string.IsNullOrWhiteSpace(selectedSchema))
            return true;

        return string.Equals(selectedSchema, schemaName, StringComparison.OrdinalIgnoreCase);
    }

    private static DbMetadata BuildAnalysisMetadata(
        DbMetadata metadata,
        string? selectedSchema,
        SchemaAnalysisPanelViewModel panel)
    {
        IReadOnlyList<SchemaMetadata> filteredSchemas = metadata.Schemas
            .Where(schema => string.IsNullOrWhiteSpace(selectedSchema)
                             || string.Equals(schema.Name, selectedSchema, StringComparison.OrdinalIgnoreCase))
            .Select(schema =>
            {
                IReadOnlyList<TableMetadata> tables = schema.Tables
                    .Where(table => !panel.ShouldIgnoreTableForAnalysis(schema.Name, table.Name, table.Kind))
                    .ToList();
                return new SchemaMetadata(schema.Name, tables);
            })
            .Where(schema => schema.Tables.Count > 0)
            .ToList();

        if (filteredSchemas.Count == 0)
            return metadata with { Schemas = [] };

        HashSet<string> survivingTables = filteredSchemas
            .SelectMany(schema => schema.Tables.Select(table => QualifyTable(schema.Name, table.Name)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<ForeignKeyRelation> filteredForeignKeys = metadata.AllForeignKeys
            .Where(foreignKey =>
                survivingTables.Contains(QualifyTable(foreignKey.ChildSchema, foreignKey.ChildTable))
                && survivingTables.Contains(QualifyTable(foreignKey.ParentSchema, foreignKey.ParentTable)))
            .ToList();

        return metadata with
        {
            Schemas = filteredSchemas,
            AllForeignKeys = filteredForeignKeys,
        };
    }

    private static string QualifyTable(string? schema, string table)
    {
        return string.IsNullOrWhiteSpace(schema) ? table : $"{schema}.{table}";
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
}

