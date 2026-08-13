import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../api/client';

interface SystemStatusResponse {
  appName: string;
  version: string;
  buildTime: string;
  isDebug: boolean;
  isProduction: boolean;
  isAdmin: boolean;
  isUserInteractive: boolean;
  startupPath: string;
  appData: string;
  osName: string;
  osVersion: string;
  isDocker: boolean;
  isLinux: boolean;
  isOsx: boolean;
  isWindows: boolean;
  branch: string;
  runtimeVersion: string;
  runtimeName: string;
  startTime: string;
  packageVersion: string;
  packageAuthor: string;
  packageUpdateMechanism: string;
}

function SystemStatus() {
  const {
    data: status,
    isLoading,
    error,
  } = useQuery<SystemStatusResponse>({
    queryKey: ['system', 'status'],
    queryFn: () => apiClient.get('/system/status'),
  });

  return (
    <div>
      <h1 className="page-heading">System Status</h1>

      {isLoading && <p className="loading">Loading system status...</p>}

      {error && (
        <div className="card">
          <p className="error">Failed to load system status.</p>
        </div>
      )}

      {status && (
        <>
          <div className="card">
            <h3>About</h3>
            <div className="status-row">
              <span className="status-label">Application</span>
              <span className="status-value">{status.appName}</span>
            </div>
            <div className="status-row">
              <span className="status-label">Version</span>
              <span className="status-value">{status.version}</span>
            </div>
            <div className="status-row">
              <span className="status-label">Branch</span>
              <span className="status-value">{status.branch}</span>
            </div>
            <div className="status-row">
              <span className="status-label">Start Time</span>
              <span className="status-value">
                {new Date(status.startTime).toLocaleString()}
              </span>
            </div>
          </div>

          <div className="card">
            <h3>Environment</h3>
            <div className="status-row">
              <span className="status-label">OS</span>
              <span className="status-value">
                {status.osName} {status.osVersion}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Runtime</span>
              <span className="status-value">
                {status.runtimeName} {status.runtimeVersion}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Docker</span>
              <span className="status-value">
                {status.isDocker ? 'Yes' : 'No'}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Debug</span>
              <span className="status-value">
                {status.isDebug ? 'Yes' : 'No'}
              </span>
            </div>
          </div>

          <div className="card">
            <h3>Paths</h3>
            <div className="status-row">
              <span className="status-label">Startup Path</span>
              <span className="status-value">{status.startupPath}</span>
            </div>
            <div className="status-row">
              <span className="status-label">App Data</span>
              <span className="status-value">{status.appData}</span>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

export default SystemStatus;
