using Nexus.Services;

namespace Nexus.Tests;

public class RefreshServiceTests
{
    [Fact]
    public void LastRefreshed_IsNullBeforeFirstNotify()
    {
        var svc = new RefreshService();
        Assert.Null(svc.LastRefreshed);
    }

    [Fact]
    public void NotifyRefreshed_SetsLastRefreshed()
    {
        var svc = new RefreshService();
        var before = DateTimeOffset.UtcNow;
        svc.NotifyRefreshed();
        Assert.NotNull(svc.LastRefreshed);
        Assert.True(svc.LastRefreshed >= before);
        Assert.True(svc.LastRefreshed <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void NotifyRefreshed_UpdatesLastRefreshedOnSubsequentCalls()
    {
        var svc = new RefreshService();
        svc.NotifyRefreshed();
        var first = svc.LastRefreshed;

        // Small delay so timestamps differ
        Thread.Sleep(5);
        svc.NotifyRefreshed();

        Assert.True(svc.LastRefreshed > first);
    }

    [Fact]
    public void NotifyRefreshed_FiresRefreshedEvent()
    {
        var svc = new RefreshService();
        var fired = false;
        svc.Refreshed += () => fired = true;

        svc.NotifyRefreshed();

        Assert.True(fired);
    }

    [Fact]
    public void NotifyRefreshed_FiresAllSubscribers()
    {
        var svc = new RefreshService();
        var count = 0;
        svc.Refreshed += () => count++;
        svc.Refreshed += () => count++;

        svc.NotifyRefreshed();

        Assert.Equal(2, count);
    }

    [Fact]
    public void NotifyRefreshed_NoSubscribers_DoesNotThrow()
    {
        var svc = new RefreshService();
        var ex = Record.Exception(svc.NotifyRefreshed);
        Assert.Null(ex);
    }
}
