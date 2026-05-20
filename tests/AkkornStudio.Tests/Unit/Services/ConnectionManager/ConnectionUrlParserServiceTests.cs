using AkkornStudio.Core;
using AkkornStudio.UI;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.ConnectionManager.Contracts;

namespace AkkornStudio.Tests.Unit.Services.ConnectionManager;

public sealed class ConnectionUrlParserServiceTests
{
    private readonly ConnectionUrlParserService _sut = new();

    [Fact]
    public async Task ParseAsync_WithEmptyUrl_ReturnsFailed()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync("", selectedProvider: null);

        Assert.Equal(ConnectionUrlParseStatusDto.Failed, result.ParseStatus);
        Assert.Equal("Connection URL is empty.", result.UserMessage);
        Assert.Null(result.NormalizedUrl);
        Assert.Empty(result.RecognizedFields);
        Assert.Empty(result.UnrecognizedTokens);
    }

    [Fact]
    public async Task ParseAsync_WithPathWithoutSchemeEndingInDb_ReturnsSqliteSuccess()
    {
        const string input = "/tmp/sample.db";

        ConnectionUrlParseResultDto result = await _sut.ParseAsync(input, selectedProvider: null);
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal(DatabaseProvider.SQLite.ToString(), result.SuggestedProvider);
        Assert.Equal($"file://{input}", result.NormalizedUrl);
        Assert.Equal(input, fields["database"]);
        Assert.Equal("0", fields["port"]);
        Assert.Equal(AppConstants.DefaultHost, fields["host"]);
    }

    [Fact]
    public async Task ParseAsync_WithoutSchemeAndUnknownExtension_ReturnsFailed()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync("not_a_connection_string", selectedProvider: null);

        Assert.Equal(ConnectionUrlParseStatusDto.Failed, result.ParseStatus);
        Assert.Equal("Could not determine database type from URL.", result.UserMessage);
    }

    [Fact]
    public async Task ParseAsync_WithUnsupportedScheme_ReturnsFailed()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync("oracle://localhost/db", selectedProvider: null);

        Assert.Equal(ConnectionUrlParseStatusDto.Failed, result.ParseStatus);
        Assert.Equal("Unsupported provider scheme: oracle.", result.UserMessage);
    }

    [Fact]
    public async Task ParseAsync_WithMalformedAbsoluteUrl_ReturnsFailed()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync("postgres://[::1/db", selectedProvider: null);

        Assert.Equal(ConnectionUrlParseStatusDto.Failed, result.ParseStatus);
        Assert.Equal("Invalid connection URL.", result.UserMessage);
    }

    [Fact]
    public async Task ParseAsync_WithCredentialsConflictAndUnknownTokens_ReturnsPartialAndPreservesNormalizedCredentials()
    {
        const string url = "postgres://user%20name:p%40ss@db.example.com:5432/mydb?ssl=true&foo=bar";

        ConnectionUrlParseResultDto result = await _sut.ParseAsync(url, selectedProvider: "mysql");
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Partial, result.ParseStatus);
        Assert.True(result.ConflictWithSelectedProvider);
        Assert.Single(result.UnrecognizedTokens);
        Assert.Equal("foo", result.UnrecognizedTokens[0]);
        Assert.Equal("user name", fields["username"]);
        Assert.Equal("p@ss", fields["password"]);
        Assert.Equal("True", fields["useSsl"]);
        Assert.Equal("postgres://user%20name:p%40ss@db.example.com:5432/mydb?ssl=true&foo=bar", result.NormalizedUrl);
    }

    [Fact]
    public async Task ParseAsync_WithUsernameOnlyCredential_NormalizedUrlKeepsUserWithoutColon()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "postgres://alice@localhost/dbname",
            selectedProvider: null);
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal("alice", fields["username"]);
        Assert.Equal(string.Empty, fields["password"]);
        Assert.Equal("postgres://alice@localhost/dbname", result.NormalizedUrl);
    }

    [Fact]
    public async Task ParseAsync_WithSqlModeAndSecurityFlags_ResolvesExpectedBooleans()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "postgres://localhost/db?sslmode=disable&integratedsecurity=true&trustservercertificate=false",
            selectedProvider: "postgres");
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal("False", fields["useSsl"]);
        Assert.Equal("False", fields["trustServerCertificate"]);
        Assert.Equal("True", fields["useIntegratedSecurity"]);
    }

    [Fact]
    public async Task ParseAsync_WithSslModeNone_DisablesSsl()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "postgres://localhost/db?sslmode=none",
            selectedProvider: null);
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal("False", fields["useSsl"]);
    }

    [Fact]
    public async Task ParseAsync_WithSqlServerEncryptAndTrustedConnection_SetsDefaultPortWhenPortIsZero()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "mssql://localhost:0/mydb?encrypt=require&trusted_connection=yes",
            selectedProvider: "mssql");
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal(ConnectionProfile.DefaultPort(DatabaseProvider.SqlServer).ToString(), fields["port"]);
        Assert.Equal("True", fields["useSsl"]);
        Assert.Equal("True", fields["useIntegratedSecurity"]);
        Assert.Equal("True", fields["trustServerCertificate"]);
    }

    [Fact]
    public async Task ParseAsync_WithSqliteFileScheme_UsesLocalPathAsDatabase()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "file:///tmp/quick-preview.sqlite",
            selectedProvider: null);
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal(DatabaseProvider.SQLite.ToString(), result.SuggestedProvider);
        Assert.Contains("quick-preview.sqlite", fields["database"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseAsync_WithSqliteScheme_UsesAbsolutePathDatabase()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "sqlite://localhost/tmp/quick-preview.sqlite",
            selectedProvider: null);
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal(DatabaseProvider.SQLite.ToString(), result.SuggestedProvider);
        Assert.Equal("tmp/quick-preview.sqlite", fields["database"]);
    }

    [Fact]
    public async Task ParseAsync_WithEmptyCredentialSection_DoesNotInjectAtSymbolIntoNormalizedUrl()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "postgres://@localhost/mydb",
            selectedProvider: null);
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal(string.Empty, fields["username"]);
        Assert.Equal(string.Empty, fields["password"]);
        Assert.Equal("postgres://localhost/mydb", result.NormalizedUrl);
    }

    [Fact]
    public async Task ParseAsync_WithPasswordOnlyCredential_PreservesCredentialInNormalizedUrl()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "postgres://:p%40ss@localhost/mydb",
            selectedProvider: null);
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal(string.Empty, fields["username"]);
        Assert.Equal("p@ss", fields["password"]);
        Assert.Equal("postgres://:p%40ss@localhost/mydb", result.NormalizedUrl);
    }

    [Fact]
    public async Task ParseAsync_WithSslWithoutValue_UsesFalseAndKeepsRecognizedToken()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "postgres://localhost/mydb?ssl",
            selectedProvider: null);
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal("False", fields["useSsl"]);
        Assert.Empty(result.UnrecognizedTokens);
    }

    [Fact]
    public async Task ParseAsync_WithEncryptFalse_UsesBoolParsingBranch()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "mssql://localhost/mydb?encrypt=false",
            selectedProvider: null);
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal("False", fields["useSsl"]);
    }

    [Fact]
    public async Task ParseAsync_WithoutSecurityQuery_UsesDefaults()
    {
        ConnectionUrlParseResultDto result = await _sut.ParseAsync(
            "postgres://localhost/mydb",
            selectedProvider: "postgres");
        Dictionary<string, string?> fields = ToFieldMap(result);

        Assert.Equal(ConnectionUrlParseStatusDto.Success, result.ParseStatus);
        Assert.Equal("False", fields["useSsl"]);
        Assert.Equal("True", fields["trustServerCertificate"]);
        Assert.Equal("False", fields["useIntegratedSecurity"]);
    }

    private static Dictionary<string, string?> ToFieldMap(ConnectionUrlParseResultDto result) =>
        result.RecognizedFields.ToDictionary(field => field.Key, field => field.Value, StringComparer.OrdinalIgnoreCase);
}
