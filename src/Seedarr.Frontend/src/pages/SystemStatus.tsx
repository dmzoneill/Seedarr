import { useSystemStatus, useHealthChecks, useDiskSpace } from "../api/hooks";
import { formatBytes, formatUptime } from "../utils/formatters";

function SystemStatus() {
  const { data: status, isLoading: statusLoading } = useSystemStatus();
  const { data: health, isLoading: healthLoading } = useHealthChecks();
  const { data: diskSpace, isLoading: diskLoading } = useDiskSpace();

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
                <div key={i} className={alertClass}>
                  <span>
                    {check.source}: {check.message || check.type}
                  </span>
                </div>
              );
            })}
          </div>
        )}
      </div>

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
