import { useTorrents, useSeedingStats, useActiveSpeedLimits } from '../api/hooks';
import { formatBytes, formatSpeed, formatRatio, formatDate } from '../utils/formatters';
import HealthAlerts from '../components/HealthAlerts';
import SpeedGraph from '../components/SpeedGraph';
import { SkeletonGrid, SkeletonLine } from '../components/Skeleton';

const STATUS_COLORS: Record<string, string> = {
  Seeding: 'var(--color-success, #27ae60)',
  Stopped: 'var(--color-danger, #e74c3c)',
  Queued: 'var(--color-warning, #f39c12)',
  Error: '#c0392b',
};

function StatusDonut({ counts, total }: { counts: Record<string, number>; total: number }) {
  if (total === 0) return null;
  const entries = Object.entries(counts).filter(([, v]) => v > 0);
  let offset = 0;
  const radius = 40;
  const circumference = 2 * Math.PI * radius;

  return (
    <div className="card" style={{ display: 'flex', alignItems: 'center', gap: 24 }}>
      <svg width={100} height={100} viewBox="0 0 100 100">
        {entries.map(([status, count]) => {
          const pct = count / total;
          const dashLength = pct * circumference;
          const dashOffset = -offset * circumference;
          offset += pct;
          return (
            <circle
              key={status}
              cx={50} cy={50} r={radius}
              fill="none"
              stroke={STATUS_COLORS[status] || '#666'}
              strokeWidth={16}
              strokeDasharray={`${dashLength} ${circumference - dashLength}`}
              strokeDashoffset={dashOffset}
              transform="rotate(-90 50 50)"
            />
          );
        })}
        <text x={50} y={54} textAnchor="middle" fontSize={16} fontWeight={700} fill="var(--color-text, #ccc)">{total}</text>
      </svg>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        {entries.map(([status, count]) => (
          <div key={status} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
            <div style={{ width: 10, height: 10, borderRadius: '50%', backgroundColor: STATUS_COLORS[status] || '#666' }} />
            <span>{status}: {count}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function Dashboard() {
  const { data: torrents, isLoading } = useTorrents();
  const { data: stats, isLoading: statsLoading } = useSeedingStats();
  const { data: activeLimits } = useActiveSpeedLimits();

  const totalSize = (torrents ?? []).reduce((sum, t) => sum + t.totalSize, 0);
  const recent = [...(torrents ?? [])]
    .sort((a, b) => new Date(b.dateAdded).getTime() - new Date(a.dateAdded).getTime())
    .slice(0, 5);

  const statusCounts: Record<string, number> = {};
  (torrents ?? []).forEach((t) => {
    const s = t.status || 'Unknown';
    statusCounts[s] = (statusCounts[s] || 0) + 1;
  });

  const trackerCounts: Record<string, number> = {};
  (torrents ?? []).forEach((t) => {
    let domain = 'No tracker';
    if (t.trackerUrl) {
      try { domain = new URL(t.trackerUrl).hostname; } catch { domain = t.trackerUrl; }
    }
    trackerCounts[domain] = (trackerCounts[domain] || 0) + 1;
  });
  const topTrackers = Object.entries(trackerCounts)
    .sort((a, b) => b[1] - a[1])
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

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 16, marginBottom: 16 }}>
        <StatusDonut counts={statusCounts} total={torrents?.length ?? 0} />

        {activeLimits && (
          <div className="card">
            <h3 style={{ marginBottom: 8 }}>Speed Schedule</h3>
            <div className="status-row">
              <span className="status-label">Active</span>
              <span className="status-value">
                {activeLimits.isScheduleActive ? (
                  <span className="badge badge-seeding">{activeLimits.activeScheduleName}</span>
                ) : (
                  <span className="badge badge-stopped">None</span>
                )}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Upload Limit</span>
              <span className="status-value">
                {activeLimits.maxUploadSpeed > 0 ? formatSpeed(activeLimits.maxUploadSpeed) : 'Unlimited'}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Download Limit</span>
              <span className="status-value">
                {activeLimits.maxDownloadSpeed > 0 ? formatSpeed(activeLimits.maxDownloadSpeed) : 'Unlimited'}
              </span>
            </div>
          </div>
        )}

        {topTrackers.length > 0 && (
          <div className="card">
            <h3 style={{ marginBottom: 8 }}>Top Trackers</h3>
            {topTrackers.map(([domain, count]) => (
              <div key={domain} className="status-row">
                <span className="status-label" style={{ fontSize: 13 }}>{domain}</span>
                <span className="status-value">{count}</span>
              </div>
            ))}
          </div>
        )}
      </div>

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
