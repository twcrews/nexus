using Nexus.Models;

namespace Nexus.Providers;

public record DashboardData(
    IEnumerable<WorkItem> AssignedWorkItems,
    IEnumerable<WorkItem> UnassignedWorkItems,
    IEnumerable<PullRequest> AssignedPullRequests,
    IEnumerable<PullRequest> UnassignedPullRequests);

public interface IDataProvider
{
    Task<DashboardData> GetDashboardDataAsync();
}
