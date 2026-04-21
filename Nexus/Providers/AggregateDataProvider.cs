using Microsoft.AspNetCore.DataProtection;
using Nexus.Models;
using Nexus.Services;

namespace Nexus.Providers;

public class AggregateDataProvider(
    SessionTokenStore session,
    IHttpClientFactory httpFactory,
    ILogger<AggregateDataProvider> logger,
    ILoggerFactory loggerFactory,
    IDataProtectionProvider dataProtection) : IDataProvider
{
    public async Task<DashboardData> GetDashboardDataAsync()
    {
        List<IDataProvider> providers = BuildProviders();
        DashboardData[] results = await Task.WhenAll(providers.Select(async p =>
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

    private List<IDataProvider> BuildProviders()
    {
        LinkedAccounts accounts = session.GetLinkedAccounts();
        var providers = new List<IDataProvider>();

        providers.AddRange(accounts.DummyAccounts.Select(t => (IDataProvider)new DummyProvider(t)));

        providers.AddRange(accounts.GitHubAccounts.Select(t =>
            (IDataProvider)new GitHubProvider(t, httpFactory,
                loggerFactory.CreateLogger<GitHubProvider>())));

        providers.AddRange(accounts.MicrosoftAccounts.Select(t =>
            (IDataProvider)new AdoProvider(t,
                loggerFactory.CreateLogger<AdoProvider>(),
                dataProtection.CreateProtector("Nexus.AvatarProxy.v1"))));

        return providers;
    }
}
