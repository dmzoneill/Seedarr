# Development Guide

## Prerequisites

- .NET 10 SDK
- Node.js 20+ and npm
- SQLite (bundled) or PostgreSQL (optional)

## Getting Started

```bash
# Clone
git clone https://github.com/dmzoneill/Seedarr.git
cd Seedarr

# Build backend
dotnet build src/Seedarr.sln

# Install frontend dependencies
cd src/Seedarr.Frontend
npm install
cd ../..

# Run (backend + frontend dev server)
dotnet run --project src/NzbDrone.Console/Seedarr.Console.csproj
```

Application starts on `http://localhost:9898`.

## Project Layout

```
Seedarr/
  docs/                     Architecture documentation
  src/
    NzbDrone.Common/        Shared infrastructure (DI, logging, HTTP)
    NzbDrone.Core/          Domain logic
      ArrIntegration/       Sonarr/Radarr/Lidarr API clients + webhooks
      Configuration/        Two-tier config system
      Datastore/            Dapper + FluentMigrator
        Migration/          Numbered DB migrations (001-017)
      Dht/                  BEP 5 DHT implementation
      DownloadClients/      qBittorrent, Transmission, Deluge integration
      HealthCheck/          Health check framework
      Indexers/             Prowlarr, Torznab, Newznab integration
      Jobs/                 Task scheduler
      Messaging/            Command + Event system
      Network/              UPnP, proxy
      Notifications/        Notification providers
      Peers/                Peer wire protocol, MSE, extensions
        Encryption/         MSE/PE (DH + RC4)
        Extensions/         PEX, metadata, fast extension, lt_donthave
        Lpd/                Local Peer Discovery
      Seeding/              Simulation engine
        Distribution/       Speed distribution algorithms
        Scheduling/         Time-based speed control + SpeedSchedule model
      Simulation/           Client behavior, traffic, swarm
        ClientBehavior/     5 client profiles (qBittorrent, Deluge, Transmission, uTorrent, BiglyBT)
        Swarm/              Swarm health analysis
        Traffic/            Traffic pattern simulation
      Tags/                 Tag system
      Torrents/             Torrent domain model + TrackerEntry
      TrackerServer/        Built-in HTTP/UDP tracker
      Trackers/             Tracker communication
        Http/               BEP 3 HTTP tracker
        Udp/                BEP 15 UDP tracker
        MultiTracker/       BEP 12 multi-tracker
      Transport/            uTP (BEP 29)
    NzbDrone.SignalR/       SignalR hub
    Seedarr.Http/           REST framework
    Seedarr.Api.V1/         API controllers (19 files, 24 controller classes)
    NzbDrone.Host/          Web server, startup
    NzbDrone.Console/       Entry point
    Seedarr.Frontend/       React SPA
    NzbDrone.Core.Test/     Unit tests (NUnit)
  test-integration.sh       Shell-based integration tests (52 tests)
  .github/workflows/       CI/CD pipelines
```

## Testing

### Unit Tests

```bash
# Unit tests only
dotnet test src/Seedarr.sln --filter "Category!=IntegrationTest"

# All dotnet tests
dotnet test src/Seedarr.sln

# Specific test class
dotnet test src/Seedarr.sln --filter "FullyQualifiedName~InfoHashCalculatorTest"
```

### Integration Tests

Integration tests use `test-integration.sh` — a shell script that brings up the full podman-compose stack and runs 52 API-level tests against live services.

```bash
# Full integration (builds, starts stack, configures, tests)
make integration

# Re-run against already-running stack
make test-integration-rerun

# Just the shell tests (stack must be running)
make test-integration-only
```

### Test Conventions

- Test project: `NzbDrone.Core.Test/` (mirrors source structure)
- Test class naming: `{ClassName}Test.cs`
- Framework: **NUnit 4.6** with built-in assertions (`Assert.That` / `Assert.Multiple`)
- Coverage: **coverlet.collector**
- No Moq, no FluentAssertions — pure NUnit constraint model
- Integration tests: shell-based via `test-integration.sh`, run against podman-compose stack

### Existing Test Files

| Test File                                        | What it covers                                           |
| ------------------------------------------------ | -------------------------------------------------------- |
| `Torrents/InfoHashCalculatorTest.cs`             | SHA-1 info hash calculation (7 tests)                    |
| `Simulation/ClientBehavior/ClientProfileTest.cs` | Peer ID generation for qBittorrent, Deluge, Transmission |
| `Seeding/Distribution/SpeedDistributorTest.cs`   | EqualDistributor and ParetoDistributor behavior          |

## Database Migrations

```bash
# Migrations auto-run on startup
# To add a new migration:
# 1. Create NzbDrone.Core/Datastore/Migration/NNN_description.cs
# 2. Use FluentMigrator API
# 3. Never modify existing migrations
```

```csharp
// Example migration (from 002_add_torrents.cs)
[Migration(2)]
public class AddTorrents : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        Create.Table("Torrents")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("InfoHash").AsString().NotNullable();
        // ...
    }
}
```

### Migration History

| #   | Migration                       | Tables/Columns                                                         |
| --- | ------------------------------- | ---------------------------------------------------------------------- |
| 001 | `initial_setup`                 | Config, ScheduledTasks, Commands, Tags                                 |
| 002 | `add_torrents`                  | Torrents, TorrentFiles                                                 |
| 003 | `add_tracker_providers`         | TrackerProviderDefinitions                                             |
| 004 | `add_client_profiles`           | ClientProfileDefinitions                                               |
| 005 | `add_arr_connections`           | ArrConnectionDefinitions                                               |
| 006 | `add_notification_definitions`  | NotificationDefinitions                                                |
| 007 | `add_speed_schedules`           | SpeedSchedules                                                         |
| 008 | `add_download_clients`          | DownloadClientDefinitions                                              |
| 009 | `add_torrent_options`           | +Priority, UploadLimit, DownloadLimit, SuperSeeding, ForceStart, Label |
| 010 | `add_download_progress`         | +Progress                                                              |
| 011 | `add_tracker_entries`           | TrackerEntries (announce stats, response times)                        |
| 012 | `add_torrent_runtime_fields`    | +SequentialDownload, AnnounceInterval, speeds, Eta, etc.               |
| 013 | `add_arr_connection_sync_flags` | +SyncEnabled, EnableAutomaticAdd                                       |
| 014 | `add_torrent_sort_order`        | +SortOrder                                                             |
| 015 | `add_force_completed`           | +ForceCompleted                                                        |
| 016 | `add_webhook_enabled`           | +WebhookEnabled                                                        |
| 017 | `add_indexer_definitions`       | IndexerDefinitions                                                     |

## Configuration

### Host Config (XML)

Location: `~/.config/Seedarr/config.xml`

```xml
<Config>
  <Port>9898</Port>
  <BindAddress>*</BindAddress>
  <ApiKey>auto-generated</ApiKey>
  <LogLevel>info</LogLevel>
</Config>
```

### App Config (Database)

Stored in `Config` table, managed via `ConfigService`. Accessed through 10 config API sections (general, seeding, bittorrent, network, peerprotocol, protocols, simulation, trackerserver, scheduler, advanced).

## Makefile Targets

```bash
make setup              # Restore .NET + npm dependencies
make test-setup         # Build solution (Release)
make test               # Run unit tests (excludes IntegrationTest category)
make integration        # Full integration: build stack, start, configure, test
make test-integration   # Same as integration
make test-integration-rerun  # Re-run tests on existing stack
make test-integration-only   # Run test script only (stack must be up)
make test-all           # Unit + integration tests
make build              # setup + test-setup
make publish            # Publish release artifacts
make frontend           # Build frontend production bundle
make clean              # Clean build artifacts
make stack-up           # Start podman-compose stack
make stack-down         # Stop stack
make stack-clean        # Stop + remove containers + volumes
make stack-rebuild      # Rebuild seedarr container (no cache)
```

## Debugging

```bash
# Run with debug logging
dotnet run --project src/NzbDrone.Console/Seedarr.Console.csproj -- --log-level=debug

# Swagger UI available at
# http://localhost:9898/swagger
```
