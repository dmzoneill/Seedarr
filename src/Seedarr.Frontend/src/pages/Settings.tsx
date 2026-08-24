import { useParams } from "react-router";
import { GeneralTab } from "./settings/GeneralTab";
import { SeedingTab } from "./settings/SeedingTab";
import { BitTorrentTab } from "./settings/BitTorrentTab";
import { NetworkTab } from "./settings/NetworkTab";
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
import { WebUITab } from "./settings/WebUITab";

const sectionTitles: Record<string, string> = {
  general: "General",
  webui: "Web UI",
  notifications: "Notifications",
  seeding: "Seeding",
  bittorrent: "BitTorrent",
  network: "Network",
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
  webui:
    "Configure web user interface access, port bindings, and session security",
  notifications:
    "Set up alerting and webhooks for download, swarm, and tracker events",
  seeding:
    "Fine-tune upload/download ratios, seeding limits, and distribution engines",
  bittorrent:
    "Manage core BitTorrent protocol features, client identities, and tracker timing",
  network:
    "Network interface binding, listening ports, global rate throttles, and proxy routing",
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

      {activeSection === "general" && <GeneralTab />}
      {activeSection === "webui" && <WebUITab />}
      {activeSection === "notifications" && <NotificationsTab />}
      {activeSection === "seeding" && <SeedingTab />}
      {activeSection === "bittorrent" && <BitTorrentTab />}
      {activeSection === "network" && <NetworkTab />}
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
