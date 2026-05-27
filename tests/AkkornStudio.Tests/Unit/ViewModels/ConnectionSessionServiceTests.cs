using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.ConnectionManager.Contracts;
using System.Reflection;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels;

public class ConnectionSessionServiceTests
{
    [Fact]
    public async Task ConnectAsync_WhenTestSucceeds_SetsActiveSession()
    {
        var testService = new StubConnectionTestService(success: true);
        var telemetry = new SpyConnectionTelemetryService();
        var sessionService = new ConnectionSessionService(testService, telemetry);

        ConnectionDetailsDto details = BuildDetails("conn-1", "Local", "Postgres");

        OperationResultDto<ActiveConnectionSessionDto> result = await sessionService.ConnectAsync(details);
        ActiveConnectionSessionDto active = await sessionService.GetActiveSessionAsync();

        Assert.True(result.Success);
        Assert.NotNull(result.Payload);
        Assert.Equal(ConnectionSessionStateDto.Active, result.Payload!.SessionState);
        Assert.Equal("conn-1", result.Payload.ConnectionId);
        Assert.Equal(ConnectionSessionStateDto.Active, active.SessionState);
        Assert.Equal("conn-1", active.ConnectionId);
        Assert.Contains(telemetry.Events, e => e == "connection.session.connect.succeeded");
    }

    [Fact]
    public async Task ConnectAsync_WhenTestFails_SetsFailedSession()
    {
        var testService = new StubConnectionTestService(success: false);
        var telemetry = new SpyConnectionTelemetryService();
        var sessionService = new ConnectionSessionService(testService, telemetry);

        ConnectionDetailsDto details = BuildDetails("conn-2", "Broken", "Postgres");

        OperationResultDto<ActiveConnectionSessionDto> result = await sessionService.ConnectAsync(details);
        ActiveConnectionSessionDto active = await sessionService.GetActiveSessionAsync();

        Assert.False(result.Success);
        Assert.NotNull(result.Payload);
        Assert.Equal(ConnectionSessionStateDto.Failed, result.Payload!.SessionState);
        Assert.Equal(ConnectionSessionStateDto.Failed, active.SessionState);
        Assert.Contains(telemetry.Events, e => e == "connection.session.connect.failed");
    }

    [Fact]
    public async Task DisconnectAsync_WhenActiveConnectionExists_ClearsSession()
    {
        var testService = new StubConnectionTestService(success: true);
        var telemetry = new SpyConnectionTelemetryService();
        var sessionService = new ConnectionSessionService(testService, telemetry);

        await sessionService.ConnectAsync(BuildDetails("conn-3", "Local", "MySql"));

        OperationResultDto<ActiveConnectionSessionDto> disconnected = await sessionService.DisconnectAsync("conn-3");
        ActiveConnectionSessionDto active = await sessionService.GetActiveSessionAsync();

        Assert.True(disconnected.Success);
        Assert.NotNull(disconnected.Payload);
        Assert.Equal(ConnectionSessionStateDto.Inactive, disconnected.Payload!.SessionState);
        Assert.Null(disconnected.Payload.ConnectionId);
        Assert.Equal(ConnectionSessionStateDto.Inactive, active.SessionState);
        Assert.Null(active.ConnectionId);
        Assert.Contains(telemetry.Events, e => e == "connection.session.disconnect");
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnectionIdDiffers_ReturnsConflictAndKeepsSessionActive()
    {
        var testService = new StubConnectionTestService(success: true);
        var telemetry = new SpyConnectionTelemetryService();
        var sessionService = new ConnectionSessionService(testService, telemetry);

        await sessionService.ConnectAsync(BuildDetails("conn-active", "Local", "MySql"));

        OperationResultDto<ActiveConnectionSessionDto> result = await sessionService.DisconnectAsync("conn-other");
        ActiveConnectionSessionDto active = await sessionService.GetActiveSessionAsync();

        Assert.False(result.Success);
        Assert.Equal(ConnectionOperationSemanticErrorCode.Conflict, result.SemanticErrorCode);
        Assert.NotNull(result.Payload);
        Assert.Equal(ConnectionSessionStateDto.Active, result.Payload!.SessionState);
        Assert.Equal("conn-active", result.Payload.ConnectionId);
        Assert.Equal(ConnectionSessionStateDto.Active, active.SessionState);
        Assert.Equal("conn-active", active.ConnectionId);
        Assert.DoesNotContain(telemetry.Events, e => e == "connection.session.disconnect");
    }

    [Fact]
    public async Task ConnectAsync_WhenGateLockTimesOut_ReturnsTimeoutResult()
    {
        var testService = new StubConnectionTestService(success: true);
        var telemetry = new SpyConnectionTelemetryService();
        var sessionService = new ConnectionSessionService(
            testService,
            telemetry,
            gateWaitTimeout: TimeSpan.FromMilliseconds(25));

        SemaphoreSlim gate = ResolveGate(sessionService);
        await gate.WaitAsync();
        try
        {
            OperationResultDto<ActiveConnectionSessionDto> result = await sessionService.ConnectAsync(
                BuildDetails("conn-timeout", "Timeout", "Postgres"));

            Assert.False(result.Success);
            Assert.Equal(ConnectionOperationSemanticErrorCode.Timeout, result.SemanticErrorCode);
            Assert.NotNull(result.TechnicalError);
        }
        finally
        {
            gate.Release();
        }
    }

    private static ConnectionDetailsDto BuildDetails(string id, string name, string provider) =>
        new(
            Id: id,
            Name: name,
            Provider: provider,
            Mode: ConnectionProviderModeDto.Fields,
            FieldValues: new Dictionary<string, string?>(),
            UrlValue: null,
            Tag: null,
            IsFavorite: false,
            AdvancedOptions: new Dictionary<string, string?>());

    private sealed class StubConnectionTestService : IConnectionTestService
    {
        private readonly bool _success;

        public StubConnectionTestService(bool success)
        {
            _success = success;
        }

        public Task<OperationResultDto<ConnectionTestResultDto>> TestAsync(
            ConnectionDetailsDto details,
            CancellationToken cancellationToken = default)
        {
            if (_success)
            {
                return Task.FromResult(new OperationResultDto<ConnectionTestResultDto>(
                    Success: true,
                    SemanticErrorCode: ConnectionOperationSemanticErrorCode.None,
                    UserMessage: string.Empty,
                    Payload: new ConnectionTestResultDto(
                        Status: ConnectionTestStatusDto.Success,
                        SummaryMessage: "ok",
                        TechnicalDetails: null,
                        LatencyMs: 12,
                        ProviderErrorCode: null,
                        TestedAt: DateTimeOffset.UtcNow),
                    TechnicalError: null,
                    CorrelationId: null));
            }

            return Task.FromResult(new OperationResultDto<ConnectionTestResultDto>(
                Success: false,
                SemanticErrorCode: ConnectionOperationSemanticErrorCode.AuthenticationFailed,
                UserMessage: "auth failed",
                Payload: new ConnectionTestResultDto(
                    Status: ConnectionTestStatusDto.AuthenticationFailure,
                    SummaryMessage: "auth failed",
                    TechnicalDetails: "bad credentials",
                    LatencyMs: null,
                    ProviderErrorCode: null,
                    TestedAt: DateTimeOffset.UtcNow),
                TechnicalError: "bad credentials",
                CorrelationId: null));
        }
    }

    private sealed class SpyConnectionTelemetryService : IConnectionTelemetryService
    {
        public List<string> Events { get; } = new();

        public Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default)
        {
            Events.Add(eventName);
            return Task.CompletedTask;
        }
    }

    private static SemaphoreSlim ResolveGate(ConnectionSessionService sessionService)
    {
        FieldInfo field = typeof(ConnectionSessionService).GetField(
            "_gate",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (SemaphoreSlim)field.GetValue(sessionService)!;
    }
}
