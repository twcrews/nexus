# Nexus

A Blazor application for software engineers to consolidate work items and pull requests from multiple platforms into a single dashboard.

## Goals

### MVP
- View work items/issues assigned to the current user
- View unassigned work items/issues
- View pull requests assigned to the current user
- View unassigned pull requests
- All of the above filterable, on a single page
- Support data from Azure DevOps (multiple projects) and GitHub (organization-owned repos)

### Future Iterations
- Support for additional data providers
- Create/update work items and PRs from the dashboard
- Monitor and interact with CI/CD pipelines

## Hosting

- **Target:** Self-hosted; the app is host-agnostic and can be deployed to any environment capable of running ASP.NET Core (bare metal, VM, container, etc.)
- **Persistent state:** ASP.NET Core Data Protection key ring, persisted to a directory configured at deployment time

## Architecture

### Technology Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor Interactive Server |
| Backend | ASP.NET Core Minimal API |

## Authentication & Authorization

### Philosophy

The app has no concept of a signed-in user. There is no authenticated vs. unauthenticated state. Instead, the app maintains an **anonymous encrypted session cookie** that holds a collection of linked provider tokens. The browser is the only place user-specific state is stored.

### Session Cookie

- Encrypted and tamper-proof using **ASP.NET Core Data Protection**
- Contains a serialized `LinkedAccounts` model holding tokens for all linked providers
- `HttpOnly`, `Secure`, `SameSite=Strict`
- No auth middleware (`AddAuthentication`, `AddCookie`, `AddOAuth`, etc.) is used — Data Protection is consumed directly

```csharp
public class LinkedAccounts
{
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}
```

A `SessionTokenStore` service handles reading and writing the encrypted cookie via `IDataProtector`, and is the only point of access for session state throughout the app.

### Data Protection Key Ring

- Keys are persisted to a directory configured at deployment time
- Ensures session cookies survive application restarts
- Example configuration:
  ```csharp
  builder.Services.AddDataProtection()
      .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeyPath"]));
  ```

### Microsoft Account Linking (for Azure DevOps)

- Implemented as a manual OAuth 2.0 authorization code flow against **Entra ID**
- A Minimal API endpoint builds the authorization URL (including a `state` parameter for CSRF) and redirects the user
- The callback endpoint exchanges the code for tokens directly via `HttpClient`
- The resulting access token, refresh token, and expiry are stored in the session cookie
- Multiple Microsoft accounts can be linked; each is a separate entry in `LinkedAccounts.MicrosoftAccounts`
- **MSAL** (`Microsoft.Identity.Client`) is used on the server side to manage token acquisition and refresh, with a custom token cache backed by the session cookie

### GitHub Account Linking

- Implemented as a manual OAuth 2.0 authorization code flow against GitHub
- Same pattern as Microsoft account linking: redirect endpoint → callback endpoint → store tokens in cookie
- A **GitHub OAuth App** is registered in the organization
- Multiple GitHub accounts can be linked; each is a separate entry in `LinkedAccounts.GitHubAccounts`
- Scope requested: `repo` (read-only access to private org repos)

### Token Flow Summary

| Platform | Auth Method | Token Storage |
|---|---|---|
| ADO API | Entra ID OAuth 2.0 (manual flow) | Encrypted session cookie |
| GitHub API | GitHub OAuth App (manual flow) | Encrypted session cookie |

### Token Refresh

There is no framework-managed refresh mechanism. Token refresh is the responsibility of the data provider services:

1. Before each API call, check the token's expiry
2. If expired, use the stored refresh token to acquire a new access token from the provider's token endpoint
3. Update the token entry in the session cookie with the new access token, refresh token, and expiry
4. Proceed with the API call

For the Microsoft/ADO side, MSAL handles this automatically given a correctly implemented custom token cache.

## Data Providers

Each provider is implemented as a service behind a shared interface, registered with the DI container.

```csharp
public interface IDataProviderService
{
    Task<IEnumerable<WorkItem>> GetAssignedWorkItemsAsync();
    Task<IEnumerable<WorkItem>> GetUnassignedWorkItemsAsync();
    Task<IEnumerable<PullRequest>> GetAssignedPullRequestsAsync();
    Task<IEnumerable<PullRequest>> GetUnassignedPullRequestsAsync();
}
```

### Azure DevOps
- **SDK:** `Microsoft.TeamFoundationServer.Client`
- **Auth:** Entra access token from the session cookie, refreshed via MSAL as needed
- **Scope:** `499b84ac-1321-427f-aa17-267ca6975798/.default` (Azure DevOps resource)
- Multiple ADO projects are supported — each project is a separate service instance, configured via app settings
- Work items are filtered by the user's **Team** within each project board

### GitHub
- **API:** GitHub REST API (or GraphQL for efficiency)
- **Auth:** GitHub OAuth token from the session cookie, refreshed as needed
- **Scope:** `repo`
- Organization and repo list configured via app settings

### App Settings Structure (suggested)

```json
{
  "DataProtection": {
    "KeyPath": "/var/keys"
  },
  "Microsoft": {
    "ClientId": "",
    "ClientSecret": "",
    "AllowedTenants": [ "client-tenant-id" ]
  },
  "GitHub": {
    "ClientId": "",
    "ClientSecret": "",
    "Organization": ""
  },
  "AdoProjects": [
    { "OrgUrl": "https://dev.azure.com/client-org", "ProjectName": "CAOS", "Team": "Our Team" },
    { "OrgUrl": "https://dev.azure.com/client-org", "ProjectName": "QT.Mobile", "Team": "Our Team" }
  ]
}
```

Sensitive values (`ClientSecret`, etc.) should be provided via environment variables or a secrets manager at runtime, not stored in `appsettings.json`.

## UI

- **Render mode:** Blazor Interactive Server
- **Component library:** MudBlazor or Radzen (TBD) for a polished dashboard feel
- **Single-page dashboard** with four filterable lists:
  - Work items assigned to me
  - Unassigned work items
  - Pull requests assigned to me
  - Unassigned pull requests
- Filters include: provider (ADO project / GitHub), date range, label/tag
- A **settings panel** for linking and unlinking Microsoft and GitHub accounts
- Data refreshes on a polling interval (suggested: 60 seconds), no webhook infrastructure required for MVP

## Expandability Considerations

- The `IDataProviderService` interface makes adding new providers (Jira, GitLab, etc.) straightforward — add a token slot to `LinkedAccounts`, implement the interface, register with DI
- Write actions (commenting, transitioning work items, approving PRs) are additive — new Minimal API endpoints and extended service methods, no structural changes required
- The session cookie model naturally supports any number of linked accounts per provider