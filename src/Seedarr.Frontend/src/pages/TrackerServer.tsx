import { Link } from "react-router";
import {
  useTrackerServerStats,
  useTrackerServerTorrents,
  useTrackerServerConfig,
  useSaveTrackerServerConfig,
  useNetworkStatus,
} from "../api/hooks";
import { formatBytes, formatDate, formatUptime } from "../utils/formatters";
import { useToast } from "../context/ToastContext";
import type { TrackerServerConfig } from "../api/types";

function TrackerServer() {
  const { data: stats, isLoading: statsLoading } = useTrackerServerStats();
  const { data: torrents, isLoading: torrentsLoading } =
    useTrackerServerTorrents();
  const { data: config } = useTrackerServerConfig();
  const { data: network } = useNetworkStatus();
  const saveConfig = useSaveTrackerServerConfig();
  const { showToast } = useToast();

  const host = network?.externalIp || window.location.hostname || "localhost";
  const httpAnnounceUrl = config?.trackerHttpPort
    ? `http://${host}:${config.trackerHttpPort}/announce`
    : null;
  const udpAnnounceUrl = config?.trackerUdpPort
    ? `udp://${host}:${config.trackerUdpPort}/announce`
    : null;

  function handleToggleEnabled() {
    if (!config) return;
    const updated: TrackerServerConfig = {
      ...config,
      trackerServerEnabled: !config.trackerServerEnabled,
    };
    saveConfig.mutate(updated);
  }

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    showToast(`Copied ${label} announce URL to clipboard`, "success");
  };

  return (
    <div>
      <div className="page-heading-row">
        <h1 className="page-heading">Tracker Server</h1>
        {config && (
          <button
            className={`btn ${config.trackerServerEnabled ? "btn-danger" : "btn-success"}`}
            onClick={handleToggleEnabled}
            disabled={saveConfig.isPending}
          >
            {config.trackerServerEnabled ? "Disable Tracker" : "Enable Tracker"}
          </button>
        )}
      </div>

      <div className="tracker-stats-grid">
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Torrents</div>
          <div className="tracker-stat-value">
            {statsLoading ? "-" : (stats?.totalTorrents ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Internal (Seedarr)</div>
          <div className="tracker-stat-value">
            {statsLoading
              ? "-"
              : (stats?.internalTorrents ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Peers</div>
          <div className="tracker-stat-value">
            {statsLoading ? "-" : (stats?.totalPeers ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Announces</div>
          <div className="tracker-stat-value">
            {statsLoading ? "-" : (stats?.totalAnnounces ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Uptime</div>
          <div className="tracker-stat-value">
            {statsLoading ? "-" : formatUptime(stats?.uptime ?? 0)}
          </div>
        </div>
      </div>

      {/* Announce Endpoints Quick-Copy Banner */}
      {config?.trackerServerEnabled && (
        <div
          className="card"
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
            gap: "1rem",
            marginBottom: "1rem",
          }}
        >
          {config.trackerHttpEnabled && httpAnnounceUrl && (
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                gap: "0.5rem",
                padding: "0.75rem",
                backgroundColor: "var(--bg-primary)",
                borderRadius: "4px",
                border: "1px solid var(--border-light)",
              }}
            >
              <div>
                <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", fontWeight: 600 }}>
                  HTTP ANNOUNCE URL
                </div>
                <code style={{ fontSize: "0.85rem", color: "var(--accent)" }}>
                  {httpAnnounceUrl}
                </code>
              </div>
              <button
                className="btn btn-small btn-outline"
                onClick={() => copyToClipboard(httpAnnounceUrl, "HTTP")}
                title="Copy HTTP Announce URL"
              >
                📋 Copy
              </button>
            </div>
          )}

          {config.trackerUdpEnabled && udpAnnounceUrl && (
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                gap: "0.5rem",
                padding: "0.75rem",
                backgroundColor: "var(--bg-primary)",
                borderRadius: "4px",
                border: "1px solid var(--border-light)",
              }}
            >
              <div>
                <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", fontWeight: 600 }}>
                  UDP ANNOUNCE URL
                </div>
                <code style={{ fontSize: "0.85rem", color: "var(--accent)" }}>
                  {udpAnnounceUrl}
                </code>
              </div>
              <button
                className="btn btn-small btn-outline"
                onClick={() => copyToClipboard(udpAnnounceUrl, "UDP")}
                title="Copy UDP Announce URL"
              >
                📋 Copy
              </button>
            </div>
          )}
        </div>
      )}

      {config && (
        <div className="card tracker-info-card">
          <div className="status-row">
            <span className="status-label">Status</span>
            <span className="status-value">
              <span
                className={`badge badge-${config.trackerServerEnabled ? "seeding" : "stopped"}`}
              >
                {config.trackerServerEnabled ? "Enabled" : "Disabled"}
              </span>
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">HTTP Port</span>
            <span className="status-value">
              {config.trackerHttpEnabled ? config.trackerHttpPort : "Disabled"}
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">UDP Port</span>
            <span className="status-value">
              {config.trackerUdpEnabled ? config.trackerUdpPort : "Disabled"}
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">Announce Interval</span>
            <span className="status-value">
              {config.trackerAnnounceInterval}s
            </span>
          </div>
        </div>
      )}

      <div className="card">
        <h3>Tracked Torrents</h3>
        {torrentsLoading ? (
          <p className="loading">Loading tracked torrents...</p>
        ) : (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Source</th>
                  <th className="torrent-table-th">Name</th>
                  <th className="torrent-table-th">Seeders</th>
                  <th className="torrent-table-th">Leechers</th>
                  <th className="torrent-table-th">Uploaded</th>
                  <th className="torrent-table-th">Downloaded</th>
                  <th className="torrent-table-th">Completed</th>
                  <th className="torrent-table-th">Peers</th>
                  <th className="torrent-table-th">Last Activity</th>
                  <th className="torrent-table-th">Info Hash</th>
                </tr>
              </thead>
              <tbody>
                {(torrents ?? []).length === 0 ? (
                  <tr>
                    <td colSpan={10} className="torrent-table-empty">
                      No tracked torrents
                    </td>
                  </tr>
                ) : (
                  (torrents ?? []).map((t) => (
                    <tr key={t.infoHash} className="torrent-table-row">
                      <td>
                        <span
                          className={`badge ${t.isInternal ? "badge-seeding" : "badge-warning"}`}
                        >
                          {t.isInternal ? "Internal" : "External"}
                        </span>
                      </td>
                      <td>
                        {t.isInternal ? (
                          <Link
                            to="/torrents"
                            style={{ color: "inherit", textDecoration: "none", fontWeight: 500 }}
                            title="Jump to active torrent in library"
                          >
                            {t.name} ↗
                          </Link>
                        ) : (
                          t.name
                        )}
                      </td>
                      <td>{t.seeders}</td>
                      <td>{t.leechers}</td>
                      <td>{formatBytes(t.uploaded)}</td>
                      <td>{formatBytes(t.downloaded)}</td>
                      <td>{t.completed}</td>
                      <td>{t.peerCount}</td>
                      <td>{formatDate(t.lastActivity)}</td>
                      <td>
                        <code className="info-hash">{t.infoHash}</code>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

export default TrackerServer;
