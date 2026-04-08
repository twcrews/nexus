namespace Nexus.Services;

public class RefreshService
{
    public DateTimeOffset? LastRefreshed { get; private set; }

    public void NotifyRefreshed() => LastRefreshed = DateTimeOffset.UtcNow;
}
