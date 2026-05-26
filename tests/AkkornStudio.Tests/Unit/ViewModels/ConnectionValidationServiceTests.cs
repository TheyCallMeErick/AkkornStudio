using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.ConnectionManager.Contracts;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels;

public class ConnectionValidationServiceTests
{
    [Fact]
    public void Validate_WhenRequiredFieldsMissing_ReturnsErrors()
    {
        var service = new ConnectionValidationService();
        ProviderCapabilityDto capability = BuildPostgresCapability();
        var details = new ConnectionDetailsDto(
            Id: Guid.NewGuid().ToString(),
            Name: string.Empty,
            Provider: "Postgres",
            Mode: ConnectionProviderModeDto.Fields,
            FieldValues: new Dictionary<string, string?>
            {
                [ConnectionContractMapper.HostKey] = string.Empty,
                [ConnectionContractMapper.PortKey] = "0",
                [ConnectionContractMapper.DatabaseKey] = string.Empty,
                [ConnectionContractMapper.UsernameKey] = string.Empty,
                [ConnectionContractMapper.TimeoutSecondsKey] = "0",
            },
            UrlValue: null,
            Tag: null,
            IsFavorite: false,
            AdvancedOptions: new Dictionary<string, string?>());

        ConnectionValidationResultDto result = service.Validate(details, capability);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.FieldKey == "name");
        Assert.Contains(result.Errors, e => e.FieldKey == ConnectionContractMapper.HostKey);
        Assert.Contains(result.Errors, e => e.FieldKey == ConnectionContractMapper.DatabaseKey);
        Assert.Contains(result.Errors, e => e.FieldKey == ConnectionContractMapper.UsernameKey);
        Assert.Contains(result.Errors, e => e.FieldKey == ConnectionContractMapper.PortKey);
        Assert.Contains(result.Errors, e => e.FieldKey == ConnectionContractMapper.TimeoutSecondsKey);
    }

    [Fact]
    public void Validate_WhenUrlModeAndUrlMissing_ReturnsUrlError()
    {
        var service = new ConnectionValidationService();
        ProviderCapabilityDto capability = BuildPostgresCapability();
        var details = new ConnectionDetailsDto(
            Id: Guid.NewGuid().ToString(),
            Name: "Local",
            Provider: "Postgres",
            Mode: ConnectionProviderModeDto.Url,
            FieldValues: new Dictionary<string, string?>(),
            UrlValue: string.Empty,
            Tag: null,
            IsFavorite: false,
            AdvancedOptions: new Dictionary<string, string?>());

        ConnectionValidationResultDto result = service.Validate(details, capability);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldKey == "url");
    }

    [Fact]
    public void Validate_WhenFieldsAreValid_ReturnsValidResult()
    {
        var service = new ConnectionValidationService();
        ProviderCapabilityDto capability = BuildPostgresCapability();
        var details = new ConnectionDetailsDto(
            Id: Guid.NewGuid().ToString(),
            Name: "Local",
            Provider: "Postgres",
            Mode: ConnectionProviderModeDto.Fields,
            FieldValues: new Dictionary<string, string?>
            {
                [ConnectionContractMapper.HostKey] = "localhost",
                [ConnectionContractMapper.PortKey] = "5432",
                [ConnectionContractMapper.DatabaseKey] = "db",
                [ConnectionContractMapper.UsernameKey] = "user",
                [ConnectionContractMapper.TimeoutSecondsKey] = "30",
                [ConnectionContractMapper.UseIntegratedSecurityKey] = "false",
            },
            UrlValue: null,
            Tag: null,
            IsFavorite: false,
            AdvancedOptions: new Dictionary<string, string?>());

        ConnectionValidationResultDto result = service.Validate(details, capability);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WhenSqlServerTrustServerCertificateEnabled_ReturnsSecurityWarning()
    {
        var service = new ConnectionValidationService();
        ProviderCapabilityDto capability = BuildSqlServerCapability();
        var details = new ConnectionDetailsDto(
            Id: Guid.NewGuid().ToString(),
            Name: "SqlServer Local",
            Provider: "SqlServer",
            Mode: ConnectionProviderModeDto.Fields,
            FieldValues: new Dictionary<string, string?>
            {
                [ConnectionContractMapper.HostKey] = "localhost",
                [ConnectionContractMapper.PortKey] = "1433",
                [ConnectionContractMapper.DatabaseKey] = "db",
                [ConnectionContractMapper.UsernameKey] = "user",
                [ConnectionContractMapper.TimeoutSecondsKey] = "30",
                [ConnectionContractMapper.UseIntegratedSecurityKey] = "false",
                [ConnectionContractMapper.TrustServerCertificateKey] = "true",
            },
            UrlValue: null,
            Tag: null,
            IsFavorite: false,
            AdvancedOptions: new Dictionary<string, string?>());

        ConnectionValidationResultDto result = service.Validate(details, capability);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.FieldKey == ConnectionContractMapper.TrustServerCertificateKey);
    }

    [Fact]
    public void Validate_WhenSqlServerTrustServerCertificateDisabled_DoesNotEmitTrustWarning()
    {
        var service = new ConnectionValidationService();
        ProviderCapabilityDto capability = BuildSqlServerCapability();
        var details = new ConnectionDetailsDto(
            Id: Guid.NewGuid().ToString(),
            Name: "SqlServer Local",
            Provider: "SqlServer",
            Mode: ConnectionProviderModeDto.Fields,
            FieldValues: new Dictionary<string, string?>
            {
                [ConnectionContractMapper.HostKey] = "localhost",
                [ConnectionContractMapper.PortKey] = "1433",
                [ConnectionContractMapper.DatabaseKey] = "db",
                [ConnectionContractMapper.UsernameKey] = "user",
                [ConnectionContractMapper.TimeoutSecondsKey] = "30",
                [ConnectionContractMapper.UseIntegratedSecurityKey] = "false",
                [ConnectionContractMapper.TrustServerCertificateKey] = "false",
            },
            UrlValue: null,
            Tag: null,
            IsFavorite: false,
            AdvancedOptions: new Dictionary<string, string?>());

        ConnectionValidationResultDto result = service.Validate(details, capability);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Warnings, w => w.FieldKey == ConnectionContractMapper.TrustServerCertificateKey);
    }

    private static ProviderCapabilityDto BuildPostgresCapability() =>
        new(
            Provider: "Postgres",
            SupportsUrlMode: true,
            SupportsSsl: true,
            SupportsIntegratedSecurity: false,
            RequiresDatabase: true,
            SupportedUrlSchemes: ["postgres", "postgresql"],
            RequiredFieldKeys:
            [
                ConnectionContractMapper.HostKey,
                ConnectionContractMapper.PortKey,
                ConnectionContractMapper.DatabaseKey,
                ConnectionContractMapper.UsernameKey,
            ]);

    private static ProviderCapabilityDto BuildSqlServerCapability() =>
        new(
            Provider: "SqlServer",
            SupportsUrlMode: true,
            SupportsSsl: true,
            SupportsIntegratedSecurity: true,
            RequiresDatabase: true,
            SupportedUrlSchemes: ["sqlserver", "mssql"],
            RequiredFieldKeys:
            [
                ConnectionContractMapper.HostKey,
                ConnectionContractMapper.PortKey,
                ConnectionContractMapper.DatabaseKey,
            ]);
}
