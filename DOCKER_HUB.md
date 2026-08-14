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

## Docker Compose

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

## Volumes

| Path | Purpose |
|------|---------|
| `/config` | Database, settings, logs |
| `/data` | Torrent files, watch folder |

## Environment

| Variable | Default | Description |
|----------|---------|-------------|
| `SEEDARR__APP_DATA` | `/config` | Config directory |
| `TZ` | `UTC` | Timezone |

## Ports

| Port | Purpose |
|------|---------|
| `9898` | Web UI + REST API |

## Features

- **Seeding Simulation** &mdash; configurable speeds, multiple distribution algorithms, time-of-day scheduling
- **Client Profiles** &mdash; impersonate qBittorrent, Deluge, Transmission with authentic peer IDs
- **Full Protocol Support** &mdash; HTTP/UDP trackers, DHT, PEX, MSE/PE encryption, uTP, LPD
- **Web Interface** &mdash; real-time dashboard, speed graphs, dark/light theme, responsive design
- **\*arr Integration** &mdash; connect to Sonarr/Radarr to auto-seed your download history
- **Built-in Tracker** &mdash; embedded HTTP + UDP tracker server
- **Notifications** &mdash; webhook, email, Discord alerts
- **Health Monitoring** &mdash; comprehensive system health checks
- **REST API** &mdash; full API at `/api/v1/` with Swagger docs at `/swagger`

## Tech Stack

- .NET 10 / ASP.NET Core
- React 18 + TypeScript
- SQLite (default) / PostgreSQL
- SignalR real-time updates
- Polly resilience (retry + circuit breaker)

## Tags

- `latest` &mdash; most recent stable release
- `x.y.z` &mdash; specific version

## Also Available

- **GHCR**: `ghcr.io/dmzoneill/seedarr:latest`
- **Source**: [github.com/dmzoneill/Seedarr](https://github.com/dmzoneill/Seedarr)

## License

Apache License 2.0
