namespace Nexus.Models;

public record WorkItemDetails(
    string? AcceptanceCriteria = null,
    string? Priority = null,
    string? IterationPath = null,
    string? AreaPath = null,
    string? Effort = null,        // pre-formatted: "5 pts" or "3 h"
    string? RemainingWork = null, // pre-formatted: "2 h"
    string? ParentId = null,
    string? ParentUrl = null,
    string? Milestone = null,
    int? CommentCount = null
);
