import { useTorrents, useSeedingStats } from '../api/hooks';
import { formatBytes, formatRatio, formatDate } from '../utils/formatters';
import HealthAlerts from '../components/HealthAlerts';
import SpeedGraph from '../components/SpeedGraph';
import { SkeletonGrid, SkeletonLine } from '../components/Skeleton';

function Dashboard() {
  const { data: torrents, isLoading } = useTorrents();
  const { data: stats, isLoading: statsLoading } = useSeedingStats();

  const totalSize = (torrents ?? []).reduce((sum, t) => sum + t.totalSize, 0);
  const recent = [...(torrents ?? [])]
    .sort((a, b) => new Date(b.dateAdded).getTime() - new Date(a.dateAdded).getTime())
    .slice(0, 5);

  return (
    <div>
      <h1 className="page-heading">Dashboard</h1>

      <HealthAlerts />

      {statsLoading ? (
        <SkeletonGrid count={4} />
      ) : (
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
      )}

      <SpeedGraph />

      <div className="card">
        <h3>Recent Torrents</h3>
        {isLoading && (
          <>
            {[0, 1, 2].map((i) => (
              <div key={i} className="status-row">
                <SkeletonLine width="50%" height="0.85rem" />
                <SkeletonLine width="20%" height="0.85rem" />
              </div>
            ))}
          </>
        )}
        {!isLoading && recent.length === 0 && (
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
