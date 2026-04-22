using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Models;
using Nexus.Providers;

namespace Nexus.Tests;

public class GitHubProviderInferWorkItemTypeTests
{
    [Theory]
    [InlineData(new[] { "bug" }, WorkItemType.Bug)]
    [InlineData(new[] { "defect" }, WorkItemType.Bug)]
    [InlineData(new[] { "BUG" }, WorkItemType.Bug)]
    [InlineData(new[] { "epic" }, WorkItemType.Epic)]
    [InlineData(new[] { "feature" }, WorkItemType.Feature)]
    [InlineData(new[] { "feat" }, WorkItemType.Feature)]
    [InlineData(new[] { "user story" }, WorkItemType.UserStory)]
    [InlineData(new[] { "story" }, WorkItemType.UserStory)]
    [InlineData(new[] { "enhancement" }, WorkItemType.Task)]
    [InlineData(new string[] { }, WorkItemType.Task)]
    [InlineData(new[] { "documentation", "help wanted" }, WorkItemType.Task)]
    [InlineData(new[] { "documentation", "bug" }, WorkItemType.Bug)]
    public void InferWorkItemType_ReturnsExpectedType(string[] labels, WorkItemType expected)
    {
        Assert.Equal(expected, GitHubProvider.InferWorkItemType(labels));
    }

    [Fact]
    public void InferWorkItemType_BugTakesPrecedenceOverEpic()
    {
        // bug is checked first in the implementation
        Assert.Equal(WorkItemType.Bug, GitHubProvider.InferWorkItemType(["bug", "epic"]));
    }
}

public class GitHubProviderIntegrationTests
{
    private const string Login = "alice";

    private static GitHubProvider MakeProvider(string jsonResponse, string[] monitoredRepos)
    {
        var token = new GitHubAccountToken
        {
            Login = Login,
            PersonalAccessToken = "ghp_fake",
            MonitoredRepos = monitoredRepos.ToList()
        };

        var handler = new FakeHttpHandler(jsonResponse);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var factory = new FakeHttpClientFactory(client);

        return new GitHubProvider(token, factory, NullLogger<GitHubProvider>.Instance);
    }

    [Fact]
    public async Task GetDashboardDataAsync_EmptyMonitoredRepos_ReturnsEmpty()
    {
        var provider = MakeProvider("{}", []);
        var data = await provider.GetDashboardDataAsync();
        Assert.Empty(data.WorkItems);
        Assert.Empty(data.PullRequests);
    }

    [Fact]
    public async Task GetDashboardDataAsync_Returns403_ReturnsEmpty()
    {
        var token = new GitHubAccountToken
        {
            Login = Login,
            PersonalAccessToken = "ghp_fake",
            MonitoredRepos = ["owner/repo"]
        };
        var handler = new FakeHttpHandler("{}", HttpStatusCode.Forbidden);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var factory = new FakeHttpClientFactory(client);
        var provider = new GitHubProvider(token, factory, NullLogger<GitHubProvider>.Instance);

        var data = await provider.GetDashboardDataAsync();
        Assert.Empty(data.WorkItems);
        Assert.Empty(data.PullRequests);
    }

    [Fact]
    public async Task GetDashboardDataAsync_MapsIssueToWorkItem()
    {
        var json = BuildGqlResponse(issues: [
            new GqlIssueData(1, "Fix the bug", "bug body", "https://github.com/owner/repo/issues/1",
                AuthorLogin: Login, AuthorName: "Alice",
                Labels: ["bug"])
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();

        var item = Assert.Single(data.WorkItems);
        Assert.Equal("#1", item.Id);
        Assert.Equal("Fix the bug", item.Title);
        Assert.Equal(WorkItemType.Bug, item.Type);
        Assert.Equal("Alice", item.Creator.DisplayName);
    }

    [Fact]
    public async Task GetDashboardDataAsync_AssignedIssue_IncludedForAssignee()
    {
        var json = BuildGqlResponse(issues: [
            new GqlIssueData(2, "Assigned issue", null, null,
                AuthorLogin: "bob", AuthorName: "Bob",
                AssigneeLogin: Login, AssigneeName: "Alice",
                Labels: [])
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();
        Assert.Single(data.WorkItems);
    }

    [Fact]
    public async Task GetDashboardDataAsync_IssueAssignedToOther_NotIncluded()
    {
        var json = BuildGqlResponse(issues: [
            new GqlIssueData(3, "Other's issue", null, null,
                AuthorLogin: "bob", AuthorName: "Bob",
                AssigneeLogin: "carol", AssigneeName: "Carol",
                Labels: [])
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();
        Assert.Empty(data.WorkItems);
    }

    [Fact]
    public async Task GetDashboardDataAsync_MapsPullRequest()
    {
        var json = BuildGqlResponse(prs: [
            new GqlPrData(10, "My PR", IsDraft: false,
                AuthorLogin: Login, AuthorName: "Alice",
                HeadRef: "feature/x", BaseRef: "main",
                Repo: "owner/repo")
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();

        var pr = Assert.Single(data.PullRequests);
        Assert.Equal("#10", pr.Id);
        Assert.Equal("My PR", pr.Title);
        Assert.Equal(PullRequestStatus.Open, pr.Status);
        Assert.Equal("Alice", pr.Creator.DisplayName);
        Assert.True(pr.WasCreatedByCurrentUser);
    }

    [Fact]
    public async Task GetDashboardDataAsync_DraftPrWithExplicitReviewer_HasDraftStatus()
    {
        // Draft PRs with no assignees/reviewers are excluded by the filter intentionally.
        // Draft PRs where the current user is explicitly a reviewer ARE included.
        var json = BuildGqlResponse(prs: [
            new GqlPrData(11, "WIP", IsDraft: true,
                AuthorLogin: "bob", AuthorName: "Bob",
                HeadRef: "draft/y", BaseRef: "main",
                Repo: "owner/repo",
                ReviewerLogin: Login)
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();

        var pr = Assert.Single(data.PullRequests);
        Assert.Equal(PullRequestStatus.Draft, pr.Status);
    }

    [Fact]
    public async Task GetDashboardDataAsync_UnassignedDraftPr_IsExcluded()
    {
        // Draft PRs with no assignees/reviewers are intentionally excluded
        // even if the current user authored them (they're not ready for review).
        var json = BuildGqlResponse(prs: [
            new GqlPrData(17, "WIP no reviewer", IsDraft: true,
                AuthorLogin: Login, AuthorName: "Alice",
                HeadRef: "draft/z", BaseRef: "main",
                Repo: "owner/repo")
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();
        Assert.Empty(data.PullRequests);
    }

    [Fact]
    public async Task GetDashboardDataAsync_ConflictingPr_HasConflictsMergeStatus()
    {
        var json = BuildGqlResponse(prs: [
            new GqlPrData(12, "Conflicting", IsDraft: false,
                AuthorLogin: Login, AuthorName: "Alice",
                HeadRef: "feat/z", BaseRef: "main",
                Repo: "owner/repo",
                Mergeable: "CONFLICTING")
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();

        var pr = Assert.Single(data.PullRequests);
        Assert.Equal("Conflicts", pr.MergeStatus);
    }

    [Fact]
    public async Task GetDashboardDataAsync_PrWithReviewer_SetsIsAssignedToCurrentUser()
    {
        var json = BuildGqlResponse(prs: [
            new GqlPrData(13, "Reviewed PR", IsDraft: false,
                AuthorLogin: "bob", AuthorName: "Bob",
                HeadRef: "feat/r", BaseRef: "main",
                Repo: "owner/repo",
                ReviewerLogin: Login)
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();

        var pr = Assert.Single(data.PullRequests);
        Assert.True(pr.IsAssignedToCurrentUser);
        Assert.False(pr.WasCreatedByCurrentUser);
    }

    [Fact]
    public async Task GetDashboardDataAsync_UnassignedOpenPr_IsIncluded()
    {
        // No assignees, no reviewers, not draft → included for anyone
        var json = BuildGqlResponse(prs: [
            new GqlPrData(14, "Open PR", IsDraft: false,
                AuthorLogin: "carol", AuthorName: "Carol",
                HeadRef: "feat/open", BaseRef: "main",
                Repo: "owner/repo")
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();
        Assert.Single(data.PullRequests);
    }

    [Fact]
    public async Task GetDashboardDataAsync_PrWithAutoMerge_SetsAutoComplete()
    {
        var json = BuildGqlResponse(prs: [
            new GqlPrData(15, "AutoMerge PR", IsDraft: false,
                AuthorLogin: Login, AuthorName: "Alice",
                HeadRef: "feat/am", BaseRef: "main",
                Repo: "owner/repo",
                AutoMerge: true)
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();

        var pr = Assert.Single(data.PullRequests);
        Assert.True(pr.AutoComplete);
    }

    [Fact]
    public async Task GetDashboardDataAsync_ClosingIssues_MappedToLinkedWorkItems()
    {
        var json = BuildGqlResponse(prs: [
            new GqlPrData(16, "PR with linked issue", IsDraft: false,
                AuthorLogin: Login, AuthorName: "Alice",
                HeadRef: "feat/linked", BaseRef: "main",
                Repo: "owner/repo",
                ClosingIssueNumber: 42,
                ClosingIssueUrl: "https://github.com/owner/repo/issues/42")
        ]);

        var provider = MakeProvider(json, ["owner/repo"]);
        var data = await provider.GetDashboardDataAsync();

        var pr = Assert.Single(data.PullRequests);
        Assert.Contains("42", pr.LinkedWorkItemIds!);
        Assert.Equal("https://github.com/owner/repo/issues/42", pr.LinkedWorkItemUrls!["42"]);
    }

    // --- Helpers for building test JSON ---

    private record GqlIssueData(
        int Number, string Title, string? Body, string? Url,
        string AuthorLogin, string? AuthorName,
        string? AssigneeLogin = null, string? AssigneeName = null,
        string[]? Labels = null);

    private record GqlPrData(
        int Number, string Title, bool IsDraft,
        string AuthorLogin, string? AuthorName,
        string HeadRef, string BaseRef, string Repo,
        string? Mergeable = null, bool AutoMerge = false,
        string? ReviewerLogin = null,
        int? ClosingIssueNumber = null, string? ClosingIssueUrl = null);

    private static string BuildGqlResponse(
        GqlIssueData[]? issues = null,
        GqlPrData[]? prs = null)
    {
        issues ??= [];
        prs ??= [];

        var issueNodes = string.Join(",", issues.Select(i =>
        {
            var assignees = i.AssigneeLogin is not null
                ? $$$"""{"nodes":[{"login":"{{{i.AssigneeLogin}}}","avatarUrl":null,"name":{{{JsonStr(i.AssigneeName)}}}}]}"""
                : """{"nodes":[]}""";
            var labels = i.Labels?.Length > 0
                ? $$$"""{"nodes":[{{{string.Join(",", i.Labels.Select(l => $$$"""{"name":"{{{l}}}"}"""))}}}]}"""
                : """{"nodes":[]}""";
            return $$$"""
                {
                    "number":{{{i.Number}}},
                    "title":{{{JsonStr(i.Title)}}},
                    "body":{{{JsonStr(i.Body)}}},
                    "url":{{{JsonStr(i.Url)}}},
                    "createdAt":"2024-01-01T00:00:00Z",
                    "updatedAt":"2024-01-02T00:00:00Z",
                    "author":{"login":{{{JsonStr(i.AuthorLogin)}}},"avatarUrl":null,"name":{{{JsonStr(i.AuthorName)}}}},
                    "assignees":{{{assignees}}},
                    "labels":{{{labels}}}
                }
                """;
        }));

        var prNodes = string.Join(",", prs.Select(p =>
        {
            var reviewRequests = p.ReviewerLogin is not null
                ? $$$"""{"nodes":[{"requestedReviewer":{"login":{{{JsonStr(p.ReviewerLogin)}}},"avatarUrl":null,"name":null}}]}"""
                : """{"nodes":[]}""";
            var autoMerge = p.AutoMerge ? """{"enabledAt":"2024-01-01T00:00:00Z"}""" : "null";
            var closingIssues = p.ClosingIssueNumber.HasValue
                ? $$$"""{"nodes":[{"number":{{{p.ClosingIssueNumber}}},"url":{{{JsonStr(p.ClosingIssueUrl)}}}}]}"""
                : """{"nodes":[]}""";

            return $$$"""
                {
                    "number":{{{p.Number}}},
                    "title":{{{JsonStr(p.Title)}}},
                    "body":null,
                    "isDraft":{{{(p.IsDraft ? "true" : "false")}}},
                    "createdAt":"2024-01-01T00:00:00Z",
                    "updatedAt":"2024-01-02T00:00:00Z",
                    "headRefName":{{{JsonStr(p.HeadRef)}}},
                    "baseRefName":{{{JsonStr(p.BaseRef)}}},
                    "headRepository":{"nameWithOwner":{{{JsonStr(p.Repo)}}}},
                    "baseRepository":{"nameWithOwner":{{{JsonStr(p.Repo)}}}},
                    "author":{"login":{{{JsonStr(p.AuthorLogin)}}},"avatarUrl":null,"name":{{{JsonStr(p.AuthorName)}}}},
                    "assignees":{"nodes":[]},
                    "reviewRequests":{{{reviewRequests}}},
                    "reviews":{"nodes":[]},
                    "mergeable":{{{JsonStr(p.Mergeable ?? "MERGEABLE")}}},
                    "autoMergeRequest":{{{autoMerge}}},
                    "labels":{"nodes":[]},
                    "closingIssuesReferences":{{{closingIssues}}},
                    "url":"https://github.com/{{{p.Repo}}}/pull/{{{p.Number}}}"
                }
                """;
        }));

        return $$"""
            {
                "data": {
                    "r0": {
                        "issues": { "nodes": [{{issueNodes}}] },
                        "pullRequests": { "nodes": [{{prNodes}}] }
                    }
                }
            }
            """;
    }

    private static string JsonStr(string? s) => s is null ? "null" : $"\"{s}\"";

    private class FakeHttpHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
