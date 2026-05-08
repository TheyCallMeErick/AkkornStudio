namespace AkkornStudio.UI.Services.ConnectionManager.Contracts;

public interface IConnectionProfilesChangedNotifier
{
    event Action<ConnectionProfilesChangedEventArgs>? ProfilesChanged;

    void NotifyProfilesChanged();
}

public sealed record ConnectionProfilesChangedEventArgs;
