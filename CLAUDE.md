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

GitHub OAuth and data fetching are fully implemented. Azure DevOps is planned but not started.

- **Components:** Full dashboard UI — metric cards, tabbed work item and PR lists, animated numbers, theme toggle, header with refresh timestamp. Theme preference (System/Light/Dark) is persisted to `localStorage`.
- **`DummyProvider`:** A mock `IDataProvider` that returns randomly generated work items and PRs. Tags each work item with the account name as a label and prefixes PR repo names with the account name so data from different instances is visually distinguishable.
- **`GitHubProvider`:** Fully implemented. Fetches issues and PRs from monitored repos via GitHub GraphQL API. Monitors only repos in `token.MonitoredRepos` (empty = no data). Uses alias-based multi-repo queries to batch all repos into a single GraphQL request. Handles 403/429 gracefully.
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

`AggregateDataProvider` constructs per-account provider instances and merges their results. `GitHubProvider` is constructed for each linked `GitHubAccountToken`; `MicrosoftAccountToken` is defined but not yet wired to a real provider.

**GitHub provider details:**
- Uses GitHub GraphQL API (not REST) to fetch issues and PRs from all monitored repos in a single request.
- Issues are mapped to `WorkItem`; type is inferred from labels (bug, epic, feature, story, user story → `WorkItemType` enum).
- PRs assigned to the user or where the user is a requested reviewer → assigned PRs. Open non-draft PRs with no assignees/reviewers → unassigned PRs.
- Uses `GitHubJsonContext` (source-generated JSON) for serialization performance.

**Planned real providers:**
- **Azure DevOps:** `Microsoft.TeamFoundationServer.Client` SDK. Auth via Entra ID OAuth 2.0 (manual flow); MSAL manages token refresh with a custom cache backed by the encrypted localStorage entry. ADO resource scope: `499b84ac-1321-427f-aa17-267ca6975798/.default`.

## Authentication model

There is no authenticated user. The app stores all linked provider tokens in a single **encrypted localStorage entry** (`nexus.linked-accounts`). No auth middleware — ASP.NET Core Data Protection is consumed directly through `SessionTokenStore`.

```csharp
public class LinkedAccounts
{
    public List<DummyAccountToken> DummyAccounts { get; set; } = [];
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}

public record GitHubAccountToken(
    string AccessToken,
    string Login,
    string DisplayName,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAt,
    DateTimeOffset ExpiresAt)
{
    public List<string> MonitoredRepos { get; set; } = []; // "owner/repo" format
}
```

`SessionTokenStore` (scoped) owns the in-memory cache and all reads/writes:
- **`LoadAsync()`** — called once from `OnAfterRenderAsync`. Reads `localStorage`, decrypts with `IDataProtector`, populates cache. Corrupt/tampered payloads are silently discarded.
- **`GetLinkedAccounts()`** — returns the in-memory cache synchronously (empty if `LoadAsync` hasn't run yet).
- **`LinkGitHubAccountAsync(token)`** / **`UnlinkGitHubAccountAsync(login)`** — mutate cache, re-encrypt, write to localStorage, fire `AccountsChanged`.
- **`UpdateGitHubMonitoredReposAsync(login, repos)`** — updates the monitored repo list for a linked GitHub account without unlinking.
- **`LinkDummyAccountAsync(token)`** / **`UnlinkDummyAccountAsync(accountName)`** — same pattern for dummy accounts.
- **`AccountsChanged`** event — `Home.razor` and `MainLayout.razor` subscribe to re-fetch data and update the UI.

Data Protection keys are persisted to a file path configured at deployment time (`DataProtection:KeyPath`) so the encryption key survives restarts. Falls back to `{ContentRoot}/keys` if not configured. Protection purpose: `"Nexus.LinkedAccounts.v1"`.

## GitHub OAuth flow

Two Minimal API endpoints in `Program.cs`:

1. **`GET /auth/github`** — Generates a random 32-byte state, stores it in an HttpOnly cookie (10 min expiry), then redirects to GitHub OAuth with scopes `repo read:user read:org`.
2. **`GET /auth/github/callback`** — Validates state cookie, exchanges the code for an access token, fetches the user's profile (login, name), encodes the `GitHubAccountToken` as Base64 JSON, and redirects to `/link-github?token=...`. The client-side `MainLayout.HandleGitHubCallbackAsync` decodes the token and calls `SessionTokenStore.LinkGitHubAccountAsync`.

After linking, `SelectGitHubReposDialog` opens so the user can choose which repos to monitor. It calls GitHub REST API (`/user/repos`, `/user/orgs`, `/orgs/{org}/repos`, `/search/repositories`) and persists selections via `UpdateGitHubMonitoredReposAsync`.

HTTP clients registered in `Program.cs`:
- `"GitHub"` — `https://api.github.com/`, Bearer auth, GitHub API version header.
- `"GitHubOAuth"` — `https://github.com/`, for token exchange.

Multiple GitHub accounts are supported.

## App settings structure

```json
{
  "DataProtection": { "KeyPath": "/var/keys" },
  "GitHub": { "ClientId": "", "ClientSecret": "", "Organization": "" },
  "Microsoft": { "ClientId": "", "ClientSecret": "", "AllowedTenants": ["tenant-id"] },
  "AdoProjects": [
    { "OrgUrl": "https://dev.azure.com/org", "ProjectName": "Project", "Team": "Team Name" }
  ]
}
```

`GitHub:ClientId` and `GitHub:ClientSecret` are required for the OAuth flow. Sensitive values must come from environment variables or a secrets manager, not `appsettings.json`. `GitHub:Organization` is defined in `GitHubSettings` but not yet used.

## Extending the app

- **New provider:** Add a token slot to `LinkedAccounts`, implement `IDataProvider` returning a `DashboardData`, wire construction into `AggregateDataProvider`.
- **Write actions:** New Minimal API endpoints + extended service methods — no structural changes needed.
