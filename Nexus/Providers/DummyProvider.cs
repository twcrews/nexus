using Nexus.Models;

namespace Nexus.Providers;

public class DummyProvider : IDataProvider
{
    private static readonly string[] Names =
    [
        "Alice Chen", "Bob Martinez", "Carol Johnson", "David Kim",
        "Eve Williams", "Frank Lee", "Grace Patel", "Henry Brown",
    ];

    private static readonly string[] WorkItemTitles =
    [
        "Fix login timeout after session expiry",
        "Add dark mode toggle to settings panel",
        "Refactor token refresh logic",
        "Investigate memory leak in background worker",
        "Implement CSV export for work item list",
        "Update dependency: Radzen.Blazor 10.3",
        "Add unit tests for SessionTokenStore",
        "Improve error messaging on OAuth failure",
        "Reduce dashboard initial load time",
        "Handle rate limiting from GitHub API",
        "Add support for multiple ADO organizations",
        "Fix avatar not loading for some users",
        "Implement label filtering in work item view",
        "Add pagination to PR list",
        "Resolve inconsistency in config schema",
    ];

    private static readonly string[] WorkItemLabels =
        ["auth", "ui", "perf", "bug", "dx", "api", "infra", "testing"];

    private static readonly string[] RepoNames =
        ["nexus", "api-gateway", "auth-service", "dashboard-ui", "data-pipeline"];

    private static readonly string[] BranchPrefixes =
        ["feature/", "fix/", "chore/", "refactor/"];

    private static readonly string[] BranchSlugs =
    [
        "login-timeout", "dark-mode", "token-refresh", "rate-limiting",
        "export-csv", "pagination", "label-filter", "error-messages",
        "perf-improvements", "multi-org-support",
    ];

    private readonly IReadOnlyList<WorkItem> _assignedWorkItems;
    private readonly IReadOnlyList<WorkItem> _unassignedWorkItems;
    private readonly IReadOnlyList<PullRequest> _assignedPullRequests;
    private readonly IReadOnlyList<PullRequest> _unassignedPullRequests;

    public DummyProvider()
    {
        var rng = new Random(42);
        var now = DateTimeOffset.UtcNow;
        var users = Names
            .Select(n => new UserReference(n, $"https://i.pravatar.cc/64?u={Uri.EscapeDataString(n)}"))
            .ToArray();

        _assignedWorkItems = GenerateWorkItems(rng, users, now, count: 8, assignedTo: users[0]);
        _unassignedWorkItems = GenerateWorkItems(rng, users, now, count: 7, assignedTo: null);
        _assignedPullRequests = GeneratePullRequests(rng, users, now, count: 5, assignedTo: users[0]);
        _unassignedPullRequests = GeneratePullRequests(rng, users, now, count: 5, assignedTo: null);
    }

    public Task<IEnumerable<WorkItem>> GetAssignedWorkItemsAsync() =>
        Task.FromResult<IEnumerable<WorkItem>>(_assignedWorkItems);

    public Task<IEnumerable<WorkItem>> GetUnassignedWorkItemsAsync() =>
        Task.FromResult<IEnumerable<WorkItem>>(_unassignedWorkItems);

    public Task<IEnumerable<PullRequest>> GetAssignedPullRequestsAsync() =>
        Task.FromResult<IEnumerable<PullRequest>>(_assignedPullRequests);

    public Task<IEnumerable<PullRequest>> GetUnassignedPullRequestsAsync() =>
        Task.FromResult<IEnumerable<PullRequest>>(_unassignedPullRequests);

    private static List<WorkItem> GenerateWorkItems(
        Random rng, UserReference[] users, DateTimeOffset now, int count, UserReference? assignedTo)
    {
        var items = new List<WorkItem>(count);
        var usedTitleIndices = new HashSet<int>();

        for (var i = 0; i < count; i++)
        {
            int titleIdx;
            do { titleIdx = rng.Next(WorkItemTitles.Length); } while (!usedTitleIndices.Add(titleIdx));

            var createdDaysAgo = rng.Next(1, 60);
            var labels = Enumerable.Range(0, rng.Next(0, 4))
                .Select(_ => WorkItemLabels[rng.Next(WorkItemLabels.Length)])
                .Distinct()
                .ToList();

            items.Add(new WorkItem(
                Id: $"WI-{rng.Next(1000, 9999)}",
                Type: rng.NextEnum<WorkItemType>(),
                Title: WorkItemTitles[titleIdx],
                Description: rng.Next(2) == 0 ? $"Details: {WorkItemTitles[titleIdx]}." : null,
                Creator: users[rng.Next(users.Length)],
                Assignee: assignedTo,
                Status: rng.NextEnum<WorkItemStatus>(),
                CreatedAt: now.AddDays(-createdDaysAgo),
                UpdatedAt: now.AddDays(-rng.Next(0, createdDaysAgo)),
                Labels: labels
            ));
        }

        return items;
    }

    private static List<PullRequest> GeneratePullRequests(
        Random rng, UserReference[] users, DateTimeOffset now, int count, UserReference? assignedTo)
    {
        var prs = new List<PullRequest>(count);

        for (var i = 0; i < count; i++)
        {
            var repo = RepoNames[rng.Next(RepoNames.Length)];
            var slug = BranchSlugs[rng.Next(BranchSlugs.Length)];
            var prefix = BranchPrefixes[rng.Next(BranchPrefixes.Length)];
            var createdDaysAgo = rng.Next(1, 30);
            UserReference[] assignees = assignedTo is not null ? [assignedTo] : [];
            var reviewers = Enumerable.Range(0, rng.Next(1, 4))
                .Select(_ => users[rng.Next(users.Length)])
                .Distinct()
                .ToList();

            prs.Add(new PullRequest(
                Id: $"PR-{rng.Next(100, 999)}",
                Title: $"{prefix.TrimEnd('/')}: {slug.Replace('-', ' ')}",
                Description: rng.Next(2) == 0 ? $"This PR addresses {slug.Replace('-', ' ')}." : null,
                Creator: users[rng.Next(users.Length)],
                Source: new RepoBranch(repo, $"{prefix}{slug}"),
                Target: new RepoBranch(repo, "main"),
                Assignees: assignees,
                Reviewers: reviewers,
                Status: rng.NextEnum<PullRequestStatus>(),
                CreatedAt: now.AddDays(-createdDaysAgo),
                UpdatedAt: now.AddDays(-rng.Next(0, createdDaysAgo))
            ));
        }

        return prs;
    }
}

file static class RandomExtensions
{
    public static T NextEnum<T>(this Random rng) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return values[rng.Next(values.Length)];
    }
}
