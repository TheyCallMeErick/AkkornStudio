using AkkornStudio.UI.Services.ConnectionManager.Contracts;

namespace AkkornStudio.UI.Services.ConnectionManager;

public sealed class ConnectionSessionService : IConnectionSessionService
{
    private static readonly TimeSpan DefaultGateWaitTimeout = TimeSpan.FromSeconds(5);
    private readonly IConnectionTestService _connectionTestService;
    private readonly IConnectionTelemetryService _connectionTelemetryService;
    private readonly TimeSpan _gateWaitTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveConnectionSessionDto _activeSession = new(
        ConnectionId: null,
        SessionState: ConnectionSessionStateDto.Inactive,
        StartedAt: null,
        SessionLabel: null);

    public ConnectionSessionService(
        IConnectionTestService connectionTestService,
        IConnectionTelemetryService connectionTelemetryService,
        TimeSpan? gateWaitTimeout = null)
    {
        _connectionTestService = connectionTestService;
        _connectionTelemetryService = connectionTelemetryService;
        _gateWaitTimeout = gateWaitTimeout.GetValueOrDefault(DefaultGateWaitTimeout);
    }

    public async Task<OperationResultDto<ActiveConnectionSessionDto>> ConnectAsync(
        ConnectionDetailsDto details,
        CancellationToken cancellationToken = default)
    {
        string connectionId = string.IsNullOrWhiteSpace(details.Id)
            ? Guid.NewGuid().ToString()
            : details.Id;

        if (!await TryEnterGateAsync(cancellationToken))
            return BuildTimeoutResult("Connect operation timed out while waiting for session state lock.");
        try
        {
            _activeSession = new ActiveConnectionSessionDto(
                ConnectionId: connectionId,
                SessionState: ConnectionSessionStateDto.Connecting,
                StartedAt: null,
                SessionLabel: details.Name);
        }
        finally
        {
            _gate.Release();
        }

        OperationResultDto<ConnectionTestResultDto> testResult = await _connectionTestService.TestAsync(details, cancellationToken);
        if (!testResult.Success)
        {
            if (!await TryEnterGateAsync(cancellationToken))
                return BuildTimeoutResult("Connect operation timed out while finalizing failed session state.");
            try
            {
                _activeSession = new ActiveConnectionSessionDto(
                    ConnectionId: connectionId,
                    SessionState: ConnectionSessionStateDto.Failed,
                    StartedAt: null,
                    SessionLabel: details.Name);
            }
            finally
            {
                _gate.Release();
            }

            await _connectionTelemetryService.TrackAsync(
                "connection.session.connect.failed",
                new Dictionary<string, object?>
                {
                    ["connectionId"] = connectionId,
                    ["provider"] = details.Provider,
                    ["errorCode"] = testResult.SemanticErrorCode.ToString(),
                },
                cancellationToken);

            return new OperationResultDto<ActiveConnectionSessionDto>(
                Success: false,
                SemanticErrorCode: testResult.SemanticErrorCode,
                UserMessage: testResult.UserMessage,
                Payload: _activeSession,
                TechnicalError: testResult.TechnicalError,
                CorrelationId: null);
        }

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;

        if (!await TryEnterGateAsync(cancellationToken))
            return BuildTimeoutResult("Connect operation timed out while finalizing active session state.");
        try
        {
            _activeSession = new ActiveConnectionSessionDto(
                ConnectionId: connectionId,
                SessionState: ConnectionSessionStateDto.Active,
                StartedAt: startedAt,
                SessionLabel: details.Name);
        }
        finally
        {
            _gate.Release();
        }

        await _connectionTelemetryService.TrackAsync(
            "connection.session.connect.succeeded",
            new Dictionary<string, object?>
            {
                ["connectionId"] = connectionId,
                ["provider"] = details.Provider,
            },
            cancellationToken);

        return new OperationResultDto<ActiveConnectionSessionDto>(
            Success: true,
            SemanticErrorCode: ConnectionOperationSemanticErrorCode.None,
            UserMessage: string.Empty,
            Payload: _activeSession,
            TechnicalError: null,
            CorrelationId: null);
    }

    public async Task<OperationResultDto<ActiveConnectionSessionDto>> DisconnectAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return new OperationResultDto<ActiveConnectionSessionDto>(
                Success: false,
                SemanticErrorCode: ConnectionOperationSemanticErrorCode.ValidationFailed,
                UserMessage: "Connection id is required.",
                Payload: _activeSession,
                TechnicalError: null,
                CorrelationId: null);
        }

        if (!await TryEnterGateAsync(cancellationToken))
            return BuildTimeoutResult("Disconnect operation timed out while waiting for session state lock.");
        try
        {
            ActiveConnectionSessionDto currentSession = _activeSession;
            if (currentSession.ConnectionId is null)
            {
                return new OperationResultDto<ActiveConnectionSessionDto>(
                    Success: false,
                    SemanticErrorCode: ConnectionOperationSemanticErrorCode.NotFound,
                    UserMessage: "There is no active connection to disconnect.",
                    Payload: currentSession,
                    TechnicalError: null,
                    CorrelationId: null);
            }

            if (!string.Equals(currentSession.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                return new OperationResultDto<ActiveConnectionSessionDto>(
                    Success: false,
                    SemanticErrorCode: ConnectionOperationSemanticErrorCode.Conflict,
                    UserMessage: "A different connection is active.",
                    Payload: currentSession,
                    TechnicalError: null,
                    CorrelationId: null);
            }

            _activeSession = currentSession with { SessionState = ConnectionSessionStateDto.Disconnecting };
            try
            {
                _activeSession = new ActiveConnectionSessionDto(
                    ConnectionId: null,
                    SessionState: ConnectionSessionStateDto.Inactive,
                    StartedAt: null,
                    SessionLabel: null);
            }
            catch
            {
                _activeSession = currentSession;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }

        await _connectionTelemetryService.TrackAsync(
            "connection.session.disconnect",
            new Dictionary<string, object?>
            {
                ["connectionId"] = connectionId,
            },
            cancellationToken);

        return new OperationResultDto<ActiveConnectionSessionDto>(
            Success: true,
            SemanticErrorCode: ConnectionOperationSemanticErrorCode.None,
            UserMessage: string.Empty,
            Payload: _activeSession,
            TechnicalError: null,
            CorrelationId: null);
    }

    public Task<ActiveConnectionSessionDto> GetActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_activeSession);
    }

    private async Task<bool> TryEnterGateAsync(CancellationToken cancellationToken)
    {
        return await _gate.WaitAsync(_gateWaitTimeout, cancellationToken);
    }

    private OperationResultDto<ActiveConnectionSessionDto> BuildTimeoutResult(string technicalError)
    {
        return new OperationResultDto<ActiveConnectionSessionDto>(
            Success: false,
            SemanticErrorCode: ConnectionOperationSemanticErrorCode.Timeout,
            UserMessage: "Connection operation timed out. Please retry.",
            Payload: _activeSession,
            TechnicalError: technicalError,
            CorrelationId: null);
    }
}
