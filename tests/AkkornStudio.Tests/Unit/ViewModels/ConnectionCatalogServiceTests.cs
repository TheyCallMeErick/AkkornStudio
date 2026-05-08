using AkkornStudio.Core;
using AkkornStudio.UI.Services.Connection;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.ConnectionManager.Contracts;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels;

public class ConnectionCatalogServiceTests
{
    [Fact]
    public async Task SaveAndGetDetailsAsync_WhenConnectionExists_ReturnsStoredValues()
    {
        var store = new InMemoryConnectionProfileStore();
        var service = new ConnectionCatalogService(store, new ConnectionProfilesChangedNotifier());

        var details = new ConnectionDetailsDto(
            Id: "conn-1",
            Name: "Local",
            Provider: "Postgres",
            Mode: ConnectionProviderModeDto.Fields,
            FieldValues: new Dictionary<string, string?>
            {
                [ConnectionContractMapper.HostKey] = "localhost",
                [ConnectionContractMapper.PortKey] = "5432",
                [ConnectionContractMapper.DatabaseKey] = "db",
                [ConnectionContractMapper.UsernameKey] = "user",
                [ConnectionContractMapper.PasswordKey] = "secret",
                [ConnectionContractMapper.TimeoutSecondsKey] = "30",
            },
            UrlValue: null,
            Tag: null,
            IsFavorite: false,
            AdvancedOptions: new Dictionary<string, string?>());

        OperationResultDto<ConnectionDetailsDto> saved = await service.SaveAsync(details);
        OperationResultDto<ConnectionDetailsDto> fetched = await service.GetDetailsAsync("conn-1");

        Assert.True(saved.Success);
        Assert.True(fetched.Success);
        Assert.NotNull(fetched.Payload);
        Assert.Equal("Local", fetched.Payload!.Name);
        Assert.Equal("Postgres", fetched.Payload.Provider);
        Assert.Equal("db", fetched.Payload.FieldValues[ConnectionContractMapper.DatabaseKey]);
    }

    [Fact]
    public async Task ListSummariesAsync_WhenProfilesExist_ReturnsOrderedByName()
    {
        var store = new InMemoryConnectionProfileStore(
            BuildProfile("b", "Zulu"),
            BuildProfile("a", "Alpha"));
        var service = new ConnectionCatalogService(store, new ConnectionProfilesChangedNotifier());

        IReadOnlyList<ConnectionSummaryDto> summaries = await service.ListSummariesAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Equal("Alpha", summaries[0].Name);
        Assert.Equal("Zulu", summaries[1].Name);
    }

    [Fact]
    public async Task DeleteAsync_WhenConnectionExists_RemovesIt()
    {
        var store = new InMemoryConnectionProfileStore(BuildProfile("a", "Alpha"));
        var service = new ConnectionCatalogService(store, new ConnectionProfilesChangedNotifier());

        OperationResultDto<bool> deleted = await service.DeleteAsync("a");
        IReadOnlyList<ConnectionSummaryDto> summaries = await service.ListSummariesAsync();

        Assert.True(deleted.Success);
        Assert.True(deleted.Payload);
        Assert.Empty(summaries);
    }

    private static ConnectionProfile BuildProfile(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db",
            Username = "user",
            Password = "secret",
            RememberPassword = true,
            UseSsl = false,
            TrustServerCertificate = true,
            UseIntegratedSecurity = false,
            TimeoutSeconds = 30,
        };

    private sealed class InMemoryConnectionProfileStore : IConnectionProfileStore
    {
        private readonly List<ConnectionProfile> _profiles;

        public InMemoryConnectionProfileStore(params ConnectionProfile[] initialProfiles)
        {
            _profiles = initialProfiles.ToList();
        }

        public IReadOnlyList<ConnectionProfile> LoadProfiles(CredentialVaultStore credentialVault)
        {
            return _profiles
                .Select(Clone)
                .ToList();
        }

        public void PersistProfiles(IEnumerable<ConnectionProfile> profiles, CredentialVaultStore credentialVault)
        {
            _profiles.Clear();
            _profiles.AddRange(profiles.Select(Clone));
        }

        private static ConnectionProfile Clone(ConnectionProfile source) =>
            new()
            {
                Id = source.Id,
                Name = source.Name,
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
    }
}
