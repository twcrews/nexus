using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Models;
using Nexus.Services;

namespace Nexus.Providers;

public class GitHubProvider(
    GitHubAccountToken token,
    IHttpClientFactory httpFactory,
    GitHubSettings settings) : IDataProvider
{
    private HttpClient CreateClient()
    {
        var client = httpFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private string Scope => string.IsNullOrWhiteSpace(settings.Organization)
        ? $"user:{token.Login}"
        : $"org:{settings.Organization}";

    public async Task<IEnumerable<WorkItem>> GetAssignedWorkItemsAsync()
    {
        var client = CreateClient();
        var items = await FetchAllSearchResultsAsync(client,
            $"is:issue is:open assignee:{token.Login}");
        return items.Select(MapIssueToWorkItem);
    }

    public async Task<IEnumerable<WorkItem>> GetUnassignedWorkItemsAsync()
    {
        var client = CreateClient();
        var items = await FetchAllSearchResultsAsync(client,
            $"is:issue is:open no:assignee {Scope}");
        return items.Select(MapIssueToWorkItem);
    }

    public async Task<IEnumerable<PullRequest>> GetAssignedPullRequestsAsync()
    {
        var client = CreateClient();
        var assigned = FetchAllSearchResultsAsync(client, $"is:pr is:open assignee:{token.Login}");
        var reviewRequested = FetchAllSearchResultsAsync(client, $"is:pr is:open review-requested:{token.Login}");
        var items = (await assigned).Concat(await reviewRequested)
            .DistinctBy(i => i.Number)
            .ToList();
        return await MapSearchItemsToPullRequestsAsync(client, items);
    }

    public async Task<IEnumerable<PullRequest>> GetUnassignedPullRequestsAsync()
    {
        var client = CreateClient();
        var items = await FetchAllSearchResultsAsync(client,
            $"is:pr is:open no:assignee draft:false {Scope}");
        return await MapSearchItemsToPullRequestsAsync(client, items);
    }

    private async Task<List<GitHubSearchItem>> FetchAllSearchResultsAsync(
        HttpClient client, string query)
    {
        var all = new List<GitHubSearchItem>();
        int page = 1;
        while (true)
        {
            var url = $"search/issues?q={Uri.EscapeDataString(query)}&per_page=100&page={page}";
            var response = await SafeGetSearchAsync(client, url);
            if (response?.Items is null or { Count: 0 }) break;
            all.AddRange(response.Items);
            if (all.Count >= response.TotalCount || response.Items.Count < 100) break;
            page++;
        }
        return all;
    }

    private async Task<GitHubSearchResponse?> SafeGetSearchAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"GitHub search failed with {(int)response.StatusCode} {response.StatusCode}: {body}",
                inner: null,
                statusCode: response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.GitHubSearchResponse);
    }

    private WorkItem MapIssueToWorkItem(GitHubSearchItem item)
    {
        return new WorkItem(
            Id: $"#{item.Number}",
            Type: InferWorkItemType(item.Labels),
            Title: item.Title,
            Description: item.Body,
            Creator: new UserReference(item.User.Name ?? item.User.Login, item.User.AvatarUrl),
            Assignee: item.Assignee is null
                ? null
                : new UserReference(item.Assignee.Name ?? item.Assignee.Login, item.Assignee.AvatarUrl),
            Status: WorkItemStatus.Active,
            CreatedAt: item.CreatedAt,
            UpdatedAt: item.UpdatedAt,
            Labels: item.Labels.Select(l => l.Name).ToList()
        );
    }

    private async Task<IEnumerable<PullRequest>> MapSearchItemsToPullRequestsAsync(
        HttpClient client, List<GitHubSearchItem> items)
    {
        var semaphore = new SemaphoreSlim(5);
        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                return await MapSearchItemToPullRequestAsync(client, item);
            }
            finally
            {
                semaphore.Release();
            }
        });
        return await Task.WhenAll(tasks);
    }

    private async Task<PullRequest> MapSearchItemToPullRequestAsync(
        HttpClient client, GitHubSearchItem item)
    {
        var repoName = ExtractRepoName(item.RepositoryUrl);
        var prDetail = await client.GetFromJsonAsync(
            $"repos/{repoName}/pulls/{item.Number}",
            GitHubJsonContext.Default.GitHubPrDetail);

        var headBranch = prDetail?.Head?.Ref ?? "unknown";
        var baseBranch = prDetail?.Base?.Ref ?? "main";
        var headRepo = prDetail?.Head?.RepoFullName ?? repoName;
        var baseRepo = prDetail?.Base?.RepoFullName ?? repoName;

        var status = (item.State, item.PullRequest?.MergedAt, item.Draft) switch
        {
            ("closed", not null, _) => PullRequestStatus.Merged,
            ("closed", _, _) => PullRequestStatus.Closed,
            (_, _, true) => PullRequestStatus.Draft,
            _ => PullRequestStatus.Open
        };

        return new PullRequest(
            Id: $"#{item.Number}",
            Title: item.Title,
            Description: item.Body,
            Creator: new UserReference(item.User.Name ?? item.User.Login, item.User.AvatarUrl),
            Source: new RepoBranch(headRepo, headBranch),
            Target: new RepoBranch(baseRepo, baseBranch),
            Assignees: item.Assignees
                .Select(u => new UserReference(u.Name ?? u.Login, u.AvatarUrl))
                .ToList(),
            Reviewers: item.RequestedReviewers
                .Select(u => new UserReference(u.Name ?? u.Login, u.AvatarUrl))
                .ToList(),
            Status: status,
            CreatedAt: item.CreatedAt,
            UpdatedAt: item.UpdatedAt
        );
    }

    private static WorkItemType InferWorkItemType(List<GitHubLabel> labels)
    {
        var names = labels.Select(l => l.Name.ToLowerInvariant()).ToHashSet();
        if (names.Contains("bug") || names.Contains("defect"))       return WorkItemType.Bug;
        if (names.Contains("epic"))                                   return WorkItemType.Epic;
        if (names.Contains("feature") || names.Contains("feat"))     return WorkItemType.Feature;
        if (names.Contains("user story") || names.Contains("story")) return WorkItemType.UserStory;
        return WorkItemType.Task;
    }

    private static string ExtractRepoName(string repositoryUrl)
    {
        var uri = new Uri(repositoryUrl);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // segments: ["repos", "owner", "repo"]
        return segments.Length >= 3 ? $"{segments[1]}/{segments[2]}" : repositoryUrl;
    }
}

// --- JSON models ---

internal record GitHubSearchResponse(
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("items")] List<GitHubSearchItem> Items);

internal record GitHubSearchItem(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("draft")] bool? Draft,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("labels")] List<GitHubLabel> Labels,
    [property: JsonPropertyName("user")] GitHubUser User,
    [property: JsonPropertyName("assignee")] GitHubUser? Assignee,
    [property: JsonPropertyName("assignees")] List<GitHubUser> Assignees,
    [property: JsonPropertyName("requested_reviewers")] List<GitHubUser> RequestedReviewers,
    [property: JsonPropertyName("pull_request")] GitHubPullRequestLinks? PullRequest,
    [property: JsonPropertyName("repository_url")] string RepositoryUrl);

internal record GitHubLabel(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("color")] string Color);

internal record GitHubUser(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("name")] string? Name);

internal record GitHubPullRequestLinks(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("merged_at")] DateTimeOffset? MergedAt);

internal record GitHubPrDetail(
    [property: JsonPropertyName("head")] GitHubBranchRef Head,
    [property: JsonPropertyName("base")] GitHubBranchRef Base);

internal record GitHubBranchRef(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("repo")] GitHubRepo? Repo)
{
    public string? RepoFullName => Repo is null ? null : $"{Repo.Owner.Login}/{Repo.Name}";
}

internal record GitHubRepo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("owner")] GitHubUser Owner);

[JsonSerializable(typeof(GitHubSearchResponse))]
[JsonSerializable(typeof(GitHubPrDetail))]
internal partial class GitHubJsonContext : JsonSerializerContext { }
