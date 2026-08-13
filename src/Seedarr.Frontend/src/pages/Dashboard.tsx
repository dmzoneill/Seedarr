import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../api/client';

interface SystemStatus {
  version: string;
  startTime: string;
}

function Dashboard() {
  const { data: status, isLoading, error } = useQuery<SystemStatus>({
    queryKey: ['system', 'status'],
    queryFn: () => apiClient.get('/system/status'),
    retry: false,
  });

  return (
    <div>
      <h1 className="page-heading">Seedarr Dashboard</h1>

      <div className="card">
        <h3>System Status</h3>
        {isLoading && <p className="loading">Loading...</p>}
        {error && (
          <p className="error">
            Unable to connect to Seedarr backend. Is it running on port 9898?
          </p>
        )}
        {status && (
          <div>
            <div className="status-row">
              <span className="status-label">Version</span>
              <span className="status-value">{status.version}</span>
            </div>
            <div className="status-row">
              <span className="status-label">Start Time</span>
              <span className="status-value">{status.startTime}</span>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default Dashboard;
