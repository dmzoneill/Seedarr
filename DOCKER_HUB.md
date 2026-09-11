<p align="center">
  <a href="https://www.seedarr.net" target="_blank" rel="noopener noreferrer">
    <img src="https://raw.githubusercontent.com/dmzoneill/Seedarr/main/logo/seedarr-skull.svg" alt="Seedarr Skull" width="140"/>
    <br/>
    <img src="https://raw.githubusercontent.com/dmzoneill/Seedarr/main/logo/seedarr-text.svg" alt="Seedarr" width="220"/>
  </a>
</p>

<p align="center">
  <strong>BitTorrent Seeding Simulator</strong> &mdash; the *arr-family approach to maintaining your ratio.
</p>

<p align="center">
  <a href="https://hub.docker.com/r/feeditout/seedarr"><img src="https://img.shields.io/docker/pulls/feeditout/seedarr?color=blue&logo=docker&style=flat-square" alt="Docker Pulls"></a>
  <a href="https://hub.docker.com/r/feeditout/seedarr"><img src="https://img.shields.io/docker/image-size/feeditout/seedarr/latest?color=blue&style=flat-square" alt="Docker Image Size"></a>
  <img src="https://img.shields.io/badge/arch-amd64%20%7C%20arm64-blue?style=flat-square" alt="Architectures">
  <a href="https://github.com/dmzoneill/Seedarr/releases/latest"><img src="https://img.shields.io/github/v/release/dmzoneill/Seedarr?color=brightgreen&label=release&style=flat-square" alt="Latest Release"></a>
  <a href="https://github.com/dmzoneill/Seedarr/actions/workflows/main.yml"><img src="https://github.com/dmzoneill/Seedarr/workflows/CICD/badge.svg?style=flat-square" alt="CI/CD Status"></a>
  <a href="https://github.com/dmzoneill/Seedarr/blob/main/LICENSE"><img src="https://img.shields.io/github/license/dmzoneill/Seedarr?color=blue&style=flat-square" alt="License"></a>
  <a href="https://www.seedarr.net"><img src="https://img.shields.io/badge/website-seedarr.net-c8a84e?style=flat-square" alt="Website"></a>
</p>

---

<p align="center">
  <img src="https://raw.githubusercontent.com/dmzoneill/Seedarr/main/logo/ss.png" alt="Seedarr Web UI Screenshot" width="100%"/>
</p>

---

## 🚀 What's Changed in this Version

{{CHANGELOG}}

> 📖 **[View Complete Version Changelog on GitHub](https://github.com/dmzoneill/Seedarr/blob/main/CHANGELOG.md)**

---

## 💡 What is Seedarr?

**Seedarr** is a **BitTorrent seeding simulator** built on the proven Sonarr/Radarr architecture. It simulates realistic seeding behavior across trackers without transferring actual data &mdash; maintaining your ratio, keeping torrents alive, and looking indistinguishable from a real BitTorrent client.

### Key Highlights
- **⚡ Seeding Simulation**: Realistic traffic patterns, burst/idle intervals, Pareto (80/20) and Power Law speed distribution algorithms.
- **🎭 Client Emulation**: Authentically impersonates qBittorrent, Deluge, Transmission, uTorrent, and BiglyBT with matching peer IDs, extension handshakes, and scrape signatures.
- **🔌 Servarr (*arr) Sync**: Integrates with Sonarr, Radarr, and Lidarr to automatically import and seed downloaded torrent history.
- **🌐 Full BitTorrent Protocol Stack**: HTTP/UDP tracker announce & scrape (BEP 3, BEP 15, BEP 12), Peer Wire protocol, MSE/PE Diffie-Hellman RC4 encryption, DHT (BEP 5), PEX (BEP 11), and uTP transport.
- **📡 Built-in Tracker Server**: Embedded HTTP & UDP tracker server with peer database and rate-limiting.
- **🔔 Multi-Channel Notifications**: Discord, Telegram, Gotify, Pushover, Apprise, Email/SMTP, and Generic Webhooks.
- **📈 Real-Time Dashboards**: Real-time speed charts, peer geographic maps, tracker health telemetry, and status cards.

---

## ⚡ Quick Start

### Single Container Run (`docker run`)

```bash
docker run -d \
  --name seedarr \
  -p 9898:9898 \
  -v seedarr-config:/config \
  -v seedarr-data:/data \
  --restart unless-stopped \
  feeditout/seedarr:latest
```

Open **http://localhost:9898** in your browser.

---

### Docker Compose (`compose.yaml` / `docker-compose.yml`)

```yaml
services:
  seedarr:
    image: feeditout/seedarr:latest
    container_name: seedarr
    restart: unless-stopped
    ports:
      - "9898:9898"       # Web UI & REST API
    volumes:
      - /opt/seedarr/config:/config
      - /opt/seedarr/data:/data
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9898/api/v1/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 15s
```

---

## 📁 Storage Volumes & Port Parameters

| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| **`-p 9898:9898`** | Port | `9898` | Web UI, REST API v1, SignalR push notifications, and OpenAPI/Swagger |
| **`-v /config`** | Volume | `/config` | Application database (`seedarr.db`), configuration settings (`config.xml`), logs, and certificates |
| **`-v /data`** | Volume | `/data` | Watched .torrent files directory, backup archives, and staged torrent metadata |
| **`-e PUID / PGID`** | Env | `1000:1000` | User and Group ID for internal filesystem permissions |
| **`-e TZ`** | Env | `UTC` | Timezone for scheduler matrix and automated backup cron jobs |

---

## 🌐 Reverse Proxy Configuration

### Nginx
```nginx
server {
    listen 80;
    server_name seedarr.yourdomain.com;

    location / {\n        proxy_pass http://127.0.0.1:9898;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # WebSocket / SignalR support
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 86400;
    }
}
```

### Traefik (Docker Labels)
```yaml
labels:
  - "traefik.enable=true"
  - "traefik.http.routers.seedarr.rule=Host(`seedarr.yourdomain.com`)"
  - "traefik.http.routers.seedarr.entrypoints=websecure"
  - "traefik.http.routers.seedarr.tls.certresolver=letsencrypt"
  - "traefik.http.services.seedarr.loadbalancer.server.port=9898"
```

---

## 🛠️ Supported Architectures & Tags

Multi-architecture builds are automatically published to both Docker Hub and GitHub Packages Container Registry (GHCR):

| Architecture | Tag Example | Status |
| :--- | :--- | :--- |
| **`linux/amd64`** | `feeditout/seedarr:latest`, `feeditout/seedarr:1.3.38` | ✅ Verified Stable |
| **`linux/arm64`** | `feeditout/seedarr:latest`, `feeditout/seedarr:1.3.38` | ✅ Verified Stable |

---

## 🔗 Links & Resources

- **Official Website**: [www.seedarr.net](https://www.seedarr.net)
- **Source Code**: [github.com/dmzoneill/Seedarr](https://github.com/dmzoneill/Seedarr)
- **Changelog**: [CHANGELOG.md](https://github.com/dmzoneill/Seedarr/blob/main/CHANGELOG.md)
- **GitHub Container Registry**: [ghcr.io/dmzoneill/seedarr](https://ghcr.io/dmzoneill/seedarr)
- **License**: [Apache License 2.0](https://github.com/dmzoneill/Seedarr/blob/main/LICENSE)
