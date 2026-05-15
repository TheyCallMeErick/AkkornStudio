using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.Benchmark;
using AkkornStudio.UI.Services.Explain;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Search;
using AkkornStudio.UI.Services.Settings;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.Services.SqlEditor.Results;
using AkkornStudio.UI.ViewModels;
using System.Diagnostics;
using System.Data;
using System.IO;
using System.Windows.Input;
using System.Text.RegularExpressions;
using Material.Icons;
using AkkornStudio.UI.Services.ConnectionManager.Models;

namespace AkkornStudio.UI.ViewModels;

public sealed class SqlEditorViewModel : ViewModelBase
{
    private const double MinResultsSheetHeight = 160;
    private const double MaxResultsSheetHeight = 720;
    private const int DraftMinimumTextLength = 5;
    private const int HeavyCompletionMetadataTableThreshold = 300;
    private const int DefaultCompletionDebounceMs = 80;
    private const int MinCompletionDebounceMs = 60;
    private const int MaxCompletionDebounceMs = 160;
    private const int CompletionDebounceCalibrationSamples = 12;
    private static readonly TimeSpan DraftAutoSaveDebounce = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DraftAutoSaveInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CompletionMetadataSamplingInterval = TimeSpan.FromSeconds(2);

    private readonly SqlSelectionExtractor _selectionExtractor;
    private readonly SqlScriptStatementSplitter _statementSplitter;
    private readonly SqlEditorExecutionService _executionService;
    private readonly SqlEditorExecutionFeedbackService _executionFeedbackService;
    private readonly SqlEditorFileService _fileService;
    private readonly SqlEditorMutationExecutionOrchestrator _mutationExecutionOrchestrator;
    private readonly SqlEditorCommandNotifier _commandNotifier;
    private readonly SqlEditorPropertyChangePublisher _propertyChangePublisher;
    private readonly SqlEditorResultStateService _resultStateService;
    private readonly SqlEditorTabCloseWorkflowService _tabCloseWorkflowService;
    private readonly SqlResultEligibilityDetector _resultEligibilityDetector;
    private readonly SqlQueryParameterProcessor _queryParameterProcessor;
    private readonly ISqlEditorSessionDraftStore _sessionDraftStore;
    private readonly ILocalizationService _localization;
    private readonly Func<ConnectionConfig?> _connectionConfigResolver;
    private readonly Func<string?, ConnectionConfig?> _connectionConfigByProfileIdResolver;
    private readonly Func<IReadOnlyList<SqlEditorConnectionProfileOption>> _connectionProfilesResolver;
    private readonly Func<ConnectionManagerViewModel?> _sharedConnectionManagerResolver;
    private readonly SqlEditorCompletionController _completionController;
    private readonly Func<DbMetadata?> _metadataResolver;
    private readonly TextSearchService _textSearch = new();
    private readonly SqlEditorExecutionController _executionController;
    private CancellationTokenSource? _executionCts;
    private CancellationTokenSource? _benchmarkCts;
    private CancellationTokenSource? _explainCts;
    private bool _isExecuting;
    private bool _hasExecutionError;
    private string _executionStatusText;
    private string? _executionDetailText;
    private string _historySearchText = string.Empty;
    private string _schemaSearchText = string.Empty;
    private bool _isHistoryClearConfirmationPending;
    private bool _isResultsSheetOpen;
    private double _resultsSheetHeight;
    private double _preferredResultsSheetHeight;
    private MutationGuardResult? _pendingMutationGuard;
    private SqlMutationDiffPreview? _pendingMutationDiff;
    private string? _pendingMutationSql;
    private long? _pendingMutationEstimatedRows;
    private bool _isCancellationPending;
    private Timer? _executionStatusTimer;
    private Stopwatch? _executionStopwatch;
    private int _activeExecutionTotalStatements;
    private int _activeExecutionStatementIndex;
    private int _activeExecutionStatementStartLine;
    private int _activeExecutionStatementEndLine;
    private int _cursorLine = 1;
    private int _cursorColumn = 1;
    private string _signatureHelpText = string.Empty;
    private string _hoverDocumentationText = string.Empty;
    private bool _top1000WithoutWhereEnabled;
    private bool _protectMutationWithoutWhereEnabled;
    private bool _isExplainRunning;
    private string _explainSummaryText = string.Empty;
    private string _explainRawOutput = string.Empty;
    private bool _isBenchmarkRunning;
    private string _benchmarkProgressText = string.Empty;
    private string _benchmarkSummaryText = string.Empty;
    private BenchmarkRunResult? _latestBenchmarkResult;
    private Timer? _draftAutoSaveDebounceTimer;
    private Timer? _draftAutoSaveForcedTimer;
    private DateTimeOffset _lastCompletionMetadataSamplingAt = DateTimeOffset.MinValue;
    private bool _isHeavyCompletionMetadataContext;
    private bool _hasPendingDraftAutoSave;
    private bool _draftAutoSaveTimersStarted;
    private string? _sidebarSelectedConnectionProfileId;
    private bool _isConnectionSwitcherOpen;
    private string? _connectionSwitcherSelectedProfileId;
    private string? _connectionSwitcherSelectedDatabase;
    private DbMetadata? _cachedSchemaMetadata;
    private string? _cachedSchemaSelection;
    private IReadOnlyList<SqlEditorSchemaTableItem> _schemaTablesCache = [];
    private IReadOnlyList<SqlEditorSchemaTableItem> _filteredSchemaTablesCache = [];
    private string _filteredSchemaNeedleCache = string.Empty;
    private bool _isSchemaTablesCacheValid;
    private bool _isFilteredSchemaTablesCacheValid;
    private string? _pendingQueryParameterSql;
    private int _pendingQueryParameterMaxRows = 1000;
    private bool _pendingQueryParameterEnforceMutationGuard = true;
    private List<SqlQueryParameterPromptItemViewModel> _pendingQueryParameters = [];

    public SqlEditorViewModel(
        DatabaseProvider initialProvider = DatabaseProvider.Postgres,
        string? initialConnectionProfileId = null,
        SqlSelectionExtractor? selectionExtractor = null,
        SqlScriptStatementSplitter? statementSplitter = null,
        SqlEditorExecutionService? executionService = null,
        SqlEditorMutationExecutionOrchestrator? mutationExecutionOrchestrator = null,
        ILocalizationService? localization = null,
        Func<ConnectionConfig?>? connectionConfigResolver = null,
        Func<string?, ConnectionConfig?>? connectionConfigByProfileIdResolver = null,
        Func<IReadOnlyList<SqlEditorConnectionProfileOption>>? connectionProfilesResolver = null,
        SqlCompletionProvider? completionProvider = null,
        Func<DbMetadata?>? metadataResolver = null,
        Func<ConnectionManagerViewModel?>? sharedConnectionManagerResolver = null,
        ISqlEditorSessionDraftStore? sessionDraftStore = null,
        SqlEditorExplainService? sqlEditorExplainService = null,
        SqlEditorBenchmarkService? sqlEditorBenchmarkService = null,
        SqlEditorCompletionController? completionController = null,
        SqlEditorExecutionController? executionController = null,
        SqlQueryParameterProcessor? queryParameterProcessor = null)
    {
        _localization = localization ?? LocalizationService.Instance;
        _selectionExtractor = selectionExtractor ?? new SqlSelectionExtractor();
        _statementSplitter = statementSplitter ?? new SqlScriptStatementSplitter();
        _executionService = executionService ?? new SqlEditorExecutionService(localization: _localization);
        _executionFeedbackService = new SqlEditorExecutionFeedbackService(_localization);
        _fileService = new SqlEditorFileService(_localization);
        MutationGuardService resolvedMutationGuardService = new(_localization);
        SqlMutationDiffService resolvedMutationDiffService = new(_executionService, _localization);
        _mutationExecutionOrchestrator = mutationExecutionOrchestrator ?? new SqlEditorMutationExecutionOrchestrator(
            _executionService,
            resolvedMutationGuardService,
            resolvedMutationDiffService,
            _localization);
        _resultStateService = new SqlEditorResultStateService(_localization);
        _commandNotifier = new SqlEditorCommandNotifier();
        _propertyChangePublisher = new SqlEditorPropertyChangePublisher();
        _tabCloseWorkflowService = new SqlEditorTabCloseWorkflowService(_localization);
        _resultEligibilityDetector = new SqlResultEligibilityDetector();
        _queryParameterProcessor = queryParameterProcessor ?? new SqlQueryParameterProcessor();
        _sessionDraftStore = sessionDraftStore ?? new SqlEditorSessionDraftStore();
        _connectionConfigResolver = connectionConfigResolver ?? (() => null);
        _connectionConfigByProfileIdResolver = connectionConfigByProfileIdResolver ?? (_ => _connectionConfigResolver());
        _connectionProfilesResolver = connectionProfilesResolver ?? (() => []);
        _sharedConnectionManagerResolver = sharedConnectionManagerResolver ?? (() => null);
        _completionController = completionController ?? new SqlEditorCompletionController(completionProvider);
        _metadataResolver = metadataResolver ?? (() => null);
        _executionController = executionController ?? new SqlEditorExecutionController(
            sqlEditorExplainService,
            sqlEditorBenchmarkService);
        _executionStatusText = L("sqlEditor.status.ready", "Pronto.");
        (bool top1000WithoutWhereEnabled, bool protectMutationWithoutWhereEnabled) = AppSettingsStore.LoadSqlEditorSafetySettings();
        _top1000WithoutWhereEnabled = top1000WithoutWhereEnabled;
        _protectMutationWithoutWhereEnabled = protectMutationWithoutWhereEnabled;
        _preferredResultsSheetHeight = ClampResultsSheetHeight(AppSettingsStore.LoadSqlEditorResultsSheetHeight());
        Tabs = new SqlEditorTabManagerViewModel(_localization);
        RestoreOrInitializeTabs(initialProvider, initialConnectionProfileId);
        TryHydrateResultFilterForTab(Tabs.GetActiveTab(), force: true);
        TryHydrateExecutionHistoryForTab(Tabs.GetActiveTab(), force: true);
        InitializeDraftAutoSaveTimers();
        NewTabCommand = new RelayCommand(
            AddNewTab,
            () => !IsExecuting);
        CloseTabCommand = new RelayCommand<string>(
            CloseTabById,
            tabId => !IsExecuting && CanCloseTab(tabId));
        CloseActiveTabCommand = new RelayCommand(
            () => CloseTabById(ActiveTab.Id),
            () => !IsExecuting && CanCloseTab(ActiveTab.Id));
        ConfirmPendingCloseTabCommand = new RelayCommand(
            ConfirmPendingCloseTab,
            () => !IsExecuting && HasPendingCloseTabConfirmation);
        CancelPendingCloseTabCommand = new RelayCommand(
            CancelPendingCloseTab,
            () => !IsExecuting && HasPendingCloseTabConfirmation);
        ConfirmPendingMutationCommand = new RelayCommand(
            () => _ = ConfirmPendingMutationAsync(),
            () => HasPendingMutationConfirmation && !IsExecuting);
        CancelPendingMutationCommand = new RelayCommand(
            CancelPendingMutation,
            () => HasPendingMutationConfirmation && !IsExecuting);
        ConfirmPendingQueryParametersCommand = new RelayCommand(
            () => _ = ConfirmPendingQueryParametersAsync(),
            () => HasPendingQueryParameterPrompt && !IsExecuting);
        CancelPendingQueryParametersCommand = new RelayCommand(
            CancelPendingQueryParameters,
            () => HasPendingQueryParameterPrompt && !IsExecuting);
        OpenConnectionSwitcherCommand = new RelayCommand(
            OpenConnectionSwitcher,
            () => AvailableConnectionProfiles.Count > 0);
        CloseConnectionSwitcherCommand = new RelayCommand(
            CloseConnectionSwitcher,
            () => IsConnectionSwitcherOpen);
        ApplyConnectionSwitcherCommand = new RelayCommand(
            ApplyConnectionSwitcher,
            () => IsConnectionSwitcherOpen);
        ApplySidebarConnectionToTabCommand = new RelayCommand(
            ApplySidebarConnectionToTab,
            () => SidebarSelectedConnectionProfile is not null);
        ApplySidebarConnectionToApplicationCommand = new RelayCommand(
            ApplySidebarConnectionToApplication,
            () => SidebarSelectedConnectionProfile is not null);
        ExecuteHistoryEntryCommand = new RelayCommand<SqlEditorHistoryEntry>(
            entry => _ = ExecuteHistoryEntryAsync(entry),
            entry => !IsExecuting && entry is not null && !string.IsNullOrWhiteSpace(entry.Sql));
        ExplainCommand = new RelayCommand<string>(
            sql => _ = RunExplainForSqlAsync(sql),
            _ => !IsExplainRunning);
        BenchmarkCommand = new RelayCommand<string>(
            sql => _ = RunBenchmarkForSqlAsync(sql),
            _ => !IsBenchmarkRunning);
        CancelBenchmarkCommand = new RelayCommand(
            CancelBenchmark,
            () => IsBenchmarkRunning);
        CloseResultsSheetCommand = new RelayCommand(
            CloseResultsSheet,
            () => IsResultsSheetOpen);
        ReopenResultsSheetCommand = new RelayCommand(
            OpenResultsSheet,
            () => CanReopenResultsSheet);
        _sidebarSelectedConnectionProfileId = ActiveTab.ConnectionProfileId;
        SyncTabCommands();
    }

    public SqlEditorTabManagerViewModel Tabs { get; }

    public SqlEditorTabState ActiveTab => Tabs.GetActiveTab();
    public IReadOnlyList<SqlEditorTabState> EditorTabs => Tabs.Tabs;
    public int ActiveEditorTabIndex
    {
        get => Tabs.ActiveTabIndex;
        set
        {
            if (Tabs.TryActivate(value))
                RaiseTabStateChanged();
        }
    }
    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand CloseActiveTabCommand { get; }
    public ICommand ConfirmPendingCloseTabCommand { get; }
    public ICommand CancelPendingCloseTabCommand { get; }
    public ICommand ConfirmPendingMutationCommand { get; }
    public ICommand CancelPendingMutationCommand { get; }
    public ICommand ConfirmPendingQueryParametersCommand { get; }
    public ICommand CancelPendingQueryParametersCommand { get; }
    public ICommand OpenConnectionSwitcherCommand { get; }
    public ICommand CloseConnectionSwitcherCommand { get; }
    public ICommand ApplyConnectionSwitcherCommand { get; }
    public ICommand ApplySidebarConnectionToTabCommand { get; }
    public ICommand ApplySidebarConnectionToApplicationCommand { get; }
    public ICommand ExecuteHistoryEntryCommand { get; }
    public ICommand ExplainCommand { get; }
    public ICommand BenchmarkCommand { get; }
    public ICommand CancelBenchmarkCommand { get; }
    public ICommand CloseResultsSheetCommand { get; }
    public ICommand ReopenResultsSheetCommand { get; }
    public IReadOnlyList<DatabaseProvider> AvailableProviders { get; } = Enum.GetValues<DatabaseProvider>();

    public event EventHandler<SqlResultPageRequestedEventArgs>? SqlResultPageRequested;

    public bool Top1000WithoutWhereEnabled
    {
        get => _top1000WithoutWhereEnabled;
        private set => Set(ref _top1000WithoutWhereEnabled, value);
    }

    public bool ProtectMutationWithoutWhereEnabled
    {
        get => _protectMutationWithoutWhereEnabled;
        private set => Set(ref _protectMutationWithoutWhereEnabled, value);
    }

    public DatabaseProvider ActiveTabProvider
    {
        get => ResolveConnectionConfigForActiveTab()?.Provider ?? ActiveTab.Provider;
        set
        {
            if (HasResolvedConnection)
                return;

            if (ActiveTab.Provider == value)
                return;

            ActiveTab.Provider = value;
            if (!string.IsNullOrWhiteSpace(ActiveTab.ConnectionProfileId))
            {
                bool hasCompatibleProfile = AvailableConnectionProfiles.Any(p =>
                    string.Equals(p.Id, ActiveTab.ConnectionProfileId, StringComparison.Ordinal));
                if (!hasCompatibleProfile)
                    ActiveTab.ConnectionProfileId = null;
            }

            RaiseSqlPanelPropertiesChanged();
        }
    }
    public IReadOnlyList<SqlEditorConnectionProfileOption> AvailableConnectionProfiles =>
        _connectionProfilesResolver();
    public SqlEditorConnectionProfileOption? ActiveTabConnectionProfile
    {
        get => AvailableConnectionProfiles.FirstOrDefault(option =>
            string.Equals(option.Id, ActiveTab.ConnectionProfileId, StringComparison.Ordinal));
        set => ActiveTabConnectionProfileId = value?.Id;
    }
    public string? ActiveTabConnectionProfileId
    {
        get => ActiveTab.ConnectionProfileId;
        set
        {
            if (string.Equals(ActiveTab.ConnectionProfileId, value, StringComparison.Ordinal))
                return;

            bool hasValue = !string.IsNullOrWhiteSpace(value);
            bool existsInAnyProfile = hasValue && AvailableConnectionProfiles.Any(p =>
                string.Equals(p.Id, value, StringComparison.Ordinal));
            ActiveTab.ConnectionProfileId = existsInAnyProfile ? value : null;
            if (existsInAnyProfile)
            {
                SqlEditorConnectionProfileOption? selected = AvailableConnectionProfiles.FirstOrDefault(p =>
                    string.Equals(p.Id, value, StringComparison.Ordinal));
                if (selected is not null)
                    ActiveTab.Provider = selected.Provider;
            }

            HistorySearchText = string.Empty;
            TryHydrateExecutionHistoryForTab(ActiveTab, force: true);
            SyncSidebarConnectionSelectionToActiveTab();
            if (IsConnectionSwitcherOpen)
            {
                ConnectionSwitcherSelectedProfileId = ActiveTab.ConnectionProfileId;
                ConnectionSwitcherSelectedDatabase = SharedConnectionManager?.SelectedDatabase
                    ?? ResolveConnectionConfigForActiveTab()?.Database;
            }
            RaiseSqlPanelPropertiesChanged();
        }
    }
    public bool HasConnectionProfiles => AvailableConnectionProfiles.Count > 0;
    public SqlEditorConnectionProfileOption? SidebarSelectedConnectionProfile
    {
        get
        {
            string? selectedId = _sidebarSelectedConnectionProfileId;
            if (string.IsNullOrWhiteSpace(selectedId))
                selectedId = ActiveTabConnectionProfileId;

            return AvailableConnectionProfiles.FirstOrDefault(option =>
                string.Equals(option.Id, selectedId, StringComparison.Ordinal));
        }
        set
        {
            string? nextId = value?.Id;
            if (!string.IsNullOrWhiteSpace(nextId)
                && !AvailableConnectionProfiles.Any(profile => string.Equals(profile.Id, nextId, StringComparison.Ordinal)))
            {
                nextId = null;
            }

            if (string.Equals(_sidebarSelectedConnectionProfileId, nextId, StringComparison.Ordinal))
                return;

            _sidebarSelectedConnectionProfileId = nextId;
            RaisePropertyChanged(nameof(SidebarSelectedConnectionProfile));
            RaisePropertyChanged(nameof(HasSidebarSelectedConnectionProfile));
            (ApplySidebarConnectionToTabCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (ApplySidebarConnectionToApplicationCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }
    public bool HasSidebarSelectedConnectionProfile => SidebarSelectedConnectionProfile is not null;
    public ConnectionManagerViewModel? SharedConnectionManager => _sharedConnectionManagerResolver();
    public bool HasSharedConnectionManager => SharedConnectionManager is not null;
    public bool HasResolvedConnection => ResolveConnectionConfigForActiveTab() is not null;
    public string ActiveConnectionDisplayName
    {
        get
        {
            SqlEditorConnectionProfileOption? profile = ActiveTabConnectionProfile;
            if (profile is not null)
                return profile.DisplayName;

            ConnectionConfig? config = ResolveConnectionConfigForActiveTab();
            if (config is null)
                return L("sqlEditor.connection.none", "Sem conexao ativa.");

            return $"{config.Database} @ {config.Host}:{config.Port}";
        }
    }
    public string ActiveConnectionSubtitle
    {
        get
        {
            ConnectionConfig? config = ResolveConnectionConfigForActiveTab();
            if (config is null)
                return L("sqlEditor.connection.required", "Conecte-se a um banco para executar e inspecionar o schema.");

            return $"{config.Provider}  •  {config.Username}";
        }
    }
    public bool IsConnectionSwitcherOpen
    {
        get => _isConnectionSwitcherOpen;
        private set
        {
            if (!Set(ref _isConnectionSwitcherOpen, value))
                return;

            RaisePropertyChanged(nameof(ConnectionSwitcherProfiles));
            RaisePropertyChanged(nameof(ConnectionSwitcherDatabases));
            RaisePropertyChanged(nameof(CurrentConnectionProfileLabel));
            RaisePropertyChanged(nameof(CurrentConnectionProviderLabel));
            RaisePropertyChanged(nameof(CurrentConnectionDatabaseLabel));
            RaisePropertyChanged(nameof(CurrentConnectionHostLabel));
            RaisePropertyChanged(nameof(CurrentConnectionUserLabel));
            RaisePropertyChanged(nameof(CurrentConnectionUrlLabel));
            NotifyCommands();
        }
    }
    public IReadOnlyList<SqlEditorConnectionProfileOption> ConnectionSwitcherProfiles => AvailableConnectionProfiles;
    public string? ConnectionSwitcherSelectedProfileId
    {
        get => _connectionSwitcherSelectedProfileId;
        set
        {
            if (!string.IsNullOrWhiteSpace(value)
                && !AvailableConnectionProfiles.Any(profile => string.Equals(profile.Id, value, StringComparison.Ordinal)))
            {
                value = null;
            }

            if (!Set(ref _connectionSwitcherSelectedProfileId, value))
                return;

            RaisePropertyChanged(nameof(ConnectionSwitcherSelectedProfile));
            RaisePropertyChanged(nameof(CurrentConnectionProfileLabel));
            RaisePropertyChanged(nameof(CurrentConnectionProviderLabel));
            RaisePropertyChanged(nameof(CurrentConnectionDatabaseLabel));
            RaisePropertyChanged(nameof(CurrentConnectionHostLabel));
            RaisePropertyChanged(nameof(CurrentConnectionUserLabel));
            RaisePropertyChanged(nameof(CurrentConnectionUrlLabel));
        }
    }
    public SqlEditorConnectionProfileOption? ConnectionSwitcherSelectedProfile
    {
        get => AvailableConnectionProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, ConnectionSwitcherSelectedProfileId, StringComparison.Ordinal));
        set => ConnectionSwitcherSelectedProfileId = value?.Id;
    }
    public string? ConnectionSwitcherSelectedDatabase
    {
        get => _connectionSwitcherSelectedDatabase;
        set => Set(ref _connectionSwitcherSelectedDatabase, value);
    }
    public IReadOnlyList<string> ConnectionSwitcherDatabases =>
        SharedConnectionManager?.AvailableDatabases.ToList() ?? [];
    public bool CanChangeDatabaseInSwitcher =>
        SharedConnectionManager?.IsDatabaseSelectionVisible == true;
    public string CurrentConnectionProfileLabel =>
        ConnectionSwitcherSelectedProfile?.DisplayName
        ?? ActiveConnectionDisplayName;
    public string CurrentConnectionProviderLabel
    {
        get
        {
            ConnectionConfig? config = ResolveConnectionConfigForProfile(ConnectionSwitcherSelectedProfileId)
                ?? ResolveConnectionConfigForActiveTab();
            return config is null ? "-" : GetProviderDisplayName(config.Provider);
        }
    }
    public string CurrentConnectionDatabaseLabel
    {
        get
        {
            ConnectionConfig? config = ResolveConnectionConfigForProfile(ConnectionSwitcherSelectedProfileId)
                ?? ResolveConnectionConfigForActiveTab();
            return config?.Database ?? "-";
        }
    }
    public string CurrentConnectionHostLabel
    {
        get
        {
            ConnectionConfig? config = ResolveConnectionConfigForProfile(ConnectionSwitcherSelectedProfileId)
                ?? ResolveConnectionConfigForActiveTab();
            return config is null ? "-" : $"{config.Host}:{config.Port}";
        }
    }
    public string CurrentConnectionUserLabel
    {
        get
        {
            ConnectionConfig? config = ResolveConnectionConfigForProfile(ConnectionSwitcherSelectedProfileId)
                ?? ResolveConnectionConfigForActiveTab();
            return config?.Username ?? "-";
        }
    }
    public string CurrentConnectionUrlLabel
    {
        get
        {
            ConnectionConfig? config = ResolveConnectionConfigForProfile(ConnectionSwitcherSelectedProfileId)
                ?? ResolveConnectionConfigForActiveTab();
            if (config is null)
                return "-";

            string provider = config.Provider.ToString().ToLowerInvariant();
            return $"{provider}://{config.Host}:{config.Port}/{config.Database}";
        }
    }
    public bool ShowDialectSelector => !HasResolvedConnection;
    public DatabaseProvider FallbackDialect
    {
        get => ActiveTab.Provider;
        set
        {
            if (ActiveTab.Provider == value)
                return;

            ActiveTab.Provider = value;
            RaiseSqlPanelPropertiesChanged();
        }
    }
    public bool IsResultsSheetOpen
    {
        get => _isResultsSheetOpen;
        private set
        {
            if (!Set(ref _isResultsSheetOpen, value))
                return;

            ResultsSheetHeight = value ? _preferredResultsSheetHeight : 0;
            RaisePropertyChanged(nameof(ShouldShowResultsSheet));
            NotifyCommands();
        }
    }

    public bool ShouldShowResultsSheet =>
        IsResultsSheetOpen
        && CurrentResult is not null;
    public bool CanReopenResultsSheet => CurrentResult is not null;
    public string RestoreResultsButtonText => L("sqlEditor.results.restore", "Exibir resultados");

    public double ResultsSheetHeight
    {
        get => _resultsSheetHeight;
        private set => Set(ref _resultsSheetHeight, value);
    }

    public void SetResultsSheetHeight(double height)
    {
        if (!IsResultsSheetOpen)
            return;

        double bounded = ClampResultsSheetHeight(height);
        ResultsSheetHeight = bounded;
        _preferredResultsSheetHeight = bounded;
    }

    public void PersistResultsSheetHeightPreference()
    {
        AppSettingsStore.SaveSqlEditorResultsSheetHeight(_preferredResultsSheetHeight);
    }

    public bool IsResultColumnHidden(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return false;

        return ActiveTab.HiddenResultColumns.Contains(columnName);
    }

    public bool IsResultColumnPinned(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return false;

        return ActiveTab.PinnedResultColumns.Contains(columnName);
    }

    public void HideResultColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return;

        if (ActiveTab.HiddenResultColumns.Contains(columnName))
            return;

        ActiveTab.HiddenResultColumns.Add(columnName);
        ActiveTab.HiddenResultColumnsHistory.Add(columnName);
        RaiseSqlPanelPropertiesChanged();
    }

    public void ShowAllResultColumns()
    {
        if (ActiveTab.HiddenResultColumns.Count == 0)
            return;

        ActiveTab.HiddenResultColumns.Clear();
        ActiveTab.HiddenResultColumnsHistory.Clear();
        RaiseSqlPanelPropertiesChanged();
    }

    public void SetResultColumnPinned(string columnName, bool pinned)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return;

        if (pinned)
            ActiveTab.PinnedResultColumns.Add(columnName);
        else
            ActiveTab.PinnedResultColumns.Remove(columnName);

        RaiseSqlPanelPropertiesChanged();
    }

    public void SetResultColumnOrder(IReadOnlyList<string> orderedColumnNames)
    {
        ArgumentNullException.ThrowIfNull(orderedColumnNames);

        ActiveTab.ResultColumnOrder = orderedColumnNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        RaiseSqlPanelPropertiesChanged();
    }

    public bool HasHiddenResultColumns => ActiveTab.HiddenResultColumns.Count > 0;
    public int HiddenResultColumnsCount => ActiveTab.HiddenResultColumns.Count;
    public string ShowAllColumnsButtonText => string.Format(
        L("sqlEditor.results.showAllColumns", "Mostrar colunas ({0})"),
        HiddenResultColumnsCount);
    public bool HasHiddenColumnUndo => ActiveTab.HiddenResultColumnsHistory.Count > 0;
    public string UndoHiddenColumnButtonText => L("sqlEditor.results.undoHidden", "Desfazer ocultar");

    public bool UndoLastHiddenResultColumn()
    {
        if (!HasHiddenColumnUndo)
            return false;

        for (int i = ActiveTab.HiddenResultColumnsHistory.Count - 1; i >= 0; i--)
        {
            string columnName = ActiveTab.HiddenResultColumnsHistory[i];
            ActiveTab.HiddenResultColumnsHistory.RemoveAt(i);
            if (!ActiveTab.HiddenResultColumns.Contains(columnName))
                continue;

            ActiveTab.HiddenResultColumns.Remove(columnName);
            RaiseSqlPanelPropertiesChanged();
            return true;
        }

        RaiseSqlPanelPropertiesChanged();
        return false;
    }

    public string ResultGridFilterText
    {
        get => ActiveTab.ResultGridFilterText;
        set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(ActiveTab.ResultGridFilterText, normalized, StringComparison.Ordinal))
                return;

            ActiveTab.ResultGridFilterText = normalized;
            PersistResultGridFilterForTab(ActiveTab);
            RaiseSqlPanelPropertiesChanged();
        }
    }

    public string? ResultGridSortColumn => ActiveTab.ResultGridSortColumn;
    public bool ResultGridSortAscending => ActiveTab.ResultGridSortAscending;

    public void SetResultGridSort(string? column, bool ascending)
    {
        ActiveTab.ResultGridSortColumn = string.IsNullOrWhiteSpace(column) ? null : column;
        ActiveTab.ResultGridSortAscending = ascending;
        RaiseSqlPanelPropertiesChanged();
    }

    public bool HasExecutionHistory => ExecutionHistory.Count > 0;
    public bool IsExecutionHistoryEmpty => !HasExecutionHistory;
    public string HistorySearchText
    {
        get => _historySearchText;
        set
        {
            string normalized = value ?? string.Empty;
            if (!Set(ref _historySearchText, normalized))
                return;

            if (_isHistoryClearConfirmationPending)
                _isHistoryClearConfirmationPending = false;

            EnsureHistorySelection();

            RaisePropertyChanged(nameof(FilteredExecutionHistory));
            RaisePropertyChanged(nameof(HasFilteredExecutionHistory));
            RaisePropertyChanged(nameof(IsFilteredExecutionHistoryEmpty));
            RaisePropertyChanged(nameof(HasHistorySearchNoResults));
            RaisePropertyChanged(nameof(HistoryFilterSummaryText));
            RaisePropertyChanged(nameof(SelectedExecutionHistoryEntry));
            RaisePropertyChanged(nameof(HasPendingHistoryClearConfirmation));
            RaisePropertyChanged(nameof(ClearHistoryButtonText));
        }
    }
    public IReadOnlyList<SqlEditorHistoryEntry> FilteredExecutionHistory
    {
        get
        {
            if (string.IsNullOrWhiteSpace(HistorySearchText))
                return ExecutionHistory;

            string needle = HistorySearchText.Trim();
            return ExecutionHistory
                .Where(entry => _textSearch.Matches(needle, entry.Sql))
                .ToList();
        }
    }
    public bool HasFilteredExecutionHistory => FilteredExecutionHistory.Count > 0;
    public bool IsFilteredExecutionHistoryEmpty => !HasFilteredExecutionHistory;
    public SqlEditorHistoryEntry? SelectedExecutionHistoryEntry
    {
        get => ActiveTab.SelectedExecutionHistoryEntry;
        set
        {
            if (ReferenceEquals(ActiveTab.SelectedExecutionHistoryEntry, value))
                return;

            ActiveTab.SelectedExecutionHistoryEntry = value;
            RaisePropertyChanged(nameof(SelectedExecutionHistoryEntry));
        }
    }

    public void SelectNextHistoryEntry()
    {
        IReadOnlyList<SqlEditorHistoryEntry> items = FilteredExecutionHistory;
        if (items.Count == 0)
            return;

        int current = ResolveHistorySelectionIndex(items, SelectedExecutionHistoryEntry);
        int next = current < 0 ? 0 : Math.Min(current + 1, items.Count - 1);
        SelectedExecutionHistoryEntry = items[next];
    }

    public void SelectPreviousHistoryEntry()
    {
        IReadOnlyList<SqlEditorHistoryEntry> items = FilteredExecutionHistory;
        if (items.Count == 0)
            return;

        int current = ResolveHistorySelectionIndex(items, SelectedExecutionHistoryEntry);
        int previous = current <= 0 ? 0 : current - 1;
        SelectedExecutionHistoryEntry = items[previous];
    }

    public Task<SqlEditorResultSet?> ExecuteSelectedHistoryEntryAsync(int maxRows = 1000)
    {
        SqlEditorHistoryEntry? target = SelectedExecutionHistoryEntry ?? FilteredExecutionHistory.FirstOrDefault();
        return ExecuteHistoryEntryAsync(target, maxRows);
    }

    public bool HasHistorySearchNoResults =>
        HasExecutionHistory
        && !string.IsNullOrWhiteSpace(HistorySearchText)
        && IsFilteredExecutionHistoryEmpty;
    public string HistoryFilterSummaryText
    {
        get
        {
            int total = ExecutionHistory.Count;
            if (total == 0)
                return L("sqlEditor.history.count.none", "0 itens");

            int filtered = FilteredExecutionHistory.Count;
            if (string.IsNullOrWhiteSpace(HistorySearchText))
                return string.Format(
                    L("sqlEditor.history.count.total", "{0} itens"),
                    total);

            return string.Format(
                L("sqlEditor.history.count.filtered", "{0} de {1} itens"),
                filtered,
                total);
        }
    }
    public bool HasPendingHistoryClearConfirmation => _isHistoryClearConfirmationPending;
    public string ClearHistoryButtonText =>
        HasPendingHistoryClearConfirmation
            ? L("sqlEditor.history.clear.confirm", "Confirmar limpeza")
            : L("sqlEditor.history.clear", "Limpar historico");
    public string HistoryEmptyText => L("sqlEditor.history.empty", "Execute uma consulta para iniciar o historico.");
    public string MessagesEmptyText => L("sqlEditor.messages.empty", "As mensagens aparecem aqui apos a execucao.");
    public string SchemaSearchText
    {
        get => _schemaSearchText;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (!Set(ref _schemaSearchText, normalized))
                return;

            _isFilteredSchemaTablesCacheValid = false;
            RaiseSchemaPropertiesChanged();
        }
    }
    public IReadOnlyList<SqlEditorSchemaTableItem> SchemaTables
    {
        get
        {
            EnsureSchemaTablesCache();
            return _schemaTablesCache;
        }
    }
    public IReadOnlyList<SqlEditorSchemaTableItem> FilteredSchemaTables
    {
        get
        {
            EnsureSchemaTablesCache();

            if (string.IsNullOrWhiteSpace(SchemaSearchText))
                return _schemaTablesCache;

            if (_isFilteredSchemaTablesCacheValid
                && string.Equals(_filteredSchemaNeedleCache, SchemaSearchText, StringComparison.Ordinal))
            {
                return _filteredSchemaTablesCache;
            }

            string needle = SchemaSearchText.Trim();
            _filteredSchemaTablesCache = _schemaTablesCache
                .Where(table =>
                    _textSearch.Matches(
                        needle,
                        table.FullName,
                        table.SearchKey,
                        table.ColumnSearchText))
                .ToList();

            _filteredSchemaNeedleCache = needle;
            _isFilteredSchemaTablesCacheValid = true;
            return _filteredSchemaTablesCache;
        }
    }
    public bool HasSchemaTables => SchemaTables.Count > 0;
    public bool HasFilteredSchemaTables => FilteredSchemaTables.Count > 0;
    public bool IsSchemaEmpty => !HasFilteredSchemaTables;
    public string SchemaEmptyText => string.IsNullOrWhiteSpace(SchemaSearchText)
        ? L("sqlEditor.schema.empty", "Sem metadados disponiveis. Conecte e recarregue para ver tabelas.")
        : L("sqlEditor.schema.empty.filter", "Nenhum item encontrado para o filtro informado.");
    public IReadOnlyList<SqlEditorResultTab> ResultTabs => ActiveTab.ResultTabs;
    public SqlEditorOutputPane SelectedOutputPane
    {
        get => ActiveTab.SelectedOutputPane;
        set
        {
            if (ActiveTab.SelectedOutputPane == value)
                return;

            ActiveTab.SelectedOutputPane = value;
            RaiseSqlPanelPropertiesChanged();
        }
    }
    public bool IsResultsOutputPaneSelected => SelectedOutputPane == SqlEditorOutputPane.Results;
    public bool IsMessagesOutputPaneSelected => SelectedOutputPane == SqlEditorOutputPane.Messages;
    public int SelectedResultTabIndex
    {
        get => ActiveTab.SelectedResultTabIndex;
        set
        {
            int boundedValue = value;
            if (value >= ResultTabs.Count)
                boundedValue = ResultTabs.Count - 1;
            if (value < -1)
                boundedValue = -1;

            if (ActiveTab.SelectedResultTabIndex == boundedValue)
                return;

            ActiveTab.SelectedResultTabIndex = boundedValue;
            RaiseSqlPanelPropertiesChanged();
        }
    }
    public DataView? ResultRowsView => SelectedResultTab?.RowsView ?? ActiveTab.LastResult?.Data?.DefaultView;
    public bool HasResultRows => ResultRowsView is not null;
    public bool IsResultRowsEmpty => !HasResultRows;
    public string ResultsEmptyText => L("sqlEditor.results.empty", "Execute uma consulta para preencher os resultados.");
    public IReadOnlyList<SqlEditorMessageEntry> OutputMessages => ActiveTab.OutputMessages;
    public bool HasOutputMessages => OutputMessages.Count > 0;
    public bool IsOutputMessagesEmpty => !HasOutputMessages;
    public string MessagesPanelEmptyText => L("sqlEditor.messages.panel.empty", "Sem mensagens registradas para esta aba.");
    public IReadOnlyList<SqlEditorHistoryEntry> ExecutionHistory => ActiveTab.ExecutionHistory;
    public SqlEditorExecutionTelemetry ExecutionTelemetry => ActiveTab.ExecutionTelemetry;
    public SqlEditorCompletionTelemetry CompletionTelemetry => ActiveTab.CompletionTelemetry;
    public string CompletionTelemetryText =>
        CompletionTelemetry.SampleCount == 0
            ? L("sqlEditor.completion.telemetry.none", "Completion: sem amostras ainda.")
            : string.Format(
                L(
                    "sqlEditor.completion.telemetry.summary",
                    "Completion p95: {0} ms    Ultima: {1} ms    Engine p95: {2} ms    Fila p95: {3} ms    UI p95: {4} ms    UI ultima: {5} ms    Amostras: {6}    Budget<= {7} ms"),
                CompletionTelemetry.P95DurationMs,
                CompletionTelemetry.LastDurationMs,
                CompletionTelemetry.P95EngineDurationMs,
                CompletionTelemetry.P95DispatchDelayMs,
                CompletionTelemetry.P95UiApplyDurationMs,
                CompletionTelemetry.LastUiApplyDurationMs,
                CompletionTelemetry.SampleCount,
                CompletionTelemetry.BudgetMs);
    public string ExecutionTelemetryText =>
        ExecutionTelemetry.StatementCount == 0
            ? L("sqlEditor.telemetry.none", "Sem telemetria de execucao ainda.")
            : string.Format(
                L("sqlEditor.telemetry.summary", "Instrucoes: {0}    Sucesso: {1}    Falhas: {2}    Total: {3} ms"),
                ExecutionTelemetry.StatementCount,
                ExecutionTelemetry.SuccessCount,
                ExecutionTelemetry.FailureCount,
                ExecutionTelemetry.TotalDurationMs);
    public string ExecutionTelemetryErrorsText =>
        ExecutionTelemetry.ErrorMessages.Count == 0
            ? L("sqlEditor.telemetry.errors.none", "Sem erros agregados.")
            : BuildExecutionTelemetryErrorsText();
    public MutationGuardResult? PendingMutationGuard
    {
        get => _pendingMutationGuard;
        private set
        {
            if (!Set(ref _pendingMutationGuard, value))
                return;

            RaisePropertyChanged(nameof(HasPendingMutationConfirmation));
            RaisePropertyChanged(nameof(PendingMutationIssues));
            RaisePropertyChanged(nameof(PendingMutationCountQuery));
            RaisePropertyChanged(nameof(PendingMutationMessage));
            NotifyMutationCommands();
        }
    }

    public bool HasPendingMutationConfirmation => PendingMutationGuard is not null;
    public SqlMutationDiffPreview? PendingMutationDiff
    {
        get => _pendingMutationDiff;
        private set
        {
            if (!Set(ref _pendingMutationDiff, value))
                return;

            RaisePropertyChanged(nameof(HasPendingMutationDiff));
            RaisePropertyChanged(nameof(PendingMutationDiffText));
        }
    }
    public bool HasPendingMutationDiff => PendingMutationDiff is { Available: true };
    public string PendingMutationDiffText => PendingMutationDiff?.Message ?? L("sqlEditor.diff.none", "Sem diff transacional disponivel.");
    public long? PendingMutationEstimatedRows => _pendingMutationEstimatedRows;
    public string PendingMutationEstimateText =>
        !HasPendingMutationConfirmation
            ? L("sqlEditor.mutation.estimate.none", "Sem estimativa de mutacao disponivel.")
            : _pendingMutationEstimatedRows.HasValue
                ? string.Format(
                    L("sqlEditor.mutation.estimate.value", "Linhas afetadas estimadas: {0}"),
                    _pendingMutationEstimatedRows.Value)
                : L("sqlEditor.mutation.estimate.unavailable", "Nao foi possivel estimar as linhas afetadas automaticamente.");
    public bool HasPendingCloseTabConfirmation => _tabCloseWorkflowService.HasPendingConfirmation;
    public string PendingCloseTabMessage => _tabCloseWorkflowService.PendingMessage;
    public bool HasManyTabsWarning => EditorTabs.Count >= 15;
    public string ManyTabsWarningText => string.Format(
        L("sqlEditor.tab.manyWarning", "Quantidade alta de abas: {0} abas abertas."),
        EditorTabs.Count);
    public IReadOnlyList<MutationGuardIssue> PendingMutationIssues => PendingMutationGuard?.Issues ?? [];
    public string? PendingMutationCountQuery => PendingMutationGuard?.CountQuery;
    public string PendingMutationMessage =>
        PendingMutationGuard is null
            ? L("sqlEditor.mutation.pending.none", "Sem confirmacao de mutacao pendente.")
            : L("sqlEditor.mutation.pending.required", "A mutacao exige confirmacao antes da execucao.");
    public bool HasPendingQueryParameterPrompt => _pendingQueryParameterSql is not null && _pendingQueryParameters.Count > 0;
    public IReadOnlyList<SqlQueryParameterPromptItemViewModel> PendingQueryParameters => _pendingQueryParameters;
    public string PendingQueryParameterPromptMessage =>
        !HasPendingQueryParameterPrompt
            ? L("sqlEditor.parameters.none", "Sem parametros pendentes.")
            : L("sqlEditor.parameters.required", "Informe os valores dos parametros antes da execucao.");
    public string LastExecutionMessage =>
        CurrentResult is null
            ? L("sqlEditor.message.empty", "Execute uma instrucao para ver mensagens.")
            : CurrentResult.ErrorMessage ?? L("sqlEditor.message.success", "Execucao concluida com sucesso.");
    private string BuildExecutionTelemetryErrorsText()
    {
        string errors = string.Join(Environment.NewLine, ExecutionTelemetry.ErrorMessages);
        if (ExecutionTelemetry.FailureByCategory.Count == 0)
            return errors;

        string categorySummary = string.Join(
            ", ",
            ExecutionTelemetry.FailureByCategory
                .OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key}:{pair.Value}"));

        return $"{errors}{Environment.NewLine}{L("sqlEditor.telemetry.errors.categories", "Categorias")}: {categorySummary}";
    }
    public bool CanExportReport => CurrentResult is not null;
    public string ResultSummaryText
    {
        get
        {
            SqlEditorResultSet? result = CurrentResult;
            if (result is null)
                return L("sqlEditor.result.summary.empty", "Linhas: -    Tempo: -");

            string rows = result.RowsAffected?.ToString() ?? "-";
            long ms = (long)Math.Round(result.ExecutionTime.TotalMilliseconds);
            return string.Format(
                L("sqlEditor.result.summary", "Linhas: {0}    Tempo: {1} ms"),
                rows,
                ms);
        }
    }
    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (!Set(ref _isExecuting, value))
                return;

            RaisePropertyChanged(nameof(CanExecuteOrCancel));
            RaisePropertyChanged(nameof(ExecuteOrCancelButtonText));
            RaisePropertyChanged(nameof(ExecuteOrCancelTooltipText));
        }
    }

    public bool IsCancellationPending
    {
        get => _isCancellationPending;
        private set
        {
            if (!Set(ref _isCancellationPending, value))
                return;

            RaisePropertyChanged(nameof(CanExecuteOrCancel));
            RaisePropertyChanged(nameof(ExecuteOrCancelButtonText));
            RaisePropertyChanged(nameof(ExecuteOrCancelTooltipText));
        }
    }

    public bool CanExecuteOrCancel => !IsExecuting || !IsCancellationPending;
    public string ExecuteOrCancelButtonText =>
        !IsExecuting
            ? L("sqlEditor.execute.run", "Executar")
            : IsCancellationPending
                ? L("sqlEditor.execute.stopping", "Parando...")
                : L("sqlEditor.execute.stop", "Parar");
    public string ExecuteOrCancelTooltipText =>
        !IsExecuting
            ? L("sqlEditor.execute.run.tooltip", "Executar selecao ou statement atual (F8)")
            : IsCancellationPending
                ? L("sqlEditor.execute.stopping.tooltip", "Parando execucao... (Esc)")
                : L("sqlEditor.execute.stop.tooltip", "Parar execucao (Esc)");
    public string CursorPositionTooltipText => L("sqlEditor.gotoLine.tooltip", "Ir para linha (Ctrl+G)");
    public int ActiveExecutionStatementStartLine
    {
        get => _activeExecutionStatementStartLine;
        private set
        {
            if (Set(ref _activeExecutionStatementStartLine, value))
                RaisePropertyChanged(nameof(HasActiveExecutionStatementRange));
        }
    }
    public int ActiveExecutionStatementEndLine
    {
        get => _activeExecutionStatementEndLine;
        private set
        {
            if (Set(ref _activeExecutionStatementEndLine, value))
                RaisePropertyChanged(nameof(HasActiveExecutionStatementRange));
        }
    }
    public bool HasActiveExecutionStatementRange =>
        ActiveExecutionStatementStartLine > 0
        && ActiveExecutionStatementEndLine >= ActiveExecutionStatementStartLine;

    public bool IsExplainRunning
    {
        get => _isExplainRunning;
        private set => Set(ref _isExplainRunning, value);
    }
    public string ExplainSummaryText
    {
        get => _explainSummaryText;
        private set => Set(ref _explainSummaryText, value ?? string.Empty);
    }
    public string ExplainRawOutput
    {
        get => _explainRawOutput;
        private set
        {
            if (!Set(ref _explainRawOutput, value ?? string.Empty))
                return;

            RaisePropertyChanged(nameof(HasExplainRawOutput));
        }
    }
    public bool HasExplainRawOutput => !string.IsNullOrWhiteSpace(ExplainRawOutput);

    public bool IsBenchmarkRunning
    {
        get => _isBenchmarkRunning;
        private set => Set(ref _isBenchmarkRunning, value);
    }
    public string BenchmarkProgressText
    {
        get => _benchmarkProgressText;
        private set => Set(ref _benchmarkProgressText, value ?? string.Empty);
    }
    public string BenchmarkSummaryText
    {
        get => _benchmarkSummaryText;
        private set
        {
            if (!Set(ref _benchmarkSummaryText, value ?? string.Empty))
                return;

            RaisePropertyChanged(nameof(HasBenchmarkResult));
        }
    }
    public bool HasBenchmarkResult => !string.IsNullOrWhiteSpace(BenchmarkSummaryText);

    public bool HasActiveConnection => ResolveConnectionConfigForActiveTab() is not null;
    public bool IsProductionConnectionContext
    {
        get
        {
            if (!HasActiveConnection)
                return false;

            string source = (ActiveTabConnectionProfile?.DisplayName ?? ActiveConnectionDisplayName).ToLowerInvariant();
            return source.Contains("prod", StringComparison.Ordinal)
                || source.Contains("production", StringComparison.Ordinal);
        }
    }

    public bool IsStagingConnectionContext
    {
        get
        {
            if (!HasActiveConnection || IsProductionConnectionContext)
                return false;

            string source = (ActiveTabConnectionProfile?.DisplayName ?? ActiveConnectionDisplayName).ToLowerInvariant();
            return source.Contains("stag", StringComparison.Ordinal)
                || source.Contains("staging", StringComparison.Ordinal);
        }
    }

    public bool IsNeutralConnectionContext =>
        HasActiveConnection
        && !IsProductionConnectionContext
        && !IsStagingConnectionContext;

    public bool HasNoActiveConnection => !HasActiveConnection;

    public string ActiveConnectionContextBadgeText
    {
        get
        {
            ConnectionConfig? config = ResolveConnectionConfigForActiveTab();
            if (config is null)
                return L("sqlEditor.connection.none", "Sem conexao ativa.");

            string? connectionName = ActiveTabConnectionProfile?.DisplayName;
            if (!string.IsNullOrWhiteSpace(connectionName))
                return connectionName;

            ConnectionManagerViewModel? manager = SharedConnectionManager;
            if (manager is not null && !string.IsNullOrWhiteSpace(manager.ActiveProfileId))
                return manager.SidebarConnectionName;

            return config.Database;
        }
    }

    public int CursorLine
    {
        get => _cursorLine;
        private set
        {
            if (value < 1)
                value = 1;

            Set(ref _cursorLine, value);
        }
    }

    public int CursorColumn
    {
        get => _cursorColumn;
        private set
        {
            if (value < 1)
                value = 1;

            Set(ref _cursorColumn, value);
        }
    }

    public string CursorPositionText => $"Ln {CursorLine}, Col {CursorColumn}";
    public string IndentationStatusText => L("sqlEditor.status.indentation", "Espacos: 2");
    public string SignatureHelpText
    {
        get => _signatureHelpText;
        private set
        {
            if (Set(ref _signatureHelpText, value))
                RaisePropertyChanged(nameof(HasSignatureHelp));
        }
    }
    public bool HasSignatureHelp => !string.IsNullOrWhiteSpace(SignatureHelpText);
    public string HoverDocumentationText
    {
        get => _hoverDocumentationText;
        private set
        {
            if (Set(ref _hoverDocumentationText, value))
                RaisePropertyChanged(nameof(HasHoverDocumentation));
        }
    }
    public bool HasHoverDocumentation => !string.IsNullOrWhiteSpace(HoverDocumentationText);
    public string ActiveProviderStatusText
    {
        get
        {
            ConnectionConfig? config = ResolveConnectionConfigForActiveTab();
            if (config is null)
                return GetProviderDisplayName(ActiveTab.Provider);

            string providerName = GetProviderDisplayName(config.Provider);
            string? serverVersion = _metadataResolver()?.ServerVersion;
            if (string.IsNullOrWhiteSpace(serverVersion))
                return providerName;

            string normalizedVersion = serverVersion.Trim();
            return $"{providerName} {normalizedVersion}";
        }
    }

    public bool HasExecutionError
    {
        get => _hasExecutionError;
        private set => Set(ref _hasExecutionError, value);
    }

    public string ExecutionStatusText
    {
        get => _executionStatusText;
        private set => Set(ref _executionStatusText, value);
    }

    public string? ExecutionDetailText
    {
        get => _executionDetailText;
        private set => Set(ref _executionDetailText, value);
    }

    public void ReceiveFromCanvas(string sql, DatabaseProvider provider)
    {
        Tabs.ReceiveFromCanvas(sql, provider);
        PersistSessionDraftsSnapshot();
        RaiseTabStateChanged();
        RaiseSqlPanelPropertiesChanged();
    }

    public string? GetSqlForExecution(int selectionStart, int selectionLength, int caretOffset)
    {
        return _selectionExtractor.ExtractSelectionOrCurrentStatement(
            ActiveTab.SqlText,
            selectionStart,
            selectionLength,
            caretOffset);
    }

    public SqlCompletionRequest GetCompletionRequest(string fullText, int caretOffset)
    {
        DbMetadata? metadata = _metadataResolver();
        return _completionController.GetCompletionRequest(
            fullText,
            caretOffset,
            metadata,
            ActiveTabProvider,
            ActiveTabConnectionProfileId);
    }

    public Task<SqlCompletionStageSnapshot> RequestCompletionAsync(
        string fullText,
        int caretOffset,
        IProgress<SqlCompletionStageSnapshot>? progress = null,
        CancellationToken cancellationToken = default)
    {
        DbMetadata? metadata = _metadataResolver();
        return _completionController.RequestCompletionAsync(
            fullText,
            caretOffset,
            metadata,
            ActiveTabProvider,
            ActiveTabConnectionProfileId,
            progress,
            cancellationToken);
    }

    public bool IsHeavyCompletionMetadataContext()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastCompletionMetadataSamplingAt <= CompletionMetadataSamplingInterval)
            return _isHeavyCompletionMetadataContext;

        DbMetadata? metadata = _metadataResolver();
        int tableCount = 0;
        if (metadata is not null)
        {
            foreach (TableMetadata _ in metadata.AllTables)
            {
                tableCount++;
                if (tableCount >= HeavyCompletionMetadataTableThreshold)
                    break;
            }
        }

        _isHeavyCompletionMetadataContext = tableCount >= HeavyCompletionMetadataTableThreshold;
        _lastCompletionMetadataSamplingAt = now;
        return _isHeavyCompletionMetadataContext;
    }

    public void RecordCompletionSuggestionAccepted(string? suggestionLabel)
    {
        if (string.IsNullOrWhiteSpace(suggestionLabel))
            return;

        _completionController.RecordSuggestionAccepted(suggestionLabel, ActiveTabConnectionProfileId);
    }

    public void RecordCompletionLatency(TimeSpan latency)
    {
        long durationMs = Math.Max(0, (long)Math.Round(latency.TotalMilliseconds));
        ActiveTab.CompletionTelemetry = ActiveTab.CompletionTelemetryTracker.AddSample(durationMs);
        RaiseSqlPanelPropertiesChanged();
    }

    public void RecordCompletionBreakdown(SqlCompletionTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        long totalMs = Math.Max(0, telemetry.TotalMs);
        long engineMs = Math.Max(0, telemetry.WorkerExecutionMs > 0 ? telemetry.WorkerExecutionMs : telemetry.TotalMs);
        long dispatchMs = Math.Max(0, telemetry.WorkerDispatchDelayMs);

        ActiveTab.CompletionTelemetry = ActiveTab.CompletionTelemetryTracker.AddSample(totalMs);
        ActiveTab.CompletionTelemetry = ActiveTab.CompletionTelemetryTracker.AddEngineSample(engineMs);
        ActiveTab.CompletionTelemetry = ActiveTab.CompletionTelemetryTracker.AddDispatchSample(dispatchMs);
        RaiseSqlPanelPropertiesChanged();
    }

    public void RecordCompletionUiApplyLatency(TimeSpan latency)
    {
        long durationMs = Math.Max(0, (long)Math.Round(latency.TotalMilliseconds));
        ActiveTab.CompletionTelemetry = ActiveTab.CompletionTelemetryTracker.AddUiApplySample(durationMs);
        RaiseSqlPanelPropertiesChanged();
    }

    public int GetRecommendedCompletionDebounceMs(bool isHeavyMetadataContext)
    {
        SqlEditorCompletionTelemetry telemetry = CompletionTelemetry;
        int debounceMs = DefaultCompletionDebounceMs;

        if (telemetry.SampleCount >= CompletionDebounceCalibrationSamples)
        {
            long p95 = telemetry.P95DurationMs;
            long budget = Math.Max(1, telemetry.BudgetMs);
            long effectiveP95 = Math.Min(p95, budget * 2);

            if (effectiveP95 <= (long)Math.Round(budget * 0.80))
                debounceMs = 70;
            else if (effectiveP95 <= budget)
                debounceMs = 80;
            else if (effectiveP95 <= (long)Math.Round(budget * 1.30))
                debounceMs = 90;
            else if (effectiveP95 <= (long)Math.Round(budget * 1.60))
                debounceMs = 100;
            else
                debounceMs = 110;
        }

        if (isHeavyMetadataContext)
            debounceMs += 15;

        return Math.Clamp(debounceMs, MinCompletionDebounceMs, MaxCompletionDebounceMs);
    }

    public async Task<bool> SaveActiveTabAsync(string? filePath = null, CancellationToken ct = default)
    {
        string? resolvedPath = filePath ?? ActiveTab.FilePath;
        SqlEditorFileSaveOutcome outcome = await _fileService.SaveAsync(ActiveTab.SqlText, resolvedPath, ct);
        ExecutionStatusText = outcome.StatusText;
        ExecutionDetailText = outcome.DetailText;
        HasExecutionError = outcome.HasError;

        if (!outcome.Success || string.IsNullOrWhiteSpace(resolvedPath))
            return false;

        ActiveTab.FilePath = resolvedPath;
        ActiveTab.FallbackTitle = Path.GetFileName(resolvedPath);
        ActiveTab.IsDirty = false;
        PersistSessionDraftsSnapshot();
        RaiseTabStateChanged();
        return true;
    }

    public async Task<bool> OpenSqlFileAsync(string filePath, CancellationToken ct = default)
    {
        SqlEditorFileOpenOutcome outcome = await _fileService.OpenAsync(filePath, ct);
        ExecutionStatusText = outcome.StatusText;
        ExecutionDetailText = outcome.DetailText;
        HasExecutionError = outcome.HasError;

        if (!outcome.Success || outcome.Content is null)
            return false;

        SqlEditorTabState active = ActiveTab;
        SqlEditorTabState target = string.IsNullOrWhiteSpace(active.SqlText) && !active.IsDirty
            ? active
            : Tabs.AddNewTab(active.Provider, active.ConnectionProfileId);

        target.SqlText = outcome.Content;
        target.FilePath = filePath;
        target.FallbackTitle = Path.GetFileName(filePath);
        target.IsDirty = false;
        PersistSessionDraftsSnapshot();
        RaiseTabStateChanged();
        return true;
    }

    public async Task<SqlEditorResultSet> ExecuteSelectionOrCurrentAsync(
        int selectionStart,
        int selectionLength,
        int caretOffset,
        int maxRows = 1000)
    {
        _executionCts?.Cancel();
        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        IsExecuting = true;
        IsCancellationPending = false;
        NotifyCommands();
        HasExecutionError = false;
        ExecutionStatusText = L("sqlEditor.status.executing", "Executando SQL...");
        ExecutionDetailText = null;
        BeginExecutionProgress(totalStatements: 1);

        try
        {
            string? sql = GetSqlForExecution(selectionStart, selectionLength, caretOffset);
            UpdateActiveExecutionStatementRange(ResolveStatementLineRangeForCaret(caretOffset));
            SqlEditorResultSet result = await ExecuteSqlAsync(sql, maxRows, enforceMutationGuard: true);

            if (IsExecutionDeferredResult(result))
                return result;

            StoreResult(result);
            ShowResultInPreferredSurface(result);
            UpdateExecutionTelemetry([result]);
            UpdateExecutionFeedback(result);
            RaiseSqlPanelPropertiesChanged();
            return result;
        }
        finally
        {
            EndExecutionProgress();
            IsExecuting = false;
            NotifyCommands();
        }
    }

    public void UpdateCursorPosition(int line, int column)
    {
        bool changed = false;
        if (line > 0 && line != CursorLine)
        {
            CursorLine = line;
            changed = true;
        }

        if (column > 0 && column != CursorColumn)
        {
            CursorColumn = column;
            changed = true;
        }

        if (changed)
            RaisePropertyChanged(nameof(CursorPositionText));
    }

    public void UpdateSignatureHelp(string fullText, int caretOffset)
    {
        SignatureHelpInfo? help = _completionController.TryResolveSignatureHelp(fullText, caretOffset, ActiveTabProvider);
        SignatureHelpText = help?.DisplayText ?? string.Empty;
    }

    public void UpdateHoverDocumentation(string fullText, int caretOffset)
    {
        DbMetadata? metadata = _metadataResolver();
        HoverDocumentationInfo? help = _completionController.TryResolveHoverDocumentation(
            fullText,
            caretOffset,
            metadata,
            ActiveTabProvider);
        HoverDocumentationText = help?.DisplayText ?? string.Empty;
    }

    public void ClearHoverDocumentation()
    {
        HoverDocumentationText = string.Empty;
    }

    public async Task<IReadOnlyList<SqlEditorResultSet>> ExecuteAllAsync(int maxRows = 1000)
    {
        _executionCts?.Cancel();
        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        IsExecuting = true;
        IsCancellationPending = false;
        NotifyCommands();
        HasExecutionError = false;
        ExecutionStatusText = L("sqlEditor.status.executingScript", "Executando script SQL...");
        ExecutionDetailText = null;

        var results = new List<SqlEditorResultSet>();
        try
        {
            IReadOnlyList<SqlStatement> statements = _statementSplitter.Split(ActiveTab.SqlText);
            BeginExecutionProgress(totalStatements: Math.Max(1, statements.Count));
            if (statements.Count == 0)
            {
                SqlEditorResultSet empty = await ExecuteSqlAsync(null, maxRows, enforceMutationGuard: true);
                StoreResult(empty);
                ShowResultInPreferredSurface(empty);
                UpdateExecutionTelemetry([empty]);
                UpdateExecutionFeedback(empty);
                RaiseSqlPanelPropertiesChanged();
                results.Add(empty);
                return results;
            }

            for (int i = 0; i < statements.Count; i++)
            {
                SqlStatement statement = statements[i];
                UpdateActiveExecutionStatementRange(statement.StartLine, statement.EndLine);
                UpdateExecutionProgressStatement(i + 1, statements.Count);
                ExecutionStatusText = string.Format(
                    L("sqlEditor.status.executingStep", "Executando {0}/{1}..."),
                    i + 1,
                    statements.Count);
                SqlEditorResultSet result = await ExecuteSqlAsync(statement.Sql, maxRows, enforceMutationGuard: true);
                results.Add(result);

                if (IsExecutionDeferredResult(result))
                    break;

                StoreResult(result);
                ShowResultInPreferredSurface(result);
                UpdateExecutionFeedback(result);
                RaiseSqlPanelPropertiesChanged();

                if (!result.Success)
                    break;
            }

            UpdateExecutionTelemetry(results);

            return results;
        }
        finally
        {
            EndExecutionProgress();
            IsExecuting = false;
            NotifyCommands();
        }
    }

    public void CancelExecution()
    {
        _executionCts?.Cancel();
        if (IsExecuting)
        {
            IsCancellationPending = true;
            ExecutionStatusText = L("sqlEditor.status.canceling", "Cancelando execucao...");
        }
    }

    public ConnectionConfig? GetActiveConnectionConfigForTools()
    {
        return ResolveConnectionConfigForActiveTab();
    }

    public Task RunExplainAsync(bool includeAnalyze = false)
    {
        return RunExplainForSqlAsync(ActiveTab.SqlText, includeAnalyze);
    }

    public async Task RunExplainForSqlAsync(string? sql, bool includeAnalyze = false)
    {
        string statement = sql?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(statement))
        {
            ExplainSummaryText = L("sqlEditor.explain.empty", "Nada para explicar. Escreva um SQL primeiro.");
            ExplainRawOutput = string.Empty;
            return;
        }

        _explainCts?.Cancel();
        _explainCts?.Dispose();
        _explainCts = new CancellationTokenSource();
        IsExplainRunning = true;
        NotifyCommands();
        ExplainSummaryText = L("sqlEditor.explain.running", "Executando explain...");

        try
        {
            ExplainResult result = await _executionController.RunExplainAsync(
                statement,
                ActiveTabProvider,
                ResolveConnectionConfigForActiveTab(),
                includeAnalyze,
                _explainCts.Token);

            string planning = result.PlanningTimeMs.HasValue
                ? $"{result.PlanningTimeMs.Value:0.###} ms"
                : "-";
            string execution = result.ExecutionTimeMs.HasValue
                ? $"{result.ExecutionTimeMs.Value:0.###} ms"
                : "-";
            ExplainSummaryText = string.Format(
                L("sqlEditor.explain.summary", "Explain: {0} etapas  Planning: {1}  Execution: {2}"),
                result.Nodes.Count,
                planning,
                execution);
            ExplainRawOutput = result.RawOutput;
            AppendOutputMessage(
                source: "Explain",
                title: ExplainSummaryText,
                detail: result.RawOutput,
                isError: false,
                relatedSql: statement,
                focusMessagesPane: false);
        }
        catch (OperationCanceledException)
        {
            ExplainSummaryText = L("sqlEditor.explain.canceled", "Explain cancelado.");
            AppendOutputMessage("Explain", ExplainSummaryText, null, false, statement, false);
        }
        catch (Exception ex)
        {
            ExplainSummaryText = string.Format(
                L("sqlEditor.explain.failed", "Falha ao executar explain: {0}"),
                ex.Message);
            ExplainRawOutput = string.Empty;
            AppendOutputMessage("Explain", ExplainSummaryText, ex.Message, true, statement, true);
        }
        finally
        {
            IsExplainRunning = false;
            NotifyCommands();
        }
    }

    public Task RunBenchmarkAsync(int iterations = 8, int warmupIterations = 2, int intervalMs = 0)
    {
        return RunBenchmarkForSqlAsync(ActiveTab.SqlText, iterations, warmupIterations, intervalMs);
    }

    public async Task RunBenchmarkForSqlAsync(
        string? sql,
        int iterations = 8,
        int warmupIterations = 2,
        int intervalMs = 0)
    {
        string statement = sql?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(statement))
        {
            BenchmarkSummaryText = L("sqlEditor.benchmark.empty", "Nada para medir. Escreva um SQL primeiro.");
            return;
        }

        _benchmarkCts?.Cancel();
        _benchmarkCts?.Dispose();
        _benchmarkCts = new CancellationTokenSource();
        IsBenchmarkRunning = true;
        NotifyCommands();
        BenchmarkProgressText = L("sqlEditor.benchmark.running", "Executando benchmark...");
        BenchmarkSummaryText = string.Empty;

        try
        {
            BenchmarkRunResult result = await _executionController.RunBenchmarkAsync(
                statement,
                ResolveConnectionConfigForActiveTab,
                iterations,
                warmupIterations,
                intervalMs,
                onProgress: progress =>
                {
                    BenchmarkProgressText = $"{progress.Stage} {progress.Completed}/{progress.Total}";
                },
                cancellationToken: _benchmarkCts.Token);

            _latestBenchmarkResult = result;
            BenchmarkSummaryText = result.Summary;
            BenchmarkProgressText = L("sqlEditor.benchmark.finished", "Benchmark concluido.");
            AppendOutputMessage(
                source: "Benchmark",
                title: BenchmarkProgressText,
                detail: BenchmarkSummaryText,
                isError: false,
                relatedSql: statement,
                focusMessagesPane: false);
        }
        catch (OperationCanceledException)
        {
            BenchmarkProgressText = L("sqlEditor.benchmark.canceled", "Benchmark cancelado.");
            AppendOutputMessage("Benchmark", BenchmarkProgressText, null, false, statement, false);
        }
        catch (Exception ex)
        {
            BenchmarkSummaryText = string.Format(
                L("sqlEditor.benchmark.failed", "Falha no benchmark: {0}"),
                ex.Message);
            AppendOutputMessage("Benchmark", BenchmarkSummaryText, ex.Message, true, statement, true);
        }
        finally
        {
            IsBenchmarkRunning = false;
            NotifyCommands();
        }
    }

    public void CancelBenchmark()
    {
        _benchmarkCts?.Cancel();
        NotifyCommands();
    }

    public async Task<SqlEditorResultSet?> ExecuteHistoryEntryAsync(SqlEditorHistoryEntry? entry, int maxRows = 1000)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Sql))
            return null;

        ActiveTab.SqlText = entry.Sql;
        ActiveTab.IsDirty = true;
        NotifyActiveTabEdited();
        RaiseTabStateChanged();

        _executionCts?.Cancel();
        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        IsExecuting = true;
        IsCancellationPending = false;
        NotifyCommands();
        HasExecutionError = false;
        ExecutionStatusText = L("sqlEditor.status.executingHistory", "Executando instrucao do historico...");
        ExecutionDetailText = null;
        BeginExecutionProgress(totalStatements: 1);

        try
        {
            ResetActiveExecutionStatementRange();
            SqlEditorResultSet result = await ExecuteSqlAsync(entry.Sql, maxRows, enforceMutationGuard: true);

            if (IsExecutionDeferredResult(result))
                return result;

            StoreResult(result);
            ShowResultInPreferredSurface(result);
            UpdateExecutionTelemetry([result]);
            UpdateExecutionFeedback(result);
            RaiseSqlPanelPropertiesChanged();
            return result;
        }
        finally
        {
            EndExecutionProgress();
            IsExecuting = false;
            NotifyCommands();
        }
    }

    public void UseHistoryEntryInEditor(SqlEditorHistoryEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Sql))
            return;

        ActiveTab.SqlText = entry.Sql;
        ActiveTab.IsDirty = true;
        NotifyActiveTabEdited();
        ExecutionStatusText = L("sqlEditor.status.historyLoaded", "Instrucao do historico carregada no editor.");
        ExecutionDetailText = null;
        HasExecutionError = false;
        RaiseTabStateChanged();
    }

    public void AppendTextToEditor(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        string suffix = ActiveTab.SqlText.EndsWith('\n') || string.IsNullOrWhiteSpace(ActiveTab.SqlText)
            ? string.Empty
            : Environment.NewLine;
        ActiveTab.SqlText = $"{ActiveTab.SqlText}{suffix}{text}";
        ActiveTab.IsDirty = true;
        NotifyActiveTabEdited();
        RaiseTabStateChanged();
    }

    public bool RequestClearExecutionHistory()
    {
        if (!HasExecutionHistory)
            return false;

        if (!HasPendingHistoryClearConfirmation)
        {
            _isHistoryClearConfirmationPending = true;
            RaisePropertyChanged(nameof(HasPendingHistoryClearConfirmation));
            RaisePropertyChanged(nameof(ClearHistoryButtonText));
            PublishStatus(
                L("sqlEditor.status.historyClearConfirm", "Clique em limpar novamente para confirmar a remocao do historico."),
                null,
                false);
            return false;
        }

        ActiveTab.ExecutionHistory = [];
        ActiveTab.IsExecutionHistoryHydratedFromSettings = true;
        ActiveTab.SelectedExecutionHistoryEntry = null;
        _isHistoryClearConfirmationPending = false;
        HistorySearchText = string.Empty;
        ClearExecutionHistoryForActiveProfileAsync();
        PublishStatus(
            L("sqlEditor.status.historyCleared", "Historico de execucao limpo."),
            null,
            false);
        RaiseSqlPanelPropertiesChanged();
        return true;
    }

    public async Task<SqlEditorResultSet?> ConfirmPendingMutationAsync(int maxRows = 1000)
    {
        if (!HasPendingMutationConfirmation || string.IsNullOrWhiteSpace(_pendingMutationSql))
            return null;

        _executionCts?.Cancel();
        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        IsExecuting = true;
        IsCancellationPending = false;
        NotifyCommands();
        HasExecutionError = false;
        ExecutionStatusText = L("sqlEditor.status.executingConfirmedMutation", "Executando mutacao confirmada...");
        ExecutionDetailText = null;
        BeginExecutionProgress(totalStatements: 1);

        try
        {
            string sql = _pendingMutationSql;
            ClearPendingMutation();
            SqlEditorResultSet result = await ExecuteSqlAsync(sql, maxRows, enforceMutationGuard: false);

            StoreResult(result);
            ShowResultInPreferredSurface(result);
            UpdateExecutionTelemetry([result]);
            UpdateExecutionFeedback(result);
            RaiseSqlPanelPropertiesChanged();
            return result;
        }
        finally
        {
            EndExecutionProgress();
            IsExecuting = false;
            NotifyCommands();
        }
    }

    public void CancelPendingMutation()
    {
        if (!HasPendingMutationConfirmation)
            return;

        ClearPendingMutation();
        ExecutionStatusText = L("sqlEditor.status.mutationCanceled", "Execucao da mutacao cancelada.");
        ExecutionDetailText = L("sqlEditor.detail.statementNotExecuted", "A instrucao nao foi executada.");
        HasExecutionError = false;
    }

    public async Task<SqlEditorResultSet?> ConfirmPendingQueryParametersAsync()
    {
        if (!HasPendingQueryParameterPrompt || string.IsNullOrWhiteSpace(_pendingQueryParameterSql))
            return null;

        List<string> missing = _pendingQueryParameters
            .Where(item => string.IsNullOrWhiteSpace(item.InputValue))
            .Select(item => item.Name)
            .ToList();
        if (missing.Count > 0)
        {
            ExecutionStatusText = L("sqlEditor.status.parameterMissing", "Parametro obrigatorio ausente.");
            ExecutionDetailText = string.Format(
                L("sqlEditor.detail.parameterMissing", "Informe valores para: {0}."),
                string.Join(", ", missing));
            HasExecutionError = true;
            return null;
        }

        foreach (SqlQueryParameterPromptItemViewModel item in _pendingQueryParameters)
            ActiveTab.QueryParameterLastValues[item.Name] = item.InputValue.Trim();

        string sqlTemplate = _pendingQueryParameterSql;
        int maxRows = _pendingQueryParameterMaxRows;
        bool enforceMutationGuard = _pendingQueryParameterEnforceMutationGuard;
        ClearPendingQueryParameterPrompt();

        _executionCts?.Cancel();
        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        IsExecuting = true;
        IsCancellationPending = false;
        NotifyCommands();
        HasExecutionError = false;
        ExecutionStatusText = L("sqlEditor.status.executingWithParameters", "Executando SQL com parametros...");
        ExecutionDetailText = null;
        BeginExecutionProgress(totalStatements: 1);

        try
        {
            ResetActiveExecutionStatementRange();
            SqlEditorResultSet result = await ExecuteSqlAsync(sqlTemplate, maxRows, enforceMutationGuard);
            if (IsExecutionDeferredResult(result))
                return result;

            StoreResult(result);
            ShowResultInPreferredSurface(result);
            UpdateExecutionTelemetry([result]);
            UpdateExecutionFeedback(result);
            RaiseSqlPanelPropertiesChanged();
            return result;
        }
        finally
        {
            EndExecutionProgress();
            IsExecuting = false;
            NotifyCommands();
        }
    }

    public void CancelPendingQueryParameters()
    {
        if (!HasPendingQueryParameterPrompt)
            return;

        ClearPendingQueryParameterPrompt();
        ExecutionStatusText = L("sqlEditor.status.parameterPromptCanceled", "Execucao cancelada: parametros nao informados.");
        ExecutionDetailText = L("sqlEditor.detail.statementNotExecuted", "A instrucao nao foi executada.");
        HasExecutionError = false;
    }

    private void UpdateExecutionFeedback(SqlEditorResultSet result)
    {
        SqlEditorExecutionFeedback feedback = _executionFeedbackService.Build(result);
        ExecutionStatusText = feedback.StatusText;
        ExecutionDetailText = feedback.DetailText;
        HasExecutionError = feedback.HasError;
    }

    private async Task<SqlEditorResultSet> ExecuteSqlAsync(string? sql, int maxRows, bool enforceMutationGuard)
    {
        if (!TryResolveSqlParametersForExecution(sql, maxRows, enforceMutationGuard, out string resolvedSql, out SqlEditorResultSet? parameterResult))
            return parameterResult!;

        ConnectionConfig? config = ResolveConnectionConfigForActiveTab();
        int effectiveMaxRows = ResolveEffectiveMaxRows(resolvedSql, maxRows);
        bool shouldEnforceMutationGuard = enforceMutationGuard && ProtectMutationWithoutWhereEnabled;
        SqlEditorMutationExecutionOutcome outcome = await _mutationExecutionOrchestrator.ExecuteAsync(
            resolvedSql,
            config,
            effectiveMaxRows,
            shouldEnforceMutationGuard,
            BuildEstimateCacheKey(resolvedSql),
            _executionCts?.Token ?? CancellationToken.None);

        if (!outcome.RequiresConfirmation || outcome.ConfirmationState is null)
            return outcome.Result;

        PendingMutationGuard = outcome.ConfirmationState.Guard;
        _pendingMutationSql = outcome.ConfirmationState.StatementSql;
        _pendingMutationEstimatedRows = outcome.ConfirmationState.EstimatedRows;
        PendingMutationDiff = outcome.ConfirmationState.DiffPreview;
        ExecutionStatusText = L("sqlEditor.status.confirmationRequired", "Confirmacao necessaria antes da execucao.");
        ExecutionDetailText = outcome.ConfirmationState.Guard.Issues.FirstOrDefault()?.Message;
        HasExecutionError = false;
        RaiseSqlPanelPropertiesChanged();
        return outcome.Result;
    }

    private void ClearPendingMutation()
    {
        PendingMutationGuard = null;
        PendingMutationDiff = null;
        _pendingMutationSql = null;
        _pendingMutationEstimatedRows = null;
    }

    private bool TryResolveSqlParametersForExecution(
        string? sql,
        int maxRows,
        bool enforceMutationGuard,
        out string resolvedSql,
        out SqlEditorResultSet? parameterResult)
    {
        resolvedSql = sql ?? string.Empty;
        parameterResult = null;

        IReadOnlyList<string> names = _queryParameterProcessor.DetectNames(sql);
        if (names.Count == 0)
        {
            ClearPendingQueryParameterPrompt();
            return true;
        }

        if (!_queryParameterProcessor.TryApply(
            sql,
            ActiveTab.QueryParameterLastValues,
            ActiveTabProvider,
            out string materializedSql,
            out IReadOnlyList<string> missingParameters,
            out string? conversionError))
        {
            if (missingParameters.Count > 0)
            {
                PreparePendingQueryParameterPrompt(sql!, names, maxRows, enforceMutationGuard);
                parameterResult = new SqlEditorResultSet
                {
                    StatementSql = sql?.Trim() ?? string.Empty,
                    Success = false,
                    ErrorMessage = QueryParameterPromptRequiredError(),
                    ErrorCategory = SqlExecutionErrorCategory.Validation,
                    ExecutedAt = DateTimeOffset.UtcNow,
                };
                ExecutionStatusText = L("sqlEditor.status.parametersRequired", "Parametros obrigatorios para executar a consulta.");
                ExecutionDetailText = string.Format(
                    L("sqlEditor.detail.parametersMissing", "Informe valores para: {0}."),
                    string.Join(", ", missingParameters));
                HasExecutionError = false;
                RaiseSqlPanelPropertiesChanged();
                return false;
            }

            parameterResult = new SqlEditorResultSet
            {
                StatementSql = sql?.Trim() ?? string.Empty,
                Success = false,
                ErrorMessage = conversionError ?? L("sqlEditor.error.parameterConversion", "Falha ao converter parametro."),
                ErrorCategory = SqlExecutionErrorCategory.Validation,
                ExecutedAt = DateTimeOffset.UtcNow,
            };
            ExecutionStatusText = L("sqlEditor.status.parameterError", "Falha na validacao de parametros.");
            ExecutionDetailText = conversionError ?? L("sqlEditor.detail.parameterConversion", "Revise os valores informados para os parametros.");
            HasExecutionError = true;
            return false;
        }

        ClearPendingQueryParameterPrompt();
        resolvedSql = materializedSql;
        return true;
    }

    private void PreparePendingQueryParameterPrompt(
        string sql,
        IReadOnlyList<string> parameterNames,
        int maxRows,
        bool enforceMutationGuard)
    {
        _pendingQueryParameterSql = sql;
        _pendingQueryParameterMaxRows = maxRows;
        _pendingQueryParameterEnforceMutationGuard = enforceMutationGuard;
        _pendingQueryParameters = parameterNames
            .Select(name =>
            {
                ActiveTab.QueryParameterLastValues.TryGetValue(name, out string? existingValue);
                return new SqlQueryParameterPromptItemViewModel(name, existingValue ?? string.Empty);
            })
            .ToList();
        RaisePropertyChanged(nameof(HasPendingQueryParameterPrompt));
        RaisePropertyChanged(nameof(PendingQueryParameters));
        RaisePropertyChanged(nameof(PendingQueryParameterPromptMessage));
        NotifyCommands();
    }

    private void ClearPendingQueryParameterPrompt()
    {
        _pendingQueryParameterSql = null;
        _pendingQueryParameters = [];
        RaisePropertyChanged(nameof(HasPendingQueryParameterPrompt));
        RaisePropertyChanged(nameof(PendingQueryParameters));
        RaisePropertyChanged(nameof(PendingQueryParameterPromptMessage));
        NotifyCommands();
    }

    public void SetExecutionSafetyOptions(bool top1000WithoutWhereEnabled, bool protectMutationWithoutWhereEnabled)
    {
        Top1000WithoutWhereEnabled = top1000WithoutWhereEnabled;
        ProtectMutationWithoutWhereEnabled = protectMutationWithoutWhereEnabled;
    }

    private int ResolveEffectiveMaxRows(string? sql, int requestedMaxRows)
    {
        int normalizedRequestedMaxRows = requestedMaxRows > 0 ? requestedMaxRows : 1000;
        if (!Top1000WithoutWhereEnabled && IsSelectLikeQuery(sql))
            return PreviewExecutionOptions.NoLimit;

        if (Top1000WithoutWhereEnabled && IsSelectWithoutWhere(sql))
            return Math.Min(normalizedRequestedMaxRows, 1000);

        return normalizedRequestedMaxRows;
    }

    private static bool IsSelectLikeQuery(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        string statement = sql.TrimStart();
        return Regex.IsMatch(statement, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsSelectWithoutWhere(string? sql)
    {
        if (!IsSelectLikeQuery(sql))
            return false;

        return !Regex.IsMatch(sql!, @"\bWHERE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private void NotifyMutationCommands()
    {
        _commandNotifier.NotifyMutationCommands(
            ConfirmPendingMutationCommand,
            CancelPendingMutationCommand);
    }

    private void NotifyTabCommands()
    {
        _commandNotifier.NotifyTabCommands(
            NewTabCommand,
            CloseTabCommand,
            CloseActiveTabCommand,
            ConfirmPendingCloseTabCommand,
            CancelPendingCloseTabCommand);
    }

    private void NotifyCommands()
    {
        _commandNotifier.NotifyAll(
            ConfirmPendingMutationCommand,
            CancelPendingMutationCommand,
            NewTabCommand,
            CloseTabCommand,
            CloseActiveTabCommand,
            ConfirmPendingCloseTabCommand,
            CancelPendingCloseTabCommand);

        (CloseResultsSheetCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ReopenResultsSheetCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenConnectionSwitcherCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CloseConnectionSwitcherCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplyConnectionSwitcherCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplySidebarConnectionToTabCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplySidebarConnectionToApplicationCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ExecuteHistoryEntryCommand as RelayCommand<SqlEditorHistoryEntry>)?.NotifyCanExecuteChanged();
        (ExplainCommand as RelayCommand<string>)?.NotifyCanExecuteChanged();
        (BenchmarkCommand as RelayCommand<string>)?.NotifyCanExecuteChanged();
        (CancelBenchmarkCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ConfirmPendingQueryParametersCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CancelPendingQueryParametersCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void RaiseSqlPanelPropertiesChanged()
    {
        SyncDialectFromConnection();
        _propertyChangePublisher.PublishSqlPanelChanges(RaisePropertyChanged);
    }

    public void NotifyConnectionContextChanged()
    {
        ConnectionManagerViewModel? manager = SharedConnectionManager;
        if (manager is not null)
        {
            string? managerActiveProfileId = string.IsNullOrWhiteSpace(manager.ActiveProfileId)
                ? null
                : manager.ActiveProfileId;

            if (string.IsNullOrWhiteSpace(managerActiveProfileId))
            {
                if (!string.IsNullOrWhiteSpace(ActiveTabConnectionProfileId))
                    ActiveTabConnectionProfileId = null;
            }
            else if (!string.Equals(ActiveTabConnectionProfileId, managerActiveProfileId, StringComparison.Ordinal))
            {
                bool knownProfile = AvailableConnectionProfiles.Any(profile =>
                    string.Equals(profile.Id, managerActiveProfileId, StringComparison.Ordinal));
                if (knownProfile)
                    ActiveTabConnectionProfileId = managerActiveProfileId;
            }
        }

        RaisePropertyChanged(nameof(SharedConnectionManager));
        RaisePropertyChanged(nameof(HasSharedConnectionManager));
        RaisePropertyChanged(nameof(ConnectionSwitcherDatabases));
        RaisePropertyChanged(nameof(CanChangeDatabaseInSwitcher));
        InvalidateSchemaTableCaches();
        SyncSidebarConnectionSelectionToActiveTab();
        if (IsConnectionSwitcherOpen)
        {
            ConnectionSwitcherSelectedDatabase = SharedConnectionManager?.SelectedDatabase
                ?? ResolveConnectionConfigForActiveTab()?.Database;
        }
        RaiseSqlPanelPropertiesChanged();
        RaiseSchemaPropertiesChanged();
    }

    private void RaiseSchemaPropertiesChanged()
    {
        _propertyChangePublisher.PublishSchemaChanges(RaisePropertyChanged);
    }

    private void InvalidateSchemaTableCaches()
    {
        _cachedSchemaMetadata = null;
        _cachedSchemaSelection = null;
        _schemaTablesCache = [];
        _filteredSchemaTablesCache = [];
        _filteredSchemaNeedleCache = string.Empty;
        _isSchemaTablesCacheValid = false;
        _isFilteredSchemaTablesCacheValid = false;
    }

    private void EnsureSchemaTablesCache()
    {
        DbMetadata? metadata = _metadataResolver();
        string? selectedSchema = ResolveSchemaSelection();
        if (metadata is null)
        {
            if (_isSchemaTablesCacheValid && _schemaTablesCache.Count == 0)
                return;

            InvalidateSchemaTableCaches();
            _isSchemaTablesCacheValid = true;
            return;
        }

        if (_isSchemaTablesCacheValid
            && ReferenceEquals(_cachedSchemaMetadata, metadata)
            && string.Equals(_cachedSchemaSelection, selectedSchema, StringComparison.OrdinalIgnoreCase))
        return;

        _schemaTablesCache = BuildSchemaTables(metadata, selectedSchema);
        _cachedSchemaMetadata = metadata;
        _cachedSchemaSelection = selectedSchema;
        _filteredSchemaTablesCache = [];
        _filteredSchemaNeedleCache = string.Empty;
        _isSchemaTablesCacheValid = true;
        _isFilteredSchemaTablesCacheValid = false;
    }

    private string? ResolveSchemaSelection()
    {
        string? selected = SharedConnectionManager?.SelectedSchema;
        if (string.IsNullOrWhiteSpace(selected))
            return null;

        return selected.Trim();
    }

    public bool TryBuildReportExportContext(out SqlEditorReportExportContext? context)
    {
        SqlEditorResultSet? result = CurrentResult;
        if (result is null)
        {
            context = null;
            return false;
        }

        var columns = new List<string>();
        var resultRows = new List<IReadOnlyDictionary<string, object?>>();
        if (result.Data is not null)
        {
            foreach (DataColumn column in result.Data.Columns)
                columns.Add(column.ColumnName);

            foreach (DataRow row in result.Data.Rows)
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DataColumn column in result.Data.Columns)
                    values[column.ColumnName] = NormalizeReportCellValue(row[column]);

                resultRows.Add(values);
            }
        }

        string status = result.Success ? "success" : "error";
        if (result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            status = "warning";

        long? executionMs = (long)Math.Round(result.ExecutionTime.TotalMilliseconds);
        long? rowCount = result.RowsAffected;
        if (!rowCount.HasValue && result.Data is not null)
            rowCount = result.Data.Rows.Count;

        context = new SqlEditorReportExportContext(
            Sql: result.StatementSql,
            SchemaColumns: columns,
            SchemaDetails: BuildReportSchemaDetails(columns, resultRows),
            ResultRows: resultRows,
            ExecutionResult: new SqlEditorReportExecutionResult(
                RowCount: rowCount,
                ExecutionTimeMs: executionMs,
                Status: status,
                ErrorMessage: result.ErrorMessage),
            Connection: ResolveConnectionConfigForActiveTab(),
            ActiveFilePath: ActiveTab.FilePath,
            TabTitle: ActiveTab.FallbackTitle);

        return true;
    }

    private static IReadOnlyList<SqlEditorReportSchemaDetail> BuildReportSchemaDetails(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var details = new List<SqlEditorReportSchemaDetail>(columns.Count);

        foreach (string column in columns)
        {
            long nullCount = 0;
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            string? example = null;
            string? minValue = null;
            string? maxValue = null;
            string kind = "null";

            foreach (IReadOnlyDictionary<string, object?> row in rows)
            {
                row.TryGetValue(column, out object? value);
                if (value is null)
                {
                    nullCount += 1;
                    continue;
                }

                string text = value.ToString() ?? string.Empty;
                distinct.Add(text);
                example ??= text;

                if (minValue is null || string.CompareOrdinal(text, minValue) < 0)
                    minValue = text;

                if (maxValue is null || string.CompareOrdinal(text, maxValue) > 0)
                    maxValue = text;

                string detectedKind = DetectReportValueKind(value);
                if (kind is "null" or "text")
                {
                    kind = detectedKind;
                }
                else if (!string.Equals(kind, detectedKind, StringComparison.Ordinal))
                {
                    kind = "text";
                }
            }

            details.Add(new SqlEditorReportSchemaDetail(
                Name: column,
                Kind: kind,
                NullCount: nullCount,
                DistinctCount: distinct.Count,
                Example: example,
                MinValue: minValue,
                MaxValue: maxValue));
        }

        return details;
    }

    private static object? NormalizeReportCellValue(object? value)
    {
        if (value is null || value is DBNull)
            return null;

        return value switch
        {
            DateTimeOffset dto => dto.ToString("O"),
            DateTime dt => dt.ToString("O"),
            TimeSpan ts => ts.ToString(),
            Guid guid => guid.ToString("D"),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value,
        };
    }

    private static string DetectReportValueKind(object value)
    {
        return value switch
        {
            bool => "bool",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => "number",
            DateTime or DateTimeOffset => "date",
            _ => "text",
        };
    }

    public void PublishStatus(string statusText, string? detailText = null, bool hasError = false)
    {
        ExecutionStatusText = statusText;
        ExecutionDetailText = detailText;
        HasExecutionError = hasError;
        AppendOutputMessage(
            source: L("sqlEditor.messages.source.status", "Status"),
            title: statusText,
            detail: detailText,
            isError: hasError,
            relatedSql: null,
            focusMessagesPane: hasError);
    }

    public SqlInlineEditEligibility EvaluateInlineEditEligibility(DataTable table)
    {
        SqlEditorResultSet? result = CurrentResult;
        if (result is null)
            return SqlInlineEditEligibility.NotEligible;

        DbMetadata? metadata = _metadataResolver();
        ConnectionConfig? config = ResolveConnectionConfigForActiveTab();
        return _resultEligibilityDetector.Evaluate(result.StatementSql, table, metadata, config);
    }

    public async Task<SqlEditorResultSet> ExecuteInlineUpdateAsync(string updateSql, CancellationToken ct = default)
    {
        ConnectionConfig? config = ResolveConnectionConfigForActiveTab();
        SqlEditorResultSet result = await _executionService.ExecuteAsync(updateSql, config, maxRows: 1, ct);
        UpdateExecutionFeedback(result);
        RaiseSqlPanelPropertiesChanged();
        return result;
    }

    private void StoreResult(SqlEditorResultSet result)
    {
        _isHistoryClearConfirmationPending = false;
        _resultStateService.AppendResult(ActiveTab, result);
        if (!result.Success)
        {
            AppendOutputMessage(
                source: L("sqlEditor.messages.source.execution", "Execucao"),
                title: result.ErrorMessage ?? L("sqlEditor.status.failed", "Execution failed."),
                detail: result.StatementSql,
                isError: true,
                relatedSql: result.StatementSql,
                focusMessagesPane: true);
        }
        else
        {
            string rows = result.RowsAffected?.ToString() ?? "-";
            AppendOutputMessage(
                source: L("sqlEditor.messages.source.execution", "Execucao"),
                title: string.Format(L("sqlEditor.messages.execution.ok", "Instrucao executada. Linhas: {0}"), rows),
                detail: string.Format(L("sqlEditor.messages.execution.time", "Tempo: {0} ms"), Math.Round(result.ExecutionTime.TotalMilliseconds)),
                isError: false,
                relatedSql: result.StatementSql,
                focusMessagesPane: false);
        }
        PersistExecutionHistoryForActiveProfileAsync();
        SelectedExecutionHistoryEntry = ActiveTab.ExecutionHistory.FirstOrDefault();
        RaisePropertyChanged(nameof(CanExportReport));
        RaisePropertyChanged(nameof(ShouldShowResultsSheet));
        RaisePropertyChanged(nameof(FilteredExecutionHistory));
        RaisePropertyChanged(nameof(HasFilteredExecutionHistory));
        RaisePropertyChanged(nameof(IsFilteredExecutionHistoryEmpty));
        RaisePropertyChanged(nameof(HasHistorySearchNoResults));
        RaisePropertyChanged(nameof(HistoryFilterSummaryText));
        RaisePropertyChanged(nameof(HasHiddenColumnUndo));
        RaisePropertyChanged(nameof(UndoHiddenColumnButtonText));
        RaisePropertyChanged(nameof(SelectedExecutionHistoryEntry));
        RaisePropertyChanged(nameof(HasPendingHistoryClearConfirmation));
        RaisePropertyChanged(nameof(ClearHistoryButtonText));
    }

    private void UpdateExecutionTelemetry(IReadOnlyList<SqlEditorResultSet> results)
    {
        if (results.Count == 0)
            return;

        ActiveTab.ExecutionTelemetry = _resultStateService.BuildTelemetry(results);
    }

    private void AddNewTab()
    {
        SqlEditorTabState tab = Tabs.AddNewTab();
        TryHydrateResultFilterForTab(tab, force: true);
        TryHydrateExecutionHistoryForTab(tab, force: true);
        PersistSessionDraftsSnapshot();
        RaiseTabStateChanged();
    }

    private void CloseTabById(string? tabId)
    {
        SqlEditorTabCloseOutcome outcome = _tabCloseWorkflowService.RequestClose(Tabs, tabId);
        if (!string.IsNullOrWhiteSpace(outcome.StatusText))
        {
            ExecutionStatusText = outcome.StatusText;
            ExecutionDetailText = outcome.DetailText;
            HasExecutionError = outcome.HasError;
        }

        PersistSessionDraftsSnapshot();
        RaiseTabStateChanged();
    }

    private bool CanCloseTab(string? tabId)
    {
        return _tabCloseWorkflowService.CanCloseTab(Tabs, tabId);
    }

    private void ConfirmPendingCloseTab()
    {
        SqlEditorTabCloseOutcome outcome = _tabCloseWorkflowService.ConfirmClose(Tabs);
        if (!string.IsNullOrWhiteSpace(outcome.StatusText))
        {
            ExecutionStatusText = outcome.StatusText;
            ExecutionDetailText = outcome.DetailText;
            HasExecutionError = outcome.HasError;
        }

        PersistSessionDraftsSnapshot();
        RaiseTabStateChanged();
    }

    private void CancelPendingCloseTab()
    {
        SqlEditorTabCloseOutcome outcome = _tabCloseWorkflowService.CancelClose();
        if (!string.IsNullOrWhiteSpace(outcome.StatusText))
        {
            ExecutionStatusText = outcome.StatusText;
            ExecutionDetailText = outcome.DetailText;
            HasExecutionError = outcome.HasError;
            RaiseTabStateChanged();
        }
    }

    private void RaiseTabStateChanged()
    {
        _isHistoryClearConfirmationPending = false;
        ClearPendingQueryParameterPrompt();
        SyncSidebarConnectionSelectionToActiveTab();
        TryHydrateResultFilterForTab(ActiveTab);
        TryHydrateExecutionHistoryForTab(ActiveTab);
        InvalidateSchemaTableCaches();
        EnsureHistorySelection();
        SyncTabCommands();
        SyncDialectFromConnection();
        _propertyChangePublisher.PublishTabStateChanges(RaisePropertyChanged);
        RaiseSqlPanelPropertiesChanged();
        RaiseSchemaPropertiesChanged();
        RaisePropertyChanged(nameof(CanExportReport));
        RaisePropertyChanged(nameof(ShouldShowResultsSheet));
        RaisePropertyChanged(nameof(HasHiddenColumnUndo));
        RaisePropertyChanged(nameof(UndoHiddenColumnButtonText));
        RaisePropertyChanged(nameof(SelectedExecutionHistoryEntry));
        RaisePropertyChanged(nameof(HasPendingHistoryClearConfirmation));
        RaisePropertyChanged(nameof(ClearHistoryButtonText));
        RaisePropertyChanged(nameof(HistoryFilterSummaryText));
        RaisePropertyChanged(nameof(ActiveConnectionContextBadgeText));
        RaisePropertyChanged(nameof(HasActiveConnection));
        RaisePropertyChanged(nameof(IsProductionConnectionContext));
        RaisePropertyChanged(nameof(IsStagingConnectionContext));
        RaisePropertyChanged(nameof(ActiveProviderStatusText));
        RaisePropertyChanged(nameof(ExecuteOrCancelTooltipText));
        RaisePropertyChanged(nameof(CursorPositionTooltipText));
        RaisePropertyChanged(nameof(SignatureHelpText));
        RaisePropertyChanged(nameof(HasSignatureHelp));
        RaisePropertyChanged(nameof(HoverDocumentationText));
        RaisePropertyChanged(nameof(HasHoverDocumentation));
        NotifyCommands();
    }

    public void NotifyActiveTabEdited()
    {
        ClearPendingQueryParameterPrompt();
        _hasPendingDraftAutoSave = true;
        EnsureDraftAutoSaveTimersStarted();
        _draftAutoSaveDebounceTimer?.Change(DraftAutoSaveDebounce, Timeout.InfiniteTimeSpan);
    }

    private void SyncTabCommands()
    {
        foreach (SqlEditorTabState tab in Tabs.Tabs)
            tab.CloseCommand = CloseTabCommand;
    }

    private SqlEditorResultTab? SelectedResultTab
    {
        get
        {
            int selectedIndex = ActiveTab.SelectedResultTabIndex;
            if (selectedIndex < 0 || selectedIndex >= ActiveTab.ResultTabs.Count)
                return null;

            return ActiveTab.ResultTabs[selectedIndex];
        }
    }

    private SqlEditorResultSet? CurrentResult => SelectedResultTab?.Result ?? ActiveTab.LastResult;

    private ConnectionConfig? ResolveConnectionConfigForActiveTab()
    {
        ConnectionConfig? profileConfig = _connectionConfigByProfileIdResolver(ActiveTab.ConnectionProfileId);
        return profileConfig ?? _connectionConfigResolver();
    }

    private ConnectionConfig? ResolveConnectionConfigForProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return null;

        return _connectionConfigByProfileIdResolver(profileId);
    }

    private void OpenConnectionSwitcher()
    {
        IReadOnlyList<SqlEditorConnectionProfileOption> profiles = AvailableConnectionProfiles;
        if (profiles.Count == 0)
            return;

        ConnectionManagerViewModel? manager = SharedConnectionManager;
        string? activeProfileId = ActiveTabConnectionProfileId;
        if (string.IsNullOrWhiteSpace(activeProfileId))
            activeProfileId = manager?.ActiveProfileId;

        ConnectionSwitcherSelectedProfileId = activeProfileId;

        string? selectedDatabase = manager?.SelectedDatabase;
        if (string.IsNullOrWhiteSpace(selectedDatabase))
            selectedDatabase = ResolveConnectionConfigForActiveTab()?.Database;
        ConnectionSwitcherSelectedDatabase = selectedDatabase;

        IsConnectionSwitcherOpen = true;
    }

    private void CloseConnectionSwitcher()
    {
        if (!IsConnectionSwitcherOpen)
            return;

        IsConnectionSwitcherOpen = false;
    }

    private void ApplyConnectionSwitcher()
    {
        try
        {
            ConnectionManagerViewModel? manager = SharedConnectionManager;
            string? selectedProfileId = ConnectionSwitcherSelectedProfileId;
            bool switchedConnection = false;

            if (!string.IsNullOrWhiteSpace(selectedProfileId))
            {
                ActiveTabConnectionProfileId = selectedProfileId;

                if (manager is not null)
                {
                    ConnectionProfile? target = manager.Profiles
                        .FirstOrDefault(profile => string.Equals(profile.Id, selectedProfileId, StringComparison.Ordinal));
                    if (target is not null && manager.SwitchConnectionCommand.CanExecute(target))
                    {
                        manager.SwitchConnectionCommand.Execute(target);
                        switchedConnection = true;
                    }
                }
            }

            if (manager is not null
                && !string.IsNullOrWhiteSpace(ConnectionSwitcherSelectedDatabase)
                && !string.Equals(ConnectionSwitcherSelectedDatabase, manager.SelectedDatabase, StringComparison.OrdinalIgnoreCase)
                && manager.SwitchDatabaseCommand.CanExecute(ConnectionSwitcherSelectedDatabase))
            {
                manager.SwitchDatabaseCommand.Execute(ConnectionSwitcherSelectedDatabase);
            }

            if (!switchedConnection
                && !string.IsNullOrWhiteSpace(selectedProfileId)
                && string.IsNullOrWhiteSpace(ActiveTabConnectionProfileId))
            {
                ActiveTabConnectionProfileId = selectedProfileId;
            }
        }
        finally
        {
            CloseConnectionSwitcher();
            NotifyConnectionContextChanged();
        }
    }

    private void ApplySidebarConnectionToTab()
    {
        SqlEditorConnectionProfileOption? selected = SidebarSelectedConnectionProfile;
        if (selected is null)
            return;

        ActiveTabConnectionProfileId = selected.Id;
        ConnectionManagerViewModel? manager = SharedConnectionManager;
        if (manager is not null)
        {
            ConnectionProfile? profile = manager.Profiles
                .FirstOrDefault(candidate => string.Equals(candidate.Id, selected.Id, StringComparison.Ordinal));
            if (profile is not null && manager.SwitchConnectionCommand.CanExecute(profile))
                manager.SwitchConnectionCommand.Execute(profile);
        }

        PublishStatus(
            string.Format(
                L("sqlEditor.connection.sidebar.tabApplied", "Conexao aplicada para a aba atual: {0}."),
                selected.DisplayName),
            null,
            false);
    }

    private void ApplySidebarConnectionToApplication()
    {
        SqlEditorConnectionProfileOption? selected = SidebarSelectedConnectionProfile;
        if (selected is null)
            return;

        foreach (SqlEditorTabState tab in Tabs.Tabs)
        {
            tab.ConnectionProfileId = selected.Id;
            tab.Provider = selected.Provider;
        }

        ConnectionManagerViewModel? manager = SharedConnectionManager;
        if (manager is not null)
        {
            ConnectionProfile? profile = manager.Profiles
                .FirstOrDefault(candidate => string.Equals(candidate.Id, selected.Id, StringComparison.Ordinal));
            if (profile is not null && manager.SwitchConnectionCommand.CanExecute(profile))
                manager.SwitchConnectionCommand.Execute(profile);
        }

        _sidebarSelectedConnectionProfileId = selected.Id;
        RaiseTabStateChanged();
        PublishStatus(
            string.Format(
                L("sqlEditor.connection.sidebar.applicationApplied", "Conexao aplicada para toda a aplicacao: {0}."),
                selected.DisplayName),
            null,
            false);
    }

    private void SyncSidebarConnectionSelectionToActiveTab()
    {
        string? activeProfileId = ActiveTabConnectionProfileId;
        if (string.Equals(_sidebarSelectedConnectionProfileId, activeProfileId, StringComparison.Ordinal))
            return;

        _sidebarSelectedConnectionProfileId = activeProfileId;
        RaisePropertyChanged(nameof(SidebarSelectedConnectionProfile));
        RaisePropertyChanged(nameof(HasSidebarSelectedConnectionProfile));
        (ApplySidebarConnectionToTabCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplySidebarConnectionToApplicationCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void OpenResultsSheet()
    {
        SqlEditorResultSet? result = CurrentResult;
        if (result is null)
            return;

        if (!TryRequestResultPage(result))
        {
            PublishStatus(
                L("sqlEditor.results.open.unsupported", "A nova tela de resultados suporta consultas SELECT com dados."),
                null,
                hasError: false);
            return;
        }

        IsResultsSheetOpen = false;
        NotifyCommands();
    }

    private void CloseResultsSheet()
    {
        IsResultsSheetOpen = false;
        NotifyCommands();
    }

    private void ShowResultInPreferredSurface(SqlEditorResultSet result)
    {
        _ = TryRequestResultPage(result);
        IsResultsSheetOpen = false;
    }

    private bool TryRequestResultPage(SqlEditorResultSet result)
    {
        if (SqlResultPageRequested is null)
            return false;

        if (!result.Success || result.Data is null)
            return false;

        string statement = result.StatementSql?.TrimStart() ?? string.Empty;
        if (!statement.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return false;

        ConnectionConfig? config = ResolveConnectionConfigForActiveTab();
        DbMetadata? metadata = _metadataResolver();
        SqlInlineEditEligibility inlineEditEligibility = _resultEligibilityDetector.Evaluate(
            result.StatementSql,
            result.Data,
            metadata,
            config);

        string connectionId = ActiveTabConnectionProfileId
            ?? config?.Database
            ?? "unknown-connection";

        var request = new SqlResultSessionCreateRequest(
            SqlText: result.StatementSql ?? string.Empty,
            ConnectionId: connectionId,
            DatabaseName: config?.Database,
            SchemaName: null,
            ResultSet: result,
            Provider: config?.Provider ?? ActiveTabProvider,
            InlineEditEligibility: inlineEditEligibility);

        SqlResultPageRequested.Invoke(this, new SqlResultPageRequestedEventArgs(request));
        return true;
    }

    public void ClearOutputMessages()
    {
        if (ActiveTab.OutputMessages.Count == 0)
            return;

        ActiveTab.OutputMessages = [];
        RaiseSqlPanelPropertiesChanged();
    }

    private void AppendOutputMessage(
        string source,
        string title,
        string? detail,
        bool isError,
        string? relatedSql,
        bool focusMessagesPane)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        var entry = new SqlEditorMessageEntry(
            Timestamp: DateTimeOffset.Now,
            Source: source,
            Title: title,
            Detail: detail,
            IsError: isError,
            Sql: relatedSql);

        List<SqlEditorMessageEntry> list = ActiveTab.OutputMessages.ToList();
        list.Insert(0, entry);
        if (list.Count > 500)
            list = list.Take(500).ToList();
        ActiveTab.OutputMessages = list;

        if (focusMessagesPane)
            ActiveTab.SelectedOutputPane = SqlEditorOutputPane.Messages;

        RaiseSqlPanelPropertiesChanged();
    }

    private void SyncDialectFromConnection()
    {
        ConnectionConfig? config = ResolveConnectionConfigForActiveTab();
        if (config is null)
            return;

        if (ActiveTab.Provider != config.Provider)
            ActiveTab.Provider = config.Provider;
    }

    private void BeginExecutionProgress(int totalStatements)
    {
        _executionStatusTimer?.Dispose();
        _executionStatusTimer = null;
        _executionStopwatch = Stopwatch.StartNew();
        _activeExecutionTotalStatements = Math.Max(1, totalStatements);
        _activeExecutionStatementIndex = 1;
        UpdateExecutingStatusText();
        _executionStatusTimer = new Timer(
            static state => ((SqlEditorViewModel)state!).OnExecutionStatusTimerTick(),
            this,
            dueTime: TimeSpan.FromMilliseconds(100),
            period: TimeSpan.FromMilliseconds(100));
    }

    private void UpdateExecutionProgressStatement(int currentStatementIndex, int totalStatements)
    {
        _activeExecutionTotalStatements = Math.Max(1, totalStatements);
        _activeExecutionStatementIndex = Math.Clamp(currentStatementIndex, 1, _activeExecutionTotalStatements);
        UpdateExecutingStatusText();
    }

    private void EndExecutionProgress()
    {
        _executionStatusTimer?.Dispose();
        _executionStatusTimer = null;
        _executionStopwatch?.Stop();
        _executionStopwatch = null;
        _activeExecutionTotalStatements = 0;
        _activeExecutionStatementIndex = 0;
        ResetActiveExecutionStatementRange();
        IsCancellationPending = false;
    }

    private void OnExecutionStatusTimerTick()
    {
        if (!IsExecuting)
            return;

        UpdateExecutingStatusText();
    }

    private void UpdateExecutingStatusText()
    {
        if (!IsExecuting)
            return;

        if (IsCancellationPending)
        {
            ExecutionStatusText = L("sqlEditor.status.canceling", "Cancelando execucao...");
            return;
        }

        double elapsedSeconds = Math.Max(0, _executionStopwatch?.Elapsed.TotalSeconds ?? 0);
        string elapsedText = string.Format(CultureInfo.InvariantCulture, "{0:0.0}s", elapsedSeconds);

        if (_activeExecutionTotalStatements > 1)
        {
            ExecutionStatusText = string.Format(
                L("sqlEditor.status.executingStatementTimer", "Executando statement {0} de {1} — {2}"),
                Math.Clamp(_activeExecutionStatementIndex, 1, _activeExecutionTotalStatements),
                _activeExecutionTotalStatements,
                elapsedText);
            return;
        }

        ExecutionStatusText = string.Format(
            L("sqlEditor.status.executingTimer", "Executando... {0}"),
            elapsedText);
    }

    private (int StartLine, int EndLine) ResolveStatementLineRangeForCaret(int caretOffset)
    {
        string sql = ActiveTab.SqlText ?? string.Empty;
        IReadOnlyList<SqlStatement> statements = _statementSplitter.Split(sql);
        if (statements.Count == 0)
            return (0, 0);

        int caretLine = ResolveLineFromOffset(sql, caretOffset);
        SqlStatement target = statements.FirstOrDefault(statement =>
            caretLine >= statement.StartLine && caretLine <= statement.EndLine)
            ?? statements[0];

        return (target.StartLine, target.EndLine);
    }

    private static int ResolveLineFromOffset(string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        int boundedOffset = Math.Clamp(offset, 0, text.Length);
        int line = 1;
        for (int i = 0; i < boundedOffset; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    private void UpdateActiveExecutionStatementRange((int StartLine, int EndLine) range)
    {
        UpdateActiveExecutionStatementRange(range.StartLine, range.EndLine);
    }

    private void UpdateActiveExecutionStatementRange(int startLine, int endLine)
    {
        if (startLine <= 0 || endLine <= 0 || endLine < startLine)
        {
            ResetActiveExecutionStatementRange();
            return;
        }

        ActiveExecutionStatementStartLine = startLine;
        ActiveExecutionStatementEndLine = endLine;
    }

    private void ResetActiveExecutionStatementRange()
    {
        ActiveExecutionStatementStartLine = 0;
        ActiveExecutionStatementEndLine = 0;
    }

    private static string GetProviderDisplayName(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.Postgres => "PostgreSQL",
            DatabaseProvider.MySql => "MySQL",
            DatabaseProvider.SqlServer => "SQL Server",
            DatabaseProvider.SQLite => "SQLite",
            _ => provider.ToString(),
        };
    }

    private static IReadOnlyDictionary<string, string> BuildColumnRelationshipIndex(DbMetadata metadata)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (ForeignKeyRelation fk in metadata.AllForeignKeys)
        {
            string childKey = BuildColumnRelationshipKey(fk.ChildFullTable, fk.ChildColumn);
            if (!index.ContainsKey(childKey))
                index[childKey] = $"{fk.ParentFullTable}.{fk.ParentColumn}";

            string parentKey = BuildColumnRelationshipKey(fk.ParentFullTable, fk.ParentColumn);
            if (!index.ContainsKey(parentKey))
                index[parentKey] = $"{fk.ChildFullTable}.{fk.ChildColumn}";
        }

        return index;
    }

    private static string BuildColumnRelationshipKey(string tableFullName, string columnName)
        => $"{tableFullName}|{columnName}";

    private static string ResolveRelatedTable(
        string tableFullName,
        string columnName,
        IReadOnlyDictionary<string, string> relationshipIndex)
    {
        string key = BuildColumnRelationshipKey(tableFullName, columnName);
        return relationshipIndex.TryGetValue(key, out string? related)
            ? related
            : string.Empty;
    }

    private static string BuildSchemaColumnSearchText(IReadOnlyList<SqlEditorSchemaColumnItem> columns)
    {
        if (columns.Count == 0)
            return string.Empty;

        var parts = new List<string>(columns.Count);
        foreach (SqlEditorSchemaColumnItem column in columns)
            parts.Add($"{column.Name} {column.DataType}");

        return string.Join(' ', parts);
    }

    private static IReadOnlyList<SqlEditorSchemaColumnItem> BuildSchemaColumns(
        TableMetadata table,
        IReadOnlyDictionary<string, string> relationshipIndex)
    {
        return table.Columns
            .OrderBy(column => column.OrdinalPosition)
            .Select(column => new SqlEditorSchemaColumnItem
            {
                Name = column.Name,
                DataType = column.DataType,
                IsPrimaryKey = column.IsPrimaryKey,
                IsForeignKey = column.IsForeignKey,
                IsIndexed = column.IsIndexed,
                IsUnique = column.IsUnique,
                RelatedTable = ResolveRelatedTable(table.FullName, column.Name, relationshipIndex),
                TypeIcon = ResolveTypeIcon(column),
            })
            .ToList();
    }

    private IReadOnlyList<SqlEditorSchemaTableItem> BuildSchemaTables(DbMetadata metadata, string? selectedSchema)
    {
        IReadOnlyDictionary<string, string> relationshipIndex = BuildColumnRelationshipIndex(metadata);
        IEnumerable<SchemaMetadata> schemas = metadata.Schemas;
        if (!string.IsNullOrWhiteSpace(selectedSchema))
        {
            schemas = schemas.Where(schema =>
                string.Equals(schema.Name, selectedSchema, StringComparison.OrdinalIgnoreCase));
        }

        return schemas
            .SelectMany(schema => schema.Tables.Select(table =>
            {
                IReadOnlyList<SqlEditorSchemaColumnItem> columns = BuildSchemaColumns(table, relationshipIndex);

                return new SqlEditorSchemaTableItem
                {
                Schema = schema.Name,
                Name = table.Name,
                FullName = table.FullName,
                    ColumnSearchText = BuildSchemaColumnSearchText(columns),
                    Columns = columns,
                    PrimaryKeyCount = table.Columns.Count(column => column.IsPrimaryKey),
                    ForeignKeyCount = table.Columns.Count(column => column.IsForeignKey),
                    IndexedColumnCount = table.Columns.Count(column => column.IsIndexed),
                };
            }))
            .OrderBy(table => table.Schema)
            .ThenBy(table => table.Name)
            .ToList();
    }

    public IReadOnlyList<ForeignKeyRelation> ResolveColumnRelationships(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return [];

        DbMetadata? metadata = _metadataResolver();
        if (metadata is null)
            return [];

        string? tableFullName = ResolveResultSourceTableFullName(metadata);
        if (string.IsNullOrWhiteSpace(tableFullName))
            return [];

        return metadata.AllForeignKeys
            .Where(fk =>
                (fk.ChildFullTable.Equals(tableFullName, StringComparison.OrdinalIgnoreCase)
                 && fk.ChildColumn.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                || (fk.ParentFullTable.Equals(tableFullName, StringComparison.OrdinalIgnoreCase)
                    && fk.ParentColumn.Equals(columnName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public SqlEditorSchemaColumnItem? ResolveResultSchemaColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return null;

        DbMetadata? metadata = _metadataResolver();
        if (metadata is null)
            return null;

        string? tableFullName = ResolveResultSourceTableFullName(metadata);
        if (!string.IsNullOrWhiteSpace(tableFullName))
        {
            TableMetadata? table = metadata.FindTable(tableFullName);
            ColumnMetadata? column = table?.Columns.FirstOrDefault(c =>
                c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            if (column is not null)
            {
                return new SqlEditorSchemaColumnItem
                {
                    Name = column.Name,
                    DataType = column.DataType,
                    IsPrimaryKey = column.IsPrimaryKey,
                    IsForeignKey = column.IsForeignKey,
                    IsIndexed = column.IsIndexed,
                    IsUnique = column.IsUnique,
                    RelatedTable = ResolveRelatedTable(tableFullName, column.Name, metadata),
                    TypeIcon = ResolveTypeIcon(column),
                };
            }
        }

        List<(TableMetadata Table, ColumnMetadata Column)> candidates = metadata.AllTables
            .SelectMany(table => table.Columns
                .Where(column => column.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                .Select(column => (table, column)))
            .Take(2)
            .ToList();
        if (candidates.Count != 1)
            return null;

        (TableMetadata candidateTable, ColumnMetadata candidateColumn) = candidates[0];
        return new SqlEditorSchemaColumnItem
        {
            Name = candidateColumn.Name,
            DataType = candidateColumn.DataType,
            IsPrimaryKey = candidateColumn.IsPrimaryKey,
            IsForeignKey = candidateColumn.IsForeignKey,
            IsIndexed = candidateColumn.IsIndexed,
            IsUnique = candidateColumn.IsUnique,
            RelatedTable = ResolveRelatedTable(candidateTable.FullName, candidateColumn.Name, metadata),
            TypeIcon = ResolveTypeIcon(candidateColumn),
        };
    }

    private string? ResolveResultSourceTableFullName(DbMetadata metadata)
    {
        SqlEditorResultSet? result = CurrentResult;
        if (result is null)
            return null;

        DataTable? resultData = result.Data;
        if (resultData is null)
            return null;

        SqlInlineEditEligibility eligibility = _resultEligibilityDetector.Evaluate(
            result.StatementSql,
            resultData,
            metadata,
            ResolveConnectionConfigForActiveTab());
        if (eligibility.IsEligible && !string.IsNullOrWhiteSpace(eligibility.TableFullName))
            return eligibility.TableFullName;

        return null;
    }

    private static string? ResolveRelatedTable(string tableFullName, string columnName, DbMetadata metadata)
    {
        ForeignKeyRelation? relation = metadata.AllForeignKeys.FirstOrDefault(fk =>
            fk.ChildFullTable.Equals(tableFullName, StringComparison.OrdinalIgnoreCase)
            && fk.ChildColumn.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (relation is not null)
            return $"{relation.ParentFullTable}.{relation.ParentColumn}";

        relation = metadata.AllForeignKeys.FirstOrDefault(fk =>
            fk.ParentFullTable.Equals(tableFullName, StringComparison.OrdinalIgnoreCase)
            && fk.ParentColumn.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        return relation is null ? null : $"{relation.ChildFullTable}.{relation.ChildColumn}";
    }

    private static MaterialIconKind ResolveTypeIcon(ColumnMetadata column)
    {
        return column.SemanticType switch
        {
            ColumnSemanticType.Numeric => MaterialIconKind.Numeric,
            ColumnSemanticType.Text => MaterialIconKind.AlphabeticalVariant,
            ColumnSemanticType.DateTime => MaterialIconKind.CalendarClock,
            ColumnSemanticType.Boolean => MaterialIconKind.ToggleSwitchOutline,
            ColumnSemanticType.Guid => MaterialIconKind.Fingerprint,
            ColumnSemanticType.Document => MaterialIconKind.CodeJson,
            ColumnSemanticType.Binary => MaterialIconKind.FileDocumentOutline,
            ColumnSemanticType.Spatial => MaterialIconKind.MapMarkerPath,
            _ => MaterialIconKind.TableColumn,
        };
    }

    private string MutationConfirmationRequiredError() =>
        L("sqlEditor.error.mutationConfirmationRequired", "Confirmacao de mutacao necessaria.");

    private string QueryParameterPromptRequiredError() =>
        L("sqlEditor.error.queryParametersRequired", "Parametros obrigatorios para execucao.");

    private bool IsExecutionDeferredResult(SqlEditorResultSet result)
    {
        if (result.Success)
            return false;

        return string.Equals(result.ErrorMessage, MutationConfirmationRequiredError(), StringComparison.Ordinal)
            || string.Equals(result.ErrorMessage, QueryParameterPromptRequiredError(), StringComparison.Ordinal);
    }

    private string BuildEstimateCacheKey(string? sql)
    {
        string statement = sql?.Trim() ?? string.Empty;
        return $"{ActiveTab.Id}::{statement}";
    }

    private string L(string key, string fallback)
    {
        string value = _localization[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static double ClampResultsSheetHeight(double height)
    {
        return Math.Clamp(height, MinResultsSheetHeight, MaxResultsSheetHeight);
    }

    private void TryHydrateResultFilterForTab(SqlEditorTabState tab, bool force = false)
    {
        if (!force && tab.IsResultGridFilterHydratedFromSettings)
            return;

        string tabKey = BuildTabFilterSettingsKey(tab);
        string persistedFilter = AppSettingsStore.LoadSqlEditorResultFilter(tabKey);
        tab.ResultGridFilterText = persistedFilter;
        tab.IsResultGridFilterHydratedFromSettings = true;
    }

    private void PersistResultGridFilterForTab(SqlEditorTabState tab)
    {
        string tabKey = BuildTabFilterSettingsKey(tab);
        AppSettingsStore.SaveSqlEditorResultFilter(tabKey, tab.ResultGridFilterText);
    }

    private void TryHydrateExecutionHistoryForTab(SqlEditorTabState tab, bool force = false)
    {
        if (!force && tab.IsExecutionHistoryHydratedFromSettings)
            return;

        string? historyProfileKey = ResolveHistoryProfileKey(tab);
        if (string.IsNullOrWhiteSpace(historyProfileKey))
        {
            tab.IsExecutionHistoryHydratedFromSettings = true;
            return;
        }

        IReadOnlyList<SqlEditorHistoryEntry> persistedHistory = AppSettingsStore.LoadSqlEditorExecutionHistory(historyProfileKey);
        tab.ExecutionHistory = persistedHistory.Take(500).ToList();
        tab.IsExecutionHistoryHydratedFromSettings = true;
    }

    private void PersistExecutionHistoryForActiveProfileAsync()
    {
        string? historyProfileKey = ResolveHistoryProfileKey(ActiveTab);
        if (string.IsNullOrWhiteSpace(historyProfileKey))
            return;

        List<SqlEditorHistoryEntry> snapshot = ActiveTab.ExecutionHistory.Take(500).ToList();
        _ = Task.Run(() => AppSettingsStore.SaveSqlEditorExecutionHistory(historyProfileKey, snapshot));
    }

    private void ClearExecutionHistoryForActiveProfileAsync()
    {
        string? historyProfileKey = ResolveHistoryProfileKey(ActiveTab);
        if (string.IsNullOrWhiteSpace(historyProfileKey))
            return;

        _ = Task.Run(() => AppSettingsStore.ClearSqlEditorExecutionHistory(historyProfileKey));
    }

    private static string BuildTabFilterSettingsKey(SqlEditorTabState tab)
    {
        string anchor = !string.IsNullOrWhiteSpace(tab.FilePath)
            ? tab.FilePath
            : tab.FallbackTitle;
        return $"{tab.Provider}::{anchor}";
    }

    private string? ResolveHistoryProfileKey(SqlEditorTabState tab)
    {
        if (tab is null)
            return null;

        if (string.IsNullOrWhiteSpace(tab.ConnectionProfileId))
            return null;

        bool knownProfile = AvailableConnectionProfiles.Any(profile =>
            string.Equals(profile.Id, tab.ConnectionProfileId, StringComparison.Ordinal));
        if (!knownProfile)
            return null;

        return tab.ConnectionProfileId.Trim();
    }

    private void EnsureHistorySelection()
    {
        if (FilteredExecutionHistory.Count == 0)
        {
            SelectedExecutionHistoryEntry = null;
            return;
        }

        if (SelectedExecutionHistoryEntry is null || !FilteredExecutionHistory.Contains(SelectedExecutionHistoryEntry))
            SelectedExecutionHistoryEntry = FilteredExecutionHistory[0];
    }

    private static int ResolveHistorySelectionIndex(
        IReadOnlyList<SqlEditorHistoryEntry> history,
        SqlEditorHistoryEntry? selected)
    {
        if (selected is null)
            return -1;

        for (int i = 0; i < history.Count; i++)
        {
            SqlEditorHistoryEntry item = history[i];
            if (item == selected)
                return i;
        }

        return -1;
    }

    public bool ReorderTabs(string? sourceTabId, string? targetTabId)
    {
        if (string.IsNullOrWhiteSpace(sourceTabId) || string.IsNullOrWhiteSpace(targetTabId))
            return false;

        if (string.Equals(sourceTabId, targetTabId, StringComparison.Ordinal))
            return false;

        int sourceIndex = EditorTabs
            .Select((tab, index) => new { tab, index })
            .FirstOrDefault(x => string.Equals(x.tab.Id, sourceTabId, StringComparison.Ordinal))
            ?.index ?? -1;
        int targetIndex = EditorTabs
            .Select((tab, index) => new { tab, index })
            .FirstOrDefault(x => string.Equals(x.tab.Id, targetTabId, StringComparison.Ordinal))
            ?.index ?? -1;

        if (sourceIndex < 0 || targetIndex < 0)
            return false;

        if (!Tabs.MoveTab(sourceIndex, targetIndex))
            return false;

        RaiseTabStateChanged();
        return true;
    }

    private void RestoreOrInitializeTabs(DatabaseProvider initialProvider, string? initialConnectionProfileId)
    {
        IReadOnlyList<SqlEditorSessionDraftEntry> drafts = _sessionDraftStore.LoadDrafts();
        if (drafts.Count == 0)
        {
            Tabs.Initialize(initialProvider, initialConnectionProfileId);
            return;
        }

        SqlEditorSessionDraftEntry firstDraft = drafts[0];
        Tabs.Initialize(firstDraft.Provider, firstDraft.ConnectionProfileId ?? initialConnectionProfileId);

        int activeIndex = 0;
        for (int i = 0; i < drafts.Count; i++)
        {
            SqlEditorSessionDraftEntry draft = drafts[i];
            SqlEditorTabState tab = i == 0
                ? Tabs.GetActiveTab()
                : Tabs.AddNewTab(draft.Provider, draft.ConnectionProfileId);

            tab.SqlText = draft.SqlText;
            tab.FilePath = draft.FilePath;
            if (!string.IsNullOrWhiteSpace(draft.FallbackTitle))
                tab.FallbackTitle = draft.FallbackTitle;
            tab.Provider = draft.Provider;
            tab.ConnectionProfileId = draft.ConnectionProfileId;
            tab.QueryParameterLastValues = draft.QueryParameterLastValues is { Count: > 0 }
                ? new Dictionary<string, string>(draft.QueryParameterLastValues, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            tab.IsDirty = true;

            if (draft.IsActive)
                activeIndex = i;
        }

        Tabs.TryActivate(activeIndex);
        _sessionDraftStore.ClearDrafts();
        PublishStatus(L("sqlEditor.status.sessionDraftsRestored", "Rascunhos da sessao anterior restaurados."));
    }

    private void InitializeDraftAutoSaveTimers()
    {
        _draftAutoSaveDebounceTimer = new Timer(
            static state => ((SqlEditorViewModel)state!).OnDraftAutoSaveDebounceTick(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _draftAutoSaveForcedTimer = new Timer(
            static state => ((SqlEditorViewModel)state!).OnDraftAutoSaveForcedTick(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    private void EnsureDraftAutoSaveTimersStarted()
    {
        if (_draftAutoSaveTimersStarted)
            return;

        _draftAutoSaveTimersStarted = true;
        _draftAutoSaveForcedTimer?.Change(DraftAutoSaveInterval, DraftAutoSaveInterval);
    }

    private void OnDraftAutoSaveDebounceTick()
    {
        if (!_hasPendingDraftAutoSave)
            return;

        _hasPendingDraftAutoSave = false;
        PersistSessionDraftsSnapshot();
    }

    private void OnDraftAutoSaveForcedTick()
    {
        PersistSessionDraftsSnapshot();
    }

    private void PersistSessionDraftsSnapshot()
    {
        SqlEditorTabState[] tabsSnapshot;
        try
        {
            tabsSnapshot = [.. Tabs.Tabs];
        }
        catch (InvalidOperationException)
        {
            return;
        }

        List<SqlEditorSessionDraftEntry> drafts = tabsSnapshot
            .Select((tab, index) => new { tab, index })
            .Where(static item => item.tab.IsDirty)
            .Where(static item => !string.IsNullOrWhiteSpace(item.tab.SqlText))
            .Where(static item => item.tab.SqlText.Trim().Length >= DraftMinimumTextLength)
            .Select(item => new SqlEditorSessionDraftEntry
            {
                TabId = item.tab.Id,
                FallbackTitle = item.tab.FallbackTitle,
                SqlText = item.tab.SqlText,
                FilePath = item.tab.FilePath,
                Provider = item.tab.Provider,
                ConnectionProfileId = item.tab.ConnectionProfileId,
                QueryParameterLastValues = new Dictionary<string, string>(item.tab.QueryParameterLastValues, StringComparer.OrdinalIgnoreCase),
                TabOrder = item.index,
                IsActive = item.index == Tabs.ActiveTabIndex,
                SavedAtUtc = DateTimeOffset.UtcNow,
            })
            .ToList();

        if (drafts.Count == 0)
        {
            _sessionDraftStore.ClearDrafts();
            return;
        }

        _sessionDraftStore.SaveDrafts(drafts);
    }
}
