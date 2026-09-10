export type SettingsGroupId =
  | "general-security"
  | "storage-queues"
  | "bittorrent-engine"
  | "network-bandwidth"
  | "integrations"
  | "advanced-ai";

export interface SettingsPageDefinition {
  id: string;
  groupId: SettingsGroupId;
  title: string;
  shortLabel: string;
  description: string;
  icon: string;
  badge?: string;
  keywords?: string[];
}

export interface SettingsGroupDefinition {
  id: SettingsGroupId;
  title: string;
  shortLabel: string;
  description: string;
  icon: string;
  badge?: string;
  pages: SettingsPageDefinition[];
}

export const SETTINGS_GROUPS: SettingsGroupDefinition[] = [
  {
    id: "general-security",
    title: "General & Security",
    shortLabel: "GENERAL",
    description: "Host server endpoints, theme appearance, security gates, and watch folder automation",
    icon: "⚙️",
    pages: [
      {
        id: "general",
        groupId: "general-security",
        title: "Host & Server",
        shortLabel: "Host & Server",
        description: "Configure web server port, bind address, URL base, and SSL/HTTPS certificate",
        icon: "🖥️",
        badge: "Core",
      },
      {
        id: "webui",
        groupId: "general-security",
        title: "Web UI & Themes",
        shortLabel: "Web UI & Themes",
        description: "Theme surface palettes, accent colors, and UI experience",
        icon: "🎨",
      },
      {
        id: "security",
        groupId: "general-security",
        title: "Security & API",
        shortLabel: "Security & API",
        description: "Authentication gate, CSRF protection, SSO identity providers, and API keys",
        icon: "🔒",
        badge: "Auth",
      },
      {
        id: "watch-folder",
        groupId: "general-security",
        title: "Watch Folder",
        shortLabel: "Watch Folder",
        description: "Automated torrent directory drop monitoring and auto-start",
        icon: "📁",
      },
    ],
  },
  {
    id: "storage-queues",
    title: "Storage & Queues",
    shortLabel: "STORAGE",
    description: "Storage paths, queue distribution, category routing, and lifecycle scripts",
    icon: "💾",
    pages: [
      {
        id: "categories",
        groupId: "storage-queues",
        title: "Categories",
        shortLabel: "Categories",
        description: "Organize torrents into categories with dedicated save paths and ratio limits",
        icon: "🏷️",
        badge: "Paths",
      },
      {
        id: "custom-scripts",
        groupId: "storage-queues",
        title: "Custom Scripts",
        shortLabel: "Custom Scripts",
        description: "Execute shell scripts and webhook triggers on torrent lifecycle events",
        icon: "📜",
      },
    ],
  },
  {
    id: "bittorrent-engine",
    title: "BitTorrent Engine",
    shortLabel: "ENGINE",
    description: "Core protocol parameters, extensions, swarm simulation, and embedded tracker server",
    icon: "⚡",
    badge: "Core",
    pages: [
      {
        id: "bittorrent",
        groupId: "bittorrent-engine",
        title: "BitTorrent Engine",
        shortLabel: "BitTorrent Engine",
        description: "Manage core protocol parameters, client emulation, and piece cache",
        icon: "🔄",
        badge: "Core",
      },
      {
        id: "protocols",
        groupId: "bittorrent-engine",
        title: "Protocols & BEP",
        shortLabel: "Protocols & BEP",
        description: "BEP extensions, transport layers, DHT, PEX, and encryption mode",
        icon: "📡",
      },
      {
        id: "peer-protocol",
        groupId: "bittorrent-engine",
        title: "Peer Protocol",
        shortLabel: "Peer Protocol",
        description: "Handshake timeouts, keepalive intervals, and connection policies",
        icon: "🤝",
      },
      {
        id: "seeding",
        groupId: "bittorrent-engine",
        title: "Seeding & Ratios",
        shortLabel: "Seeding & Ratios",
        description: "Target seed ratios, distribution algorithms, and swarm seeding policies",
        icon: "🌱",
      },
      {
        id: "simulation",
        groupId: "bittorrent-engine",
        title: "Simulation & Swarm",
        shortLabel: "Simulation & Swarm",
        description: "Simulated peer swarms, synthetic traffic curves, and agent behavior",
        icon: "🎲",
      },
      {
        id: "tracker-server",
        groupId: "bittorrent-engine",
        title: "Tracker Server",
        shortLabel: "Tracker Server",
        description: "Inbuilt HTTP/UDP BitTorrent tracker server endpoints and scrape support",
        icon: "🛰️",
      },
    ],
  },
  {
    id: "network-bandwidth",
    title: "Network & Bandwidth",
    shortLabel: "NETWORK",
    description: "Network interface binding, VPN killswitch, proxy tunnels, and schedule windows",
    icon: "🌐",
    pages: [
      {
        id: "network",
        groupId: "network-bandwidth",
        title: "Network & Ports",
        shortLabel: "Network & Ports",
        description: "Interface binding, listening ports, IPv6, VPN kill switch, and socket limits",
        icon: "🛡️",
        badge: "VPN",
      },
      {
        id: "proxy",
        groupId: "network-bandwidth",
        title: "Proxy & Tunnel",
        shortLabel: "Proxy & Tunnel",
        description: "Outbound SOCKS5/HTTP proxy tunnel, anonymous routing, and proxy killswitch",
        icon: "🔀",
      },
      {
        id: "scheduler",
        groupId: "network-bandwidth",
        title: "Schedule",
        shortLabel: "Schedule",
        description: "Weekly alternative speed limit windows and active throttling rules",
        icon: "🕒",
      },
    ],
  },
  {
    id: "integrations",
    title: "Integrations",
    shortLabel: "INTEGRATIONS",
    description: "Prowlarr indexers, Servarr application suites, download clients, and alerts",
    icon: "🔌",
    pages: [
      {
        id: "indexers",
        groupId: "integrations",
        title: "Indexers & RSS",
        shortLabel: "Indexers & RSS",
        description: "Torznab/Newznab indexer discovery and automated RSS download rules",
        icon: "🔍",
      },
      {
        id: "connections",
        groupId: "integrations",
        title: "Arr Connections",
        shortLabel: "Arr Connections",
        description: "Radarr, Sonarr, Readarr, Lidarr, Whisparr integration",
        icon: "📺",
        badge: "Servarr",
      },
      {
        id: "download-clients",
        groupId: "integrations",
        title: "Download Clients",
        shortLabel: "Download Clients",
        description: "Import state and control qBittorrent, Transmission, Deluge, rTorrent",
        icon: "📥",
      },
      {
        id: "notifications",
        groupId: "integrations",
        title: "Notifications",
        shortLabel: "Notifications",
        description: "Discord, Telegram, Gotify, Pushover, Email, and Webhook alerts",
        icon: "🔔",
      },
    ],
  },
  {
    id: "advanced-ai",
    title: "Advanced & Diagnostics",
    shortLabel: "ADVANCED & AI",
    description: "System logging verbosity, diagnostics, developer options, and subsystem flags",
    icon: "🧠",
    pages: [
      {
        id: "advanced",
        groupId: "advanced-ai",
        title: "Advanced & Logs",
        shortLabel: "Advanced & Logs",
        description: "System logging verbosity, diagnostics, developer flags, and database tools",
        icon: "📋",
      },
    ],
  },
];
