import { useParams } from "react-router";
import { usePermissions } from "../hooks/usePermissions";
import { GeneralTab } from "./settings/GeneralTab";
import { SeedingTab } from "./settings/SeedingTab";
import { BitTorrentTab } from "./settings/BitTorrentTab";
import { NetworkSettingsTab } from "./settings/NetworkSettingsTab";
import { ProxySettingsTab } from "./settings/ProxySettingsTab";
import { PeerProtocolTab } from "./settings/PeerProtocolTab";
import { ProtocolsTab } from "./settings/ProtocolsTab";
import { SimulationTab } from "./settings/SimulationTab";
import { TrackerServerTab } from "./settings/TrackerServerTab";
import { SchedulerTab } from "./settings/SchedulerTab";
import { AdvancedTab } from "./settings/AdvancedTab";
import { IndexersTab } from "./settings/IndexersTab";
import { ConnectionsTab } from "./settings/ConnectionsTab";
import { DownloadClientsTab } from "./settings/DownloadClientsTab";
import { NotificationsTab } from "./settings/NotificationsTab";
import { CategorySettingsTab } from "./settings/CategorySettingsTab";
import { CustomScriptsTab } from "./settings/CustomScriptsTab";
import { WebUiSettingsTab } from "./settings/WebUiSettingsTab";
import { SecurityTab } from "./settings/SecurityTab";
import { AiTab } from "./settings/AiTab";

const sectionTitles: Record<string, string> = {
  general: "General",
  "watch-folder": "Watch Folder",
  webui: "Web UI",
  security: "Security",
  notifications: "Notifications",
  categories: "Categories",
  "custom-scripts": "Custom Scripts",
  seeding: "Seeding",
  bittorrent: "BitTorrent",
  network: "Network",
  proxy: "Proxy",
  "peer-protocol": "Peer Protocol",
  protocols: "Protocols",
  simulation: "Simulation",
  "tracker-server": "Tracker Server",
  scheduler: "Scheduler",
  indexers: "Indexers",
  connections: "Connections",
  "download-clients": "Download Clients",
  ai: "AI & Copilot",
  advanced: "Advanced",
};

const sectionDescriptions: Record<string, string> = {
  general:
    "Configure application behavior, host endpoints, and watch folder automation",
  "watch-folder":
    "Automated torrent directory drop monitoring, scanning intervals, and auto-start",
  webui:
    "Configure web user interface access, port bindings, and session security",
  security:
    "Configure authentication, CSRF protection, identity providers (SSO), and REST API keys",
  notifications:
    "Set up alerting and webhooks for download, swarm, and tracker events",
  categories:
    "Organize torrents into categories with dedicated save paths, ratio goals, and speed limits",
  "custom-scripts":
    "Execute custom shell scripts or executables on torrent lifecycle events and Transmission hooks",
  seeding:
    "Fine-tune upload/download ratios, seeding limits, and distribution engines",
  bittorrent:
    "Manage core BitTorrent protocol features, client identities, and tracker timing",
  network:
    "Network interface binding, listening ports, IPv6, VPN kill switch, and socket limits",
  proxy:
    "Outbound SOCKS5 / HTTP proxy tunnel, strict enforcement kill switch, and anonymous privacy routing",
  "peer-protocol":
    "Peer handshake timeouts, keepalive intervals, and connection behavior",
  protocols:
    "BEP extensions, transport layers, PEX peer exchange, multi-tracker, and DHT",
  simulation:
    "Simulation engine behavior, traffic patterns, and swarm intelligence",
  "tracker-server":
    "Inbuilt HTTP/UDP BitTorrent tracker server configuration and endpoints",
  scheduler: "Alternative speed limit scheduling windows and active day rules",
  indexers: "Prowlarr and external indexer synchronization and discovery",
  connections:
    "Arr suite integration (Radarr, Sonarr, Readarr, Lidarr, Whisparr)",
  "download-clients":
    "Manage download agents (qBittorrent, Transmission, Deluge, rTorrent)",
  ai: "Configure AI providers (Ollama, Gemini, ONNX), Copilot drawer, and swarm diagnostics",
  advanced: "System logging verbosity, diagnostics, and developer flags",
};

const TAB_COMPONENTS: Record<string, React.ComponentType> = {
  general: GeneralTab,
  webui: WebUiSettingsTab,
  security: SecurityTab,
  notifications: NotificationsTab,
  categories: CategorySettingsTab,
  "custom-scripts": CustomScriptsTab,
  seeding: SeedingTab,
  bittorrent: BitTorrentTab,
  network: NetworkSettingsTab,
  proxy: ProxySettingsTab,
  "peer-protocol": PeerProtocolTab,
  protocols: ProtocolsTab,
  simulation: SimulationTab,
  "tracker-server": TrackerServerTab,
  scheduler: SchedulerTab,
  indexers: IndexersTab,
  connections: ConnectionsTab,
  "download-clients": DownloadClientsTab,
  ai: AiTab,
  advanced: AdvancedTab,
};

function Settings() {
  const { canSaveSettings } = usePermissions();
  const { section } = useParams<{ section?: string }>();
  const activeSection = section || "general";
  const title = sectionTitles[activeSection] || "Settings";
  const description =
    sectionDescriptions[activeSection] ||
    "Manage Seedarr application and operational parameters";

  const tabKey = activeSection === "watch-folder" ? "general" : activeSection;
  const ActiveComponent = TAB_COMPONENTS[tabKey];

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {!canSaveSettings && (
        <div
          role="alert"
          style={{
            padding: "0.75rem 1rem",
            marginBottom: "1rem",
            backgroundColor: "rgba(239, 68, 68, 0.1)",
            border: "1px solid rgba(239, 68, 68, 0.3)",
            borderRadius: "6px",
            color: "#fca5a5",
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            fontSize: "0.875rem",
          }}
        >
          <span>🔒</span>
          <span>You have ReadOnly permissions. Settings cannot be modified.</span>
        </div>
      )}
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>⚙️</span> {title}
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {description}
          </p>
        </div>
      </div>

      {ActiveComponent ? <ActiveComponent /> : null}
    </div>
  );
}

export default Settings;
