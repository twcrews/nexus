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

**Stack:** Blazor Interactive Server (frontend) + ASP.NET Core Minimal API (backend) on .NET 10.

### Authentication model

There is no authenticated user. The app uses an **anonymous encrypted session cookie** (via ASP.NET Core Data Protection) that holds all linked provider tokens. No auth middleware (`AddAuthentication`, `AddCookie`, `AddOAuth`) is used — Data Protection is consumed directly through a `SessionTokenStore` service.

```csharp
public class LinkedAccounts
{
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}
```

Data Protection keys are persisted to a file path configured at deployment time (`DataProtection:KeyPath`) so cookies survive restarts.

### Data providers

All data sources implement `IDataProviderService` and are registered with DI:

```csharp
public interface IDataProviderService
{
    Task<IEnumerable<WorkItem>> GetAssignedWorkItemsAsync();
    Task<IEnumerable<WorkItem>> GetUnassignedWorkItemsAsync();
    Task<IEnumerable<PullRequest>> GetAssignedPullRequestsAsync();
    Task<IEnumerable<PullRequest>> GetUnassignedPullRequestsAsync();
}
```

- **Azure DevOps:** Uses `Microsoft.TeamFoundationServer.Client` SDK. Auth via Entra ID OAuth 2.0 (manual flow); MSAL manages token refresh with a custom cache backed by the session cookie. ADO resource scope: `499b84ac-1321-427f-aa17-267ca6975798/.default`. Multiple ADO projects supported as separate service instances.
- **GitHub:** GitHub REST/GraphQL API. Auth via GitHub OAuth App (manual flow, `repo` scope). Token refresh is the provider's responsibility — check expiry before each call and update the session cookie with refreshed tokens.

### OAuth account linking

Both Microsoft and GitHub linking follow the same pattern:
1. A Minimal API endpoint builds the authorization URL (with a `state` param for CSRF) and redirects.
2. A callback endpoint exchanges the code for tokens via `HttpClient`.
3. Tokens are stored in the encrypted session cookie.

Multiple accounts per provider are supported.

### App settings structure

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

### Extending the app

- **New provider:** Add a token slot to `LinkedAccounts`, implement `IDataProviderService`, register with DI.
- **Write actions:** New Minimal API endpoints + extended service methods — no structural changes.
