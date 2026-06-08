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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AkkornStudio.UI;

public partial class App : Application
{
    private static readonly ILoggerFactory _startupLoggerFactory = new StartupLoggerFactory();
    private static readonly ILogger<App> _logger = _startupLoggerFactory.CreateLogger<App>();
    private IServiceProvider? _services;
    private static int _exceptionHandlersWired;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        WireGlobalExceptionHandlers();
        ApplySavedThemeVariant();
        ApplyUserThemeIfPresent();

        IServiceProvider services;
        try
        {
            services = BuildServices();
        }
        catch (Exception ex)
        {
            HandleFatalException(ex, "startup_build_services", isTerminating: true);
            throw new InvalidOperationException("Application startup failed while building service provider.", ex);
        }

        if (!TryValidateCriticalStartupServices(services, out string validationError))
        {
            var ex = new InvalidOperationException(validationError);
            HandleFatalException(ex, "startup_validate_services", isTerminating: true);
            throw ex;
        }

        _services = services;
        TryTrackStartupTelemetry(_services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                desktop.MainWindow = _services.GetRequiredService<MainWindow>();
            }
            catch (Exception ex)
            {
                HandleFatalException(ex, "startup_main_window", isTerminating: true);
                throw new InvalidOperationException("Application startup failed while creating main window.", ex);
            }
        }

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

        services.AddAkkornStudio();
        services.AddSingleton<ILoggerFactory>(_ => _startupLoggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(StartupGenericLogger<>));
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

    private static bool TryValidateCriticalStartupServices(IServiceProvider services, out string error)
    {
        error = string.Empty;
        if (services.GetService<IServiceProviderIsService>() is not IServiceProviderIsService validator)
        {
            error = "Startup service provider does not expose IServiceProviderIsService for validation.";
            return false;
        }

        Type[] requiredTypes =
        [
            typeof(ICriticalFlowTelemetryService),
            typeof(MainWindow),
        ];

        List<string> missing = requiredTypes
            .Where(type => !validator.IsService(type))
            .Select(type => type.Name)
            .ToList();

        if (missing.Count == 0)
            return true;

        error = $"Startup service provider is missing required service(s): {string.Join(", ", missing)}.";
        return false;
    }

    private static void TryTrackStartupTelemetry(IServiceProvider services)
    {
        try
        {
            ICriticalFlowTelemetryService? telemetry = services.GetService<ICriticalFlowTelemetryService>();
            if (telemetry is null)
            {
                _logger.LogWarning("Startup telemetry service is not available; skipping app bootstrap telemetry event.");
                return;
            }

            telemetry.Track(
                flowId: "CF-01-open-app-load-project",
                step: "app_bootstrap",
                outcome: "ok",
                properties: new Dictionary<string, object?>
                {
                    ["app"] = AppConstants.AppDisplayName,
                    ["version"] = AppConstants.AppVersion,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup telemetry dispatch failed.");
        }
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

internal sealed class StartupTraceLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new StartupTraceLogger(categoryName);

    public void Dispose()
    {
    }
}

internal sealed class StartupLoggerFactory : ILoggerFactory
{
    private readonly StartupTraceLoggerProvider _provider = new();

    public void AddProvider(ILoggerProvider provider)
    {
        // Startup logger factory keeps a fixed provider set by design.
    }

    public ILogger CreateLogger(string categoryName) => _provider.CreateLogger(categoryName);

    public void Dispose()
    {
        _provider.Dispose();
    }
}

internal sealed class StartupGenericLogger<T> : ILogger<T>
{
    private readonly ILogger _inner;

    public StartupGenericLogger(ILoggerFactory factory)
    {
        _inner = factory.CreateLogger(typeof(T).FullName ?? typeof(T).Name);
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
        _inner.BeginScope(state) ?? StartupNoopScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}

internal sealed class StartupTraceLogger : ILogger
{
    private readonly string _categoryName;

    public StartupTraceLogger(string categoryName)
    {
        _categoryName = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => StartupNoopScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        string message = formatter(state, exception);
        string line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {_categoryName}: {message}";
        if (exception is not null)
            line += $"{Environment.NewLine}{exception}";

        try
        {
            Trace.WriteLine(line);
        }
        catch
        {
            // Best-effort logging only.
        }

        try
        {
            Console.Error.WriteLine(line);
        }
        catch
        {
            // Best-effort logging only.
        }
    }
}

internal sealed class StartupNoopScope : IDisposable
{
    public static StartupNoopScope Instance { get; } = new();

    private StartupNoopScope()
    {
    }

    public void Dispose()
    {
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
