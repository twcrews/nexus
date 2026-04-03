# Nexus

A Blazor dashboard for software engineers to consolidate work items and pull requests from Azure DevOps and GitHub into a single view.

## Local Development

**Prerequisites:** .NET 10 SDK

```bash
# Restore and build
dotnet build

# Run (HTTP: http://localhost:5120, HTTPS: https://localhost:7005)
dotnet run --project Nexus/Nexus.csproj

# Run tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~Nexus.Tests.ClassName"
```

## Configuration

The app requires OAuth app credentials for Microsoft (Entra ID) and GitHub. Copy `appsettings.json` and populate via user secrets or environment variables — do not commit secrets.

```bash
dotnet user-secrets set "Microsoft:ClientId" "<value>" --project Nexus
dotnet user-secrets set "Microsoft:ClientSecret" "<value>" --project Nexus
dotnet user-secrets set "GitHub:ClientId" "<value>" --project Nexus
dotnet user-secrets set "GitHub:ClientSecret" "<value>" --project Nexus
```

See [docs/spec.md](docs/spec.md) for the full app settings schema and architecture overview.
