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
        string Comment
        string CreatedBy
        datetime CreationDate
        bool IsPrivate
        string Status
        long Uploaded
        long Downloaded
        float Ratio
        int Seeders
        int Leechers
        string TrackerUrl
        string SourcePath
        datetime DateAdded
        datetime LastActive
        int Priority
        int UploadLimit
        int DownloadLimit
        bool SuperSeeding
        bool ForceStart
        bool ForceCompleted
        string Label
        float Progress
        bool SequentialDownload
        int AnnounceInterval
        long SessionUploaded
        long SessionDownloaded
        long UploadSpeed
        long DownloadSpeed
        bool Active
        float Availability
        long Eta
        int SortOrder
    }

    TorrentFile {
        int Id PK
        int TorrentId FK
        string Path
        long Size
        int PieceOffset
        int PieceCount
    }

    TrackerEntry {
        int Id PK
        int TorrentId FK
        string Url
        int Tier
        string Status
        bool Enabled
        int Seeders
        int Leechers
        int Downloaded
        int TotalAnnounces
        int SuccessfulAnnounces
        int ConsecutiveFailures
        long LastResponseTime
        long AverageResponseTime
        int AnnounceInterval
        int MinAnnounceInterval
        datetime LastAnnounce
        datetime LastScrape
        datetime NextAnnounce
        string ErrorMessage
        string WarningMessage
    }

    TrackerProviderDefinition {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        bool Enable
        int Priority
    }

    ClientProfileDefinition {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        bool Enable
        int Priority
    }

    ArrConnectionDefinition {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        bool Enable
        string Url
        string ApiKey
        string ArrType
        int SyncIntervalMinutes
        bool SyncEnabled
        bool EnableAutomaticAdd
        bool WebhookEnabled
    }

    DownloadClientDefinition {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        bool Enable
        string ClientType
        string Host
        int Port
        bool UseSsl
        string Username
        string Password
        string Category
    }

    IndexerDefinition {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        bool Enable
        string IndexerType
        string Url
        string ApiKey
        string ApiPath
        bool EnableRss
        bool EnableSearch
        string Categories
        int DownloadClientId
    }

    NotificationDefinition {
        int Id PK
        string Name
        string Implementation
        string ConfigContract
        string Settings
        bool Enable
        bool OnTorrentAdded
        bool OnSeedingStarted
        bool OnSeedingStopped
        bool OnHealthIssue
    }

    SpeedSchedule {
        int Id PK
        string Name
        int Days
        string StartTime
        string EndTime
        int MaxUploadSpeed
        int MaxDownloadSpeed
        bool IsEnabled
        int Priority
    }

    Tag {
        int Id PK
        string Label
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

    CommandModel {
        int Id PK
        string Name
        string Body
        string Status
        datetime QueuedAt
        datetime StartedAt
        datetime EndedAt
        string Message
        int Priority
        string Trigger
    }

    Torrent ||--o{ TorrentFile : "has files"
    Torrent ||--o{ TrackerEntry : "announces to"
    Torrent }o--o{ Tag : "tagged with"
```

## Torrent Status Enum

| Value | Description |
|-------|-------------|
| `Stopped` | Not seeding |
| `Seeding` | Actively simulating upload |
| `Paused` | Temporarily paused |
| `Error` | Tracker/network failure |
| `Queued` | Waiting to start |
| `Downloading` | Simulating download |

## Torrent Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Queued: Upload .torrent / Magnet / *arr webhook / sync
    Queued --> Seeding: Start command
    Seeding --> Paused: Pause command
    Paused --> Seeding: Resume command
    Seeding --> Stopped: Stop command
    Stopped --> Seeding: Start command
    Seeding --> Error: Tracker/network failure
    Error --> Seeding: Retry / backoff
    Stopped --> [*]: Remove command
```

## In-Memory Models (Not Persisted)

These exist only at runtime, not in the database:

- **PeerConnection** (`NzbDrone.Core/Peers/PeerConnection.cs`) — active TCP peer connection with handshake state, message framing, encryption
- **DhtNode** (`NzbDrone.Core/Dht/`) — DHT routing table node
- **DhtPeerStore** — in-memory peer store with 30-minute TTL
- **TrackerServerPeerDatabase** — built-in tracker's peer registry (ConcurrentDictionary with TTL)

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

Used for pluggable components (TrackerProviders, ClientProfiles, ArrConnections, DownloadClients, Indexers, Notifications).

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
        +int Priority
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
    IProvider <|-- DownloadClient
    IProvider <|-- Indexer
    IProvider <|-- Notification
    ProviderDefinition <|-- TrackerProviderDefinition
    ProviderDefinition <|-- ClientProfileDefinition
    ProviderDefinition <|-- ArrConnectionDefinition
    ProviderDefinition <|-- DownloadClientDefinition
    ProviderDefinition <|-- IndexerDefinition
    ProviderDefinition <|-- NotificationDefinition
    ProviderFactory --> ProviderDefinition
    ProviderFactory --> ProviderRepository
    ProviderFactory --> IProvider
```

## Table Registration

Entity-to-table mapping defined in `NzbDrone.Core/Datastore/TableRegistration.cs`:

| Entity Class | Table Name |
|-------------|------------|
| Torrent | Torrents |
| TorrentFile | TorrentFiles |
| TrackerEntry | TrackerEntries |
| TrackerProviderDefinition | TrackerProviderDefinitions |
| ClientProfileDefinition | ClientProfileDefinitions |
| ArrConnectionDefinition | ArrConnectionDefinitions |
| DownloadClientDefinition | DownloadClientDefinitions |
| IndexerDefinition | IndexerDefinitions |
| NotificationDefinition | NotificationDefinitions |
| SpeedSchedule | SpeedSchedules |
| Tag | Tags |
| ConfigModel | Config |
| ScheduledTask | ScheduledTasks |
| CommandModel | Commands |
