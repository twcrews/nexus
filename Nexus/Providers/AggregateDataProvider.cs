using Nexus.Models;
using Nexus.Services;

namespace Nexus.Providers;

public class AggregateDataProvider(SessionTokenStore session) : IDataProvider
{
    public Task<IEnumerable<WorkItem>> GetAssignedWorkItemsAsync() =>
        AggregateAsync(p => p.GetAssignedWorkItemsAsync());

    public Task<IEnumerable<WorkItem>> GetUnassignedWorkItemsAsync() =>
        AggregateAsync(p => p.GetUnassignedWorkItemsAsync());

    public Task<IEnumerable<PullRequest>> GetAssignedPullRequestsAsync() =>
        AggregateAsync(p => p.GetAssignedPullRequestsAsync());

    public Task<IEnumerable<PullRequest>> GetUnassignedPullRequestsAsync() =>
        AggregateAsync(p => p.GetUnassignedPullRequestsAsync());

    private IEnumerable<IDataProvider> BuildProviders()
    {
        var accounts = session.GetLinkedAccounts();

        // Real providers will be constructed here from accounts.MicrosoftAccounts
        // and accounts.GitHubAccounts once ADO/GitHub providers are implemented.
        return accounts.DummyAccounts.Select(t => (IDataProvider)new DummyProvider(t));
    }

    private async Task<IEnumerable<T>> AggregateAsync<T>(Func<IDataProvider, Task<IEnumerable<T>>> fetch)
    {
        var tasks = BuildProviders().Select(async p =>
        {
            try { return await fetch(p); }
            catch { return Enumerable.Empty<T>(); }
        });
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(x => x);
    }
}
