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
      ArrIntegration/       Sonarr/Radarr API clients
      Configuration/        Two-tier config system
      Datastore/            Dapper + FluentMigrator
        Migration/          Numbered DB migrations
      Dht/                  BEP 5 DHT implementation
      HealthCheck/          Health check framework
      Jobs/                 Task scheduler
      Messaging/            Command + Event system
      Network/              UPnP, proxy
      Notifications/        Notification providers
      Peers/                Peer wire protocol, MSE, extensions
        Encryption/         MSE/PE (DH + RC4)
        Extensions/         PEX, metadata, fast extension
        Lpd/                Local Peer Discovery
      Seeding/              Simulation engine
        Distribution/       Speed distribution algorithms
        Scheduling/         Time-based speed control
      Simulation/           Client behavior, traffic, swarm
        ClientBehavior/     5 client profiles
        Swarm/              Swarm health analysis
        Traffic/            Traffic pattern simulation
      Tags/                 Tag system
      Torrents/             Torrent domain model
      TrackerServer/        Built-in tracker
      Trackers/             Tracker communication
        Http/               BEP 3 HTTP tracker
        Udp/                BEP 15 UDP tracker
        MultiTracker/       BEP 12 multi-tracker
      Transport/            uTP (BEP 29)
    NzbDrone.SignalR/       SignalR hub
    Seedarr.Http/           REST framework
    Seedarr.Api.V1/         API controllers
    NzbDrone.Host/          Web server, startup
    NzbDrone.Console/       Entry point
    Seedarr.Frontend/       React SPA
    NzbDrone.Core.Test/     Unit tests
    NzbDrone.Integration.Test/  Integration tests
  .github/workflows/       CI/CD pipelines
```

## Testing

```bash
# Unit tests only
dotnet test src/Seedarr.sln --filter "Category!=IntegrationTest"

# Integration tests only
dotnet test src/Seedarr.sln --filter "Category=IntegrationTest"

# All tests
dotnet test src/Seedarr.sln

# Specific test class
dotnet test src/Seedarr.sln --filter "FullyQualifiedName~TorrentServiceFixture"
```

### Test Conventions

- Test projects mirror source structure
- Test class naming: `{ClassName}Fixture.cs`
- Use NUnit + Moq + FluentAssertions
- Unit tests: mock all dependencies
- Integration tests: use in-memory SQLite, marked `[Category("IntegrationTest")]`
- Test data builders for domain objects

## Database Migrations

```bash
# Migrations auto-run on startup
# To add a new migration:
# 1. Create NzbDrone.Core/Datastore/Migration/NNN_description.cs
# 2. Use FluentMigrator API
# 3. Never modify existing migrations
```

```csharp
// Example migration
[Migration(2)]
public class AddTrackerFields : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        Alter.Table("TorrentTrackers")
            .AddColumn("Seeders").AsInt32().WithDefaultValue(0)
            .AddColumn("Leechers").AsInt32().WithDefaultValue(0);
    }
}
```

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

Stored in `Config` table, managed via `ConfigService`. Accessed through Settings API.

## Debugging

```bash
# Run with debug logging
dotnet run --project src/NzbDrone.Console/Seedarr.Console.csproj -- --log-level=debug

# Swagger UI available at
# http://localhost:9898/swagger
```
