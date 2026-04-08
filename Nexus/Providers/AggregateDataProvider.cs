using Nexus.Models;

namespace Nexus.Providers;

public class AggregateDataProvider(IEnumerable<IDataProvider> providers) : IDataProvider
{
    public Task<IEnumerable<WorkItem>> GetAssignedWorkItemsAsync() =>
        AggregateAsync(p => p.GetAssignedWorkItemsAsync());

    public Task<IEnumerable<WorkItem>> GetUnassignedWorkItemsAsync() =>
        AggregateAsync(p => p.GetUnassignedWorkItemsAsync());

    public Task<IEnumerable<PullRequest>> GetAssignedPullRequestsAsync() =>
        AggregateAsync(p => p.GetAssignedPullRequestsAsync());

    public Task<IEnumerable<PullRequest>> GetUnassignedPullRequestsAsync() =>
        AggregateAsync(p => p.GetUnassignedPullRequestsAsync());

    private async Task<IEnumerable<T>> AggregateAsync<T>(Func<IDataProvider, Task<IEnumerable<T>>> fetch)
    {
        var tasks = providers.Select(async p =>
        {
            try { return await fetch(p); }
            catch { return Enumerable.Empty<T>(); }
        });
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(x => x);
    }
}
