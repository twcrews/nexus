using Microsoft.Extensions.Options;
using Nexus.Models;
using Nexus.Services;

namespace Nexus.Providers;

public class AggregateDataProvider(
    SessionTokenStore session,
    IHttpClientFactory httpFactory,
    IOptions<GitHubSettings> githubSettings,
    ILogger<AggregateDataProvider> logger,
    ILoggerFactory loggerFactory) : IDataProvider
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
        var providers = new List<IDataProvider>();

        providers.AddRange(accounts.DummyAccounts.Select(t => (IDataProvider)new DummyProvider(t)));

        providers.AddRange(accounts.GitHubAccounts.Select(t =>
            (IDataProvider)new GitHubProvider(t, httpFactory, githubSettings.Value,
                loggerFactory.CreateLogger<GitHubProvider>())));

        return providers;
    }

    private async Task<IEnumerable<T>> AggregateAsync<T>(Func<IDataProvider, Task<IEnumerable<T>>> fetch)
    {
        var tasks = BuildProviders().Select(async p =>
        {
            try { return await fetch(p); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Provider {Provider} failed", p.GetType().Name);
                return Enumerable.Empty<T>();
            }
        });
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(x => x);
    }
}
