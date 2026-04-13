namespace Nexus.Services;

public class RefreshService
{
    public DateTimeOffset? LastRefreshed { get; private set; }

    public event Action? Refreshed;

    public void NotifyRefreshed()
    {
        LastRefreshed = DateTimeOffset.UtcNow;
        Refreshed?.Invoke();
    }
}
