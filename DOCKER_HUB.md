# Seedarr

**BitTorrent Seeding Simulator** &mdash; the \*arr-family approach to maintaining your ratio.

[![CI/CD](https://github.com/dmzoneill/Seedarr/workflows/CICD/badge.svg)](https://github.com/dmzoneill/Seedarr/actions)
[![GitHub Release](https://img.shields.io/github/v/release/dmzoneill/Seedarr?color=brightgreen)](https://github.com/dmzoneill/Seedarr/releases/latest)
[![License](https://img.shields.io/github/license/dmzoneill/Seedarr?color=blue)](https://github.com/dmzoneill/Seedarr/blob/main/LICENSE)

## What is Seedarr?

Seedarr simulates realistic BitTorrent seeding behavior &mdash; announcing to trackers, responding to peers, and reporting upload stats &mdash; without transferring actual data. Built on the Sonarr/Radarr architecture with a polished web UI, REST API, and real-time SignalR updates.

## Quick Start

```bash
docker run -d \
  --name seedarr \
  -p 9898:9898 \
  -v seedarr-config:/config \
  -v seedarr-data:/data \
  --restart unless-stopped \
  feeditout/seedarr:latest
```

Open **<http://localhost:9898>**

## Docker Compose / Podman Compose

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

## Volumes

| Path      | Purpose                     |
| --------- | --------------------------- |
| `/config` | Database, settings, logs    |
| `/data`   | Torrent files, watch folder |

## Environment

| Variable            | Default   | Description      |
| ------------------- | --------- | ---------------- |
| `SEEDARR__APP_DATA` | `/config` | Config directory |
| `TZ`                | `UTC`     | Timezone         |

## Ports

| Port   | Purpose           |
| ------ | ----------------- |
| `9898` | Web UI + REST API |

## Features

- **Seeding Simulation** &mdash; configurable upload/download speeds, 4 distribution algorithms (Pareto, Power Law, Log-Normal, Equal), time-of-day scheduling with day-of-week support
- **Client Profiles** &mdash; impersonate qBittorrent, Deluge, Transmission, uTorrent, BiglyBT with authentic peer IDs and protocol behavior
- **Full Protocol Support** &mdash; HTTP/UDP trackers, multi-tracker failover, DHT, PEX, MSE/PE encryption, uTP, LPD, metadata exchange, fast extension
- **Web Interface** &mdash; Sonarr-style UI with chunky card layouts, real-time aggregate speed display, table and grid views, drag-and-drop upload, dark/light theme with custom scrollbars, responsive design
- **\*arr Integration** &mdash; connect Sonarr, Radarr, and Lidarr to auto-seed your download history via provider card tiles
- **Download Clients** &mdash; integrate with qBittorrent, Transmission, and Deluge
- **Built-in Tracker** &mdash; embedded HTTP + UDP tracker server with rate limiting and peer database
- **Notifications** &mdash; webhook, email, Discord alerts with configurable event triggers
- **Health Monitoring** &mdash; comprehensive system health checks with status dashboard
- **System Pages** &mdash; Status, Tasks, Backup, Updates, Events, Log Files
- **REST API** &mdash; full API at `/api/v1/` with Swagger docs at `/swagger`
- **Auto-Start** &mdash; seeding engine starts automatically on container boot, with force start support

## Settings

14 settings tabs with 120+ configurable properties covering general, seeding, BitTorrent protocol, network, peer protocol, protocol extensions, simulation, tracker server, scheduler, advanced, connections, download clients, notifications, and web UI.

## Tech Stack

- .NET 10 / ASP.NET Core
- React 18 + TypeScript 5
- SQLite (default) / PostgreSQL
- SignalR real-time updates
- Polly 8 resilience (retry + circuit breaker)
- BouncyCastle (MSE/PE encryption)
- BencodeNET (torrent parsing)

## Tags

- `latest` &mdash; most recent stable release
- `x.y.z` &mdash; specific version

## Also Available

- **GHCR**: `ghcr.io/dmzoneill/seedarr:latest`
- **Website**: [www.seedarr.net](https://www.seedarr.net)
- **Source**: [github.com/dmzoneill/Seedarr](https://github.com/dmzoneill/Seedarr)

## License

Apache License 2.0
