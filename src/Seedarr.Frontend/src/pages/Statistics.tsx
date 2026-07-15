import { useTorrents } from '../api/hooks';
import { formatBytes, formatSpeed, formatRatio } from '../utils/formatters';
import SpeedGraph from '../components/SpeedGraph';

const STATUS_COLORS: Record<string, string> = {
  Seeding: 'var(--color-success, #27ae60)',
  Stopped: 'var(--color-danger, #e74c3c)',
  Queued: 'var(--color-warning, #f39c12)',
  Error: '#c0392b',
};

function Statistics() {
  const { data: torrents, isLoading: torrentsLoading, isError: torrentsError } = useTorrents();

  const statusCounts: Record<string, number> = {};
  (torrents ?? []).forEach((t) => {
    statusCounts[t.status] = (statusCounts[t.status] || 0) + 1;
  });
  const total = torrents?.length ?? 0;
  const entries = Object.entries(statusCounts).filter(([, v]) => v > 0);

  const topTorrents = [...(torrents ?? [])]
    .sort((a, b) => b.uploaded - a.uploaded)
    .slice(0, 10);

  const trackerCounts: Record<string, number> = {};
  (torrents ?? []).forEach((t) => {
    let domain = 'No tracker';
    if (t.trackerUrl) {
      try { domain = new URL(t.trackerUrl).hostname; } catch { domain = t.trackerUrl; }
    }
    trackerCounts[domain] = (trackerCounts[domain] || 0) + 1;
  });
  const topTrackers = Object.entries(trackerCounts).sort((a, b) => b[1] - a[1]).slice(0, 10);

  return (
    <div>
      <div className="page-heading-row">
        <h1 className="page-heading">Statistics</h1>
      </div>

      <SpeedGraph />

      {total > 0 && (
        <div className="card">
          <h3>Status Breakdown</h3>
          <div style={{ display: 'flex', height: 32, borderRadius: 4, overflow: 'hidden', marginBottom: 12 }}>
            {entries.map(([status, count]) => {
              const pct = (count / total) * 100;
              return (
                <div
                  key={status}
                  style={{
                    width: `${pct}%`,
                    backgroundColor: STATUS_COLORS[status] || '#666',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    color: '#fff',
                    fontSize: 12,
                    fontWeight: 600,
                  }}
                  title={`${status}: ${count}`}
                >
                  {pct > 10 ? `${status} (${count})` : ''}
                </div>
              );
            })}
          </div>
          <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
            {entries.map(([status, count]) => (
              <div key={status} style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                <div style={{ width: 12, height: 12, borderRadius: 2, backgroundColor: STATUS_COLORS[status] || '#666' }} />
                <span>{status}: {count}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: 16, marginBottom: 16 }}>
        <div className="card">
          <h3>Top Torrents by Upload</h3>
          {torrentsLoading ? (
            <p className="loading">Loading...</p>
          ) : torrentsError ? (
            <p className="error">Failed to load data.</p>
          ) : (
            <div className="torrent-table-wrapper">
              <table className="torrent-table">
                <thead>
                  <tr>
                    <th className="torrent-table-th">Name</th>
                    <th className="torrent-table-th">Uploaded</th>
                    <th className="torrent-table-th">Ratio</th>
                    <th className="torrent-table-th">Speed</th>
                  </tr>
                </thead>
                <tbody>
                  {topTorrents.length === 0 ? (
                    <tr><td colSpan={4} className="torrent-table-empty">No torrents</td></tr>
                  ) : (
                    topTorrents.map((t) => (
                      <tr key={t.id} className="torrent-table-row">
                        <td>{t.name}</td>
                        <td>{formatBytes(t.uploaded)}</td>
                        <td>{formatRatio(t.ratio)}</td>
                        <td>{formatSpeed(t.uploadSpeed)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {topTrackers.length > 0 && (
          <div className="card">
            <h3>Tracker Distribution</h3>
            <div className="torrent-table-wrapper">
              <table className="torrent-table">
                <thead>
                  <tr>
                    <th className="torrent-table-th">Tracker</th>
                    <th className="torrent-table-th">Torrents</th>
                  </tr>
                </thead>
                <tbody>
                  {topTrackers.map(([domain, count]) => (
                    <tr key={domain} className="torrent-table-row">
                      <td>{domain}</td>
                      <td>{count}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default Statistics;
