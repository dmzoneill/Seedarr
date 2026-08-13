# Seedarr Architecture

## System Overview

Seedarr is a BitTorrent seeding simulator built on the *arr-family (Sonarr/Radarr) application framework. It simulates seeding activity for torrent files without transferring real data.

```mermaid
graph TB
    subgraph "Seedarr Application"
        subgraph "Frontend"
            UI[React SPA<br/>TypeScript + Webpack 5]
        end

        subgraph "API Layer"
            API[REST API V1<br/>ASP.NET Controllers]
            SR[SignalR Hub<br/>/signalr/messages]
        end

        subgraph "Core Domain"
            TE[Torrent Engine]
            SE[Seeding Simulator]
            TC[Tracker Communication]
            PP[Peer Protocol]
            SIM[Client Simulation]
        end

        subgraph "Infrastructure"
            DB[(SQLite/PostgreSQL<br/>Dapper + FluentMigrator)]
            CFG[Configuration<br/>XML + DB]
            CMD[Command System<br/>3 Workers]
            EVT[Event Aggregator]
            SCH[Task Scheduler]
        end
    end

    subgraph "External"
        TR[BitTorrent Trackers<br/>HTTP + UDP]
        PE[Peers<br/>TCP + uTP]
        DHT_NET[DHT Network]
        ARR[Sonarr / Radarr<br/>API v3]
    end

    UI <-->|HTTP + WebSocket| API
    UI <-->|Real-time| SR
    API --> TE
    API --> SE
    TE --> DB
    SE --> TC
    SE --> PP
    SE --> SIM
    TC --> TR
    PP --> PE
    TE --> DHT_NET
    TE --> ARR
    CMD --> EVT
    SCH --> CMD
```

## Project Dependency Graph

```mermaid
graph LR
    Console[Seedarr.Console] --> Host[Seedarr.Host]
    Host --> ApiV1[Seedarr.Api.V1]
    Host --> SignalR[Seedarr.SignalR]
    ApiV1 --> Http[Seedarr.Http]
    Http --> Core[Seedarr.Core]
    SignalR --> Common[Seedarr.Common]
    Core --> Common
    Http --> Common
```

| Project | Directory | Responsibility |
|---------|-----------|----------------|
| Seedarr.Common | `NzbDrone.Common/` | DI container, logging, HTTP utilities, disk operations, serialization, caching |
| Seedarr.Core | `NzbDrone.Core/` | All domain logic: torrents, seeding, trackers, peers, protocols, simulation |
| Seedarr.SignalR | `NzbDrone.SignalR/` | Single SignalR hub for real-time browser updates |
| Seedarr.Http | `Seedarr.Http/` | REST framework, middleware, auth, versioned routing |
| Seedarr.Api.V1 | `Seedarr.Api.V1/` | API controllers (one per resource) |
| Seedarr.Host | `NzbDrone.Host/` | Kestrel web server, startup pipeline, middleware registration |
| Seedarr.Console | `NzbDrone.Console/` | Console entry point, restart loop |
| Seedarr.Frontend | `Seedarr.Frontend/` | React SPA |

## NzbDrone Fork Pattern

```mermaid
graph TD
    subgraph "Directory Names (NzbDrone.*)"
        NC[NzbDrone.Common/]
        NCR[NzbDrone.Core/]
        NH[NzbDrone.Host/]
        NCO[NzbDrone.Console/]
        NS[NzbDrone.SignalR/]
    end

    subgraph "New Directories (Seedarr.*)"
        SH[Seedarr.Http/]
        SA[Seedarr.Api.V1/]
        SF[Seedarr.Frontend/]
    end

    subgraph "Directory.Build.props"
        MAP[RootNamespace mapping<br/>NzbDrone.* dirs keep NzbDrone namespace<br/>Seedarr.* dirs use Seedarr namespace]
    end

    NC --> MAP
    NCR --> MAP
    NH --> MAP
    NCO --> MAP
    NS --> MAP
    SH --> MAP
    SA --> MAP
```

## Request Flow

```mermaid
sequenceDiagram
    participant Browser
    participant Kestrel
    participant Auth
    participant Controller
    participant Service
    participant Repository
    participant DB
    participant SignalR

    Browser->>Kestrel: HTTP Request
    Kestrel->>Auth: Authentication Middleware
    Auth->>Controller: Authorized Request
    Controller->>Service: Business Logic
    Service->>Repository: Data Access
    Repository->>DB: Dapper Query
    DB-->>Repository: Results
    Repository-->>Service: Model
    Service-->>Controller: Model
    Controller-->>Browser: Resource (JSON)
    Controller->>SignalR: Broadcast Update
    SignalR-->>Browser: WebSocket Push
```

## Command/Event System

```mermaid
graph TD
    subgraph "Command Pipeline"
        TRIGGER[Trigger<br/>API / Scheduler / Internal] --> CMD[Command]
        CMD --> QUEUE[Command Queue]
        QUEUE --> W1[Worker 1]
        QUEUE --> W2[Worker 2]
        QUEUE --> W3[Worker 3]
        W1 --> EXEC["IExecute&lt;T&gt; Handler"]
        W2 --> EXEC
        W3 --> EXEC
    end

    subgraph "Event Pipeline"
        EXEC --> EVT[EventAggregator.PublishEvent]
        EVT --> H1["IHandle&lt;T&gt; Handler 1"]
        EVT --> H2["IHandle&lt;T&gt; Handler 2"]
        EVT --> H3["IHandle&lt;T&gt; Handler N"]
    end
```

## DI Container (DryIoc)

Auto-registration rules:
- **Interfaces** registered as **Singleton** (one instance per application lifetime)
- **Concrete classes** registered as **Transient** (new instance per resolution)
- No manual wiring needed for standard services
- Convention: `IFooService` + `FooService` auto-matched by DryIoc scanner

## Database Layer

```mermaid
graph TD
    subgraph "ORM Stack"
        REPO["BasicRepository&lt;T&gt;"] --> DAPPER[Dapper]
        DAPPER --> CONN[IDbConnection]
        CONN --> SQLITE[SQLite]
        CONN --> PG[PostgreSQL]
    end

    subgraph "Migration"
        FM[FluentMigrator] --> MIG1["001_initial_setup"]
        FM --> MIG2["002_add_tracker_fields"]
        FM --> MIGN["NNN_description"]
    end

    subgraph "Models"
        MB[ModelBase<br/>int Id] --> TORRENT[Torrent]
        MB --> TF[TorrentFile]
        MB --> PEER[Peer]
        MB --> TP[TrackerProvider]
    end
```
