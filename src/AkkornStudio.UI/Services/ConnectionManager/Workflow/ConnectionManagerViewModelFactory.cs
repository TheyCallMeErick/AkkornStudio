using AkkornStudio.UI.Services.ConnectionManager.Contracts;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Modal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AkkornStudio.UI.Services.ConnectionManager;

public sealed class ConnectionManagerViewModelFactory : IConnectionManagerViewModelFactory
{
    private readonly IConnectionErrorMessageMapper _errorMessageMapper;
    private readonly IConnectionStatusPresenter _statusPresenter;
    private readonly IConnectionCanvasPromptCoordinator _canvasPromptCoordinator;
    private readonly IConnectionHealthMonitorService _healthMonitorService;
    private readonly IConnectionSessionOrchestrator _sessionOrchestrator;
    private readonly IConnectionProfileStore _profileStore;
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
    private readonly IConnectionProfilesChangedNotifier _profilesChangedNotifier;

    public static IConnectionManagerViewModelFactory CreateDefault(
        ILocalizationService? localizationService = null,
        ILogger<ConnectionManagerViewModel>? connectionManagerLogger = null)
    {
        ILocalizationService localization = localizationService ?? LocalizationService.Instance;

        var errorMessageMapper = new ConnectionErrorMessageMapper(localization);
        var statusPresenter = new ConnectionStatusPresenter(localization);
        var canvasPromptCoordinator = new ConnectionCanvasPromptCoordinator();
        var healthMonitorService = new ConnectionHealthMonitorService();
        var sessionOrchestrator = new ConnectionSessionOrchestrator();
        var profileStore = new ConnectionProfileStore();
        var connectionTestExecutor = new DbOrchestratorConnectionTestExecutor();
        var profilesChangedNotifier = new ConnectionProfilesChangedNotifier();
        var connectionCatalogService = new ConnectionCatalogService(profileStore, profilesChangedNotifier);
        var connectionValidationService = new ConnectionValidationService();
        var providerCapabilityService = new ProviderCapabilityService();
        var connectionUrlParserService = new ConnectionUrlParserService();
        var connectionTestService = new ConnectionTestService(
            connectionTestExecutor,
            connectionValidationService,
            providerCapabilityService,
            connectionUrlParserService);
        var connectionTelemetryService = new ConnectionTelemetryService(NullLogger<ConnectionTelemetryService>.Instance);
        var connectionSessionService = new ConnectionSessionService(connectionTestService, connectionTelemetryService);
        var formMapper = new ConnectionProfileFormMapper(localization);
        var activationWorkflow = new ConnectionActivationWorkflow();
        var fireAndForgetSafetyExecutor = new FireAndForgetSafetyExecutor(
            connectionManagerLogger ?? NullLogger<ConnectionManagerViewModel>.Instance);
        var healthLifecycleCoordinator = new ConnectionHealthLifecycleCoordinator(healthMonitorService);

        return new ConnectionManagerViewModelFactory(
            errorMessageMapper,
            statusPresenter,
            canvasPromptCoordinator,
            healthMonitorService,
            sessionOrchestrator,
            profileStore,
            connectionTestExecutor,
            connectionCatalogService,
            connectionTestService,
            connectionSessionService,
            connectionUrlParserService,
            formMapper,
            activationWorkflow,
            fireAndForgetSafetyExecutor,
            healthLifecycleCoordinator,
            GlobalModalManager.Instance,
            profilesChangedNotifier);
    }

    public ConnectionManagerViewModelFactory(
        IConnectionErrorMessageMapper errorMessageMapper,
        IConnectionStatusPresenter statusPresenter,
        IConnectionCanvasPromptCoordinator canvasPromptCoordinator,
        IConnectionHealthMonitorService healthMonitorService,
        IConnectionSessionOrchestrator sessionOrchestrator,
        IConnectionProfileStore profileStore,
        IConnectionTestExecutor connectionTestExecutor,
        IConnectionCatalogService connectionCatalogService,
        IConnectionTestService connectionTestService,
        IConnectionSessionService connectionSessionService,
        IConnectionUrlParserService connectionUrlParserService,
        IConnectionProfileFormMapper formMapper,
        IConnectionActivationWorkflow activationWorkflow,
        IFireAndForgetSafetyExecutor fireAndForgetSafetyExecutor,
        IConnectionHealthLifecycleCoordinator healthLifecycleCoordinator,
        IGlobalModalManager globalModalManager,
        IConnectionProfilesChangedNotifier profilesChangedNotifier)
    {
        _errorMessageMapper = errorMessageMapper;
        _statusPresenter = statusPresenter;
        _canvasPromptCoordinator = canvasPromptCoordinator;
        _healthMonitorService = healthMonitorService;
        _sessionOrchestrator = sessionOrchestrator;
        _profileStore = profileStore;
        _connectionTestExecutor = connectionTestExecutor;
        _connectionCatalogService = connectionCatalogService;
        _connectionTestService = connectionTestService;
        _connectionSessionService = connectionSessionService;
        _connectionUrlParserService = connectionUrlParserService;
        _formMapper = formMapper;
        _activationWorkflow = activationWorkflow;
        _fireAndForgetSafetyExecutor = fireAndForgetSafetyExecutor;
        _healthLifecycleCoordinator = healthLifecycleCoordinator;
        _globalModalManager = globalModalManager;
        _profilesChangedNotifier = profilesChangedNotifier;
    }

    public ConnectionManagerViewModel Create()
    {
        return new ConnectionManagerViewModel(
            errorMessageMapper: _errorMessageMapper,
            statusPresenter: _statusPresenter,
            canvasPromptCoordinator: _canvasPromptCoordinator,
            healthMonitorService: _healthMonitorService,
            sessionOrchestrator: _sessionOrchestrator,
            profileStore: _profileStore,
            connectionTestExecutor: _connectionTestExecutor,
            connectionCatalogService: _connectionCatalogService,
            connectionTestService: _connectionTestService,
            connectionSessionService: _connectionSessionService,
            connectionUrlParserService: _connectionUrlParserService,
            formMapper: _formMapper,
            activationWorkflow: _activationWorkflow,
            fireAndForgetSafetyExecutor: _fireAndForgetSafetyExecutor,
            healthLifecycleCoordinator: _healthLifecycleCoordinator,
            globalModalManager: _globalModalManager,
            profilesChangedNotifier: _profilesChangedNotifier);
    }
}
