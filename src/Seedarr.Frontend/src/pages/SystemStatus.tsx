import { Link } from "react-router";
import {
  useSystemStatus,
  useHealthChecks,
  useDiskSpace,
  useArrConnections,
  useDownloadClients,
  useIndexers,
} from "../api/hooks";
import { formatBytes, formatUptime } from "../utils/formatters";

function SystemStatus() {
  const { data: status, isLoading: statusLoading } = useSystemStatus();
  const { data: health, isLoading: healthLoading } = useHealthChecks();
  const { data: diskSpace, isLoading: diskLoading } = useDiskSpace();
  const { data: arrConnections } = useArrConnections();
  const { data: downloadClients } = useDownloadClients();
  const { data: indexers } = useIndexers();

  const isLoading = statusLoading || healthLoading || diskLoading;

  return (
    <div>
      <h1 className="page-heading">System Status</h1>

      {isLoading && <p className="loading">Loading system status...</p>}

      {/* Health Section */}
      <div className="system-status-section">
        <h2>Health</h2>
        {health && health.length === 0 && (
          <div className="health-ok-message">
            No issues with your configuration
          </div>
        )}
        {health && health.length > 0 && (
          <div className="health-alerts">
            {health.map((check, i) => {
              let alertClass = "health-alert health-alert-notice";
              if (check.type === "Warning")
                alertClass = "health-alert health-alert-warning";
              if (check.type === "Error")
                alertClass = "health-alert health-alert-error";
              return (
                <div
                  key={i}
                  className={alertClass}
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                  }}
                >
                  <span>
                    <strong>{check.source}</strong>:{" "}
                    {check.message || check.type}
                  </span>
                  <Link
                    to="/settings/general"
                    className="btn btn-small btn-outline"
                    style={{
                      fontSize: "0.75rem",
                      textDecoration: "none",
                      marginLeft: "1rem",
                    }}
                  >
                    Fix in Settings ⚙️
                  </Link>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Arr & Download Client Integrations Diagnostic Table */}
      {((arrConnections && arrConnections.length > 0) ||
        (downloadClients && downloadClients.length > 0) ||
        (indexers && indexers.length > 0)) && (
        <div className="system-status-section">
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <h2>Ecosystem & Integration Endpoints</h2>
            <Link
              to="/settings/connections"
              style={{ fontSize: "0.85rem", color: "var(--accent)" }}
            >
              Manage in Settings →
            </Link>
          </div>
          <table className="system-status-table">
            <thead>
              <tr>
                <th>Service Name</th>
                <th>Type</th>
                <th>Endpoint / Host</th>
                <th>State</th>
                <th>Integration Features</th>
                <th style={{ textAlign: "right" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {arrConnections?.map((conn) => (
                <tr key={`arr-${conn.id}`}>
                  <td>
                    <strong>{conn.name}</strong>
                  </td>
                  <td>
                    <span className="badge badge-primary">{conn.arrType}</span>
                  </td>
                  <td>
                    <code>{conn.url}</code>
                  </td>
                  <td>
                    <span
                      className={`badge ${conn.enable ? "badge-seeding" : "badge-stopped"}`}
                    >
                      {conn.enable ? "Enabled" : "Disabled"}
                    </span>
                  </td>
                  <td>
                    <span
                      style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}
                    >
                      {[
                        conn.syncEnabled && "Sync",
                        conn.enableAutomaticAdd && "Auto-Add",
                        conn.webhookEnabled && "Webhook",
                      ]
                        .filter(Boolean)
                        .join(" • ") || "None"}
                    </span>
                  </td>
                  <td style={{ textAlign: "right" }}>
                    {conn.url && (
                      <a
                        href={conn.url}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="btn btn-small btn-outline"
                        style={{ fontSize: "0.75rem", textDecoration: "none" }}
                        title={`Open ${conn.name} Web UI`}
                      >
                        Open ↗
                      </a>
                    )}
                  </td>
                </tr>
              ))}

              {downloadClients?.map((client) => (
                <tr key={`client-${client.id}`}>
                  <td>
                    <strong>{client.name}</strong>
                  </td>
                  <td>
                    <span className="badge badge-secondary">
                      {client.clientType}
                    </span>
                  </td>
                  <td>
                    <code>
                      {client.host}:{client.port}
                    </code>
                  </td>
                  <td>
                    <span
                      className={`badge ${client.enable ? "badge-seeding" : "badge-stopped"}`}
                    >
                      {client.enable ? "Enabled" : "Disabled"}
                    </span>
                  </td>
                  <td>
                    <span
                      style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}
                    >
                      Download Agent Client
                    </span>
                  </td>
                  <td style={{ textAlign: "right" }}>
                    {client.host && (
                      <a
                        href={`${client.useSsl ? "https" : "http"}://${client.host}${client.port ? `:${client.port}` : ""}`}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="btn btn-small btn-outline"
                        style={{ fontSize: "0.75rem", textDecoration: "none" }}
                        title={`Open ${client.name} Web UI`}
                      >
                        Open ↗
                      </a>
                    )}
                  </td>
                </tr>
              ))}

              {indexers?.map((idx) => (
                <tr key={`indexer-${idx.id}`}>
                  <td>
                    <strong>{idx.name}</strong>
                  </td>
                  <td>
                    <span className="badge badge-secondary">
                      {idx.indexerType}
                    </span>
                  </td>
                  <td>
                    <code>{idx.url || "-"}</code>
                  </td>
                  <td>
                    <span
                      className={`badge ${idx.enable ? "badge-seeding" : "badge-stopped"}`}
                    >
                      {idx.enable ? "Enabled" : "Disabled"}
                    </span>
                  </td>
                  <td>
                    <span
                      style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}
                    >
                      {[idx.enableRss && "RSS", idx.enableSearch && "Search"]
                        .filter(Boolean)
                        .join(" • ") || "Indexer"}
                    </span>
                  </td>
                  <td style={{ textAlign: "right" }}>
                    {idx.url && (
                      <a
                        href={idx.url}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="btn btn-small btn-outline"
                        style={{ fontSize: "0.75rem", textDecoration: "none" }}
                        title={`Open ${idx.name} Web UI`}
                      >
                        Open ↗
                      </a>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Disk Space Section */}
      <div className="system-status-section">
        <h2>Disk Space</h2>
        {diskSpace && diskSpace.length > 0 && (
          <table className="system-status-table">
            <thead>
              <tr>
                <th>Location</th>
                <th>Free Space</th>
                <th>Total Space</th>
                <th style={{ width: "30%" }}>Usage</th>
              </tr>
            </thead>
            <tbody>
              {diskSpace.map((d, i) => {
                const usedPercent =
                  d.totalSpace > 0
                    ? ((d.totalSpace - d.freeSpace) / d.totalSpace) * 100
                    : 0;
                let barClass = "disk-progress-bar";
                if (usedPercent >= 90) barClass += " disk-progress-bar-danger";
                else if (usedPercent >= 75)
                  barClass += " disk-progress-bar-warning";
                return (
                  <tr key={i}>
                    <td>
                      {d.label} ({d.path})
                    </td>
                    <td>{formatBytes(d.freeSpace)}</td>
                    <td>{formatBytes(d.totalSpace)}</td>
                    <td>
                      <div className="disk-progress">
                        <div
                          className={barClass}
                          style={{ width: `${usedPercent}%` }}
                        />
                        <span className="disk-progress-text">
                          {usedPercent.toFixed(1)}%
                        </span>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* About Section */}
      {status && (
        <div className="system-status-section">
          <h2>About</h2>
          <table className="system-status-table">
            <tbody>
              <tr>
                <td className="status-label-cell">Version</td>
                <td>{status.version}</td>
              </tr>
              <tr>
                <td className="status-label-cell">.NET</td>
                <td>
                  {status.runtimeName} ({status.runtimeVersion})
                </td>
              </tr>
              <tr>
                <td className="status-label-cell">Database</td>
                <td>{status.databaseVersion}</td>
              </tr>
              <tr>
                <td className="status-label-cell">Database Migration</td>
                <td>{status.databaseMigration}</td>
              </tr>
              <tr>
                <td className="status-label-cell">AppData Directory</td>
                <td>{status.appDataPath}</td>
              </tr>
              <tr>
                <td className="status-label-cell">Startup Directory</td>
                <td>{status.startupPath}</td>
              </tr>
              <tr>
                <td className="status-label-cell">Mode</td>
                <td>
                  {status.isDocker ? "Docker" : "Console"}
                  {status.isDebug ? " (Debug)" : ""}
                </td>
              </tr>
              <tr>
                <td className="status-label-cell">Uptime</td>
                <td>{formatUptime(status.uptimeSeconds)}</td>
              </tr>
            </tbody>
          </table>
        </div>
      )}

      {/* More Info Section */}
      <div className="system-status-section">
        <h2>More Info</h2>
        <table className="system-status-table">
          <tbody>
            <tr>
              <td className="status-label-cell">Home Page</td>
              <td>
                <a
                  href="https://www.seedarr.net"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  www.seedarr.net
                </a>
              </td>
            </tr>
            <tr>
              <td className="status-label-cell">Source</td>
              <td>
                <a
                  href="https://github.com/dmzoneill/Seedarr"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  github.com/dmzoneill/Seedarr
                </a>
              </td>
            </tr>
            <tr>
              <td className="status-label-cell">Feature Requests</td>
              <td>
                <a
                  href="https://github.com/dmzoneill/Seedarr/issues"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  github.com/dmzoneill/Seedarr/issues
                </a>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  );
}

export default SystemStatus;
