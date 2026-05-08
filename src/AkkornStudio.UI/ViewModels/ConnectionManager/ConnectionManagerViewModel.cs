using System.Collections.ObjectModel;
using Material.Icons;
using AkkornStudio.UI;
using Avalonia.Svg.Skia;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.Services.Connection;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.ConnectionManager.Contracts;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Modal;
using AkkornStudio.UI.ViewModels.Canvas;
using AkkornStudio.UI.Services.Theming;
using System.Diagnostics;
using System.IO;

namespace AkkornStudio.UI.ViewModels;

// ── ViewModel ─────────────────────────────────────────────────────────────────

public sealed class ConnectionManagerViewModel : ViewModelBase
{
    public enum ConnectionWizardStep
    {
        SelectProvider = 1,
        ConfigureConnection = 2,
    }

    private const string ConnectionModalLogFile = "connection-modal-debug.log";
    // Latency above this threshold is considered "Degraded" rather than "Online"
    private const double DegradedLatencyThresholdMs = 500.0;
    // How often the background health monitor pings the active connection (seconds)
    // Value is defined in AppConstants.HealthCheckIntervalSeconds

    // ── Services ───────────────────────────────────────────────────────────────

    private readonly DatabaseConnectionService _dbConnectionService;
    private readonly ILogger<ConnectionManagerViewModel> _logger;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly IConnectionErrorMessageMapper _errorMessageMapper;
    private readonly IConnectionStatusPresenter _statusPresenter;
    private readonly IConnectionCanvasPromptCoordinator _canvasPromptCoordinator;
    private readonly IConnectionHealthMonitorService _healthMonitorService;
    private readonly IConnectionSessionOrchestrator _sessionOrchestrator;
    private readonly IConnectionTestExecutor _connectionTestExecutor;
    private readonly IConnectionCatalogService _connectionCatalogService;
    private readonly IConnectionTestService _connectionTestService;
    private readonly IConnectionSessionService _connectionSessionService;
    private readonly IConnectionUrlParserService _connectionUrlParserService;
    private readonly IConnectionProfileFormMapper _formMapper;
    private readonly IConnectionActivationWorkflow _activationWorkflow;
    private readonly IFireAndForgetSafetyExecutor _fireAndForgetSafetyExecutor;
    private readonly IConnectionHealthLifecycleCoordinator _healthLifecycleCoordinator;
    private readonly IGlobalModalManager _globalModalManager;
    private readonly IConnectionCatalogCapabilityProvider _connectionCatalogCapabilityProvider;

    /// <summary>
    /// Reference to the canvas search menu where database tables will be loaded.
    /// Set by the CanvasViewModel after initialization.
    /// </summary>
    public SearchMenuViewModel? SearchMenu { get; set; }

    /// <summary>
    /// Reference to the canvas view model to reset/update when connecting to a new database.
    /// Set by the CanvasViewModel after initialization.
    /// </summary>
    public CanvasViewModel? Canvas { get; set; }

    // ── Visibility ────────────────────────────────────────────────────────────

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    // ── Profile list ──────────────────────────────────────────────────────────

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    private ConnectionProfile? _selectedProfile;
    public ConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            Set(ref _selectedProfile, value);
            if (value is not null) LoadProfileIntoForm(value);
            DeleteProfileCommand.NotifyCanExecuteChanged();
            ConnectCommand.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(IsSelectedProfileActive));
            RaisePropertyChanged(nameof(CanShowConnectAction));
            RaisePropertyChanged(nameof(CanShowDisconnectAction));
            RaisePropertyChanged(nameof(SidebarSelectedConnection));
            RaisePropertyChanged(nameof(SidebarConnectionName));
            RaisePropertyChanged(nameof(SidebarConnectionSubtitle));
            RaisePropertyChanged(nameof(IsDatabaseSelectionVisible));
            SwitchDatabaseCommand.NotifyCanExecuteChanged();
        }
    }

    // ── Active connection & health ────────────────────────────────────────────

    private string? _activeProfileId;
    public string? ActiveProfileId
    {
        get => _activeProfileId;
        set
        {
            Set(ref _activeProfileId, value);
            RaisePropertyChanged(nameof(ActiveConnectionLabel));
            RaisePropertyChanged(nameof(IsSelectedProfileActive));
            RaisePropertyChanged(nameof(SidebarSelectedConnection));
            RaisePropertyChanged(nameof(SidebarConnectionName));
            RaisePropertyChanged(nameof(SidebarConnectionSubtitle));
            ActiveHealthStatus = value is null
                ? ConnectionHealthStatus.Unknown
                : ConnectionHealthStatus.Online;
            RestartHealthMonitor();
            DisconnectCommand.NotifyCanExecuteChanged();
            ConnectCommand.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(CanShowConnectAction));
            RaisePropertyChanged(nameof(CanShowDisconnectAction));
            RefreshHealthCommand.NotifyCanExecuteChanged();
            ReloadMetadataCommand.NotifyCanExecuteChanged();
            SwitchSchemaCommand.NotifyCanExecuteChanged();
            SwitchDatabaseCommand.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(IsDatabaseSelectionVisible));
            ProfilesChanged?.Invoke();
        }
    }

    private bool _isConnecting;
    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            if (!Set(ref _isConnecting, value))
                return;

            RaisePropertyChanged(nameof(IsNotConnecting));
            RaisePropertyChanged(nameof(IsBusy));
            ConnectOrOpenManagerCommand.NotifyCanExecuteChanged();
            SwitchConnectionCommand.NotifyCanExecuteChanged();
            SwitchSchemaCommand.NotifyCanExecuteChanged();
            SwitchDatabaseCommand.NotifyCanExecuteChanged();
            ReloadMetadataCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// True when NOT connecting (for UI bindings that need the inverse).
    /// </summary>
    public bool IsNotConnecting => !IsConnecting;

    /// <summary>
    /// True while connection is being established or schema/database switch is running.
    /// </summary>
    public bool IsBusy => IsConnecting || IsReloadingSchema;

    // ── Schema reload state ────────────────────────────────────────────────────

    private bool _isReloadingSchema;
    public bool IsReloadingSchema
    {
        get => _isReloadingSchema;
        private set
        {
            if (!Set(ref _isReloadingSchema, value))
                return;

            RaisePropertyChanged(nameof(IsBusy));
            ConnectOrOpenManagerCommand.NotifyCanExecuteChanged();
            SwitchConnectionCommand.NotifyCanExecuteChanged();
            SwitchSchemaCommand.NotifyCanExecuteChanged();
            SwitchDatabaseCommand.NotifyCanExecuteChanged();
            ReloadMetadataCommand.NotifyCanExecuteChanged();
        }
    }

    // ── Sidebar schema list ───────────────────────────────────────────────────

    public ObservableCollection<string> AvailableSchemas { get; } = [];

    public ObservableCollection<string> AvailableDatabases { get; } = [];

    private string? _selectedDatabase;
    public string? SelectedDatabase
    {
        get => _selectedDatabase;
        set
        {
            if (Set(ref _selectedDatabase, value))
                SwitchDatabaseCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsDatabaseSelectionVisible =>
        SupportsDatabaseOrCatalogSelection && AvailableDatabases.Count > 0;

    private bool SupportsDatabaseOrCatalogSelection
    {
        get
        {
            ConnectionProfile? profile = Profiles.FirstOrDefault(x => x.Id == _activeProfileId)
                ?? SelectedProfile;
            if (profile is null)
                return false;

            IReadOnlyList<ConnectionContextLevel> supportedLevels = _connectionCatalogCapabilityProvider
                .GetSupportedLevels(profile.Provider);
            return supportedLevels.Contains(ConnectionContextLevel.DatabaseOrCatalog);
        }
    }

    private string? _selectedSchema;
    public string? SelectedSchema
    {
        get => _selectedSchema;
        set
        {
            if (!Set(ref _selectedSchema, value))
                return;

            if (Canvas?.Schema is not null)
                Canvas.Schema.SelectedSchema = value;
        }
    }

    // ── Active connection info ─────────────────────────────────────────────────

    private string? _activeServerVersion;
    public string? ActiveServerVersion
    {
        get => _activeServerVersion;
        private set => Set(ref _activeServerVersion, value);
    }

    private int? _activeLatencyMs;
    public int? ActiveLatencyMs
    {
        get => _activeLatencyMs;
        private set => Set(ref _activeLatencyMs, value);
    }

    public ConnectionProfile? SidebarSelectedConnection
    {
        get => Profiles.FirstOrDefault(x => x.Id == _activeProfileId) ?? SelectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value))
                return;

            SelectedProfile = value;
            RaisePropertyChanged(nameof(SidebarSelectedConnection));
        }
    }

    public string SidebarConnectionName =>
        Profiles.FirstOrDefault(x => x.Id == _activeProfileId)?.Name
        ?? SelectedProfile?.Name
        ?? L("connection.none", "No connection");

    public string SidebarConnectionSubtitle
    {
        get
        {
            ConnectionProfile? profile = Profiles.FirstOrDefault(x => x.Id == _activeProfileId) ?? SelectedProfile;
            if (profile is null)
                return string.Empty;

            return $"{profile.Provider} · {profile.Database}";
        }
    }

    public string SchemaFilterQuery
    {
        get => Canvas?.Schema.FilterQuery ?? string.Empty;
        set
        {
            if (Canvas?.Schema is null)
                return;

            if (string.Equals(Canvas.Schema.FilterQuery, value, StringComparison.Ordinal))
                return;

            Canvas.Schema.FilterQuery = value;
            RaisePropertyChanged(nameof(SchemaFilterQuery));
        }
    }

    public string ActiveMetadataSummary
    {
        get
        {
            DbMetadata? metadata = ResolveCurrentMetadata();
            if (metadata is null)
                return L("connection.metadata.unavailable", "Metadados indisponíveis");

            return $"Schemas: {metadata.Schemas.Count} · Tabelas: {metadata.TotalTables} · Views: {metadata.TotalViews} · FKs: {metadata.TotalForeignKeys}";
        }
    }

    public string ActiveMetadataDetails
    {
        get
        {
            DbMetadata? metadata = ResolveCurrentMetadata();
            if (metadata is not null)
                return $"{metadata.Provider} · {metadata.DatabaseName}";

            return !string.IsNullOrWhiteSpace(ActiveServerVersion)
                ? ActiveServerVersion
                : L("connection.metadata.none", "Conecte-se para carregar metadados.");
        }
    }

    public string ActiveConnectionLabel
    {
        get
        {
            var p = Profiles.FirstOrDefault(x => x.Id == _activeProfileId);
            return p is null ? _loc["connection.none"] : $"{p.Provider} · {p.Database}";
        }
    }

    private ConnectionHealthStatus _activeHealthStatus = ConnectionHealthStatus.Unknown;
    public ConnectionHealthStatus ActiveHealthStatus
    {
        get => _activeHealthStatus;
        private set
        {
            Set(ref _activeHealthStatus, value);
            RaisePropertyChanged(nameof(ConnectionIndicatorColor));
            RaisePropertyChanged(nameof(ConnectionHealthLabel));
            RaisePropertyChanged(nameof(ConnectionHealthTooltip));
        }
    }

    public string ConnectionIndicatorColor => _activeHealthStatus switch
    {
        ConnectionHealthStatus.Online   => UiColorConstants.C_4ADE80,
        ConnectionHealthStatus.Degraded => UiColorConstants.C_FBBF24,
        ConnectionHealthStatus.Offline  => UiColorConstants.C_EF4444,
        _                               => UiColorConstants.C_4A5568,
    };

    public string ConnectionHealthLabel => _activeHealthStatus switch
    {
        ConnectionHealthStatus.Online   => _loc["connection.health.online"],
        ConnectionHealthStatus.Degraded => _loc["connection.health.degraded"],
        ConnectionHealthStatus.Offline  => _loc["connection.health.offline"],
        _                               => _loc["connection.none"],
    };

    public string ConnectionHealthTooltip
    {
        get
        {
            var p = Profiles.FirstOrDefault(x => x.Id == _activeProfileId);
            if (p is null) return _loc["connection.tooltip.none"];
            var label = ConnectionHealthLabel;
            return $"{p.Name} ({p.Provider} · {p.Host}:{p.Port}/{p.Database}) — {label}";
        }
    }

    // ── Edit form ─────────────────────────────────────────────────────────────

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (!Set(ref _isEditing, value))
                return;

            RaisePropertyChanged(nameof(IsProviderSelectionStepVisible));
            RaisePropertyChanged(nameof(IsConnectionFormStepVisible));
            SelectProviderForNewProfileCommand.NotifyCanExecuteChanged();
            BackToProviderSelectionCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _isNewProfileFlow;
    public bool IsNewProfileFlow
    {
        get => _isNewProfileFlow;
        private set
        {
            if (!Set(ref _isNewProfileFlow, value))
                return;

            RaisePropertyChanged(nameof(IsProviderSelectionStepVisible));
            RaisePropertyChanged(nameof(IsConnectionFormStepVisible));
            RaisePropertyChanged(nameof(IsProviderSummaryVisible));
            SelectProviderForNewProfileCommand.NotifyCanExecuteChanged();
            BackToProviderSelectionCommand.NotifyCanExecuteChanged();
        }
    }

    private ConnectionWizardStep _wizardStep = ConnectionWizardStep.ConfigureConnection;
    public ConnectionWizardStep WizardStep
    {
        get => _wizardStep;
        private set
        {
            if (!Set(ref _wizardStep, value))
                return;

            RaisePropertyChanged(nameof(IsProviderSelectionStepVisible));
            RaisePropertyChanged(nameof(IsConnectionFormStepVisible));
            RaisePropertyChanged(nameof(IsProviderSummaryVisible));
            SelectProviderForNewProfileCommand.NotifyCanExecuteChanged();
            BackToProviderSelectionCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsProviderSelectionStepVisible =>
        IsEditing && IsNewProfileFlow && WizardStep == ConnectionWizardStep.SelectProvider;

    public bool IsConnectionFormStepVisible =>
        IsEditing && (!IsNewProfileFlow || WizardStep == ConnectionWizardStep.ConfigureConnection);

    public bool IsProviderSummaryVisible => IsNewProfileFlow && WizardStep == ConnectionWizardStep.ConfigureConnection;

    private string _editId = Guid.NewGuid().ToString();

    private string _editName = "New Connection";
    public string EditName { get => _editName; set => Set(ref _editName, value); }

    private DatabaseProvider _editProvider = DatabaseProvider.Postgres;
    public DatabaseProvider EditProvider
    {
        get => _editProvider;
        set
        {
            var oldDefault = ConnectionProfile.DefaultPort(_editProvider);
            Set(ref _editProvider, value);
            if (EditPort == oldDefault)
                EditPort = ConnectionProfile.DefaultPort(value);

            if (value == DatabaseProvider.SQLite)
                EditPort = 0;

            if (value != DatabaseProvider.SqlServer)
                EditUseIntegratedSecurity = false;

            if (!OperatingSystem.IsWindows())
                EditUseIntegratedSecurity = false;

            RaisePropertyChanged(nameof(IsSqliteProvider));
            RaisePropertyChanged(nameof(SupportsSsl));
            RaisePropertyChanged(nameof(SupportsIntegratedSecurity));
            RaisePropertyChanged(nameof(ShowCredentials));
            RaisePropertyChanged(nameof(SelectedProviderOption));
            SyncWizardProviderSelection(value);
        }
    }

    public ProviderOption SelectedProviderOption
    {
        get => AvailableProviderOptions.First(x => x.Provider == EditProvider);
        set
        {
            if (value is null)
                return;

            if (value.Provider != EditProvider)
                EditProvider = value.Provider;
        }
    }

    private string _editHost = AppConstants.DefaultHost;
    public string EditHost { get => _editHost; set => Set(ref _editHost, value); }

    private int _editPort = 5432;
    public int EditPort { get => _editPort; set => Set(ref _editPort, value); }

    private string _editDatabase = "";
    public string EditDatabase { get => _editDatabase; set => Set(ref _editDatabase, value); }

    private string _editUsername = "";
    public string EditUsername { get => _editUsername; set => Set(ref _editUsername, value); }

    private string _editPassword = "";
    public string EditPassword { get => _editPassword; set => Set(ref _editPassword, value); }

    private bool _editRememberPassword = true;
    public bool EditRememberPassword
    {
        get => _editRememberPassword;
        set => Set(ref _editRememberPassword, value);
    }

    private bool _editUseSsl;
    public bool EditUseSsl
    {
        get => _editUseSsl;
        set => Set(ref _editUseSsl, value);
    }

    private bool _editTrustServerCertificate = true;
    public bool EditTrustServerCertificate
    {
        get => _editTrustServerCertificate;
        set => Set(ref _editTrustServerCertificate, value);
    }

    private bool _editUseIntegratedSecurity;
    public bool EditUseIntegratedSecurity
    {
        get => _editUseIntegratedSecurity;
        set
        {
            bool normalized = SupportsIntegratedSecurity && value;
            Set(ref _editUseIntegratedSecurity, normalized);
            RaisePropertyChanged(nameof(ShowCredentials));
        }
    }

    private int _editTimeout = 30;
    public int EditTimeout { get => _editTimeout; set => Set(ref _editTimeout, value); }

    private string _editConnectionUrl = string.Empty;
    public string EditConnectionUrl
    {
        get => _editConnectionUrl;
        set
        {
            Set(ref _editConnectionUrl, value);
            ImportFromUrlCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsSqliteProvider => EditProvider == DatabaseProvider.SQLite;
    public bool SupportsSsl =>
        EditProvider == DatabaseProvider.Postgres ||
        EditProvider == DatabaseProvider.MySql ||
        EditProvider == DatabaseProvider.SqlServer;
    public bool SupportsIntegratedSecurity =>
        OperatingSystem.IsWindows() && EditProvider == DatabaseProvider.SqlServer;
    public bool ShowCredentials => !IsSqliteProvider && !EditUseIntegratedSecurity;
    public bool IsFormValid => TryValidateForm(requireName: false, out _);
    public bool IsSelectedProfileActive =>
        SelectedProfile is not null
        && !string.IsNullOrWhiteSpace(ActiveProfileId)
        && string.Equals(SelectedProfile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase);
    public bool CanShowConnectAction => SelectedProfile is not null && !IsSelectedProfileActive;
    public bool CanShowDisconnectAction => SelectedProfile is not null && IsSelectedProfileActive;

    // ── Test connection state ─────────────────────────────────────────────────

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        set
        {
            Set(ref _isTesting, value);
            TestConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    private string _testStatus = "";
    public string TestStatus { get => _testStatus; set => Set(ref _testStatus, value); }

    private string _testStatusColor = UiColorConstants.C_4A5568;
    public string TestStatusColor { get => _testStatusColor; set => Set(ref _testStatusColor, value); }

    // ── Post-connect canvas confirmation ────────────────────────────────────

    private bool _isClearCanvasPromptVisible;
    public bool IsClearCanvasPromptVisible
    {
        get => _isClearCanvasPromptVisible;
        private set => Set(ref _isClearCanvasPromptVisible, value);
    }

    private DbMetadata? _pendingLoadedMetadata;
    private ConnectionConfig? _pendingLoadedConfig;

    // ── Background health monitor ─────────────────────────────────────────────

    private CancellationTokenSource? _healthMonitorCts;
    private CancellationTokenSource? _connectCts;
    private CancellationTokenSource? _wizardStepAdvanceCts;

    // ── Provider list for ComboBox ────────────────────────────────────────────

    public static IReadOnlyList<ProviderOption> AvailableProviderOptions { get; } =
    [
        new(DatabaseProvider.Postgres, "PostgreSQL", MaterialIconKind.Elephant, "avares://AkkornStudio.UI/Assets/Images/Icons/Databases/PostgreSQL.svg"),
        new(DatabaseProvider.MySql, "MySQL", MaterialIconKind.Database, "avares://AkkornStudio.UI/Assets/Images/Icons/Databases/MySQL.svg"),
        new(DatabaseProvider.SqlServer, "SQL Server", MaterialIconKind.MicrosoftWindows, "avares://AkkornStudio.UI/Assets/Images/Icons/Databases/Microsoft_SQL_Server.svg"),
        new(DatabaseProvider.SQLite, "SQLite", MaterialIconKind.FileOutline, "avares://AkkornStudio.UI/Assets/Images/Icons/Databases/Sqlite.svg"),
    ];

    // ── Commands ──────────────────────────────────────────────────────────────

    public RelayCommand NewProfileCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand TestConnectionCommand { get; }
    public RelayCommand ImportFromUrlCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand OpenNewProfileCommand { get; }
    public RelayCommand ConnectOrOpenManagerCommand { get; }
    public RelayCommand<ProviderOption> SelectProviderForNewProfileCommand { get; }
    public RelayCommand BackToProviderSelectionCommand { get; }
    public RelayCommand RefreshHealthCommand { get; }
    public RelayCommand<ConnectionProfile> SwitchConnectionCommand { get; }
    public RelayCommand<string> SwitchDatabaseCommand { get; }
    public RelayCommand<string> SwitchSchemaCommand { get; }
    public RelayCommand ReloadMetadataCommand { get; }
    public RelayCommand ClearCanvasAfterConnectCommand { get; }
    public RelayCommand KeepCanvasAfterConnectCommand { get; }
    public RelayCommand CloseClearCanvasPromptCommand { get; }

    public event Action<ConnectionProfile>? ConnectionActivated;
    public event Action<string, string?>? ConnectionFailed;
    public event Action? ProfilesChanged;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ConnectionManagerViewModel(
        IConnectionErrorMessageMapper? errorMessageMapper = null,
        IConnectionStatusPresenter? statusPresenter = null,
        IConnectionCanvasPromptCoordinator? canvasPromptCoordinator = null,
        IConnectionHealthMonitorService? healthMonitorService = null,
        IConnectionSessionOrchestrator? sessionOrchestrator = null,
        IConnectionProfileStore? profileStore = null,
        IConnectionTestExecutor? connectionTestExecutor = null,
        IConnectionCatalogService? connectionCatalogService = null,
        IConnectionTestService? connectionTestService = null,
        IConnectionSessionService? connectionSessionService = null,
        IConnectionUrlParserService? connectionUrlParserService = null,
        IConnectionProfileFormMapper? formMapper = null,
        IConnectionActivationWorkflow? activationWorkflow = null,
        IFireAndForgetSafetyExecutor? fireAndForgetSafetyExecutor = null,
        IConnectionHealthLifecycleCoordinator? healthLifecycleCoordinator = null,
        IGlobalModalManager? globalModalManager = null,
        IConnectionCatalogCapabilityProvider? connectionCatalogCapabilityProvider = null)
    {
        _logger = NullLogger<ConnectionManagerViewModel>.Instance;
        _dbConnectionService = new DatabaseConnectionService();
        _errorMessageMapper = errorMessageMapper ?? new ConnectionErrorMessageMapper(_loc);
        _statusPresenter = statusPresenter ?? new ConnectionStatusPresenter(_loc);
        _canvasPromptCoordinator = canvasPromptCoordinator ?? new ConnectionCanvasPromptCoordinator();
        _healthMonitorService = healthMonitorService ?? new ConnectionHealthMonitorService();
        _sessionOrchestrator = sessionOrchestrator ?? new ConnectionSessionOrchestrator();
        IConnectionProfileStore resolvedProfileStore = profileStore ?? new ConnectionProfileStore();
        _connectionTestExecutor = connectionTestExecutor ?? new DbOrchestratorConnectionTestExecutor();

        _connectionCatalogService = connectionCatalogService ?? new ConnectionCatalogService(resolvedProfileStore);

        IProviderCapabilityService providerCapabilityService = new ProviderCapabilityService();
        IConnectionValidationService connectionValidationService = new ConnectionValidationService();
        _connectionUrlParserService = connectionUrlParserService ?? new ConnectionUrlParserService();
        _connectionTestService = connectionTestService ?? new ConnectionTestService(
            _connectionTestExecutor,
            connectionValidationService,
            providerCapabilityService,
            _connectionUrlParserService);

        IConnectionTelemetryService connectionTelemetryService =
            new ConnectionTelemetryService(NullLogger<ConnectionTelemetryService>.Instance);
        _connectionSessionService = connectionSessionService
            ?? new ConnectionSessionService(_connectionTestService, connectionTelemetryService);

        _formMapper = formMapper ?? new ConnectionProfileFormMapper(_loc);
        _activationWorkflow = activationWorkflow ?? new ConnectionActivationWorkflow();
        _fireAndForgetSafetyExecutor = fireAndForgetSafetyExecutor ?? new FireAndForgetSafetyExecutor(_logger);
        _healthLifecycleCoordinator = healthLifecycleCoordinator ?? new ConnectionHealthLifecycleCoordinator(_healthMonitorService);
        _globalModalManager = globalModalManager ?? GlobalModalManager.Instance;
        _connectionCatalogCapabilityProvider = connectionCatalogCapabilityProvider ?? new ConnectionCatalogCapabilityProvider();

        _loc.PropertyChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(ActiveConnectionLabel));
            RaisePropertyChanged(nameof(ConnectionHealthLabel));
            RaisePropertyChanged(nameof(ConnectionHealthTooltip));
        };

        NewProfileCommand     = new RelayCommand(BeginNewProfile);
        SaveProfileCommand    = new RelayCommand(StartSaveProfileSafe);
        DeleteProfileCommand  = new RelayCommand(StartDeleteProfileSafe, () => SelectedProfile is not null);
        TestConnectionCommand = new RelayCommand(StartTestConnectionSafe, () => !IsTesting && IsEditing);
        ImportFromUrlCommand  = new RelayCommand(StartImportFromUrlSafe, () => !string.IsNullOrWhiteSpace(EditConnectionUrl));
        ConnectCommand        = new RelayCommand(StartConnectSafe, () => SelectedProfile is not null);
        DisconnectCommand     = new RelayCommand(StartDisconnectSafe, () => _activeProfileId is not null);
        CloseCommand          = new RelayCommand(() =>
        {
            LogConnectionModal("CloseCommand invoked");
            IsVisible = false;
            LogConnectionModal("CloseCommand completed");
        });
        OpenNewProfileCommand = new RelayCommand(() =>
        {
            Open();
            BeginNewProfile();
        });
        ConnectOrOpenManagerCommand = new RelayCommand(
            ConnectOrOpenManager,
            () => !IsConnecting && !IsReloadingSchema);
        SelectProviderForNewProfileCommand = new RelayCommand<ProviderOption>(
            option =>
            {
                if (option is null)
                    return;

                _ = AdvanceWizardFromProviderSelectionAsync(option);
            },
            option => option is not null && IsEditing && IsNewProfileFlow);
        BackToProviderSelectionCommand = new RelayCommand(
            () =>
            {
                SyncWizardProviderSelection(EditProvider);
                WizardStep = ConnectionWizardStep.SelectProvider;
            },
            () => IsEditing && IsNewProfileFlow && WizardStep == ConnectionWizardStep.ConfigureConnection);
        RefreshHealthCommand  = new RelayCommand(StartRefreshHealthSafe, () => _activeProfileId is not null);
        SwitchConnectionCommand = new RelayCommand<ConnectionProfile>(
            profile =>
            {
                if (profile is not null)
                    _ = SwitchConnectionAsync(profile);
            },
            profile => profile is not null && !IsConnecting && !IsReloadingSchema);
        SwitchDatabaseCommand = new RelayCommand<string>(
            databaseName =>
            {
                if (!string.IsNullOrWhiteSpace(databaseName))
                    _ = SwitchDatabaseAsync(databaseName);
            },
            databaseName =>
                !string.IsNullOrWhiteSpace(databaseName)
                && SupportsDatabaseOrCatalogSelection
                && !string.Equals(databaseName, SelectedDatabase, StringComparison.OrdinalIgnoreCase)
                && !IsConnecting
                && !IsReloadingSchema);
        SwitchSchemaCommand = new RelayCommand<string>(
            schema =>
            {
                if (!string.IsNullOrWhiteSpace(schema))
                    SelectedSchema = schema;
            },
            schema => !string.IsNullOrWhiteSpace(schema) && !IsConnecting && !IsReloadingSchema);
        ReloadMetadataCommand = new RelayCommand(
            StartReloadMetadataSafe,
            () => _activeProfileId is not null && !IsConnecting && !IsReloadingSchema);
        ClearCanvasAfterConnectCommand = new RelayCommand(ClearCanvasAfterConnect);
        KeepCanvasAfterConnectCommand = new RelayCommand(KeepCanvasAfterConnect);
        CloseClearCanvasPromptCommand = new RelayCommand(() => CloseClearCanvasPrompt(dismissedByUser: true));

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EditName)
                or nameof(EditProvider)
                or nameof(EditHost)
                or nameof(EditPort)
                or nameof(EditDatabase)
                or nameof(EditUsername)
                or nameof(EditUseIntegratedSecurity)
                or nameof(EditTimeout)
                or nameof(IsEditing))
            {
                RaisePropertyChanged(nameof(IsFormValid));
                ConnectCommand.NotifyCanExecuteChanged();
            }
        };

        LoadProfiles();
    }

    public void Open()
    {
        LogConnectionModal("Open invoked");
        IsVisible = true;
        LogConnectionModal("Open completed");
    }

    // ── Form operations ───────────────────────────────────────────────────────

    private void BeginNewProfile()
    {
        _selectedProfile = null;
        RaisePropertyChanged(nameof(SelectedProfile));

        ApplyFormData(_formMapper.CreateNew());
        SyncWizardProviderSelection(EditProvider);
        IsNewProfileFlow = true;
        WizardStep = ConnectionWizardStep.SelectProvider;
        IsEditing = true;
        TestStatus = "";
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    private void LoadProfileIntoForm(ConnectionProfile p)
    {
        ApplyFormData(_formMapper.FromProfile(p));
        SyncWizardProviderSelection(EditProvider);
        IsNewProfileFlow = false;
        WizardStep = ConnectionWizardStep.ConfigureConnection;
        IsEditing = true;
        TestStatus = "";
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    private async Task AdvanceWizardFromProviderSelectionAsync(ProviderOption option)
    {
        if (!IsEditing || !IsNewProfileFlow)
            return;

        if (option.Provider != EditProvider)
            EditProvider = option.Provider;
        else
            SyncWizardProviderSelection(option.Provider);

        _wizardStepAdvanceCts?.Cancel();
        _wizardStepAdvanceCts?.Dispose();
        CancellationTokenSource localCts = new();
        _wizardStepAdvanceCts = localCts;

        try
        {
            await Task.Delay(170, localCts.Token);
            WizardStep = ConnectionWizardStep.ConfigureConnection;
        }
        catch (TaskCanceledException)
        {
            // Ignore stale step-change attempts from rapid clicks.
        }
        finally
        {
            if (ReferenceEquals(_wizardStepAdvanceCts, localCts))
                _wizardStepAdvanceCts = null;

            localCts.Dispose();
        }
    }

    private static void SyncWizardProviderSelection(DatabaseProvider provider)
    {
        foreach (ProviderOption option in AvailableProviderOptions)
            option.IsSelectedWizard = option.Provider == provider;
    }

    private async Task SaveProfileAsync()
    {
        if (!TryValidateForm(requireName: true, out string? validationError))
        {
            ApplyTestStatus(_statusPresenter.Failed(validationError));
            return;
        }

        ConnectionProfile profile = _formMapper.ToProfile(CaptureFormData());
        OperationResultDto<ConnectionDetailsDto> result = await _connectionCatalogService.SaveAsync(
            ConnectionContractMapper.ToDetails(profile),
            CancellationToken.None);
        if (!result.Success || result.Payload is null)
        {
            ApplyTestStatus(_statusPresenter.Failed(result.UserMessage));
            return;
        }

        await ReloadProfilesFromCatalogAsync(result.Payload.Id);

        if (string.Equals(_activeProfileId, result.Payload.Id, StringComparison.Ordinal))
        {
            RaisePropertyChanged(nameof(ActiveConnectionLabel));
            RaisePropertyChanged(nameof(ConnectionHealthTooltip));
        }

        ProfilesChanged?.Invoke();
    }

    private async Task DeleteProfileAsync()
    {
        ConnectionProfile? selected = SelectedProfile;
        if (selected is null)
            return;

        OperationResultDto<bool> result = await _connectionCatalogService.DeleteAsync(selected.Id, CancellationToken.None);
        if (!result.Success)
        {
            ApplyTestStatus(_statusPresenter.Failed(result.UserMessage));
            return;
        }

        bool wasActive = string.Equals(selected.Id, ActiveProfileId, StringComparison.Ordinal);

        await ReloadProfilesFromCatalogAsync(null);

        if (wasActive)
            ActiveProfileId = null;

        SelectedProfile = null;
        IsNewProfileFlow = false;
        WizardStep = ConnectionWizardStep.ConfigureConnection;
        IsEditing = false;
        TestStatus = string.Empty;
        ProfilesChanged?.Invoke();
    }

    private async Task ConnectAsync()
    {
        ConnectionProfile? candidate = SelectedProfile;
        if (candidate is null)
        {
            string message = L("connection.validation.saveBeforeConnect", "Save the connection before connecting.");
            ApplyTestStatus(_statusPresenter.Failed(message));
            NotifyConnectionFailed(message);
            return;
        }

        OperationResultDto<ActiveConnectionSessionDto> connectSessionResult = await _connectionSessionService.ConnectAsync(
            ConnectionContractMapper.ToDetails(candidate),
            CancellationToken.None);
        if (!connectSessionResult.Success)
        {
            ApplyTestStatus(_statusPresenter.Failed(connectSessionResult.UserMessage));
            NotifyConnectionFailed(connectSessionResult.UserMessage, connectSessionResult.TechnicalError);
            return;
        }

        ConnectionConnectState state = _sessionOrchestrator.BeginConnect(_selectedProfile, _connectCts);
        if (!state.Started || state.Profile is null)
            return;

        _connectCts = state.ConnectCts;
        IsConnecting = state.IsConnecting;
        ApplyTestStatus(_statusPresenter.Connecting());
        // Load database tables into the search menu in the background
        StartLoadDatabaseTablesSafe(state.Profile, _connectCts!.Token);
    }

    private void ConnectOrOpenManager()
    {
        LogConnectionModal("ConnectOrOpenManager invoked");
        if (IsConnecting || IsReloadingSchema)
        {
            LogConnectionModal("ConnectOrOpenManager aborted: busy");
            return;
        }

        bool beginNewProfile = Profiles.Count == 0;
        // When invoked from a canvas-local sidebar (DDL/Query), keep the flow local
        // to this manager instance and avoid double-open race with global routing.
        if (Canvas is null)
            _ = _globalModalManager.RequestConnectionManager(beginNewProfile: beginNewProfile, keepStartVisible: false);

        Open();
        LogConnectionModal("ConnectOrOpenManager after Open");

        if (Profiles.Count > 0)
        {
            if (SelectedProfile is null)
                SelectedProfile = Profiles[0];

            IsNewProfileFlow = false;
            WizardStep = ConnectionWizardStep.ConfigureConnection;
            IsEditing = false;
            TestStatus = string.Empty;
            LogConnectionModal("ConnectOrOpenManager completed with existing profiles");
            return;
        }

        BeginNewProfile();
        LogConnectionModal("ConnectOrOpenManager completed with BeginNewProfile");
    }

    private void LogConnectionModal(string message)
    {
        string line =
            $"{DateTimeOffset.Now:O} | message={message} | isVisible={IsVisible} | canvasBound={(Canvas is not null)} | profileCount={Profiles.Count} | selectedProfile={(SelectedProfile?.Id ?? "<null>")} | activeProfile={(ActiveProfileId ?? "<null>")}";
        Debug.WriteLine(line);
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, ConnectionModalLogFile);
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Best-effort diagnostics only.
        }
    }

    private async Task DisconnectAsync()
    {
        if (!string.IsNullOrWhiteSpace(_activeProfileId))
            await _connectionSessionService.DisconnectAsync(_activeProfileId, CancellationToken.None);

        ConnectionDisconnectState state = _sessionOrchestrator.BeginDisconnect(_connectCts);
        _connectCts = state.ConnectCts;
        _dbConnectionService.Cancel();
        IsConnecting = state.IsConnecting;
        ActiveProfileId = state.ActiveProfileId;
        ActiveServerVersion = null;
        ActiveLatencyMs = null;
        AvailableDatabases.Clear();
        SelectedDatabase = null;
        AvailableSchemas.Clear();
        SelectedSchema = null;
        RaisePropertyChanged(nameof(IsDatabaseSelectionVisible));
        RaisePropertyChanged(nameof(ActiveMetadataSummary));
        RaisePropertyChanged(nameof(ActiveMetadataDetails));
        RaisePropertyChanged(nameof(SchemaFilterQuery));
        DisconnectCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
        RefreshHealthCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Asynchronously loads database tables into the search menu.
    /// This executes the normal workflow after a successful database connection:
    /// 1. Fetches database schema and metadata
    /// 2. Converts to search menu format
    /// 3. Populates the search menu with available tables
    /// 4. Resets the canvas to show the new database
    /// </summary>
    private Task LoadDatabaseTablesAsync(ConnectionProfile profile)
        => LoadDatabaseTablesAsync(profile, CancellationToken.None);

    private async Task LoadDatabaseTablesAsync(ConnectionProfile profile, CancellationToken ct)
    {
        try
        {
            SearchMenuViewModel searchMenu = SearchMenu ?? new SearchMenuViewModel();
            ConnectionActivationResult result = await _activationWorkflow.ExecuteAsync(
                profile,
                searchMenu,
                Canvas,
                async (config, searchMenu, token) =>
                {
                    await _dbConnectionService.ConnectAndLoadAsync(config, searchMenu, token);
                    return _dbConnectionService.LoadedMetadata;
                },
                ct);

            switch (result.Outcome)
            {
                case ConnectionActivationOutcome.Connected:
                    if (result.ShouldOpenClearCanvasPrompt && result.Metadata is not null && result.Config is not null)
                        OpenClearCanvasPrompt(result.Metadata, result.Config);
                    else
                        CloseClearCanvasPrompt(dismissedByUser: false);

                    // Mark as active only after successful activation.
                    ActiveProfileId = profile.Id;
                    StartRefreshHealthSafe();

                    ActiveServerVersion = await _dbConnectionService.GetServerVersionAsync()
                        ?? $"{profile.Provider} @ {profile.Host}:{profile.Port}";

                    await SyncDatabaseOptionsAsync(ct);
                    SyncSchemaOptions();
                    RaisePropertyChanged(nameof(ActiveMetadataSummary));
                    RaisePropertyChanged(nameof(ActiveMetadataDetails));
                    RaisePropertyChanged(nameof(SchemaFilterQuery));

                    ApplyTestStatus(_statusPresenter.Connected());
                    IsVisible = false;
                    ConnectionActivated?.Invoke(profile);
                    break;

                case ConnectionActivationOutcome.SearchMenuUnavailable:
                    string reason = $"{result.FailureReason ?? L("connection.error.searchMenuNotInitialized", "search menu not initialized")}.";
                    ApplyTestStatus(_statusPresenter.FailedWithPrefix(reason));
                    NotifyConnectionFailed(reason);
                    break;

                case ConnectionActivationOutcome.MetadataUnavailable:
                    ApplyTestStatus(_statusPresenter.MetadataUnavailable());
                    NotifyConnectionFailed(L("connection.status.metadataUnavailable", "Metadata unavailable after connection."));
                    break;

                case ConnectionActivationOutcome.Cancelled:
                    ApplyTestStatus(_statusPresenter.Cancelled());
                    break;

                case ConnectionActivationOutcome.Failed:
                    Exception ex = result.FailureException ?? new InvalidOperationException("Unknown connection activation failure.");
                    string mappedError = _errorMessageMapper.Map(ex, profile.Provider);
                    ApplyTestStatus(_statusPresenter.FailedWithPrefix(mappedError));
                    NotifyConnectionFailed(mappedError, ex.Message);
                    _logger?.LogError(ex, "Failed to load database tables for connection {Profile}", profile.Name);
                    break;
            }
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task TestConnectionAsync()
    {
        if (!TryValidateForm(requireName: false, out string? validationError))
        {
            ApplyTestStatus(_statusPresenter.Failed(validationError));
            return;
        }

        IsTesting = true;
        ApplyTestStatus(_statusPresenter.Testing());

        try
        {
            OperationResultDto<ConnectionTestResultDto> result = await _connectionTestService.TestAsync(
                BuildConnectionDetailsFromForm(),
                CancellationToken.None);

            if (result.Success && result.Payload?.Status == ConnectionTestStatusDto.Success)
            {
                TimeSpan? latency = result.Payload.LatencyMs.HasValue
                    ? TimeSpan.FromMilliseconds(result.Payload.LatencyMs.Value)
                    : null;
                ApplyTestStatus(_statusPresenter.TestSuccess(latency, DegradedLatencyThresholdMs));
            }
            else
            {
                string? message = result.Payload?.SummaryMessage;
                if (string.IsNullOrWhiteSpace(message))
                    message = string.IsNullOrWhiteSpace(result.UserMessage)
                        ? _loc["connection.status.failedPrefix"]
                        : result.UserMessage;

                ApplyTestStatus(_statusPresenter.Failed(message));
            }
        }
        catch (Exception ex)
        {
            ApplyTestStatus(_statusPresenter.Failed(_errorMessageMapper.Map(ex, EditProvider)));
        }
        finally
        {
            IsTesting = false;
        }
    }

    // ── Background health monitor ─────────────────────────────────────────────

    private void RestartHealthMonitor()
    {
        _healthMonitorCts = _healthLifecycleCoordinator.Restart(
            _activeProfileId,
            _healthMonitorCts,
            StartHealthMonitorLoopSafe);
    }

    private void StartTestConnectionSafe() =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(TestConnectionAsync, "test connection");

    private void StartSaveProfileSafe() =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(SaveProfileAsync, "save profile");

    private void StartDeleteProfileSafe() =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(DeleteProfileAsync, "delete profile");

    private void StartImportFromUrlSafe() =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(ImportFromUrlAsync, "import connection url");

    private void StartConnectSafe() =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(ConnectAsync, "connect");

    private void StartDisconnectSafe() =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(DisconnectAsync, "disconnect");

    private void StartRefreshHealthSafe() =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(() => RunHealthCheckAsync(), "refresh health");

    private void StartReloadMetadataSafe() =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(ReloadMetadataAsync, "reload metadata");

    private void StartLoadDatabaseTablesSafe(ConnectionProfile profile, CancellationToken ct) =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(() => LoadDatabaseTablesAsync(profile, ct), "load database tables");

    private void StartHealthMonitorLoopSafe(CancellationToken token) =>
        _ = _fireAndForgetSafetyExecutor.ExecuteSafeAsync(() => HealthMonitorLoopAsync(token), "health monitor loop");

    private async Task HealthMonitorLoopAsync(CancellationToken ct)
    {
        await _healthMonitorService.HealthMonitorLoopAsync(ct, RunHealthCheckAsync);
    }

    private async Task RunHealthCheckAsync(CancellationToken ct = default)
    {
        ConnectionProfile? profile = Profiles.FirstOrDefault(x => x.Id == _activeProfileId);
        if (profile is not null)
        {
            try
            {
                ConnectionTestResult testResult = await _connectionTestExecutor.ExecuteAsync(
                    profile.ToConnectionConfig(),
                    profile.Provider,
                    profile.TimeoutSeconds,
                    ct);

                if (testResult.Success && testResult.Latency.HasValue)
                    ActiveLatencyMs = (int)testResult.Latency.Value.TotalMilliseconds;
                else if (!testResult.Success)
                    ActiveLatencyMs = null;
            }
            catch
            {
                ActiveLatencyMs = null;
            }
        }

        ActiveHealthStatus = await _healthLifecycleCoordinator.EvaluateActiveStatusAsync(
            Profiles,
            _activeProfileId,
            _connectionTestExecutor.ExecuteAsync,
            DegradedLatencyThresholdMs,
            ct);
    }

    private async Task ReloadMetadataAsync()
    {
        ConnectionProfile? activeProfile = Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        if (activeProfile is null)
            return;

        IsReloadingSchema = true;
        try
        {
            CancellationToken token = _connectCts?.Token ?? CancellationToken.None;
            await LoadDatabaseTablesAsync(activeProfile, token);
            await SyncDatabaseOptionsAsync(token);
            SyncSchemaOptions();
            RaisePropertyChanged(nameof(ActiveMetadataSummary));
            RaisePropertyChanged(nameof(ActiveMetadataDetails));
        }
        finally
        {
            IsReloadingSchema = false;
        }
    }

    private void SyncSchemaOptions()
    {
        DbMetadata? metadata = ResolveCurrentMetadata();
        AvailableSchemas.Clear();

        if (metadata is null)
        {
            SelectedSchema = null;
            return;
        }

        foreach (string schemaName in metadata.Schemas
            .Select(schema => schema.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            AvailableSchemas.Add(schemaName);
        }

        string? nextSchema = AvailableSchemas.Any(name =>
            string.Equals(name, _selectedSchema, StringComparison.OrdinalIgnoreCase))
            ? _selectedSchema
            : AvailableSchemas.FirstOrDefault();

        if (!string.Equals(_selectedSchema, nextSchema, StringComparison.OrdinalIgnoreCase))
        {
            _selectedSchema = nextSchema;
            RaisePropertyChanged(nameof(SelectedSchema));
        }

        if (Canvas?.Schema is not null)
            Canvas.Schema.SelectedSchema = _selectedSchema;
    }

    private async Task SyncDatabaseOptionsAsync(CancellationToken ct)
    {
        AvailableDatabases.Clear();

        ConnectionProfile? activeProfile = Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        if (activeProfile is null)
        {
            SelectedDatabase = null;
            RaisePropertyChanged(nameof(IsDatabaseSelectionVisible));
            return;
        }

        if (!SupportsDatabaseOrCatalogSelection)
        {
            SelectedDatabase = activeProfile.Database;
            RaisePropertyChanged(nameof(IsDatabaseSelectionVisible));
            return;
        }

        string[] databases = await _dbConnectionService.ListDatabasesAsync(ct);
        foreach (string name in databases)
        {
            if (!string.IsNullOrWhiteSpace(name))
                AvailableDatabases.Add(name);
        }

        string? nextDatabase = AvailableDatabases.Any(name =>
            string.Equals(name, _selectedDatabase, StringComparison.OrdinalIgnoreCase))
            ? _selectedDatabase
            : AvailableDatabases.FirstOrDefault(name =>
                string.Equals(name, activeProfile.Database, StringComparison.OrdinalIgnoreCase))
              ?? AvailableDatabases.FirstOrDefault();

        SelectedDatabase = nextDatabase;
        RaisePropertyChanged(nameof(IsDatabaseSelectionVisible));
    }

    private DbMetadata? ResolveCurrentMetadata() => _dbConnectionService.LoadedMetadata ?? Canvas?.DatabaseMetadata;

    private async Task SwitchDatabaseAsync(string databaseName)
    {
        IsReloadingSchema = true;
        try
        {
            await _dbConnectionService.SwitchDatabaseAsync(databaseName);

            ConnectionProfile? activeProfile = Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase));

            if (activeProfile is not null)
            {
                activeProfile.Database = databaseName;
                RaisePropertyChanged(nameof(ActiveConnectionLabel));
                RaisePropertyChanged(nameof(ConnectionHealthTooltip));

                OperationResultDto<ConnectionDetailsDto> saveResult = await _connectionCatalogService.SaveAsync(
                    ConnectionContractMapper.ToDetails(activeProfile),
                    CancellationToken.None);
                if (saveResult.Success && saveResult.Payload is not null)
                    await ReloadProfilesFromCatalogAsync(saveResult.Payload.Id);

                ProfilesChanged?.Invoke();

                CancellationToken token = _connectCts?.Token ?? CancellationToken.None;
                ConnectionProfile? updatedActiveProfile = Profiles.FirstOrDefault(p =>
                    string.Equals(p.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase));
                if (updatedActiveProfile is not null)
                    await LoadDatabaseTablesAsync(updatedActiveProfile, token);
            }

            ActiveServerVersion = await _dbConnectionService.GetServerVersionAsync()
                ?? ActiveServerVersion;
            await SyncDatabaseOptionsAsync(CancellationToken.None);
            SyncSchemaOptions();
            RaisePropertyChanged(nameof(ActiveMetadataSummary));
            RaisePropertyChanged(nameof(ActiveMetadataDetails));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch database to {Database}", databaseName);
        }
        finally
        {
            IsReloadingSchema = false;
        }
    }

    public async Task SwitchConnectionAsync(ConnectionProfile profile)
    {
        if (string.Equals(profile.Id, ActiveProfileId, StringComparison.Ordinal))
            return;

        IsReloadingSchema = true;
        try
        {
            SelectedProfile = profile;
            if (ConnectCommand.CanExecute(null))
                ConnectCommand.Execute(null);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch connection to profile {Name}", profile.Name);
        }
        finally
        {
            IsReloadingSchema = false;
        }
    }

    private string L(string key, string fallback)
    {
        string value = _loc[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private void ApplyTestStatus(ConnectionStatusViewState state)
    {
        TestStatus = state.Message;
        TestStatusColor = state.Color;
    }

    private void NotifyConnectionFailed(string message, string? details = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        ConnectionFailed?.Invoke(message, details);
    }

    private bool TryValidateForm(bool requireName, out string errorMessage)
    {
        if (requireName && string.IsNullOrWhiteSpace(EditName))
        {
            errorMessage = L("connection.validation.nameRequired", "Connection name is required.");
            return false;
        }

        if (EditTimeout <= 0)
        {
            errorMessage = L("connection.validation.timeoutInvalid", "Timeout must be greater than zero.");
            return false;
        }

        if (IsSqliteProvider)
        {
            if (string.IsNullOrWhiteSpace(EditDatabase))
            {
                errorMessage = L("connection.validation.sqlitePathRequired", "SQLite database path is required.");
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(EditHost))
        {
            errorMessage = L("connection.validation.hostRequired", "Host is required.");
            return false;
        }

        if (EditPort <= 0)
        {
            errorMessage = L("connection.validation.portInvalid", "Port must be greater than zero.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(EditDatabase))
        {
            errorMessage = L("connection.validation.databaseRequired", "Database is required.");
            return false;
        }

        if (!EditUseIntegratedSecurity && string.IsNullOrWhiteSpace(EditUsername))
        {
            errorMessage = L("connection.validation.usernameRequired", "Username is required.");
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private async Task ImportFromUrlAsync()
    {
        ConnectionUrlParseResultDto parse = await _connectionUrlParserService.ParseAsync(
            EditConnectionUrl,
            EditProvider.ToString(),
            CancellationToken.None);

        if (parse.ParseStatus == ConnectionUrlParseStatusDto.Failed)
        {
            ApplyTestStatus(_statusPresenter.Failed(parse.UserMessage));
            return;
        }

        if (!string.IsNullOrWhiteSpace(parse.SuggestedProvider)
            && ConnectionContractMapper.TryParseProvider(parse.SuggestedProvider, out DatabaseProvider parsedProvider))
        {
            EditProvider = parsedProvider;
        }

        Dictionary<string, string?> recognized = parse.RecognizedFields
            .ToDictionary(token => token.Key, token => token.Value, StringComparer.OrdinalIgnoreCase);

        EditHost = GetToken(recognized, ConnectionContractMapper.HostKey, EditHost);
        EditPort = GetIntToken(recognized, ConnectionContractMapper.PortKey, EditPort);
        EditDatabase = GetToken(recognized, ConnectionContractMapper.DatabaseKey, EditDatabase);
        EditUsername = GetToken(recognized, ConnectionContractMapper.UsernameKey, EditUsername);
        EditPassword = GetToken(recognized, ConnectionContractMapper.PasswordKey, EditPassword);
        EditUseSsl = GetBoolToken(recognized, ConnectionContractMapper.UseSslKey, EditUseSsl);
        EditTrustServerCertificate = GetBoolToken(recognized, ConnectionContractMapper.TrustServerCertificateKey, EditTrustServerCertificate);
        EditUseIntegratedSecurity = GetBoolToken(recognized, ConnectionContractMapper.UseIntegratedSecurityKey, EditUseIntegratedSecurity);

        if (string.IsNullOrWhiteSpace(EditName))
            EditName = string.IsNullOrWhiteSpace(EditDatabase) ? EditHost : EditDatabase;

        bool partial = parse.ParseStatus == ConnectionUrlParseStatusDto.Partial;
        string message = string.IsNullOrWhiteSpace(parse.UserMessage)
            ? L("connection.status.urlImported", "Connection URL imported successfully.")
            : parse.UserMessage;

        ApplyTestStatus(new ConnectionStatusViewState(
            message,
            partial ? UiColorConstants.C_FBBF24 : UiColorConstants.C_4ADE80));
    }

    private ConnectionProfileFormData CaptureFormData() =>
        new(
            Id: _editId,
            Name: EditName,
            Provider: EditProvider,
            Host: EditHost,
            Port: EditPort,
            Database: EditDatabase,
            Username: EditUsername,
            Password: EditPassword,
            RememberPassword: EditRememberPassword,
            UseSsl: EditUseSsl,
            TrustServerCertificate: EditTrustServerCertificate,
            UseIntegratedSecurity: EditUseIntegratedSecurity,
            TimeoutSeconds: EditTimeout,
            ConnectionUrl: EditConnectionUrl);

    private void ApplyFormData(ConnectionProfileFormData formData)
    {
        _editId = formData.Id;
        EditName = formData.Name;
        EditProvider = formData.Provider;
        EditHost = formData.Host;
        EditPort = formData.Port;
        EditDatabase = formData.Database;
        EditUsername = formData.Username;
        EditPassword = formData.Password;
        EditRememberPassword = formData.RememberPassword;
        EditUseSsl = formData.UseSsl;
        EditTrustServerCertificate = formData.TrustServerCertificate;
        EditUseIntegratedSecurity = formData.UseIntegratedSecurity;
        EditTimeout = formData.TimeoutSeconds;
        EditConnectionUrl = formData.ConnectionUrl;
    }

    private void OpenClearCanvasPrompt(DbMetadata metadata, ConnectionConfig config)
    {
        ConnectionCanvasPromptState state = _canvasPromptCoordinator.Open(metadata, config);
        _pendingLoadedMetadata = state.PendingMetadata;
        _pendingLoadedConfig = state.PendingConfig;
        IsClearCanvasPromptVisible = state.IsVisible;
    }

    private void CloseClearCanvasPrompt(bool dismissedByUser)
    {
        bool shouldAddWarning = _canvasPromptCoordinator.ShouldAddDismissWarning(
            dismissedByUser,
            IsClearCanvasPromptVisible,
            _pendingLoadedMetadata);
        if (shouldAddWarning && Canvas is not null)
        {
            Canvas.Diagnostics.AddWarning(
                area: L("diagnostics.area.connection", "Connection"),
                message: L("connection.warning.canvasMayContainOldTables", "The canvas may still contain tables from a previous connection."),
                recommendation: L("connection.warning.canvasMayContainOldTablesRecommendation", "Clear the canvas manually or reconnect and choose keep/clear again."),
                openPanel: false
            );
        }

        ConnectionCanvasPromptState state = _canvasPromptCoordinator.Close();
        IsClearCanvasPromptVisible = state.IsVisible;
        _pendingLoadedMetadata = state.PendingMetadata;
        _pendingLoadedConfig = state.PendingConfig;
    }

    private void ClearCanvasAfterConnect()
    {
        if (Canvas is not null && _pendingLoadedMetadata is not null)
            Canvas.SetDatabaseAndResetCanvas(_pendingLoadedMetadata, _pendingLoadedConfig);

        CloseClearCanvasPrompt(dismissedByUser: false);
    }

    private void KeepCanvasAfterConnect()
    {
        CloseClearCanvasPrompt(dismissedByUser: false);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadProfiles()
    {
        Profiles.Clear();
        IReadOnlyList<ConnectionProfile> loaded = LoadProfilesFromCatalogSync();
        foreach (ConnectionProfile profile in loaded)
        {
            Profiles.Add(profile);
        }

        RaisePropertyChanged(nameof(SidebarSelectedConnection));
        RaisePropertyChanged(nameof(SidebarConnectionName));
        RaisePropertyChanged(nameof(SidebarConnectionSubtitle));
        ProfilesChanged?.Invoke();
    }

    private async Task ReloadProfilesFromCatalogAsync(string? selectedProfileId)
    {
        IReadOnlyList<ConnectionSummaryDto> summaries = await _connectionCatalogService.ListSummariesAsync(CancellationToken.None);
        var loaded = new List<ConnectionProfile>(summaries.Count);

        foreach (ConnectionSummaryDto summary in summaries)
        {
            OperationResultDto<ConnectionDetailsDto> details = await _connectionCatalogService.GetDetailsAsync(
                summary.Id,
                CancellationToken.None);

            if (!details.Success || details.Payload is null)
                continue;

            loaded.Add(ConnectionContractMapper.ToProfile(details.Payload));
        }

        Profiles.Clear();
        foreach (ConnectionProfile profile in loaded)
            Profiles.Add(profile);

        SelectedProfile = string.IsNullOrWhiteSpace(selectedProfileId)
            ? null
            : Profiles.FirstOrDefault(p => string.Equals(p.Id, selectedProfileId, StringComparison.Ordinal));

        RaisePropertyChanged(nameof(SidebarSelectedConnection));
        RaisePropertyChanged(nameof(SidebarConnectionName));
        RaisePropertyChanged(nameof(SidebarConnectionSubtitle));
    }

    private static string GetToken(IReadOnlyDictionary<string, string?> tokens, string key, string fallback)
    {
        return tokens.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static int GetIntToken(IReadOnlyDictionary<string, string?> tokens, string key, int fallback)
    {
        return tokens.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
            ? parsed
            : fallback;
    }

    private static bool GetBoolToken(IReadOnlyDictionary<string, string?> tokens, string key, bool fallback)
    {
        if (!tokens.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            return fallback;

        if (bool.TryParse(value, out bool parsed))
            return parsed;

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("required", StringComparison.OrdinalIgnoreCase)
            || value.Equals("require", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<ConnectionProfile> LoadProfilesFromCatalogSync()
    {
        try
        {
            IReadOnlyList<ConnectionSummaryDto> summaries = _connectionCatalogService
                .ListSummariesAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (summaries.Count == 0)
                return [];

            var loaded = new List<ConnectionProfile>(summaries.Count);
            foreach (ConnectionSummaryDto summary in summaries)
            {
                OperationResultDto<ConnectionDetailsDto> details = _connectionCatalogService
                    .GetDetailsAsync(summary.Id, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                if (!details.Success || details.Payload is null)
                    continue;

                loaded.Add(ConnectionContractMapper.ToProfile(details.Payload));
            }

            return loaded;
        }
        catch
        {
            return [];
        }
    }

    private ConnectionDetailsDto BuildConnectionDetailsFromForm()
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ConnectionContractMapper.HostKey] = EditHost,
            [ConnectionContractMapper.PortKey] = EditPort.ToString(),
            [ConnectionContractMapper.DatabaseKey] = EditDatabase,
            [ConnectionContractMapper.UsernameKey] = EditUsername,
            [ConnectionContractMapper.PasswordKey] = EditPassword,
            [ConnectionContractMapper.RememberPasswordKey] = EditRememberPassword.ToString(),
            [ConnectionContractMapper.UseSslKey] = EditUseSsl.ToString(),
            [ConnectionContractMapper.TrustServerCertificateKey] = EditTrustServerCertificate.ToString(),
            [ConnectionContractMapper.UseIntegratedSecurityKey] = EditUseIntegratedSecurity.ToString(),
            [ConnectionContractMapper.TimeoutSecondsKey] = EditTimeout.ToString(),
        };

        ConnectionProviderModeDto mode = string.IsNullOrWhiteSpace(EditConnectionUrl)
            ? ConnectionProviderModeDto.Fields
            : ConnectionProviderModeDto.Url;

        return new ConnectionDetailsDto(
            Id: _editId,
            Name: EditName,
            Provider: EditProvider.ToString(),
            Mode: mode,
            FieldValues: fields,
            UrlValue: string.IsNullOrWhiteSpace(EditConnectionUrl) ? null : EditConnectionUrl,
            Tag: null,
            IsFavorite: false,
            AdvancedOptions: new Dictionary<string, string?>());
    }

    public sealed class ProviderOption(
        DatabaseProvider provider,
        string displayName,
        MaterialIconKind iconKind,
        string svgAssetUri) : ViewModelBase
    {
        private static SvgImage? CreateSvgImageSource(string assetUri)
        {
            try
            {
                return new SvgImage
                {
                    Source = SvgSource.Load(assetUri, baseUri: null),
                };
            }
            catch
            {
                return null;
            }
        }

        public DatabaseProvider Provider { get; } = provider;
        public string DisplayName { get; } = displayName;
        public MaterialIconKind IconKind { get; } = iconKind;
        public string SvgAssetUri { get; } = svgAssetUri;
        public SvgImage? SvgImageSource { get; } = CreateSvgImageSource(svgAssetUri);

        private bool _isSelectedWizard;
        public bool IsSelectedWizard
        {
            get => _isSelectedWizard;
            set => Set(ref _isSelectedWizard, value);
        }
    }
}
