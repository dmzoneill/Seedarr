import { useParams } from "react-router";
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
  advanced: "System logging verbosity, diagnostics, and developer flags",
};

function Settings() {
  const { section } = useParams<{ section?: string }>();
  const activeSection = section || "general";
  const title = sectionTitles[activeSection] || "Settings";
  const description =
    sectionDescriptions[activeSection] ||
    "Manage Seedarr application and operational parameters";

  return (
    <div className="content-area">
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.25rem",
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              {title}
            </h1>
            <span className="badge badge-primary">Settings</span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {description}
          </div>
        </div>
      </div>

      {(activeSection === "general" || activeSection === "watch-folder") && (
        <GeneralTab />
      )}
      {activeSection === "webui" && <WebUiSettingsTab />}
      {activeSection === "security" && <SecurityTab />}
      {activeSection === "notifications" && <NotificationsTab />}
      {activeSection === "categories" && <CategorySettingsTab />}
      {activeSection === "custom-scripts" && <CustomScriptsTab />}
      {activeSection === "seeding" && <SeedingTab />}
      {activeSection === "bittorrent" && <BitTorrentTab />}
      {activeSection === "network" && <NetworkSettingsTab />}
      {activeSection === "proxy" && <ProxySettingsTab />}
      {activeSection === "peer-protocol" && <PeerProtocolTab />}
      {activeSection === "protocols" && <ProtocolsTab />}
      {activeSection === "simulation" && <SimulationTab />}
      {activeSection === "tracker-server" && <TrackerServerTab />}
      {activeSection === "scheduler" && <SchedulerTab />}
      {activeSection === "indexers" && <IndexersTab />}
      {activeSection === "connections" && <ConnectionsTab />}
      {activeSection === "download-clients" && <DownloadClientsTab />}
      {activeSection === "advanced" && <AdvancedTab />}
    </div>
  );
}

export default Settings;
