using Nexus.Models;

namespace Nexus.Providers;

public interface IDataProvider
{
    Task<IEnumerable<WorkItem>> GetAssignedWorkItemsAsync();
    Task<IEnumerable<WorkItem>> GetUnassignedWorkItemsAsync();
    Task<IEnumerable<PullRequest>> GetAssignedPullRequestsAsync();
    Task<IEnumerable<PullRequest>> GetUnassignedPullRequestsAsync();
}
