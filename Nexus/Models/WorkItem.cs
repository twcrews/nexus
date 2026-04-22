namespace Nexus.Models;

public enum WorkItemType { Bug, Task, UserStory, Feature, Epic }

public enum WorkItemStatus { New, Active, InProgress, Resolved, Closed, Blocked }

public enum DataProvider { GitHub, AzureDevOps }

public record WorkItem(
    string Id,
    WorkItemType Type,
    string Title,
    string? Description,
    UserReference Creator,
    UserReference? Assignee,
    WorkItemStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Labels,
    string? Url = null,
    DataProvider Provider = DataProvider.GitHub
);
