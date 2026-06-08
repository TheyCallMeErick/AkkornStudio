using AkkornStudio.Core;
using AkkornStudio.UI.Services.ConnectionManager.Contracts;

namespace AkkornStudio.UI.Services.ConnectionManager;

public sealed class ConnectionValidationService : IConnectionValidationService
{
    public ConnectionValidationResultDto Validate(ConnectionDetailsDto details, ProviderCapabilityDto capability)
    {
        var errors = new List<ConnectionValidationMessageDto>();
        var warnings = new List<ConnectionValidationMessageDto>();

        if (string.IsNullOrWhiteSpace(details.Name))
        {
            errors.Add(new ConnectionValidationMessageDto("name", "name.required", "Connection name is required."));
        }

        if (details.Mode == ConnectionProviderModeDto.Url)
        {
            if (!capability.SupportsUrlMode)
            {
                errors.Add(new ConnectionValidationMessageDto("url", "url.unsupported", "The selected provider does not support URL mode."));
            }

            if (string.IsNullOrWhiteSpace(details.UrlValue))
            {
                errors.Add(new ConnectionValidationMessageDto("url", "url.required", "Connection URL is required in URL mode."));
            }

            return new ConnectionValidationResultDto(errors.Count == 0, errors, warnings);
        }

        IReadOnlyDictionary<string, string?> fields = details.FieldValues;

        if (!TryGetInt(fields, ConnectionContractMapper.TimeoutSecondsKey, out int timeout) || timeout <= 0)
        {
            errors.Add(new ConnectionValidationMessageDto(
                ConnectionContractMapper.TimeoutSecondsKey,
                "timeout.invalid",
                "Timeout must be greater than zero."));
        }

        bool providerParsed = ConnectionContractMapper.TryParseProvider(details.Provider, out DatabaseProvider provider);
        bool isSqlite = providerParsed && provider == DatabaseProvider.SQLite;

        if (isSqlite)
        {
            if (string.IsNullOrWhiteSpace(GetString(fields, ConnectionContractMapper.DatabaseKey)))
            {
                errors.Add(new ConnectionValidationMessageDto(
                    ConnectionContractMapper.DatabaseKey,
                    "database.required",
                    "SQLite database path is required."));
            }

            return new ConnectionValidationResultDto(errors.Count == 0, errors, warnings);
        }

        ValidateRequiredField(fields, errors, ConnectionContractMapper.HostKey, "Host is required.");
        ValidateRequiredField(fields, errors, ConnectionContractMapper.DatabaseKey, "Database is required.");

        if (!TryGetInt(fields, ConnectionContractMapper.PortKey, out int port) || port <= 0)
        {
            errors.Add(new ConnectionValidationMessageDto(
                ConnectionContractMapper.PortKey,
                "port.invalid",
                "Port must be greater than zero."));
        }

        bool useIntegratedSecurity = GetBool(fields, ConnectionContractMapper.UseIntegratedSecurityKey, false);
        if (useIntegratedSecurity && !capability.SupportsIntegratedSecurity)
        {
            errors.Add(new ConnectionValidationMessageDto(
                ConnectionContractMapper.UseIntegratedSecurityKey,
                "integratedSecurity.unsupported",
                "Integrated security is not supported by the selected provider in this environment."));
        }

        if (!useIntegratedSecurity)
        {
            ValidateRequiredField(fields, errors, ConnectionContractMapper.UsernameKey, "Username is required.");
        }

        if (GetBool(fields, ConnectionContractMapper.UseSslKey, false) && !capability.SupportsSsl)
        {
            warnings.Add(new ConnectionValidationMessageDto(
                ConnectionContractMapper.UseSslKey,
                "ssl.unsupported",
                "SSL was requested, but the selected provider typically does not use SSL in this mode."));
        }

        if (providerParsed
            && provider == DatabaseProvider.SqlServer
            && GetBool(fields, ConnectionContractMapper.TrustServerCertificateKey, true))
        {
            warnings.Add(new ConnectionValidationMessageDto(
                ConnectionContractMapper.TrustServerCertificateKey,
                "trustServerCertificate.enabled",
                "TrustServerCertificate is enabled. This weakens TLS certificate validation and should be used only in trusted environments."));
        }

        return new ConnectionValidationResultDto(errors.Count == 0, errors, warnings);
    }

    private static void ValidateRequiredField(
        IReadOnlyDictionary<string, string?> fields,
        ICollection<ConnectionValidationMessageDto> errors,
        string key,
        string message)
    {
        if (string.IsNullOrWhiteSpace(GetString(fields, key)))
        {
            errors.Add(new ConnectionValidationMessageDto(key, $"{key}.required", message));
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, string?> fields, string key)
    {
        return fields.TryGetValue(key, out string? value) ? value : null;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string?> fields, string key, out int value)
    {
        value = default;
        return fields.TryGetValue(key, out string? raw) && int.TryParse(raw, out value);
    }

    private static bool GetBool(IReadOnlyDictionary<string, string?> fields, string key, bool fallback)
    {
        if (!fields.TryGetValue(key, out string? raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (bool.TryParse(raw, out bool parsed))
            return parsed;

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("required", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("require", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
