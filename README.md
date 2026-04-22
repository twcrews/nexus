# Nexus

A Blazor dashboard for software engineers to consolidate work items and pull requests from multiple providers into a single view.

## Hosting

- **Target:** Self-hosted; the app is host-agnostic and can be deployed to any environment capable of running ASP.NET Core (bare metal, VM, container, etc.)
- **Persistent state:** ASP.NET Core Data Protection key ring, persisted to a directory configured at deployment time

## Local Development

**Prerequisites:** .NET 10 SDK

We use [`just`](https://just.systems) as a command runner. Install it, then:

```bash
# Start the app with hot reload (HTTP: http://localhost:5120, HTTPS: https://localhost:7005)
just start

# Build the solution
just build

# Run all tests
just test

# Start the VSCode debugger
just debug
```

You can also still use the standard `dotnet` commands, if you prefer.

## Authentication & Authorization

The app has no concept of a signed-in user. There is no authenticated vs. unauthenticated state. Instead, the app maintains an **encrypted localStorage entry** that holds a collection of linked provider tokens (Personal Access Tokens). The browser is the only place user-specific state is stored.

### `localStorage` Storage

- Encrypted and tamper-proof using **ASP.NET Core Data Protection**
- Contains a serialized `LinkedAccounts` model holding PATs for all linked providers
- Corrupt or tampered payloads are silently discarded

```csharp
public class LinkedAccounts
{
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}
```

A `SessionTokenStore` service handles reading and writing the encrypted localStorage entry via `IDataProtector`, and is the only point of access for session state throughout the app.

### Data Protection Key Ring

- Keys are persisted to a directory configured at deployment time
- Ensures the encryption key survives application restarts
- Example configuration:
  ```csharp
  builder.Services.AddDataProtection()
      .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeyPath"]));
  ```

## Data Providers

Each provider is implemented as a service behind a shared interface, registered with the DI container.

```csharp
public interface IDataProvider
{
    Task<DashboardData> GetDashboardDataAsync();
}

public record DashboardData(IEnumerable<WorkItem> WorkItems, IEnumerable<PullRequest> PullRequests);
```

An `AggregateDataProvider` fans out to all per-account providers in parallel and merges results. Provider failures are isolated — errors are logged but don't crash the dashboard.

### Azure DevOps
- **SDK:** `Microsoft.TeamFoundationServer.Client`
- **Auth:** `VssBasicCredential(string.Empty, pat)` — username is always empty for PAT auth
- Work items fetched via WIQL scoped to team area paths; PRs fetched per repo via `GitHttpClient`
- Monitored projects and repos are configured per linked account (stored in `MicrosoftAccountToken.MonitoredProjects`)

### GitHub
- **API:** GitHub GraphQL API — all monitored repos batched into a single request
- **Auth:** Bearer PAT from the linked account
- Monitored repos are configured per linked account (stored in `GitHubAccountToken.MonitoredRepos` as `"owner/repo"` strings)

## Configuration

The only required app setting is the Data Protection key path:

```json
{
  "DataProtection": {
    "KeyPath": "/var/keys"
  }
}
```

## UI

- **Render mode:** Blazor Interactive Server
- **Component library:** MudBlazor
- **Single-page dashboard** with two filterable lists:
  - Work items
  - Pull requests
- A **settings panel** for linking and unlinking accounts
- Data refreshes on a 60-second polling interval; no webhook infrastructure required

## Contributing

Feel free to open a pull request with fixes or new features.

The following are some suggestions for contribution:

- Support for additional data providers
- Support for creating/updating work items and PRs
- Support for CI/CD monitoring

### Adding Data Providers

When implementing a new provider:

1. Add a new token type to `LinkedAccounts`
2. Implement `IDataProvider`
3. Wire construction into `AggregateDataProvider.BuildProviders()`