using AkkornStudio.Core;

namespace AkkornStudio.UI.Services.ConnectionManager;

public sealed class ConnectionHealthMonitorService : IConnectionHealthMonitorService
{
    public CancellationTokenSource? Restart(
        string? activeProfileId,
        CancellationTokenSource? existing,
        Action<CancellationToken> startLoop)
    {
        existing?.Cancel();
        existing?.Dispose();

        if (activeProfileId is null)
            return null;

        var cts = new CancellationTokenSource();
        startLoop(cts.Token);
        return cts;
    }

    public async Task HealthMonitorLoopAsync(CancellationToken ct, Func<CancellationToken, Task> runHealthCheckAsync)
    {
        await HealthMonitorLoopAsync(
            ct,
            runHealthCheckAsync,
            static (delay, token) => Task.Delay(delay, token));
    }

    internal async Task HealthMonitorLoopAsync(
        CancellationToken ct,
        Func<CancellationToken, Task> runHealthCheckAsync,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await delayAsync(TimeSpan.FromSeconds(AppConstants.HealthCheckIntervalSeconds), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }

            if (ct.IsCancellationRequested)
                break;

            try
            {
                await runHealthCheckAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Keep monitoring loop alive on transient health-check failures.
            }
        }
    }

    public async Task<ConnectionHealthStatus> EvaluateStatusAsync(
        ConnectionProfile? profile,
        Func<ConnectionConfig, DatabaseProvider, int, CancellationToken, Task<ConnectionTestResult>> runTestAsync,
        double degradedLatencyThresholdMs,
        CancellationToken ct = default)
    {
        if (profile is null)
            return ConnectionHealthStatus.Unknown;

        try
        {
            ConnectionTestResult result = await runTestAsync(
                profile.ToConnectionConfig(),
                profile.Provider,
                profile.TimeoutSeconds,
                ct);

            if (!result.Success)
                return ConnectionHealthStatus.Offline;

            if (result.Latency is null)
                return ConnectionHealthStatus.Offline;

            double ms = result.Latency.Value.TotalMilliseconds;
            return ms >= degradedLatencyThresholdMs
                ? ConnectionHealthStatus.Degraded
                : ConnectionHealthStatus.Online;
        }
        catch (OperationCanceledException)
        {
            return ct.IsCancellationRequested
                ? ConnectionHealthStatus.Unknown
                : ConnectionHealthStatus.Offline;
        }
        catch
        {
            return ConnectionHealthStatus.Offline;
        }
    }
}
