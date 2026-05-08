using AkkornStudio.UI.Services.Connection;
using AkkornStudio.UI.Services.ConnectionManager.Contracts;

namespace AkkornStudio.UI.Services.ConnectionManager;

public sealed class ConnectionCatalogService : IConnectionCatalogService
{
    private readonly IConnectionProfileStore _profileStore;
    private readonly CredentialVaultStore _credentialVault;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IConnectionProfilesChangedNotifier _notifier;

    public ConnectionCatalogService(IConnectionProfileStore profileStore, IConnectionProfilesChangedNotifier notifier)
    {
        _profileStore = profileStore;
        _credentialVault = new CredentialVaultStore();
        _notifier = notifier;
    }

    public async Task<IReadOnlyList<ConnectionSummaryDto>> ListSummariesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<ConnectionProfile> profiles = _profileStore.LoadProfiles(_credentialVault);
            return profiles
                .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static p => ConnectionContractMapper.ToSummary(p, isActive: false))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResultDto<ConnectionDetailsDto>> GetDetailsAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            return Fail<ConnectionDetailsDto>(ConnectionOperationSemanticErrorCode.ValidationFailed, "Connection id is required.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ConnectionProfile? profile = _profileStore
                .LoadProfiles(_credentialVault)
                .FirstOrDefault(p => string.Equals(p.Id, connectionId, StringComparison.Ordinal));

            if (profile is null)
                return Fail<ConnectionDetailsDto>(ConnectionOperationSemanticErrorCode.NotFound, "Connection not found.");

            return Ok(ConnectionContractMapper.ToDetails(profile));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResultDto<ConnectionDetailsDto>> SaveAsync(
        ConnectionDetailsDto details,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<ConnectionProfile> profiles = _profileStore.LoadProfiles(_credentialVault).ToList();
            ConnectionProfile profile = ConnectionContractMapper.ToProfile(details);

            int existingIndex = profiles.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.Ordinal));
            if (existingIndex >= 0)
                profiles[existingIndex] = profile;
            else
                profiles.Add(profile);

            _profileStore.PersistProfiles(profiles, _credentialVault);
            _notifier.NotifyProfilesChanged();
            return Ok(ConnectionContractMapper.ToDetails(profile));
        }
        catch (Exception ex)
        {
            return Fail<ConnectionDetailsDto>(ConnectionOperationSemanticErrorCode.Unknown, "Could not save connection.", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResultDto<ConnectionDetailsDto>> DuplicateAsync(
        string connectionId,
        string? newName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            return Fail<ConnectionDetailsDto>(ConnectionOperationSemanticErrorCode.ValidationFailed, "Connection id is required.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<ConnectionProfile> profiles = _profileStore.LoadProfiles(_credentialVault).ToList();
            ConnectionProfile? source = profiles.FirstOrDefault(p => string.Equals(p.Id, connectionId, StringComparison.Ordinal));
            if (source is null)
                return Fail<ConnectionDetailsDto>(ConnectionOperationSemanticErrorCode.NotFound, "Connection not found.");

            string candidateName = string.IsNullOrWhiteSpace(newName)
                ? $"{source.Name} Copy"
                : newName.Trim();

            string uniqueName = EnsureUniqueName(candidateName, profiles);
            var duplicate = new ConnectionProfile
            {
                Id = Guid.NewGuid().ToString(),
                Name = uniqueName,
                Provider = source.Provider,
                Host = source.Host,
                Port = source.Port,
                Database = source.Database,
                Username = source.Username,
                Password = source.Password,
                RememberPassword = source.RememberPassword,
                UseSsl = source.UseSsl,
                TrustServerCertificate = source.TrustServerCertificate,
                UseIntegratedSecurity = source.UseIntegratedSecurity,
                TimeoutSeconds = source.TimeoutSeconds,
            };

            profiles.Add(duplicate);
            _profileStore.PersistProfiles(profiles, _credentialVault);
            _notifier.NotifyProfilesChanged();

            return Ok(ConnectionContractMapper.ToDetails(duplicate));
        }
        catch (Exception ex)
        {
            return Fail<ConnectionDetailsDto>(ConnectionOperationSemanticErrorCode.Unknown, "Could not duplicate connection.", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResultDto<bool>> DeleteAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            return Fail<bool>(ConnectionOperationSemanticErrorCode.ValidationFailed, "Connection id is required.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<ConnectionProfile> profiles = _profileStore.LoadProfiles(_credentialVault).ToList();
            ConnectionProfile? profile = profiles.FirstOrDefault(p => string.Equals(p.Id, connectionId, StringComparison.Ordinal));
            if (profile is null)
                return Fail<bool>(ConnectionOperationSemanticErrorCode.NotFound, "Connection not found.");

            profiles.Remove(profile);
            _profileStore.PersistProfiles(profiles, _credentialVault);
            _credentialVault.RemoveSecret(connectionId);
            _notifier.NotifyProfilesChanged();

            return Ok(true);
        }
        catch (Exception ex)
        {
            return Fail<bool>(ConnectionOperationSemanticErrorCode.Unknown, "Could not delete connection.", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string EnsureUniqueName(string initialName, IReadOnlyList<ConnectionProfile> profiles)
    {
        string candidate = initialName;
        int suffix = 2;

        while (profiles.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{initialName} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    private static OperationResultDto<T> Ok<T>(T payload) =>
        new(
            Success: true,
            SemanticErrorCode: ConnectionOperationSemanticErrorCode.None,
            UserMessage: string.Empty,
            Payload: payload,
            TechnicalError: null,
            CorrelationId: null);

    private static OperationResultDto<T> Fail<T>(
        ConnectionOperationSemanticErrorCode code,
        string message,
        string? technicalError = null) =>
        new(
            Success: false,
            SemanticErrorCode: code,
            UserMessage: message,
            Payload: default,
            TechnicalError: technicalError,
            CorrelationId: null);
}
