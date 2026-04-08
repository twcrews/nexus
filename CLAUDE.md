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

**Stack:** Blazor Interactive Server (frontend) + ASP.NET Core Minimal API (backend) on .NET 10. Uses **Radzen.Blazor** for UI components and theming.

## Current state

The project is in a UI foundation phase. The dashboard shell, component hierarchy, and mock data layer are built. Real data providers and authentication are not yet implemented.

- **Components:** Full dashboard UI — metric cards, tabbed work item and PR lists, animated numbers, theme toggle, header with refresh timestamp.
- **`DummyProvider`:** A mock `IDataProvider` that returns randomly generated work items and PRs. Used until real providers are wired in.
- **`RefreshService`:** Tracks the last data refresh time (`LastRefreshed`). Components poll this to display "refreshed X seconds ago" in the header. `Home.razor` auto-refreshes on a 60-second `PeriodicTimer`.
- **No authentication yet.** OAuth, session cookie storage, and real ADO/GitHub providers are planned (see below) but not implemented.

## Data providers

All data sources implement `IDataProvider` and are registered with DI:

```csharp
public interface IDataProvider
{
    Task<IEnumerable<WorkItem>> GetAssignedWorkItemsAsync();
    Task<IEnumerable<WorkItem>> GetUnassignedWorkItemsAsync();
    Task<IEnumerable<PullRequest>> GetAssignedPullRequestsAsync();
    Task<IEnumerable<PullRequest>> GetUnassignedPullRequestsAsync();
}
```

Currently `DummyProvider` is the only implementation and is registered as a scoped `IDataProvider` in `Program.cs`.

**Planned real providers:**
- **Azure DevOps:** `Microsoft.TeamFoundationServer.Client` SDK. Auth via Entra ID OAuth 2.0 (manual flow); MSAL manages token refresh with a custom cache backed by the session cookie. ADO resource scope: `499b84ac-1321-427f-aa17-267ca6975798/.default`. Multiple ADO projects as separate service instances.
- **GitHub:** GitHub REST/GraphQL API. Auth via GitHub OAuth App (manual flow, `repo` scope). Provider checks token expiry before each call and updates the session cookie with refreshed tokens.

## Planned authentication model

There will be no authenticated user. The app will use an **anonymous encrypted session cookie** (via ASP.NET Core Data Protection) to hold all linked provider tokens. No auth middleware (`AddAuthentication`, `AddCookie`, `AddOAuth`) — Data Protection consumed directly through a `SessionTokenStore` service.

```csharp
public class LinkedAccounts
{
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}
```

Data Protection keys will be persisted to a file path configured at deployment time (`DataProtection:KeyPath`) so cookies survive restarts.

Both Microsoft and GitHub linking follow the same OAuth pattern:
1. A Minimal API endpoint builds the authorization URL (with a `state` param for CSRF) and redirects.
2. A callback endpoint exchanges the code for tokens via `HttpClient`.
3. Tokens are stored in the encrypted session cookie.

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

Sensitive values must come from environment variables or a secrets manager, not `appsettings.json`.

## Extending the app

- **New provider:** Add a token slot to `LinkedAccounts`, implement `IDataProvider`, register with DI.
- **Write actions:** New Minimal API endpoints + extended service methods — no structural changes.
