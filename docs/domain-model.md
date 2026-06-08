# Seedarr Domain Model

## Entity Relationship Diagram

```mermaid
erDiagram
    Torrent {
        int Id PK
        string Name
        string InfoHash
        long TotalSize
        int PieceLength
        int PieceCount
        string Source
        datetime Added
        string Status
        long Uploaded
        long Downloaded
        float Ratio
    }

    TorrentFile {
        int Id PK
        int TorrentId FK
        string Path
        long Size
        int FirstPiece
        int LastPiece
    }

    TrackerProvider {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        bool EnableHttp
        bool EnableUdp
    }

    TorrentTracker {
        int Id PK
        int TorrentId FK
        int TrackerProviderId FK
        string AnnounceUrl
        string Status
        int Seeders
        int Leechers
        datetime LastAnnounce
        datetime NextAnnounce
    }

    ClientProfile {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        string PeerIdPrefix
        string UserAgent
    }

    ArrConnection {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        string BaseUrl
        string ApiKey
        int SyncIntervalMinutes
    }

    Tag {
        int Id PK
        string Label
    }

    Notification {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
    }

    Config {
        int Id PK
        string Key
        string Value
    }

    ScheduledTask {
        int Id PK
        string TypeName
        int Interval
        datetime LastExecution
        datetime LastStartTime
    }

    Torrent ||--o{ TorrentFile : "has files"
    Torrent ||--o{ TorrentTracker : "announces to"
    TrackerProvider ||--o{ TorrentTracker : "provides"
    Torrent }o--o{ Tag : "tagged with"
    TrackerProvider }o--o{ Tag : "tagged with"
    ClientProfile }o--o{ Tag : "tagged with"
    ArrConnection }o--o{ Tag : "tagged with"
```

## Torrent Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Added: Upload .torrent / Watch folder / *arr sync
    Added --> Parsing: TorrentFileParser
    Parsing --> Ready: Metadata extracted
    Ready --> Seeding: Start command
    Seeding --> Announcing: Tracker announce cycle
    Announcing --> Seeding: Announce success
    Seeding --> Paused: Pause command
    Paused --> Seeding: Resume command
    Seeding --> Stopped: Stop command
    Stopped --> Seeding: Start command
    Seeding --> Error: Tracker/network failure
    Error --> Seeding: Retry / backoff
    Stopped --> [*]: Remove command
```

## Seeding Simulation Flow

```mermaid
flowchart TD
    START[Seeding Engine Tick] --> SELECT[Select active torrents]
    SELECT --> DIST[Speed Distribution Manager]
    DIST --> PARETO[Pareto Distribution]
    DIST --> POWER[Power Law Distribution]
    DIST --> LOG[Log-Normal Distribution]
    DIST --> EQUAL[Equal Distribution]

    PARETO --> SPEED[Allocated speed per torrent]
    POWER --> SPEED
    LOG --> SPEED
    EQUAL --> SPEED

    SPEED --> SCHED{Schedule check}
    SCHED -->|Active hours| FULL[Full speed]
    SCHED -->|Alt speed hours| ALT[Reduced speed]

    FULL --> TRAFFIC[Traffic Pattern Simulator]
    ALT --> TRAFFIC

    TRAFFIC --> CLIENT[Client Behavior Profile]
    CLIENT --> UPDATE[Update torrent stats<br/>uploaded += simulated bytes]
    UPDATE --> ANNOUNCE{Announce interval?}
    ANNOUNCE -->|Yes| TRACKER[Send tracker announce<br/>with updated stats]
    ANNOUNCE -->|No| START
    TRACKER --> START
```

## ThingiProvider Pattern

Used for pluggable components (Trackers, ClientProfiles, ArrConnections, Notifications).

```mermaid
classDiagram
    class IProvider {
        <<interface>>
        +string Name
        +ProviderMessage Message
    }

    class ProviderDefinition {
        +int Id
        +string Name
        +string Implementation
        +string ConfigContract
        +ProviderSettings Settings
        +bool Enable
    }

    class ProviderFactory~TProvider, TDefinition~ {
        +List~TDefinition~ All()
        +TDefinition Get(int id)
        +TDefinition Create(TDefinition definition)
        +void Update(TDefinition definition)
        +void Delete(int id)
        +TProvider GetInstance(TDefinition definition)
    }

    class ProviderRepository~TDefinition~ {
        +BasicRepository methods
    }

    IProvider <|-- TrackerProvider
    IProvider <|-- ClientProfile
    IProvider <|-- ArrConnection
    IProvider <|-- Notification
    ProviderDefinition <|-- TrackerProviderDefinition
    ProviderDefinition <|-- ClientProfileDefinition
    ProviderFactory --> ProviderDefinition
    ProviderFactory --> ProviderRepository
    ProviderFactory --> IProvider
```
