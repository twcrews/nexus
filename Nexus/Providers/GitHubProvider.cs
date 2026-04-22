using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Models;
using Nexus.Services;

namespace Nexus.Providers;

public class GitHubProvider(
    GitHubAccountToken token,
    IHttpClientFactory httpFactory,
    ILogger<GitHubProvider> logger) : IDataProvider
{
    private HttpClient CreateClient()
    {
        HttpClient client = httpFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.PersonalAccessToken);
        return client;
    }

    public async Task<DashboardData> GetDashboardDataAsync()
    {
        if (token.MonitoredRepos.Count == 0)
            return new DashboardData([], []);

        GqlCache cache = await FetchGqlCacheAsync();
        return new DashboardData(
            WorkItems: cache.Issues
                .Where(x => x.AssigneeLogin == token.Login || x.AssigneeLogin is null)
                .Select(x => x.WorkItem),
            PullRequests: cache.PullRequests
                .Where(x => x.AssigneeLogins.Contains(token.Login) || x.ReviewerLogins.Contains(token.Login)
                         || (!x.AssigneeLogins.Any() && !x.ReviewerLogins.Contains(token.Login) && x.PullRequest.Status != PullRequestStatus.Draft))
                .Select(x =>
                {
                    bool assigned = x.AssigneeLogins.Contains(token.Login) || x.ReviewerLogins.Contains(token.Login);
                    bool created = string.Equals(x.AuthorLogin, token.Login, StringComparison.OrdinalIgnoreCase);
                    return assigned || created
                        ? x.PullRequest with { IsAssignedToCurrentUser = assigned, WasCreatedByCurrentUser = created }
                        : x.PullRequest;
                })
        );
    }

    private async Task<GqlCache> FetchGqlCacheAsync()
    {
        HttpClient client = CreateClient();
        var query = BuildQuery();
        var payload = JsonSerializer.Serialize(
            new GqlRequest(query), GitHubJsonContext.Default.GqlRequest);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        Console.WriteLine($"\x1b[33m[GitHub] GraphQL request → {token.Login} ({token.MonitoredRepos.Count} repos)\x1b[0m");

        HttpResponseMessage response = await client.PostAsync("graphql", content);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogWarning(
                "GitHub GraphQL returned {StatusCode} for {Login}: {Body}",
                (int)response.StatusCode, token.Login, body);
            return new GqlCache([], []);
        }

        response.EnsureSuccessStatusCode();

        GqlResponse? result = await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.GqlResponse);

        if (result?.Errors is { Count: > 0 })
        {
            foreach (GqlError err in result.Errors)
                logger.LogWarning("GitHub GraphQL error for {Login}: {Message}", token.Login, err.Message);
        }

        return ParseGqlResponse(result);
    }

    private string BuildQuery()
    {
        var repoFragment = """
            issues(first: 100, states: [OPEN]) {
              nodes {
                number title body url createdAt updatedAt
                author { login avatarUrl ... on User { name } }
                assignees(first: 20) { nodes { login avatarUrl name } }
                labels(first: 20) { nodes { name } }
              }
            }
            pullRequests(first: 100, states: [OPEN]) {
              nodes {
                number title body isDraft createdAt updatedAt url
                headRefName baseRefName
                headRepository { nameWithOwner }
                baseRepository { nameWithOwner }
                author { login avatarUrl ... on User { name } }
                assignees(first: 20) { nodes { login avatarUrl name } }
                reviewRequests(first: 20) {
                  nodes { requestedReviewer { ... on User { login avatarUrl name } } }
                }
                reviews(first: 50) {
                  nodes { author { login avatarUrl ... on User { name } } state }
                }
                mergeable
                autoMergeRequest { enabledAt }
                labels(first: 20) { nodes { name } }
                closingIssuesReferences(first: 20) { nodes { number url } }
              }
            }
            """;

        var sb = new StringBuilder("{ ");
        for (int i = 0; i < token.MonitoredRepos.Count; i++)
        {
            var parts = token.MonitoredRepos[i].Split('/', 2);
            var owner = parts[0].Replace("\"", "");
            var name = parts[1].Replace("\"", "");
            sb.Append($"r{i}: repository(owner: \"{owner}\", name: \"{name}\") {{ {repoFragment} }} ");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private GqlCache ParseGqlResponse(GqlResponse? result)
    {
        var issues = new List<MappedIssue>();
        var prs = new List<MappedPr>();

        if (result?.Data is not { } dataEl)
            return new GqlCache(issues, prs);

        foreach (JsonProperty repoProp in dataEl.EnumerateObject())
        {
            JsonElement repoEl = repoProp.Value;

            if (repoEl.TryGetProperty("issues", out JsonElement issuesEl))
            {
                GqlIssueConnection? conn = issuesEl.Deserialize(GitHubJsonContext.Default.GqlIssueConnection);
                if (conn is not null)
                    issues.AddRange(conn.Nodes.Select(MapIssue));
            }

            if (repoEl.TryGetProperty("pullRequests", out JsonElement prsEl))
            {
                GqlPrConnection? conn = prsEl.Deserialize(GitHubJsonContext.Default.GqlPrConnection);
                if (conn is not null)
                    prs.AddRange(conn.Nodes.Select(MapPr));
            }
        }

        return new GqlCache(issues, prs);
    }

    private static MappedIssue MapIssue(GqlIssue node)
    {
        UserReference author = node.Author is { } a
            ? new UserReference(a.Name ?? a.Login, a.AvatarUrl)
            : new UserReference("unknown", null);

        GqlUser? firstAssignee = node.Assignees.Nodes.FirstOrDefault();
        UserReference? assigneeRef = firstAssignee is null
            ? null
            : new UserReference(firstAssignee.Name ?? firstAssignee.Login, firstAssignee.AvatarUrl);

        var workItem = new WorkItem(
            Id: $"#{node.Number}",
            Type: InferWorkItemType(node.Labels.Nodes.Select(l => l.Name)),
            Title: node.Title,
            Description: node.Body,
            Creator: author,
            Assignee: assigneeRef,
            Status: WorkItemStatus.Active,
            CreatedAt: node.CreatedAt,
            UpdatedAt: node.UpdatedAt,
            Labels: node.Labels.Nodes.Select(l => l.Name).ToList(),
            Url: node.Url
        );

        return new MappedIssue(workItem, firstAssignee?.Login);
    }

    private static MappedPr MapPr(GqlPr node)
    {
        UserReference author = node.Author is { } a
            ? new UserReference(a.Name ?? a.Login, a.AvatarUrl)
            : new UserReference("unknown", null);

        var assignees = node.Assignees.Nodes
            .Select(u => new UserReference(u.Name ?? u.Login, u.AvatarUrl))
            .ToList();
        var assigneeLogins = node.Assignees.Nodes.Select(u => u.Login).ToList();

        // Build vote map from submitted reviews (latest state per author wins)
        var voteByLogin = new Dictionary<string, ReviewerVote>(StringComparer.OrdinalIgnoreCase);
        foreach (var review in node.Reviews?.Nodes ?? [])
        {
            if (review.Author?.Login is not { } login) continue;
            var vote = review.State switch
            {
                "APPROVED" => ReviewerVote.Approved,
                "CHANGES_REQUESTED" => ReviewerVote.Rejected,
                _ => ReviewerVote.None
            };
            voteByLogin[login] = vote;
        }

        // Requested reviewers (preserve order, apply vote if submitted)
        var requestedLogins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reviewers = new List<ReviewerReference>();
        foreach (var req in node.ReviewRequests.Nodes)
        {
            if (req.RequestedReviewer is not { } actor) continue;
            requestedLogins.Add(actor.Login);
            reviewers.Add(new ReviewerReference(
                new UserReference(actor.Name ?? actor.Login, actor.AvatarUrl),
                voteByLogin.GetValueOrDefault(actor.Login, ReviewerVote.None)));
        }

        // Add self-assigned reviewers who submitted a non-None review but weren't in reviewRequests
        foreach (var review in node.Reviews?.Nodes ?? [])
        {
            if (review.Author?.Login is not { } login) continue;
            if (requestedLogins.Contains(login)) continue;
            if (voteByLogin.TryGetValue(login, out var vote) && vote != ReviewerVote.None)
            {
                reviewers.Add(new ReviewerReference(
                    new UserReference(review.Author.Name ?? login, review.Author.AvatarUrl),
                    vote));
                requestedLogins.Add(login);
            }
        }

        var reviewerLogins = requestedLogins.ToList();

        var headRepo = node.HeadRepository?.NameWithOwner ?? "unknown/unknown";
        var baseRepo = node.BaseRepository?.NameWithOwner ?? headRepo;

        var pr = new PullRequest(
            Id: $"#{node.Number}",
            Title: node.Title,
            Description: node.Body,
            Creator: author,
            Source: new RepoBranch(headRepo, node.HeadRefName),
            Target: new RepoBranch(baseRepo, node.BaseRefName),
            Assignees: assignees,
            Reviewers: reviewers,
            Status: node.IsDraft ? PullRequestStatus.Draft : PullRequestStatus.Open,
            CreatedAt: node.CreatedAt,
            UpdatedAt: node.UpdatedAt,
            Url: node.Url,
            MergeStatus: node.Mergeable == "CONFLICTING" ? "Conflicts" : null,
            AutoComplete: node.AutoMergeRequest is not null,
            Labels: node.Labels?.Nodes.Select(l => l.Name).ToList(),
            LinkedWorkItemIds: node.ClosingIssues?.Nodes.Select(i => i.Number.ToString()).ToList(),
            LinkedWorkItemUrls: node.ClosingIssues?.Nodes
                .Where(i => i.Url is not null)
                .ToDictionary(i => i.Number.ToString(), i => i.Url!)
        );

        return new MappedPr(pr, assigneeLogins, reviewerLogins, node.Author?.Login);
    }

    internal static WorkItemType InferWorkItemType(IEnumerable<string> labelNames)
    {
        var names = labelNames.Select(n => n.ToLowerInvariant()).ToHashSet();
        if (names.Contains("bug") || names.Contains("defect"))       return WorkItemType.Bug;
        if (names.Contains("epic"))                                   return WorkItemType.Epic;
        if (names.Contains("feature") || names.Contains("feat"))     return WorkItemType.Feature;
        if (names.Contains("user story") || names.Contains("story")) return WorkItemType.UserStory;
        return WorkItemType.Task;
    }
}

// --- Cache types (private to this file via nested-style placement) ---

internal record GqlCache(List<MappedIssue> Issues, List<MappedPr> PullRequests);
internal record MappedIssue(WorkItem WorkItem, string? AssigneeLogin);
internal record MappedPr(PullRequest PullRequest, List<string> AssigneeLogins, List<string> ReviewerLogins, string? AuthorLogin);

// --- JSON models ---

internal record GqlRequest(
    [property: JsonPropertyName("query")] string Query);

internal record GqlResponse(
    [property: JsonPropertyName("data")] JsonElement? Data,
    [property: JsonPropertyName("errors")] List<GqlError>? Errors);

internal record GqlError(
    [property: JsonPropertyName("message")] string Message);

internal record GqlIssueConnection(
    [property: JsonPropertyName("nodes")] List<GqlIssue> Nodes);

internal record GqlIssue(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("author")] GqlActor? Author,
    [property: JsonPropertyName("assignees")] GqlUserConnection Assignees,
    [property: JsonPropertyName("labels")] GqlLabelConnection Labels);

internal record GqlPrConnection(
    [property: JsonPropertyName("nodes")] List<GqlPr> Nodes);

internal record GqlPr(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("isDraft")] bool IsDraft,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("headRefName")] string HeadRefName,
    [property: JsonPropertyName("baseRefName")] string BaseRefName,
    [property: JsonPropertyName("headRepository")] GqlRepo? HeadRepository,
    [property: JsonPropertyName("baseRepository")] GqlRepo? BaseRepository,
    [property: JsonPropertyName("author")] GqlActor? Author,
    [property: JsonPropertyName("assignees")] GqlUserConnection Assignees,
    [property: JsonPropertyName("reviewRequests")] GqlReviewRequestConnection ReviewRequests,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("reviews")] GqlPrReviewConnection? Reviews,
    [property: JsonPropertyName("mergeable")] string? Mergeable,
    [property: JsonPropertyName("autoMergeRequest")] GqlAutoMergeRequest? AutoMergeRequest,
    [property: JsonPropertyName("labels")] GqlLabelConnection? Labels,
    [property: JsonPropertyName("closingIssuesReferences")] GqlClosingIssueConnection? ClosingIssues);

internal record GqlActor(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("avatarUrl")] string? AvatarUrl,
    [property: JsonPropertyName("name")] string? Name);

internal record GqlUserConnection(
    [property: JsonPropertyName("nodes")] List<GqlUser> Nodes);

internal record GqlUser(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("avatarUrl")] string? AvatarUrl,
    [property: JsonPropertyName("name")] string? Name);

internal record GqlReviewRequest(
    [property: JsonPropertyName("requestedReviewer")] GqlActor? RequestedReviewer);

internal record GqlReviewRequestConnection(
    [property: JsonPropertyName("nodes")] List<GqlReviewRequest> Nodes);

internal record GqlRepo(
    [property: JsonPropertyName("nameWithOwner")] string NameWithOwner);

internal record GqlLabelConnection(
    [property: JsonPropertyName("nodes")] List<GqlLabel> Nodes);

internal record GqlLabel(
    [property: JsonPropertyName("name")] string Name);

internal record GqlPrReviewConnection(
    [property: JsonPropertyName("nodes")] List<GqlPrReview> Nodes);

internal record GqlPrReview(
    [property: JsonPropertyName("author")] GqlActor? Author,
    [property: JsonPropertyName("state")] string State);

internal record GqlAutoMergeRequest(
    [property: JsonPropertyName("enabledAt")] DateTimeOffset? EnabledAt);

internal record GqlClosingIssueConnection(
    [property: JsonPropertyName("nodes")] List<GqlClosingIssue> Nodes);

internal record GqlClosingIssue(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("url")] string? Url);

[JsonSerializable(typeof(GqlRequest))]
[JsonSerializable(typeof(GqlResponse))]
[JsonSerializable(typeof(GqlIssueConnection))]
[JsonSerializable(typeof(GqlPrConnection))]
internal partial class GitHubJsonContext : JsonSerializerContext { }
