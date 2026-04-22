using Nexus.Models;

namespace Nexus.Providers;

public record DashboardData(
    IEnumerable<WorkItem> WorkItems,
    IEnumerable<PullRequest> PullRequests);

public interface IDataProvider
{
    Task<DashboardData> GetDashboardDataAsync();
}
