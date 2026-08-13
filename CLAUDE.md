# Seedarr - Claude Code Project Guide

## What is Seedarr?

BitTorrent seeding simulator built on Sonarr/Radarr *arr-family architecture. C# .NET 10 backend + React/TypeScript frontend. Ports all DFakeSeeder (Python GTK4) functionality into the NzbDrone framework with Sonarr/Radarr API integration for auto-seeding downloaded torrents.

**Port:** 9898 | **API:** V1 (`/api/v1/`)

## Build Commands

```bash
# Build entire solution
dotnet build src/Seedarr.sln

# Run application
dotnet run --project src/NzbDrone.Console/Seedarr.Console.csproj

# Run unit tests
dotnet test src/Seedarr.sln --filter "Category!=IntegrationTest"

# Run integration tests
dotnet test src/Seedarr.sln --filter "Category=IntegrationTest"

# Run all tests
dotnet test src/Seedarr.sln

# Frontend dev server
cd src/Seedarr.Frontend && npm install && npm start
```

## Architecture

### Project Structure

```
src/
  NzbDrone.Common/       -> Seedarr.Common.csproj    (DI, logging, HTTP, disk, serialization)
  NzbDrone.Core/         -> Seedarr.Core.csproj      (domain logic, all business rules)
  NzbDrone.SignalR/       -> Seedarr.SignalR.csproj   (real-time hub)
  Seedarr.Http/          -> Seedarr.Http.csproj      (REST framework, middleware)
  Seedarr.Api.V1/        -> Seedarr.Api.V1.csproj    (API controllers)
  NzbDrone.Host/         -> Seedarr.Host.csproj      (Kestrel, startup, middleware pipeline)
  NzbDrone.Console/      -> Seedarr.Console.csproj   (entry point)
  Seedarr.Frontend/      -> React app (webpack 5)
```

### NzbDrone Fork Pattern

- Directory names keep `NzbDrone.*` prefix for shared infrastructure (Common, Core, Host, Console, SignalR)
- `.csproj` files renamed to `Seedarr.*`
- `Directory.Build.props` maps assembly names back to `NzbDrone` namespaces where needed
- New projects (`Seedarr.Api.V1/`, `Seedarr.Http/`) use `Seedarr.*` namespaces

### Key Patterns

- **DI:** DryIoc auto-registration (interfaces = Singleton, concrete = Transient)
- **Database:** Dapper + FluentMigrator, SQLite default + PostgreSQL support
- **API:** RestControllerWithSignalR<TResource, TModel> base pattern
- **Commands:** Command base -> IExecute<T> handler -> CommandExecutor (3 workers)
- **Events:** EventAggregator pub/sub with IHandle<T>
- **Providers:** ThingiProvider pattern (IProvider -> ProviderDefinition -> ProviderFactory -> ProviderRepository)
- **Config:** Two-tier (XML file for host config, DB for app settings)

### Domain Entity Mapping (from Sonarr concepts)

| Sonarr Entity   | Seedarr Entity    |
|-----------------|-------------------|
| Series          | Torrent           |
| Episode         | Peer              |
| EpisodeFile     | TorrentFile       |
| Indexer         | TrackerProvider   |
| DownloadClient  | ClientProfile     |
| ImportList      | ArrConnection     |

## Conventions

### Code Style

- Follow existing Sonarr/Radarr patterns for infrastructure code
- No comments unless explaining non-obvious WHY
- Use `FluentValidation` for all request validation
- Use `NLog` for logging (inject `Logger` via DryIoc)
- Use `Polly` for retry/circuit-breaker on external calls
- Test classes mirror source structure: `NzbDrone.Core.Test/Torrents/TorrentServiceFixture.cs`

### Database Migrations

- Numbered migrations in `NzbDrone.Core/Datastore/Migration/`
- Format: `NNN_description.cs` (e.g., `001_initial_setup.cs`)
- Use FluentMigrator API: `Create.Table()`, `Alter.Table()`, etc.
- Never modify existing migrations; always create new ones

### API Controllers

- One controller per resource in `Seedarr.Api.V1/`
- Extend `RestControllerWithSignalR<TResource, TModel>` for CRUD with real-time updates
- Route attribute: `[VersionedApiController("torrents")]` produces `/api/v1/torrents`
- Return `TResource` (API model), never `TModel` (DB model) directly

### Frontend

- React 18 + TypeScript
- TanStack Query for data fetching
- Zustand for client state
- SignalR client for real-time updates
- CSS Modules for styling

## Key Dependencies

- **BencodeNET** - torrent file parsing
- **BouncyCastle.Cryptography** - RC4 for MSE/PE encryption
- **Open.NAT** - UPnP port mapping
- **DryIoc** - dependency injection
- **Dapper** - micro-ORM
- **FluentMigrator** - database migrations
- **FluentValidation** - request validation
- **Polly** - resilience policies

## Reference Projects

When implementing Seedarr features, reference these local codebases:

- `../Sonarr/` - Primary architecture reference (DI, datastore, commands, providers, API patterns)
- `../Radarr/` - Secondary reference (Directory.Build.props namespace mapping at line 99)
- `../d_fake_seeder/` - Feature reference (all BitTorrent protocol implementations, simulation logic)
