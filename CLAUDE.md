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

The project is in a UI foundation phase. The dashboard shell, component hierarchy, and mock data layer are built. Real data providers and authentication are not yet implemented.

- **Components:** Full dashboard UI — metric cards, tabbed work item and PR lists, animated numbers, theme toggle, header with refresh timestamp.
- **`DummyProvider`:** A mock `IDataProvider` that returns randomly generated work items and PRs. Used until real providers are wired in. Tags each work item with the account name as a label and prefixes PR repo names with the account name so data from different instances is visually distinguishable.
- **`AggregateDataProvider`:** Implements `IDataProvider` by combining results from all `DummyProvider` instances constructed from the linked dummy accounts. Registered as the scoped `IDataProvider` in `Program.cs`. Will add real provider construction here as GitHub/ADO providers are implemented.
- **`RefreshService`:** Tracks the last data refresh time (`LastRefreshed`). Components poll this to display "refreshed X seconds ago" in the header. `Home.razor` auto-refreshes on a 60-second `PeriodicTimer`.
- **`SessionTokenStore`:** Scoped service that encrypts/decrypts linked account tokens using ASP.NET Core Data Protection and persists them to `localStorage` via JSInterop. Loads on first render; fires `AccountsChanged` when accounts are mutated.

## Data providers

All data sources implement `IDataProvider` and are aggregated via `AggregateDataProvider`:

```csharp
public interface IDataProvider
{
    Task<IEnumerable<WorkItem>> GetAssignedWorkItemsAsync();
    Task<IEnumerable<WorkItem>> GetUnassignedWorkItemsAsync();
    Task<IEnumerable<PullRequest>> GetAssignedPullRequestsAsync();
    Task<IEnumerable<PullRequest>> GetUnassignedPullRequestsAsync();
}
```

`AggregateDataProvider` is registered as the scoped `IDataProvider`. It constructs per-account provider instances and fans out calls to all of them. Currently only `DummyProvider` instances are constructed; `MicrosoftAccountToken` and `GitHubAccountToken` entries in `LinkedAccounts` are not yet wired to real providers.

**Planned real providers:**
- **Azure DevOps:** `Microsoft.TeamFoundationServer.Client` SDK. Auth via Entra ID OAuth 2.0 (manual flow); MSAL manages token refresh with a custom cache backed by the encrypted localStorage entry. ADO resource scope: `499b84ac-1321-427f-aa17-267ca6975798/.default`. Multiple ADO projects as separate service instances.
- **GitHub:** GitHub REST/GraphQL API. Auth via GitHub OAuth App (manual flow, `repo` scope). Provider checks token expiry before each call and updates the localStorage entry with refreshed tokens.

## Authentication model

There is no authenticated user. The app stores all linked provider tokens in a single **encrypted localStorage entry** (`nexus.linked-accounts`). No auth middleware — ASP.NET Core Data Protection is consumed directly through `SessionTokenStore`.

```csharp
public class LinkedAccounts
{
    public List<DummyAccountToken> DummyAccounts { get; set; } = [];
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}
```

`SessionTokenStore` (scoped) owns the in-memory cache and all reads/writes:
- **`LoadAsync()`** — called once from `OnAfterRenderAsync` (JS interop requires the browser to be connected). Reads `localStorage`, decrypts with `IDataProtector`, and populates the cache. Corrupt/tampered payloads are silently discarded and the store starts fresh.
- **`GetLinkedAccounts()`** — returns the in-memory cache synchronously (empty if `LoadAsync` hasn't run yet).
- **`LinkDummyAccountAsync(token)`** — mutates the cache, re-encrypts, writes to localStorage, then fires `AccountsChanged`.
- **`UnlinkDummyAccountAsync(accountName)`** — removes account by name, re-encrypts, writes to localStorage, then fires `AccountsChanged`.
- **`AccountsChanged`** event — `Home.razor` and `MainLayout.razor` subscribe to re-fetch data and update the header button label respectively.

Data Protection keys are persisted to a file path configured at deployment time (`DataProtection:KeyPath`) so the encryption key survives restarts and localStorage entries remain readable. Falls back to `{ContentRoot}/keys` if not configured.

Both Microsoft and GitHub linking will follow the same OAuth pattern:
1. A Minimal API endpoint builds the authorization URL (with a `state` param for CSRF) and redirects.
2. A callback endpoint exchanges the code for tokens via `HttpClient`.
3. Tokens are encrypted and written to localStorage via `SessionTokenStore`.

OAuth endpoints are **not yet implemented** — no Minimal API routes exist beyond the Blazor component mapping.

Multiple accounts per provider are supported.

## App settings structure (planned)

```json
{
  "DataProtection": { "KeyPath": "/var/keys" },
  "Microsoft": { "ClientId": "", "ClientSecret": "", "AllowedTenants": ["tenant-id"] },
  "GitHub": { "ClientId": "", "ClientSecret": "", "Organization": "" },
  "AdoProjects": [
    { "OrgUrl": "https://dev.azure.com/org", "ProjectName": "Project", "Team": "Team Name" }
  ]
}
```

Currently `appsettings.json` contains only logging configuration. Sensitive values must come from environment variables or a secrets manager, not `appsettings.json`.

## Extending the app

- **New provider:** Add a token slot to `LinkedAccounts`, implement `IDataProvider`, wire construction into `AggregateDataProvider`.
- **Write actions:** New Minimal API endpoints + extended service methods — no structural changes.
