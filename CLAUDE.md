# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run (HTTP: localhost:5120, HTTPS: localhost:7005)
dotnet run --project Nexus/Nexus.csproj

# Run with hot reload
dotnet watch --project Nexus/Nexus.csproj

# Test
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~Nexus.Tests.ClassName"
```

A `justfile` is also present: `just start` (hot reload), `just build`, `just test`, `just debug` (launches VSCode debugger).

## Architecture

Nexus is a Blazor Interactive Server dashboard that aggregates work items and pull requests from Azure DevOps and GitHub into a single unified view.

**Stack:** Blazor Interactive Server (frontend) + ASP.NET Core Minimal API (backend) on .NET 10. Uses **MudBlazor** for UI components and theming.

## Current state

Both GitHub and Azure DevOps are fully implemented using PAT (Personal Access Token) authentication.

- **Components:** Full dashboard UI — metric cards, tabbed work item and PR lists, animated numbers, theme toggle, header with refresh timestamp, PR/work item detail views. Theme preference (System/Light/Dark) is persisted to `localStorage`. Empty state shown when no accounts are linked. `Home.razor` fetches rich details on demand (`FetchWorkItemDetailsAsync`, `FetchGitHubIssueDetailsAsync`, `FetchPrDescriptionAsync`) when items are expanded — these call ADO/GitHub APIs directly using tokens from `SessionTokenStore`.
- **`GitHubProvider`:** Fetches issues and PRs from monitored repos via GitHub GraphQL API. Monitors only repos in `token.MonitoredRepos` (empty = no data). Uses alias-based multi-repo queries to batch all repos into a single GraphQL request. Handles 403/429 gracefully.
- **`AdoProvider`:** Fetches work items and PRs from Azure DevOps via the `Microsoft.TeamFoundationServer.Client` SDK using `VssBasicCredential` (PAT). Monitors only projects in `token.MonitoredProjects` (empty = no data). Work items fetched via WIQL scoped to team area paths; PRs fetched per repo via `GitHttpClient`. Avatar images for ADO users are proxied through a local `/ado-avatar` endpoint.
- **`AggregateDataProvider`:** Implements `IDataProvider` by fanning out to all per-account providers in parallel. Isolates provider failures — errors are logged but don't crash the dashboard. Registered as the scoped `IDataProvider` in `Program.cs`.
- **`RefreshService`:** Tracks the last data refresh time (`LastRefreshed`). `Home.razor` auto-refreshes on a 60-second `PeriodicTimer`.
- **`SessionTokenStore`:** Scoped service that encrypts/decrypts linked account tokens using ASP.NET Core Data Protection and persists them to `localStorage` via JSInterop. Loads on first render; fires `AccountsChanged` when accounts are mutated.
- **`ImagePlaceholderExtension`:** Markdig extension (`UseImagePlaceholders()`) that replaces `![alt](url)` image nodes with a text placeholder span in rendered PR descriptions, preventing broken image requests.

## Data providers

All data sources implement `IDataProvider` (in `Nexus/Providers/`) and are aggregated via `AggregateDataProvider`:

```csharp
public interface IDataProvider
{
    Task<DashboardData> GetDashboardDataAsync();
}

public record DashboardData(
    IEnumerable<WorkItem> WorkItems,
    IEnumerable<PullRequest> PullRequests);
```

Providers return all relevant items for the user; the UI layer (`Home.razor`) separates them into assigned vs. unassigned display buckets using `IsAssignedToCurrentUser`/`WasCreatedByCurrentUser` flags on each item. `AggregateDataProvider` constructs per-account provider instances and merges their results. A `GitHubProvider` is constructed for each linked `GitHubAccountToken`; an `AdoProvider` is constructed for each linked `MicrosoftAccountToken`.

**GitHub provider details:**
- Uses GitHub GraphQL API (not REST) to fetch issues and PRs from all monitored repos in a single request.
- Issues are mapped to `WorkItem`; type is inferred from labels (bug, epic, feature, story, user story → `WorkItemType` enum).
- PRs included if: user is assignee OR user is reviewer OR (no assignees AND no reviewers AND not draft). `IsAssignedToCurrentUser` is set when user is assignee or reviewer; `WasCreatedByCurrentUser` is set when user is the PR author.
- Reviewer votes are derived from submitted reviews (APPROVED → `ReviewerVote.Approved`, CHANGES_REQUESTED → `ReviewerVote.Rejected`). Self-assigned reviewers who submitted a non-None review but were not in `reviewRequests` are included.
- PRs carry `Url`, `MergeStatus` ("Conflicts" if `mergeable == CONFLICTING`), `AutoComplete`, `Labels`, and `LinkedWorkItemIds`/`LinkedWorkItemUrls` (from `closingIssuesReferences`).
- Uses `GitHubJsonContext` (source-generated JSON) for serialization performance.

**ADO provider details:**
- Uses `VssBasicCredential(string.Empty, pat)` for auth — username is always empty for PAT auth.
- Work items fetched via WIQL (`WorkItemTrackingHttpClient`) scoped to team area paths; skipped if `project.TeamNames` is empty. Supports multiple teams per project — results are union-deduplicated.
- Area path filter is built from `WorkHttpClient.GetTeamFieldValuesAsync`, respecting `IncludeChildren`.
- PRs fetched via `GitHttpClient` per repo in `project.RepoNames`; skipped if `RepoNames` is empty.
- PRs included if: user is author OR reviewer OR (not draft AND no reviewers). `IsAssignedToCurrentUser` is set when user is a reviewer; `WasCreatedByCurrentUser` is set when user is the PR author.
- Reviewer votes are mapped from ADO vote integers: 10=Approved, 5=ApprovedWithSuggestions, -5=WaitingForAuthor, -10=Rejected.
- PRs carry `Url`, `MergeStatus`, `AutoComplete`, `Labels`, `LinkedWorkItemIds`, and `LinkedWorkItemUrls`.
- ADO avatar images require PAT auth; they are proxied through `GET /ado-avatar?t=<token>`. The token is a Data Protection–encrypted string (`"Nexus.AvatarProxy.v1"` purpose) containing `"{pat}|{imageUrl}"`. The endpoint fetches the image server-side and streams it back.

## Authentication model

There is no authenticated user. The app stores all linked provider tokens in a single **encrypted localStorage entry** (`nexus.linked-accounts`). No auth middleware — ASP.NET Core Data Protection is consumed directly through `SessionTokenStore`. Both GitHub and ADO use PAT auth; there is no OAuth flow.

```csharp
public class LinkedAccounts
{
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}

public class GitHubAccountToken
{
    public string PersonalAccessToken { get; set; } = "";
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public List<string> MonitoredRepos { get; set; } = []; // "owner/repo" format
}

public class MicrosoftAccountToken
{
    public string Login { get; set; } = "";        // UPN/email, used as stable identifier
    public string DisplayName { get; set; } = "";
    public string PersonalAccessToken { get; set; } = "";
    public string OrgUrl { get; set; } = "";       // e.g. "https://dev.azure.com/myorg"
    public List<AdoMonitoredProject> MonitoredProjects { get; set; } = [];
}

public class AdoMonitoredProject
{
    public string OrgUrl { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public List<string> RepoNames { get; set; } = [];   // Git repos to monitor for PRs
    public List<string> TeamNames { get; set; } = [];   // Teams for work items; empty = skip WI
}
```

Key model types:

```csharp
public enum DataProvider { GitHub, AzureDevOps }

// Reviewers on PRs carry a vote
public enum ReviewerVote { None, Approved, ApprovedWithSuggestions, WaitingForAuthor, Rejected }
public record ReviewerReference(UserReference User, ReviewerVote Vote);

// UserReference handles "Last, First" display name formatting
public record UserReference(string Name, string? AvatarUrl)
{
    public string DisplayName { get; }  // re-orders "Last, First" → "First Last"
}

public record RepoBranch(string Repository, string Branch);

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

// Rich work item metadata fetched on demand (not from provider)
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
```

`SessionTokenStore` (scoped) owns the in-memory cache and all reads/writes:
- **`LoadAsync()`** — called once from `OnAfterRenderAsync`. Reads `localStorage`, decrypts with `IDataProtector`, populates cache. Corrupt/tampered payloads are silently discarded.
- **`GetLinkedAccounts()`** — returns the in-memory cache synchronously (empty if `LoadAsync` hasn't run yet).
- **`LinkGitHubAccountAsync(token)`** / **`UnlinkGitHubAccountAsync(login)`** — mutate cache, re-encrypt, write to localStorage, fire `AccountsChanged`.
- **`UpdateGitHubMonitoredReposAsync(login, repos)`** — updates the monitored repo list for a linked GitHub account without unlinking.
- **`LinkMicrosoftAccountAsync(token)`** / **`UnlinkMicrosoftAccountAsync(login)`** — same pattern for ADO accounts.
- **`UpdateMicrosoftMonitoredProjectsAsync(login, projects)`** — updates monitored projects for a linked ADO account without unlinking.
- **`AccountsChanged`** event — `Home.razor` and `MainLayout.razor` subscribe to re-fetch data and update the UI.

Data Protection keys are persisted to a file path configured at deployment time (`DataProtection:KeyPath`) so the encryption key survives restarts. Falls back to `{ContentRoot}/keys` if not configured. Protection purpose: `"Nexus.LinkedAccounts.v1"`.

## Linking accounts (UI flow)

**GitHub:** `LinkGitHubPatDialog` prompts for a PAT, validates it against `GET /user` using the `"GitHub"` named HTTP client, then saves the resolved `GitHubAccountToken`. After linking, `SelectGitHubReposDialog` opens so the user can choose which repos to monitor.

**Azure DevOps:** `LinkAdoPatDialog` prompts for an org URL and PAT, validates via `GET {orgUrl}/_apis/connectionData`, then saves the resolved `MicrosoftAccountToken`. After linking, `SelectAdoMonitoringDialog` opens so the user can choose which projects, repos, and teams to monitor. It calls `GET /_apis/projects`, `GET /{project}/_apis/git/repositories`, and `GET /_apis/projects/{project}/teams` to populate options. Multiple teams per project can be selected (stored in `AdoMonitoredProject.TeamNames`).

HTTP clients registered in `Program.cs`:
- `"GitHub"` — `https://api.github.com/`, Bearer auth, GitHub API version header.

Multiple GitHub and ADO accounts are supported. ADO accounts are keyed by `Login` (UPN); one `MicrosoftAccountToken` per org.

## App settings structure

```json
{
  "DataProtection": { "KeyPath": "/var/keys" }
}
```

Only `DataProtection:KeyPath` is meaningful at runtime. No OAuth credentials are required. Sensitive values must come from environment variables or a secrets manager, not `appsettings.json`.

## Static content and proxy endpoints

`Nexus/Content/` holds static HTML files served by Minimal API endpoints:
- `GET /privacy` → `Content/privacy.html`
- `GET /terms` → `Content/terms.html`

`GET /ado-avatar?t=<token>` — ADO avatar image proxy. The `t` parameter is a Data Protection–encrypted payload (`"Nexus.AvatarProxy.v1"` purpose) containing `"{pat}|{imageUrl}"`. The endpoint decrypts it, fetches the image from ADO with Basic auth, and streams it back. Used because ADO avatar URLs require PAT authentication.

## Extending the app

- **New provider:** Add a token slot to `LinkedAccounts`, implement `IDataProvider` returning a `DashboardData`, wire construction into `AggregateDataProvider`.
- **Write actions:** New Minimal API endpoints + extended service methods — no structural changes needed.
