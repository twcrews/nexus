using Microsoft.AspNetCore.DataProtection;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.Work.WebApi;
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
    ILogger<AdoProvider> logger,
    IDataProtector avatarProtector) : IDataProvider
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

        var assignedIds = new HashSet<int>();
        var unassignedIds = new HashSet<int>();

        var workClient = await connection.GetClientAsync<WorkHttpClient>();

        foreach (var teamName in project.TeamNames)
        {
            var teamContext = new Microsoft.TeamFoundation.Core.WebApi.Types.TeamContext(project.ProjectName, teamName);

            var teamFieldValues = await workClient.GetTeamFieldValuesAsync(teamContext);
            var areaFilter = BuildAreaPathFilter(teamFieldValues);

            if (areaFilter is null)
                continue;

            var assignedWiql = new Wiql
            {
                Query = $"""
                    SELECT [System.Id] FROM WorkItems
                    WHERE [System.AssignedTo] = @Me
                    AND [System.TeamProject] = '{EscapeWiql(project.ProjectName)}'
                    AND {areaFilter}
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
                    AND {areaFilter}
                    AND [System.State] NOT IN ('Closed', 'Done', 'Removed', 'Resolved')
                    ORDER BY [System.ChangedDate] DESC
                    """
            };

            var assignedResult = await client.QueryByWiqlAsync(assignedWiql, top: 100);
            var unassignedResult = await client.QueryByWiqlAsync(unassignedWiql, top: 100);

            foreach (var w in assignedResult.WorkItems) assignedIds.Add(w.Id);
            foreach (var w in unassignedResult.WorkItems) unassignedIds.Add(w.Id);
        }

        // Items assigned to someone shouldn't appear in the unassigned list
        unassignedIds.ExceptWith(assignedIds);

        var assignedDetails = assignedIds.Count > 0
            ? await client.GetWorkItemsAsync(assignedIds, fields)
            : [];
        var unassignedDetails = unassignedIds.Count > 0
            ? await client.GetWorkItemsAsync(unassignedIds, fields)
            : [];

        return (
            assignedDetails.Select(wi => MapWorkItem(wi, project.OrgUrl, project.ProjectName)).ToList(),
            unassignedDetails.Select(wi => MapWorkItem(wi, project.OrgUrl, project.ProjectName)).ToList()
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
                    var mapped = this.MapPullRequest(pr, repoName, project);
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

    private Models.WorkItem MapWorkItem(Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem wi, string orgUrl, string projectName)
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
        string? assigneeAvatar = at is IdentityRef irAvatar ? irAvatar.ImageUrl : null;
        string? creatorName = wi.Fields.TryGetValue("System.CreatedBy", out var cb)
            ? (cb is IdentityRef cir ? cir.DisplayName : cb?.ToString()) : null;
        string? creatorAvatar = cb is IdentityRef cirAvatar ? cirAvatar.ImageUrl : null;

        var tags = Get("System.Tags")
            ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        var created = wi.Fields.TryGetValue("System.CreatedDate", out var cd) && cd is DateTime cdt
            ? new DateTimeOffset(cdt) : DateTimeOffset.MinValue;
        var updated = wi.Fields.TryGetValue("System.ChangedDate", out var ud) && ud is DateTime udt
            ? new DateTimeOffset(udt) : DateTimeOffset.MinValue;

        string? url = wi.Id is int id
            ? $"{orgUrl.TrimEnd('/')}/{Uri.EscapeDataString(projectName)}/_workitems/edit/{id}"
            : null;

        return new Models.WorkItem(
            Id: wi.Id?.ToString() ?? "",
            Type: type,
            Title: Get("System.Title") ?? "",
            Description: Get("System.Description"),
            Creator: new UserReference(creatorName ?? "Unknown", ProxyAvatarUrl(creatorAvatar)),
            Assignee: assigneeName is not null ? new UserReference(assigneeName, ProxyAvatarUrl(assigneeAvatar)) : null,
            Status: status,
            CreatedAt: created,
            UpdatedAt: updated,
            Labels: tags,
            Url: url);
    }

    private string? ProxyAvatarUrl(string? imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl)) return null;
        var protected_ = avatarProtector.Protect($"{token.PersonalAccessToken}|{imageUrl}");
        return $"/ado-avatar?t={Uri.EscapeDataString(protected_)}";
    }

    private Models.PullRequest MapPullRequest(GitPullRequest pr, string repoName, AdoMonitoredProject project)
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

        var url = $"{project.OrgUrl.TrimEnd('/')}/{Uri.EscapeDataString(project.ProjectName)}/_git/{Uri.EscapeDataString(repoName)}/pullrequest/{pr.PullRequestId}";

        return new Models.PullRequest(
            Id: pr.PullRequestId.ToString(),
            Title: pr.Title,
            Description: pr.Description,
            Creator: new UserReference(pr.CreatedBy?.DisplayName ?? "Unknown", ProxyAvatarUrl(pr.CreatedBy?.ImageUrl)),
            Source: new RepoBranch(repoName, BranchName(pr.SourceRefName)),
            Target: new RepoBranch(repoName, BranchName(pr.TargetRefName)),
            Assignees: [],
            Reviewers: pr.Reviewers
                .Select(r => new UserReference(r.DisplayName, ProxyAvatarUrl(r.ImageUrl)))
                .ToList(),
            Status: status,
            CreatedAt: pr.CreationDate,
            UpdatedAt: pr.CreationDate,
            Url: url);
    }

    private static string? BuildAreaPathFilter(TeamFieldValues teamFieldValues)
    {
        var values = teamFieldValues?.Values?.ToList();
        if (values is not { Count: > 0 })
            return null;

        var conditions = values.Select(v =>
            v.IncludeChildren
                ? $"[System.AreaPath] UNDER '{EscapeWiql(v.Value)}'"
                : $"[System.AreaPath] = '{EscapeWiql(v.Value)}'");

        return $"({string.Join(" OR ", conditions)})";
    }

    private static string EscapeWiql(string value) => value.Replace("'", "''");
}
