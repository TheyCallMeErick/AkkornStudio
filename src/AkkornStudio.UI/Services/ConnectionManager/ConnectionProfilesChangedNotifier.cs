using AkkornStudio.UI.Services.ConnectionManager.Contracts;

namespace AkkornStudio.UI.Services.ConnectionManager;

public sealed class ConnectionProfilesChangedNotifier : IConnectionProfilesChangedNotifier
{
    public event Action<ConnectionProfilesChangedEventArgs>? ProfilesChanged;

    public void NotifyProfilesChanged()
    {
        ProfilesChanged?.Invoke(new ConnectionProfilesChangedEventArgs());
    }
}
