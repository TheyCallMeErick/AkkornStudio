using AkkornStudio.Core;
using AkkornStudio.QueryEngine;
using AkkornStudio.Registry;

namespace AkkornStudio;

/// <summary>
/// Holds the live orchestrator, function registry and query builder for the
/// currently active connection in the canvas session.
///
/// Swap <see cref="SwitchAsync"/> when the user changes data-source nodes.
/// </summary>
public sealed class ActiveConnectionContext : IAsyncDisposable
{
    private const string QueryBuilderBootstrapFromTable = "__bootstrap__";
    private const int MaxTransientConnectionValidationAttempts = 3;

    private IDbOrchestrator? _orchestrator;
    private ConnectionConfig? _config;
    private readonly IProviderRegistry _providerRegistry;
    private readonly IDbOrchestratorFactory _orchestratorFactory;
    private readonly Func<int, TimeSpan> _retryDelayFactory;

    public ActiveConnectionContext(
        IProviderRegistry providerRegistry,
        IDbOrchestratorFactory orchestratorFactory,
        Func<int, TimeSpan>? retryDelayFactory = null
    )
    {
        ArgumentNullException.ThrowIfNull(providerRegistry);
        ArgumentNullException.ThrowIfNull(orchestratorFactory);
        _providerRegistry = providerRegistry;
        _orchestratorFactory = orchestratorFactory;
        _retryDelayFactory = retryDelayFactory ?? DefaultRetryDelay;
    }

    public IDbOrchestrator Orchestrator =>
        _orchestrator
        ?? throw new InvalidOperationException("No active connection. Call SwitchAsync() first.");

    public ISqlFunctionRegistry FunctionRegistry { get; private set; } =
        new SqlFunctionRegistry(DatabaseProvider.Postgres); // safe default

    public QueryBuilderService QueryBuilder { get; private set; } = QueryBuilderService.Create(
        DatabaseProvider.Postgres,
        QueryBuilderBootstrapFromTable
    );

    public DatabaseProvider Provider => _orchestrator?.Provider ?? DatabaseProvider.Postgres;

    public ConnectionConfig? Config => _config;

    /// <summary>
    /// Replaces the active connection.  Disposes the previous orchestrator
    /// gracefully before switching.
    /// </summary>
    public async Task SwitchAsync(ConnectionConfig config, CancellationToken ct = default)
    {
        if (_orchestrator is not null)
            await _orchestrator.DisposeAsync();

        _config = config;
        _orchestrator = _orchestratorFactory.Create(config);

        // Use IProviderRegistry to create components with all dependencies
        FunctionRegistry = _providerRegistry.CreateFunctionRegistry(config.Provider);
        QueryBuilder = _providerRegistry.CreateQueryBuilder(
            config.Provider,
            QueryBuilderBootstrapFromTable
        );

        // Eagerly validate so the canvas shows a connection error immediately
        ConnectionTestResult test = await ValidateConnectionWithRetryAsync(_orchestrator, ct);
        if (!test.Success)
            throw new InvalidOperationException($"Connection failed after validation retries: {test.ErrorMessage}");
    }

    private async Task<ConnectionTestResult> ValidateConnectionWithRetryAsync(
        IDbOrchestrator orchestrator,
        CancellationToken ct
    )
    {
        ConnectionTestResult lastResult = new(
            Success: false,
            ErrorMessage: "Connection validation did not return a result."
        );

        for (int attempt = 1; attempt <= MaxTransientConnectionValidationAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            lastResult = await orchestrator.TestConnectionAsync(ct);

            if (lastResult.Success)
                return lastResult;

            bool shouldRetryTransientFailure =
                attempt < MaxTransientConnectionValidationAttempts
                && IsLikelyTransientConnectionFailure(lastResult.ErrorMessage);
            if (!shouldRetryTransientFailure)
                return lastResult;

            TimeSpan delay = _retryDelayFactory(attempt);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
        }

        return lastResult;
    }

    private static bool IsLikelyTransientConnectionFailure(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;

        string normalized = errorMessage.ToLowerInvariant();
        return normalized.Contains("timeout", StringComparison.Ordinal)
            || normalized.Contains("timed out", StringComparison.Ordinal)
            || normalized.Contains("temporary", StringComparison.Ordinal)
            || normalized.Contains("transient", StringComparison.Ordinal)
            || normalized.Contains("network", StringComparison.Ordinal)
            || normalized.Contains("unable to connect", StringComparison.Ordinal)
            || normalized.Contains("could not connect", StringComparison.Ordinal)
            || normalized.Contains("connection reset", StringComparison.Ordinal)
            || normalized.Contains("connection refused", StringComparison.Ordinal)
            || normalized.Contains("transport-level error", StringComparison.Ordinal)
            || normalized.Contains("broken pipe", StringComparison.Ordinal);
    }

    private static TimeSpan DefaultRetryDelay(int attempt)
    {
        int safeAttempt = Math.Max(attempt, 1);
        return TimeSpan.FromMilliseconds(200 * safeAttempt);
    }

    public async ValueTask DisposeAsync()
    {
        if (_orchestrator is not null)
            await _orchestrator.DisposeAsync();
    }
}
