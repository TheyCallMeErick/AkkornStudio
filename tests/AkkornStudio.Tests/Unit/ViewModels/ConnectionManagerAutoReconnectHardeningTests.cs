using System.Reflection;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.ConnectionManager;

namespace AkkornStudio.Tests.Unit.ViewModels;

public sealed class ConnectionManagerAutoReconnectHardeningTests
{
    [Fact]
    public async Task RunHealthCheckAsync_WhenStatusTurnsOffline_TriggersSingleAutoReconnectAttempt()
    {
        var activationWorkflow = new RecordingActivationWorkflow();
        var healthLifecycle = new StaticHealthLifecycleCoordinator(ConnectionHealthStatus.Offline);
        var connectionTestExecutor = new StaticConnectionTestExecutor(
            new ConnectionTestResult(Success: false, ErrorMessage: "connection dropped"));

        var vm = new ConnectionManagerViewModel(
            activationWorkflow: activationWorkflow,
            healthLifecycleCoordinator: healthLifecycle,
            connectionTestExecutor: connectionTestExecutor)
        {
            SearchMenu = new SearchMenuViewModel(),
            Canvas = null,
        };

        ConnectionProfile profile = BuildProfile();
        vm.Profiles.Add(profile);
        vm.ActiveProfileId = profile.Id;

        await InvokeRunHealthCheckAsync(vm);

        Assert.Equal(ConnectionHealthStatus.Offline, vm.ActiveHealthStatus);
        Assert.Equal(1, activationWorkflow.CallCount);
    }

    [Fact]
    public async Task RunHealthCheckAsync_WhenOfflineTwiceWithinCooldown_DoesNotRetryReconnectImmediately()
    {
        var activationWorkflow = new RecordingActivationWorkflow();
        var healthLifecycle = new StaticHealthLifecycleCoordinator(ConnectionHealthStatus.Offline);
        var connectionTestExecutor = new StaticConnectionTestExecutor(
            new ConnectionTestResult(Success: false, ErrorMessage: "connection dropped"));

        var vm = new ConnectionManagerViewModel(
            activationWorkflow: activationWorkflow,
            healthLifecycleCoordinator: healthLifecycle,
            connectionTestExecutor: connectionTestExecutor)
        {
            SearchMenu = new SearchMenuViewModel(),
            Canvas = null,
        };

        ConnectionProfile profile = BuildProfile();
        vm.Profiles.Add(profile);
        vm.ActiveProfileId = profile.Id;

        await InvokeRunHealthCheckAsync(vm);
        await InvokeRunHealthCheckAsync(vm);

        Assert.Equal(1, activationWorkflow.CallCount);
    }

    private static async Task InvokeRunHealthCheckAsync(ConnectionManagerViewModel vm)
    {
        MethodInfo runHealthCheck = typeof(ConnectionManagerViewModel)
            .GetMethod(
                "RunHealthCheckAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(CancellationToken)],
                modifiers: null)
            ?? throw new InvalidOperationException("RunHealthCheckAsync not found.");

        Task task = (Task)runHealthCheck.Invoke(vm, [CancellationToken.None])!;
        await task;
    }

    private static ConnectionProfile BuildProfile()
    {
        return new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "AutoReconnect",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db",
            Username = "u",
            Password = "p",
            TimeoutSeconds = 5,
        };
    }

    private sealed class RecordingActivationWorkflow : IConnectionActivationWorkflow
    {
        public int CallCount { get; private set; }

        public Task<ConnectionActivationResult> ExecuteAsync(
            ConnectionProfile profile,
            SearchMenuViewModel? searchMenu,
            CanvasViewModel? canvas,
            Func<ConnectionConfig, SearchMenuViewModel, CancellationToken, Task<DbMetadata?>> loadMetadataAsync,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new ConnectionActivationResult(
                Outcome: ConnectionActivationOutcome.Failed,
                Config: profile.ToConnectionConfig(),
                FailureException: new InvalidOperationException("simulated reconnect failure")));
        }
    }

    private sealed class StaticHealthLifecycleCoordinator(ConnectionHealthStatus status)
        : IConnectionHealthLifecycleCoordinator
    {
        private readonly ConnectionHealthStatus _status = status;

        public CancellationTokenSource? Restart(
            string? activeProfileId,
            CancellationTokenSource? existing,
            Action<CancellationToken> startLoop)
        {
            return existing;
        }

        public Task<ConnectionHealthStatus> EvaluateActiveStatusAsync(
            IReadOnlyCollection<ConnectionProfile> profiles,
            string? activeProfileId,
            Func<ConnectionConfig, DatabaseProvider, int, CancellationToken, Task<ConnectionTestResult>> runTestAsync,
            double degradedLatencyThresholdMs,
            CancellationToken ct = default)
        {
            return Task.FromResult(_status);
        }
    }

    private sealed class StaticConnectionTestExecutor(ConnectionTestResult result) : IConnectionTestExecutor
    {
        private readonly ConnectionTestResult _result = result;

        public Task<ConnectionTestResult> ExecuteAsync(
            ConnectionConfig config,
            DatabaseProvider provider,
            int timeoutSeconds,
            CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }
}
