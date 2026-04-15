# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run (HTTP: localhost:5120, HTTPS: localhost:7005)
dotnet run --project Nexus/Nexus.csproj

# Test
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~Nexus.Tests.ClassName"
```

## Architecture

Nexus is a Blazor Interactive Server dashboard that aggregates work items and pull requests from Azure DevOps and GitHub into a single unified view.

**Stack:** Blazor Interactive Server (frontend) + ASP.NET Core Minimal API (backend) on .NET 10. Uses **MudBlazor** for UI components and theming.

## Current state

Both GitHub and Azure DevOps are fully implemented using PAT (Personal Access Token) authentication.

- **Components:** Full dashboard UI — metric cards, tabbed work item and PR lists, animated numbers, theme toggle, header with refresh timestamp. Theme preference (System/Light/Dark) is persisted to `localStorage`.
- **`DummyProvider`:** A mock `IDataProvider` that returns randomly generated work items and PRs. Tags each work item with the account name as a label and prefixes PR repo names with the account name so data from different instances is visually distinguishable.
- **`GitHubProvider`:** Fetches issues and PRs from monitored repos via GitHub GraphQL API. Monitors only repos in `token.MonitoredRepos` (empty = no data). Uses alias-based multi-repo queries to batch all repos into a single GraphQL request. Handles 403/429 gracefully.
- **`AdoProvider`:** Fetches work items and PRs from Azure DevOps via the `Microsoft.TeamFoundationServer.Client` SDK using `VssBasicCredential` (PAT). Monitors only projects in `token.MonitoredProjects` (empty = no data). Work items fetched via WIQL; PRs fetched per repo via `GitHttpClient`.
- **`AggregateDataProvider`:** Implements `IDataProvider` by fanning out to all per-account providers in parallel. Isolates provider failures — errors are logged but don't crash the dashboard. Registered as the scoped `IDataProvider` in `Program.cs`.
- **`RefreshService`:** Tracks the last data refresh time (`LastRefreshed`). `Home.razor` auto-refreshes on a 60-second `PeriodicTimer`.
- **`SessionTokenStore`:** Scoped service that encrypts/decrypts linked account tokens using ASP.NET Core Data Protection and persists them to `localStorage` via JSInterop. Loads on first render; fires `AccountsChanged` when accounts are mutated.

## Data providers

All data sources implement `IDataProvider` (in `Nexus/Providers/`) and are aggregated via `AggregateDataProvider`:

```csharp
public interface IDataProvider
{
    Task<DashboardData> GetDashboardDataAsync();
}

public record DashboardData(
    IEnumerable<WorkItem> AssignedWorkItems,
    IEnumerable<WorkItem> UnassignedWorkItems,
    IEnumerable<PullRequest> AssignedPullRequests,
    IEnumerable<PullRequest> UnassignedPullRequests);
```

`AggregateDataProvider` constructs per-account provider instances and merges their results. A `GitHubProvider` is constructed for each linked `GitHubAccountToken`; an `AdoProvider` is constructed for each linked `MicrosoftAccountToken`.

**GitHub provider details:**
- Uses GitHub GraphQL API (not REST) to fetch issues and PRs from all monitored repos in a single request.
- Issues are mapped to `WorkItem`; type is inferred from labels (bug, epic, feature, story, user story → `WorkItemType` enum).
- PRs assigned to the user or where the user is a requested reviewer → assigned PRs. Open non-draft PRs with no assignees/reviewers → unassigned PRs.
- Uses `GitHubJsonContext` (source-generated JSON) for serialization performance.

**ADO provider details:**
- Uses `VssBasicCredential(string.Empty, pat)` for auth — username is always empty for PAT auth.
- Work items fetched via WIQL (`WorkItemTrackingHttpClient`); skipped if `project.TeamName` is null.
- PRs fetched via `GitHttpClient` per repo in `project.RepoNames`; skipped if `RepoNames` is empty.
- Reviewer match uses `token.Login` (the user's UPN/email) against `pr.Reviewers[n].UniqueName`.

## Authentication model

There is no authenticated user. The app stores all linked provider tokens in a single **encrypted localStorage entry** (`nexus.linked-accounts`). No auth middleware — ASP.NET Core Data Protection is consumed directly through `SessionTokenStore`. Both GitHub and ADO use PAT auth; there is no OAuth flow.

```csharp
public class LinkedAccounts
{
    public List<DummyAccountToken> DummyAccounts { get; set; } = [];
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}

public class GitHubAccountToken
{
    public string PersonalAccessToken { get; set; } = "";
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
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
    public List<string> RepoNames { get; set; } = [];  // Git repos to monitor for PRs
    public string? TeamName { get; set; }               // Team for work items; null = skip WI
}
```

`SessionTokenStore` (scoped) owns the in-memory cache and all reads/writes:
- **`LoadAsync()`** — called once from `OnAfterRenderAsync`. Reads `localStorage`, decrypts with `IDataProtector`, populates cache. Corrupt/tampered payloads are silently discarded.
- **`GetLinkedAccounts()`** — returns the in-memory cache synchronously (empty if `LoadAsync` hasn't run yet).
- **`LinkGitHubAccountAsync(token)`** / **`UnlinkGitHubAccountAsync(login)`** — mutate cache, re-encrypt, write to localStorage, fire `AccountsChanged`.
- **`UpdateGitHubMonitoredReposAsync(login, repos)`** — updates the monitored repo list for a linked GitHub account without unlinking.
- **`LinkMicrosoftAccountAsync(token)`** / **`UnlinkMicrosoftAccountAsync(login)`** — same pattern for ADO accounts.
- **`UpdateMicrosoftMonitoredProjectsAsync(login, projects)`** — updates monitored projects for a linked ADO account without unlinking.
- **`LinkDummyAccountAsync(token)`** / **`UnlinkDummyAccountAsync(accountName)`** — same pattern for dummy accounts.
- **`AccountsChanged`** event — `Home.razor` and `MainLayout.razor` subscribe to re-fetch data and update the UI.

Data Protection keys are persisted to a file path configured at deployment time (`DataProtection:KeyPath`) so the encryption key survives restarts. Falls back to `{ContentRoot}/keys` if not configured. Protection purpose: `"Nexus.LinkedAccounts.v1"`.

## Linking accounts (UI flow)

**GitHub:** `LinkGitHubPatDialog` prompts for a PAT, validates it against `GET /user` using the `"GitHub"` named HTTP client, then saves the resolved `GitHubAccountToken`. After linking, `SelectGitHubReposDialog` opens so the user can choose which repos to monitor.

**Azure DevOps:** `LinkAdoPatDialog` prompts for an org URL and PAT, validates via `GET {orgUrl}/_apis/connectionData`, then saves the resolved `MicrosoftAccountToken`. After linking, `SelectAdoMonitoringDialog` opens so the user can choose which projects, repos, and teams to monitor. It calls `GET /_apis/projects`, `GET /{project}/_apis/git/repositories`, and `GET /_apis/projects/{project}/teams` to populate options. Single teams are auto-selected; multi-team projects show a dropdown.

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

## Static content

`Nexus/Content/` holds static HTML files served by Minimal API endpoints:
- `GET /privacy` → `Content/privacy.html`
- `GET /terms` → `Content/terms.html`

## Extending the app

- **New provider:** Add a token slot to `LinkedAccounts`, implement `IDataProvider` returning a `DashboardData`, wire construction into `AggregateDataProvider`.
- **Write actions:** New Minimal API endpoints + extended service methods — no structural changes needed.
