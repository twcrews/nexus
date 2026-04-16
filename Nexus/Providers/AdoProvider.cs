using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Nexus.Models;
using AdoPrStatus = Microsoft.TeamFoundation.SourceControl.WebApi.PullRequestStatus;
using NexusPrStatus = Nexus.Models.PullRequestStatus;
using NexusWiType = Nexus.Models.WorkItemType;

namespace Nexus.Providers;

public class AdoProvider(
    MicrosoftAccountToken token,
    ILogger<AdoProvider> logger) : IDataProvider
{
    public async Task<DashboardData> GetDashboardDataAsync()
    {
        if (token.MonitoredProjects.Count == 0)
            return new DashboardData([], [], [], []);

        var credentials = new VssBasicCredential(string.Empty, token.PersonalAccessToken);

        var assignedWorkItems = new List<Models.WorkItem>();
        var unassignedWorkItems = new List<Models.WorkItem>();
        var assignedPrs = new List<Models.PullRequest>();
        var unassignedPrs = new List<Models.PullRequest>();

        await Task.WhenAll(token.MonitoredProjects.Select(async project =>
        {
            try
            {
                var connection = new VssConnection(new Uri(project.OrgUrl), credentials);

                var (wi, uwi) = await FetchWorkItemsAsync(connection, project);
                var (prs, uprs) = await FetchPullRequestsAsync(connection, project);

                lock (assignedWorkItems)
                {
                    assignedWorkItems.AddRange(wi);
                    unassignedWorkItems.AddRange(uwi);
                    assignedPrs.AddRange(prs);
                    unassignedPrs.AddRange(uprs);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch ADO data for {OrgUrl}/{Project}",
                    project.OrgUrl, project.ProjectName);
            }
        }));

        return new DashboardData(assignedWorkItems, unassignedWorkItems, assignedPrs, unassignedPrs);
    }

    private async Task<(List<Models.WorkItem> assigned, List<Models.WorkItem> unassigned)> FetchWorkItemsAsync(
        VssConnection connection, AdoMonitoredProject project)
    {
        if (project.TeamNames.Count == 0)
            return ([], []);

        var client = await connection.GetClientAsync<WorkItemTrackingHttpClient>();
        string[] fields = [
            "System.Id", "System.Title", "System.Description", "System.WorkItemType",
            "System.State", "System.CreatedDate", "System.ChangedDate",
            "System.AssignedTo", "System.CreatedBy", "System.Tags"
        ];

        var assignedWiql = new Wiql
        {
            Query = $"""
                SELECT [System.Id] FROM WorkItems
                WHERE [System.AssignedTo] = @Me
                AND [System.TeamProject] = '{EscapeWiql(project.ProjectName)}'
                AND [System.State] NOT IN ('Closed', 'Done', 'Removed', 'Resolved')
                ORDER BY [System.ChangedDate] DESC
                """
        };
        var unassignedWiql = new Wiql
        {
            Query = $"""
                SELECT [System.Id] FROM WorkItems
                WHERE [System.AssignedTo] = ''
                AND [System.TeamProject] = '{EscapeWiql(project.ProjectName)}'
                AND [System.State] NOT IN ('Closed', 'Done', 'Removed', 'Resolved')
                ORDER BY [System.ChangedDate] DESC
                """
        };

        var assignedResult = await client.QueryByWiqlAsync(assignedWiql, top: 100);
        var unassignedResult = await client.QueryByWiqlAsync(unassignedWiql, top: 100);

        var assignedIds = assignedResult.WorkItems.Select(w => w.Id).ToList();
        var unassignedIds = unassignedResult.WorkItems.Select(w => w.Id).ToList();

        var assignedDetails = assignedIds.Count > 0
            ? await client.GetWorkItemsAsync(assignedIds, fields)
            : [];
        var unassignedDetails = unassignedIds.Count > 0
            ? await client.GetWorkItemsAsync(unassignedIds, fields)
            : [];

        return (
            assignedDetails.Select(MapWorkItem).ToList(),
            unassignedDetails.Select(MapWorkItem).ToList()
        );
    }

    private async Task<(List<Models.PullRequest> assigned, List<Models.PullRequest> unassigned)> FetchPullRequestsAsync(
        VssConnection connection, AdoMonitoredProject project)
    {
        if (project.RepoNames.Count == 0)
            return ([], []);

        var client = await connection.GetClientAsync<GitHttpClient>();
        var assigned = new List<Models.PullRequest>();
        var unassigned = new List<Models.PullRequest>();

        foreach (var repoName in project.RepoNames)
        {
            try
            {
                var searchCriteria = new GitPullRequestSearchCriteria { Status = AdoPrStatus.Active };
                var prs = await client.GetPullRequestsAsync(
                    project.ProjectName, repoName, searchCriteria);

                foreach (var pr in prs)
                {
                    var mapped = MapPullRequest(pr, repoName);
                    bool isReviewer = pr.Reviewers.Any(r =>
                        string.Equals(r.UniqueName, token.Login, StringComparison.OrdinalIgnoreCase));

                    if (isReviewer)
                        assigned.Add(mapped);
                    else if (pr.IsDraft != true && pr.Reviewers.Length == 0)
                        unassigned.Add(mapped);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch PRs for {Repo} in {Project}", repoName, project.ProjectName);
            }
        }

        return (assigned, unassigned);
    }

    private static Models.WorkItem MapWorkItem(Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem wi)
    {
        string? Get(string field) => wi.Fields.TryGetValue(field, out var v) ? v?.ToString() : null;

        var type = (Get("System.WorkItemType") ?? "").ToLowerInvariant() switch
        {
            "bug" => NexusWiType.Bug,
            "epic" => NexusWiType.Epic,
            "feature" => NexusWiType.Feature,
            "user story" or "story" => NexusWiType.UserStory,
            _ => NexusWiType.Task
        };

        var status = (Get("System.State") ?? "").ToLowerInvariant() switch
        {
            "new" => WorkItemStatus.New,
            "active" => WorkItemStatus.Active,
            "in progress" => WorkItemStatus.InProgress,
            "resolved" => WorkItemStatus.Resolved,
            "closed" or "done" => WorkItemStatus.Closed,
            "blocked" => WorkItemStatus.Blocked,
            _ => WorkItemStatus.Active
        };

        string? assigneeName = wi.Fields.TryGetValue("System.AssignedTo", out var at)
            ? (at is IdentityRef ir ? ir.DisplayName : at?.ToString()) : null;
        string? creatorName = wi.Fields.TryGetValue("System.CreatedBy", out var cb)
            ? (cb is IdentityRef cir ? cir.DisplayName : cb?.ToString()) : null;

        var tags = Get("System.Tags")
            ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        var created = wi.Fields.TryGetValue("System.CreatedDate", out var cd) && cd is DateTime cdt
            ? new DateTimeOffset(cdt) : DateTimeOffset.MinValue;
        var updated = wi.Fields.TryGetValue("System.ChangedDate", out var ud) && ud is DateTime udt
            ? new DateTimeOffset(udt) : DateTimeOffset.MinValue;

        return new Models.WorkItem(
            Id: wi.Id?.ToString() ?? "",
            Type: type,
            Title: Get("System.Title") ?? "",
            Description: Get("System.Description"),
            Creator: new UserReference(creatorName ?? "Unknown", null),
            Assignee: assigneeName is not null ? new UserReference(assigneeName, null) : null,
            Status: status,
            CreatedAt: created,
            UpdatedAt: updated,
            Labels: tags);
    }

    private static Models.PullRequest MapPullRequest(GitPullRequest pr, string repoName)
    {
        var status = pr.Status switch
        {
            AdoPrStatus.Active when pr.IsDraft == true => NexusPrStatus.Draft,
            AdoPrStatus.Active => NexusPrStatus.Open,
            AdoPrStatus.Completed => NexusPrStatus.Merged,
            _ => NexusPrStatus.Closed
        };

        static string BranchName(string? refName) =>
            refName?.StartsWith("refs/heads/") == true ? refName[11..] : refName ?? "";

        return new Models.PullRequest(
            Id: pr.PullRequestId.ToString(),
            Title: pr.Title,
            Description: pr.Description,
            Creator: new UserReference(pr.CreatedBy?.DisplayName ?? "Unknown", pr.CreatedBy?.ImageUrl),
            Source: new RepoBranch(repoName, BranchName(pr.SourceRefName)),
            Target: new RepoBranch(repoName, BranchName(pr.TargetRefName)),
            Assignees: [],
            Reviewers: pr.Reviewers
                .Select(r => new UserReference(r.DisplayName, r.ImageUrl))
                .ToList(),
            Status: status,
            CreatedAt: pr.CreationDate,
            UpdatedAt: pr.CreationDate);
    }

    private static string EscapeWiql(string value) => value.Replace("'", "''");
}
