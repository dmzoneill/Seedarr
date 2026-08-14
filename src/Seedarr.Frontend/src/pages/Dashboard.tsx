import { useTorrents, useSeedingStats } from '../api/hooks';
import { formatBytes, formatRatio, formatDate } from '../utils/formatters';
import HealthAlerts from '../components/HealthAlerts';

function Dashboard() {
  const { data: torrents, isLoading } = useTorrents();
  const { data: stats } = useSeedingStats();

  const totalSize = (torrents ?? []).reduce((sum, t) => sum + t.totalSize, 0);
  const recent = [...(torrents ?? [])]
    .sort((a, b) => new Date(b.dateAdded).getTime() - new Date(a.dateAdded).getTime())
    .slice(0, 5);

  return (
    <div>
      <h1 className="page-heading">Dashboard</h1>

      <HealthAlerts />

      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-value">{stats?.activeTorrents ?? 0}</div>
          <div className="stat-label">Active Torrents</div>
        </div>
        <div className="stat-card">
          <div className="stat-value">{formatBytes(stats?.totalUploaded ?? 0)}</div>
          <div className="stat-label">Total Uploaded</div>
        </div>
        <div className="stat-card">
          <div className="stat-value">{formatRatio(stats?.averageRatio ?? 0)}</div>
          <div className="stat-label">Average Ratio</div>
        </div>
        <div className="stat-card">
          <div className="stat-value">{formatBytes(totalSize)}</div>
          <div className="stat-label">Total Size</div>
        </div>
      </div>

      <div className="card">
        <h3>Recent Torrents</h3>
        {isLoading && <p className="loading">Loading...</p>}
        {recent.length === 0 && !isLoading && (
          <p className="loading">No torrents added yet.</p>
        )}
        {recent.map((t) => (
          <div key={t.id} className="status-row">
            <span className="status-label">{t.name}</span>
            <span className="status-value">
              <span className={`badge badge-${t.status.toLowerCase()}`}>{t.status}</span>
              {' '}
              {formatDate(t.dateAdded)}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

export default Dashboard;
