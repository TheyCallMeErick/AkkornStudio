using AkkornStudio.Core;
using AkkornStudio.UI.Services.ConnectionManager.Contracts;

namespace AkkornStudio.UI.Services.ConnectionManager;

public sealed class ConnectionUrlParserService : IConnectionUrlParserService
{
    public Task<ConnectionUrlParseResultDto> ParseAsync(
        string rawUrl,
        string? selectedProvider,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return Task.FromResult(new ConnectionUrlParseResultDto(
                ParseStatus: ConnectionUrlParseStatusDto.Failed,
                RecognizedFields: [],
                UnrecognizedTokens: [],
                SuggestedProvider: null,
                ConflictWithSelectedProvider: false,
                NormalizedUrl: null,
                UserMessage: "Connection URL is empty.",
                TechnicalDetails: null));
        }

        if (!TryParseConnectionUrl(rawUrl, out ParsedConnectionUrl parsed, out string error, out IReadOnlyList<string> unknownTokens))
        {
            return Task.FromResult(new ConnectionUrlParseResultDto(
                ParseStatus: ConnectionUrlParseStatusDto.Failed,
                RecognizedFields: [],
                UnrecognizedTokens: [],
                SuggestedProvider: null,
                ConflictWithSelectedProvider: false,
                NormalizedUrl: null,
                UserMessage: error,
                TechnicalDetails: null));
        }

        bool conflict = false;
        if (!string.IsNullOrWhiteSpace(selectedProvider)
            && ConnectionContractMapper.TryParseProvider(selectedProvider, out DatabaseProvider selected)
            && selected != parsed.Provider)
        {
            conflict = true;
        }

        var fields = new List<UrlParseFieldTokenDto>
        {
            new(ConnectionContractMapper.HostKey, parsed.Host),
            new(ConnectionContractMapper.PortKey, parsed.Port.ToString()),
            new(ConnectionContractMapper.DatabaseKey, parsed.Database),
            new(ConnectionContractMapper.UsernameKey, parsed.Username),
            new(ConnectionContractMapper.PasswordKey, parsed.Password),
            new(ConnectionContractMapper.UseSslKey, parsed.UseSsl.ToString()),
            new(ConnectionContractMapper.TrustServerCertificateKey, parsed.TrustServerCertificate.ToString()),
            new(ConnectionContractMapper.UseIntegratedSecurityKey, parsed.UseIntegratedSecurity.ToString()),
        };

        ConnectionUrlParseStatusDto status = unknownTokens.Count == 0 && !conflict
            ? ConnectionUrlParseStatusDto.Success
            : ConnectionUrlParseStatusDto.Partial;

        string message = status == ConnectionUrlParseStatusDto.Success
            ? "Connection URL parsed successfully."
            : "Connection URL parsed with warnings.";

        return Task.FromResult(new ConnectionUrlParseResultDto(
            ParseStatus: status,
            RecognizedFields: fields,
            UnrecognizedTokens: unknownTokens,
            SuggestedProvider: parsed.Provider.ToString(),
            ConflictWithSelectedProvider: conflict,
            NormalizedUrl: parsed.NormalizedUrl,
            UserMessage: message,
            TechnicalDetails: null));
    }

    private static bool TryParseConnectionUrl(
        string raw,
        out ParsedConnectionUrl parsed,
        out string error,
        out IReadOnlyList<string> unknownTokens)
    {
        parsed = default;
        error = string.Empty;
        unknownTokens = [];

        string input = raw.Trim();

        if (!input.Contains("://", StringComparison.Ordinal))
        {
            string lower = input.ToLowerInvariant();
            if (lower.EndsWith(".db", StringComparison.Ordinal)
                || lower.EndsWith(".sqlite", StringComparison.Ordinal)
                || lower.EndsWith(".sqlite3", StringComparison.Ordinal))
            {
                parsed = new ParsedConnectionUrl(
                    Provider: DatabaseProvider.SQLite,
                    Host: AppConstants.DefaultHost,
                    Port: 0,
                    Database: input,
                    Username: string.Empty,
                    Password: string.Empty,
                    UseSsl: false,
                    TrustServerCertificate: true,
                    UseIntegratedSecurity: false,
                    NormalizedUrl: $"file://{input}");

                return true;
            }

            error = "Could not determine database type from URL.";
            return false;
        }

        string encoded = input;
        string username = string.Empty;
        string password = string.Empty;
        bool hadCredentialSection = false;
        bool hadPasswordDelimiter = false;

        int authorityStart = input.IndexOf("://", StringComparison.Ordinal) + 3;
        int lastAt = input.LastIndexOf('@');
        if (lastAt > authorityStart)
        {
            hadCredentialSection = true;
            string credentialPart = input.Substring(authorityStart, lastAt - authorityStart);
            int firstColon = credentialPart.IndexOf(':');
            if (firstColon >= 0)
            {
                hadPasswordDelimiter = true;
                username = Uri.UnescapeDataString(credentialPart[..firstColon]);
                password = Uri.UnescapeDataString(credentialPart[(firstColon + 1)..]);
            }
            else
            {
                username = Uri.UnescapeDataString(credentialPart);
            }

            encoded = input[..authorityStart] + input[(lastAt + 1)..];
        }

        if (!Uri.TryCreate(encoded, UriKind.Absolute, out Uri? uri))
        {
            error = "Invalid connection URL.";
            return false;
        }

        if (!ConnectionContractMapper.TryParseProvider(uri.Scheme, out DatabaseProvider provider))
        {
            error = $"Unsupported provider scheme: {uri.Scheme}.";
            return false;
        }

        Dictionary<string, string> query = ParseQuery(uri.Query);
        bool useSsl = ResolveUseSsl(provider, query);
        bool trust = ResolveTrustServerCertificate(query);
        bool useIntegratedSecurity = ResolveIntegratedSecurity(query);
        string database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));

        if (provider == DatabaseProvider.SQLite)
            database = ResolveSqliteDatabasePath(uri, database);

        string resolvedUsername = string.IsNullOrWhiteSpace(username)
            ? Uri.UnescapeDataString(uri.UserInfo)
            : username;
        string normalizedUrl = BuildNormalizedUrl(
            uri,
            resolvedUsername,
            password,
            hadCredentialSection,
            hadPasswordDelimiter);

        parsed = new ParsedConnectionUrl(
            Provider: provider,
            Host: string.IsNullOrWhiteSpace(uri.Host) ? AppConstants.DefaultHost : uri.Host,
            Port: uri.IsDefaultPort ? ConnectionProfile.DefaultPort(provider) : uri.Port,
            Database: database,
            Username: resolvedUsername,
            Password: password,
            UseSsl: useSsl,
            TrustServerCertificate: trust,
            UseIntegratedSecurity: useIntegratedSecurity,
            NormalizedUrl: normalizedUrl);

        if (provider == DatabaseProvider.SqlServer && parsed.Port <= 0)
            parsed = parsed with { Port = ConnectionProfile.DefaultPort(DatabaseProvider.SqlServer) };

        unknownTokens = query.Keys
            .Where(static key => !KnownQueryKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return true;
    }

    private static readonly string[] KnownQueryKeys =
    [
        "ssl",
        "sslmode",
        "encrypt",
        "trustservercertificate",
        "integratedsecurity",
        "trusted_connection",
    ];

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
            return result;

        string[] parts = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string[] kv = part.Split('=', 2, StringSplitOptions.None);
            string key = Uri.UnescapeDataString(kv[0]);
            string value = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static bool ResolveUseSsl(DatabaseProvider provider, IReadOnlyDictionary<string, string> query)
    {
        if (query.TryGetValue("ssl", out string? ssl))
            return IsTruthy(ssl);

        if (query.TryGetValue("sslmode", out string? sslMode))
            return !sslMode.Equals("disable", StringComparison.OrdinalIgnoreCase)
                && !sslMode.Equals("none", StringComparison.OrdinalIgnoreCase);

        if (provider == DatabaseProvider.SqlServer && query.TryGetValue("encrypt", out string? encrypt))
            return IsTruthy(encrypt);

        return false;
    }

    private static bool ResolveTrustServerCertificate(IReadOnlyDictionary<string, string> query)
    {
        if (!query.TryGetValue("trustservercertificate", out string? value))
            return true;

        return IsTruthy(value);
    }

    private static bool ResolveIntegratedSecurity(IReadOnlyDictionary<string, string> query)
    {
        if (query.TryGetValue("integratedsecurity", out string? integrated))
            return IsTruthy(integrated);

        if (query.TryGetValue("trusted_connection", out string? trustedConnection))
            return IsTruthy(trustedConnection);

        return false;
    }

    private static bool IsTruthy(string value)
    {
        if (bool.TryParse(value, out bool parsed))
            return parsed;

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("required", StringComparison.OrdinalIgnoreCase)
            || value.Equals("require", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSqliteDatabasePath(Uri uri, string fallback)
    {
        string localPath = Uri.UnescapeDataString(uri.LocalPath);
        if (localPath == "/" && string.IsNullOrWhiteSpace(fallback))
            return fallback;

        return localPath;
    }

    private static string BuildNormalizedUrl(
        Uri uri,
        string username,
        string password,
        bool hadCredentialSection,
        bool hadPasswordDelimiter)
    {
        string normalized = uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.Unescaped);
        bool shouldInjectCredentials = hadCredentialSection
            && (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password) || hadPasswordDelimiter);
        if (!shouldInjectCredentials)
            return normalized;

        int schemeSeparator = uri.Scheme.Length;

        string encodedUser = Uri.EscapeDataString(username);
        string encodedPassword = Uri.EscapeDataString(password);
        string credential = hadPasswordDelimiter
            ? $"{encodedUser}:{encodedPassword}@"
            : $"{encodedUser}@";

        return normalized[..(schemeSeparator + 3)] + credential + normalized[(schemeSeparator + 3)..];
    }

    private readonly record struct ParsedConnectionUrl(
        DatabaseProvider Provider,
        string Host,
        int Port,
        string Database,
        string Username,
        string Password,
        bool UseSsl,
        bool TrustServerCertificate,
        bool UseIntegratedSecurity,
        string NormalizedUrl);
}
