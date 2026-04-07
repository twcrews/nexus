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
    IReadOnlyList<UserReference> Reviewers,
    PullRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
