# Seedarr

<p align="center">
  <img src="logo/seedarr-skull.svg" alt="Seedarr" width="200"/>
  <br/>
  <img src="logo/seedarr-text.svg" alt="Seedarr" width="200"/>
</p>

<p align="center">
  <strong>BitTorrent Seeding Simulator</strong> &mdash; the *arr-family approach to maintaining your ratio
</p>

<p align="center">
  <a href="https://www.seedarr.net"><img src="https://img.shields.io/badge/website-seedarr.net-c8a84e?logo=data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0id2hpdGUiIGQ9Ik0xMiAyQzYuNDggMiAyIDYuNDggMiAxMnM0LjQ4IDEwIDEwIDEwIDEwLTQuNDggMTAtMTBTMTcuNTIgMiAxMiAyek0xMSAxOS45M2MtMy45NS0uNDktNy03LjctNy03LjkzIDAtLjYyLjA4LTEuMjEuMjEtMS43OWwuMTcuMjYgNC44NCA0Ljg0djFjMCAxLjEuOSAyIDIgMnYxLjkzem02LjktMi41NGMtLjI2LS44MS0xLTEuMzktMS45LTEuMzloLTF2LTNjMC0uNTUtLjQ1LTEtMS0xaC02di0yaDJjLjU1IDAgMS0uNDUgMS0xVjdoMmMxLjEgMCAyLS45IDItMnYtLjQxYzIuOTMgMS4xOSA1IDQuMDYgNSA3LjQxIDAgMi4wOC0uOCAzLjk3LTIuMSA1LjM5eiIvPjwvc3ZnPg==" alt="Website"></a>
  <a href="https://github.com/dmzoneill/Seedarr/actions/workflows/main.yml"><img src="https://github.com/dmzoneill/Seedarr/workflows/CICD/badge.svg" alt="CI/CD"></a>
  <a href="https://github.com/dmzoneill/Seedarr/releases/latest"><img src="https://img.shields.io/github/v/release/dmzoneill/Seedarr?color=brightgreen&label=release" alt="Latest Release"></a>
  <a href="https://github.com/dmzoneill/Seedarr/blob/main/LICENSE"><img src="https://img.shields.io/github/license/dmzoneill/Seedarr?color=blue" alt="License"></a>
  <a href="https://hub.docker.com/r/feeditout/seedarr"><img src="https://img.shields.io/docker/pulls/feeditout/seedarr?color=blue&logo=docker" alt="Docker Pulls"></a>
  <a href="https://ghcr.io/dmzoneill/seedarr"><img src="https://img.shields.io/badge/ghcr.io-seedarr-blue?logo=github" alt="GHCR"></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet" alt=".NET 10">
  <img src="https://img.shields.io/badge/React-18-61DAFB?logo=react" alt="React 18">
  <img src="https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript" alt="TypeScript">
</p>

---

## What is Seedarr?

Seedarr is a **BitTorrent seeding simulator** built on the proven Sonarr/Radarr architecture. It simulates realistic seeding behavior across trackers without transferring actual data &mdash; maintaining your ratio, keeping torrents alive, and looking indistinguishable from a real BitTorrent client.

Think of it as Sonarr for seeding: a polished web UI, REST API, real-time updates via SignalR, and deep integration with the \*arr ecosystem.

### Why Seedarr?

| Problem                                    | Seedarr Solution                                 |
| ------------------------------------------ | ------------------------------------------------ |
| Ratio requirements on private trackers     | Simulates realistic upload traffic patterns      |
| Need to keep rare torrents alive           | Announces to trackers and responds to peers      |
| Running a real client wastes bandwidth     | Zero actual data transfer                        |
| Manual ratio management is tedious         | Automated scheduling, distribution, and profiles |
| Want integration with Sonarr/Radarr/Lidarr | Native \*arr API integration for auto-seeding    |

---

## Features

<table>
<tr>
<td width="50%" valign="top">

### Core Simulation

- Load `.torrent` files or magnet links
- Configurable upload/download speed simulation
- Multiple speed distribution algorithms (Pareto, Power Law, Log-Normal, Equal)
- Time-of-day speed scheduling with day-of-week support
- Client behavior profiles (qBittorrent, Deluge, Transmission, uTorrent, BiglyBT)
- Traffic pattern simulation (burst/idle states, congestion modeling)
- Per-torrent speed limits, priority weighting, super-seeding boost
- Global and per-torrent seed ratio limits
- Force start and force complete support

### Protocol Support

- HTTP & UDP tracker announce/scrape (BEP 3, BEP 15)
- Multi-tracker failover with tier support (BEP 12)
- TCP peer connections with full handshake
- MSE/PE stream encryption (RC4 + DH key exchange)
- DHT distributed hash table (BEP 5)
- Peer Exchange (BEP 11)
- Metadata Exchange (BEP 9)
- Fast Extension (BEP 6)
- Local Peer Discovery (BEP 14)
- uTP transport (BEP 29)

</td>
<td width="50%" valign="top">

### Web Interface

- Sonarr-style UI with chunky card layouts
- Real-time dashboard with aggregate speed display
- Torrent management (table & grid views)
- Detailed torrent panel (Status, Details, Files, Peers, Trackers, Options, Monitoring, Log)
- Context menus: update tracker, force recheck, queue position, remove with/without data
- Provider card tiles for connections and download clients
- Drag-and-drop torrent upload
- Dark/light theme with system detection and custom scrollbars
- Responsive design (desktop, tablet, mobile)
- SignalR real-time updates

### Infrastructure

- Built-in HTTP + UDP tracker server
- Download client integration (qBittorrent, Transmission, Deluge)
- Sonarr/Radarr/Lidarr integration (auto-seed downloads)
- System pages: Status, Tasks, Backup, Updates, Events, Log Files
- Swagger/OpenAPI documentation
- Health monitoring system
- Notification system (webhook, email, Discord)
- UPnP port mapping
- Proxy support (HTTP/SOCKS)
- Tag-based organization
- Automated backup/restore
- SQLite (default) or PostgreSQL

</td>
</tr>
</table>

---

## Quick Start

### Docker (Recommended)

```bash
docker pull feeditout/seedarr:latest
docker run -d \
  --name seedarr \
  -p 9898:9898 \
  -v seedarr-config:/config \
  -v seedarr-data:/data \
  --restart unless-stopped \
  feeditout/seedarr:latest
```

Then open **<http://localhost:9898>**

### Docker Compose / Podman Compose

```yaml
services:
  seedarr:
    image: feeditout/seedarr:latest
    container_name: seedarr
    ports:
      - "9898:9898"
    volumes:
      - seedarr-config:/config
      - seedarr-data:/data
    restart: unless-stopped
    environment:
      - TZ=UTC
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9898/api/v1/system/status"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 30s

volumes:
  seedarr-config:
  seedarr-data:
```

```bash
docker compose up -d
# or
podman-compose up -d
```

### GHCR Alternative

```bash
docker pull ghcr.io/dmzoneill/seedarr:latest
```

### From Source

```bash
git clone https://github.com/dmzoneill/Seedarr.git
cd Seedarr

# Backend
dotnet run --project src/NzbDrone.Console/Seedarr.Console.csproj

# Frontend (dev server)
cd src/Seedarr.Frontend && npm install && npm start
```

---

## Settings

Seedarr provides 14 settings tabs with 120+ configurable properties:

| Tab                  | Key Settings                                                                         |
| -------------------- | ------------------------------------------------------------------------------------ |
| **General**          | Auto-start, theme, color scheme, log level                                           |
| **Seeding**          | Max upload/download speed, global ratio limit, speed variation, activity probability |
| **BitTorrent**       | Peer ID prefix, client key, user agent, protocol features                            |
| **Network**          | External IP, UPnP, proxy (HTTP/SOCKS), DNS                                           |
| **Peer Protocol**    | Max connections, request pipeline, idle timeout, encryption                          |
| **Protocols**        | DHT, PEX, metadata exchange, fast extension, LPD, uTP                                |
| **Simulation**       | Swarm analysis, traffic patterns, seeding profiles                                   |
| **Tracker Server**   | Built-in HTTP/UDP tracker, scrape, announce intervals, rate limiting                 |
| **Scheduler**        | Time-of-day speed schedules, alternative speeds, day-of-week                         |
| **Advanced**         | Download threshold, stopped percentages, force settings                              |
| **Connections**      | Sonarr/Radarr/Lidarr integration with sync and auto-add                              |
| **Download Clients** | qBittorrent, Transmission, Deluge with test and status                               |
| **Notifications**    | Webhook, email, Discord with event triggers                                          |
| **Web UI**           | Refresh interval, items per page, date/time format                                   |

---

## Configuration

### Volumes

| Path      | Purpose                              |
| --------- | ------------------------------------ |
| `/config` | Application database, settings, logs |
| `/data`   | Torrent files and watch folder       |

### Environment Variables

| Variable            | Default   | Description           |
| ------------------- | --------- | --------------------- |
| `SEEDARR__APP_DATA` | `/config` | Config/data directory |
| `TZ`                | `UTC`     | Container timezone    |

### Ports

| Port   | Protocol | Purpose      |
| ------ | -------- | ------------ |
| `9898` | TCP      | Web UI + API |

---

## API

Seedarr exposes a full REST API at `/api/v1/`. Interactive documentation is available at `/swagger` when the application is running.

### Key Endpoints

```text
GET    /api/v1/system/status          # System info
GET    /api/v1/system/task            # Scheduled tasks
GET    /api/v1/system/command         # Command queue
GET    /api/v1/torrent                # List torrents
POST   /api/v1/torrent                # Add torrent (.torrent or magnet)
GET    /api/v1/torrent/{id}           # Torrent details
PUT    /api/v1/torrent/{id}           # Update torrent
DELETE /api/v1/torrent/{id}           # Remove torrent
GET    /api/v1/torrent/{id}/peer      # List peers
POST   /api/v1/torrent/{id}/announce  # Update tracker
POST   /api/v1/torrent/{id}/recheck   # Force recheck
GET    /api/v1/config                 # All settings
PUT    /api/v1/config                 # Save settings
GET    /api/v1/backup                 # List backups
POST   /api/v1/backup                 # Create backup
GET    /api/v1/diskspace              # Disk space info
GET    /api/v1/update                 # Check for updates
GET    /api/v1/logfile                # List log files
GET    /api/v1/speedschedule          # Speed schedules
GET    /api/v1/health                 # Health checks
GET    /api/v1/tag                    # List tags
```

### SignalR

Real-time updates via SignalR at `/signalr/messages`:

- `TorrentUpdated` / `TorrentAdded` / `TorrentDeleted`
- `SeedingStatusChanged`
- `TrackerStatusChanged`
- `HealthCheckCompleted`

---

## Architecture

```text
Seedarr.Console          Entry point (Kestrel host)
  +-- Seedarr.Host       ASP.NET middleware, Swagger, auth
       +-- Seedarr.Api.V1    REST controllers + SignalR
       +-- Seedarr.Http      REST framework, middleware
       +-- Seedarr.SignalR    Real-time messaging hub
       +-- Seedarr.Core      Domain logic
            +-- Torrents/          Torrent management
            +-- Seeding/           Simulation engine + distribution
            +-- Trackers/          HTTP/UDP tracker clients
            +-- Peers/             Peer connections + encryption
            +-- Simulation/        Client profiles + traffic
            +-- Dht/               Distributed hash table
            +-- TrackerServer/     Built-in tracker
            +-- ArrIntegration/    Sonarr/Radarr/Lidarr sync
            +-- DownloadClients/   qBittorrent/Transmission/Deluge
            +-- DiskSpace/         Disk space monitoring
            +-- Backup/            Backup/restore system
            +-- Notifications/     Webhook, email, Discord
            +-- HealthCheck/       System monitoring
```

### Tech Stack

| Layer               | Technology                        |
| ------------------- | --------------------------------- |
| **Runtime**         | .NET 10 / ASP.NET Core            |
| **Frontend**        | React 18, TypeScript 5, webpack 5 |
| **Real-time**       | ASP.NET SignalR                   |
| **Database**        | SQLite (default) / PostgreSQL     |
| **ORM**             | Dapper + FluentMigrator           |
| **DI**              | DryIoc                            |
| **Validation**      | FluentValidation                  |
| **Resilience**      | Polly 8 (retry + circuit breaker) |
| **Logging**         | NLog                              |
| **Encryption**      | BouncyCastle (RC4/DH for MSE/PE)  |
| **Torrent Parsing** | BencodeNET                        |
| **Container**       | Podman / Docker                   |

---

## Client Profiles

Seedarr can impersonate multiple BitTorrent clients, generating authentic peer IDs, user agents, and protocol behavior:

| Client       | Peer ID Prefix | Version |
| ------------ | -------------- | ------- |
| qBittorrent  | `-qB4420-`     | 4.4.2   |
| Deluge       | `-DE2030-`     | 2.0.3   |
| Transmission | `-TR3000-`     | 3.00    |
| uTorrent     | `-UT3550-`     | 3.5.5   |
| BiglyBT      | `-BG2700-`     | 2.7.0.0 |

---

## Speed Distribution

Choose how upload bandwidth is distributed across torrents:

| Algorithm      | Behavior                                            |
| -------------- | --------------------------------------------------- |
| **Pareto**     | 80/20 rule &mdash; most bandwidth to a few torrents |
| **Power Law**  | Heavy-tailed &mdash; gradual falloff                |
| **Log-Normal** | Bell curve with right skew                          |
| **Equal**      | Even split across all active torrents               |

---

## Integration with \*arr Apps

Seedarr can connect to your existing Sonarr, Radarr, and Lidarr instances to automatically seed torrents from your download history:

1. Go to **Settings > Connections**
2. Click the **+** card to add a new connection
3. Select your \*arr type, enter URL and API key
4. Enable sync and auto-add
5. Seedarr periodically syncs and begins simulating seeds

---

## System Pages

| Page          | Description                                             |
| ------------- | ------------------------------------------------------- |
| **Status**    | Health checks, disk space with progress bars, app info  |
| **Tasks**     | Scheduled tasks with last/next execution, command queue |
| **Backup**    | Create/restore/download database backups                |
| **Updates**   | Version changelog with installed version badge          |
| **Events**    | Structured event log with severity-colored icons        |
| **Log Files** | Log file listing with download links                    |

---

## Development

### Prerequisites

- .NET 10 SDK
- Node.js 20+
- npm

### Build

```bash
# Full solution
dotnet build src/Seedarr.sln

# Run tests
dotnet test src/Seedarr.sln

# Frontend dev server (hot reload)
cd src/Seedarr.Frontend && npm install && npm start
```

### Makefile Targets

```bash
make setup              # Restore .NET + npm dependencies
make test-setup         # Build solution
make test               # Run all tests
make integration        # Integration tests (podman-compose stack)
make build              # Build release
make publish            # Publish release artifacts
make frontend           # Build frontend production bundle
make clean              # Clean build artifacts
```

---

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## License

Distributed under the **Apache License 2.0**. See [LICENSE](LICENSE) for details.

---

<p align="center">
  <sub>Built with the <a href="https://github.com/Sonarr/Sonarr">Sonarr</a>/<a href="https://github.com/Radarr/Radarr">Radarr</a> architecture pattern</sub>
  <br>
  <sub>Part of the *arr family of applications</sub>
  <br>
  <sub><a href="https://www.seedarr.net">www.seedarr.net</a></sub>
</p>
