using Nexus.Models;
using Nexus.Services;

namespace Nexus.Providers;

public class AggregateDataProvider(
    SessionTokenStore session,
    IHttpClientFactory httpFactory,
    ILogger<AggregateDataProvider> logger,
    ILoggerFactory loggerFactory) : IDataProvider
{
    public async Task<DashboardData> GetDashboardDataAsync()
    {
        var providers = BuildProviders();
        var results = await Task.WhenAll(providers.Select(async p =>
        {
            try { return await p.GetDashboardDataAsync(); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Provider {Provider} failed", p.GetType().Name);
                return new DashboardData([], [], [], []);
            }
        }));

        return new DashboardData(
            AssignedWorkItems: results.SelectMany(r => r.AssignedWorkItems),
            UnassignedWorkItems: results.SelectMany(r => r.UnassignedWorkItems),
            AssignedPullRequests: results.SelectMany(r => r.AssignedPullRequests),
            UnassignedPullRequests: results.SelectMany(r => r.UnassignedPullRequests)
        );
    }

    private IEnumerable<IDataProvider> BuildProviders()
    {
        var accounts = session.GetLinkedAccounts();
        var providers = new List<IDataProvider>();

        providers.AddRange(accounts.DummyAccounts.Select(t => (IDataProvider)new DummyProvider(t)));

        providers.AddRange(accounts.GitHubAccounts.Select(t =>
            (IDataProvider)new GitHubProvider(t, httpFactory,
                loggerFactory.CreateLogger<GitHubProvider>())));

        return providers;
    }
}
