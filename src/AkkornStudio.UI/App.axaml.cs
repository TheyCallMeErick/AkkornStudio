using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.ConnectionManager.Contracts;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Modal;
using AkkornStudio.UI.Services.Observability;
using AkkornStudio.UI.Services.Settings;
using AkkornStudio.UI.Services.Theming;
using AkkornStudio.UI.ViewModels.Validation.Conventions;
using AkkornStudio.UI.ViewModels.Validation.Conventions.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace AkkornStudio.UI;

public partial class App : Application
{
    private static readonly ILogger<App> _logger = NullLogger<App>.Instance;
    private IServiceProvider? _services;
    private static int _exceptionHandlersWired;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        WireGlobalExceptionHandlers();
        ApplySavedThemeVariant();
        ApplyUserThemeIfPresent();

        _services = BuildServices();
        ICriticalFlowTelemetryService telemetry = _services.GetRequiredService<ICriticalFlowTelemetryService>();
        telemetry.Track(
            flowId: "CF-01-open-app-load-project",
            step: "app_bootstrap",
            outcome: "ok",
            properties: new Dictionary<string, object?>
            {
                ["app"] = AppConstants.AppDisplayName,
                ["version"] = AppConstants.AppVersion,
            });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();

        base.OnFrameworkInitializationCompleted();
    }

    private static void WireGlobalExceptionHandlers()
    {
        if (Interlocked.Exchange(ref _exceptionHandlersWired, 1) == 1)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            HandleFatalException(e.ExceptionObject as Exception, "appdomain_unhandled", e.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            HandleFatalException(e.Exception, "task_unobserved", isTerminating: false);
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            HandleFatalException(e.Exception, "ui_unhandled", isTerminating: false);
            e.Handled = true;
        };
    }

    private static void HandleFatalException(Exception? ex, string source, bool isTerminating)
    {
        try
        {
            string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseDirectory))
                baseDirectory = AppContext.BaseDirectory;

            string crashDirectory = Path.Combine(baseDirectory, "AkkornStudio", "crash");
            Directory.CreateDirectory(crashDirectory);

            string logPath = Path.Combine(crashDirectory, $"crash-{DateTime.UtcNow:yyyy-MM-dd}.log");
            string body = ex is null
                ? "<null exception>"
                : ex.ToString();
            string line = $"{DateTime.UtcNow:O} | source={source} | terminating={isTerminating}\n{body}\n\n";
            File.AppendAllText(logPath, line);
        }
        catch
        {
            // Best-effort only.
        }

        if (ex is null)
        {
            _logger.LogError(
                "Unhandled exception with no exception object (source={Source}, terminating={IsTerminating})",
                source,
                isTerminating);
            return;
        }

        _logger.LogError(ex, "Unhandled exception (source={Source}, terminating={IsTerminating})", source, isTerminating);
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddAkkornStudio();
        services.AddSingleton<ILocalizationService>(_ => LocalizationService.Instance);
        services.AddSingleton<IConnectionErrorMessageMapper, ConnectionErrorMessageMapper>();
        services.AddSingleton<IConnectionStatusPresenter, ConnectionStatusPresenter>();
        services.AddSingleton<IConnectionCanvasPromptCoordinator, ConnectionCanvasPromptCoordinator>();
        services.AddSingleton<IConnectionHealthMonitorService, ConnectionHealthMonitorService>();
        services.AddSingleton<IConnectionSessionOrchestrator, ConnectionSessionOrchestrator>();
        services.AddSingleton<IConnectionProfileStore, ConnectionProfileStore>();
        services.AddSingleton<IConnectionProfileFormMapper, ConnectionProfileFormMapper>();
        services.AddSingleton<IConnectionActivationWorkflow, ConnectionActivationWorkflow>();
        services.AddSingleton<IFireAndForgetSafetyExecutor>(sp =>
            new FireAndForgetSafetyExecutor(sp.GetRequiredService<ILogger<ConnectionManagerViewModel>>()));
        services.AddSingleton<IConnectionHealthLifecycleCoordinator>(sp =>
            new ConnectionHealthLifecycleCoordinator(sp.GetRequiredService<IConnectionHealthMonitorService>()));
        services.AddSingleton<IConnectionProfilesChangedNotifier, ConnectionProfilesChangedNotifier>();
        services.AddSingleton<IConnectionCatalogService, ConnectionCatalogService>();
        services.AddSingleton<IConnectionValidationService, ConnectionValidationService>();
        services.AddSingleton<IConnectionUrlParserService, ConnectionUrlParserService>();
        services.AddSingleton<IProviderCapabilityService, ProviderCapabilityService>();
        services.AddSingleton<IConnectionTelemetryService, ConnectionTelemetryService>();
        services.AddSingleton<IConnectionTestExecutor, DbOrchestratorConnectionTestExecutor>();
        services.AddSingleton<IConnectionTestService, ConnectionTestService>();
        services.AddSingleton<IConnectionSessionService, ConnectionSessionService>();
        services.AddSingleton<IConnectionManagerViewModelFactory, ConnectionManagerViewModelFactory>();
        services.AddSingleton<ICriticalFlowTelemetryService, LocalCriticalFlowTelemetryService>();
        services.AddSingleton<ICriticalFlowBaselineReportService, LocalCriticalFlowBaselineReportService>();
        services.AddSingleton<ICriticalFlowRegressionAlertService, CriticalFlowRegressionAlertService>();
        services.AddSingleton<IAliasConvention, SnakeCaseConvention>();
        services.AddSingleton<IAliasConvention, CamelCaseConvention>();
        services.AddSingleton<IAliasConvention, PascalCaseConvention>();
        services.AddSingleton<IAliasConvention, ScreamingSnakeCaseConvention>();
        services.AddSingleton<IAliasConventionRegistry, AliasConventionRegistry>();
        services.AddSingleton<IGlobalModalManager>(_ => GlobalModalManager.Instance);
        services.AddSingleton<ThemeJsonSettingsService>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    private static void ApplySavedThemeVariant()
    {
        if (Application.Current is null)
            return;

        AppSettings settings = AppSettingsStore.Load();
        Application.Current.RequestedThemeVariant =
            settings.ThemeVariant.Equals("Light", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
    }

    private static void ApplyUserThemeIfPresent()
    {
        string path = ThemeLoader.GetDefaultThemePath();
        ThemeLoadResult load = ThemeLoader.LoadFromPath(path);
        if (load.Status == ThemeLoadStatus.NotFound)
            return;

        if (load.Status != ThemeLoadStatus.Loaded || load.Config is null)
        {
            _logger.LogWarning("Theme fallback: {Status} - {Message}", load.Status, load.Message);
            return;
        }

        ThemeValidationResult validation = ThemeValidator.Validate(load.Config);
        foreach (string error in validation.Errors)
            _logger.LogError("Theme validation error: {Error}", error);
        foreach (string warning in validation.Warnings)
            _logger.LogWarning("Theme validation warning: {Warning}", warning);

        if (!validation.IsValid)
        {
            _logger.LogWarning("Theme fallback: invalid configuration");
            return;
        }

        ThemeTokenMapResult mapped = ThemeTokenMapper.Map(load.Config);
        foreach (string warning in mapped.Warnings)
            _logger.LogWarning("Theme mapping warning: {Warning}", warning);

        int applied = ThemeRuntimeApplier.ApplyToCurrentApplication(mapped.TokenOverrides);
        _logger.LogInformation("Theme loaded: applied {AppliedCount} token override(s) from {Path}", applied, path);
    }
}

// ── Program entry point ───────────────────────────────────────────────────────

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
