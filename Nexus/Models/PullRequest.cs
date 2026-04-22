namespace Nexus.Models;

public enum PullRequestStatus { Draft, Open, Merged, Closed }

public record PullRequest(
    string Id,
    string Title,
    string? Description,
    UserReference Creator,
    RepoBranch Source,
    RepoBranch Target,
    IReadOnlyList<UserReference> Assignees,
    IReadOnlyList<ReviewerReference> Reviewers,
    PullRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Url = null,
    string? MergeStatus = null,
    bool AutoComplete = false,
    IReadOnlyList<string>? Labels = null,
    IReadOnlyList<string>? LinkedWorkItemIds = null,
    IReadOnlyDictionary<string, string>? LinkedWorkItemUrls = null,
    DataProvider Provider = DataProvider.GitHub,
    bool IsAssignedToCurrentUser = false,
    bool WasCreatedByCurrentUser = false
);
