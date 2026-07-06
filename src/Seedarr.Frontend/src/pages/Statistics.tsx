import { useTorrents, useSeedingStats, useSpeedHistory } from '../api/hooks';
import { formatBytes, formatSpeed, formatRatio } from '../utils/formatters';
import type { Torrent, SpeedSnapshot } from '../api/types';

function Statistics() {
  const { data: torrents, isLoading: torrentsLoading } = useTorrents();
  const { data: stats, isLoading: statsLoading } = useSeedingStats();
  const { data: history } = useSpeedHistory();

  const statusCounts = (torrents ?? []).reduce(
    (acc, t) => {
      acc[t.status] = (acc[t.status] || 0) + 1;
      return acc;
    },
    {} as Record<string, number>,
  );

  const totalTorrents = torrents?.length ?? 0;
  const seedingCount = statusCounts['Seeding'] ?? 0;
  const stoppedCount = statusCounts['Stopped'] ?? 0;
  const queuedCount = statusCounts['Queued'] ?? 0;
  const errorCount = statusCounts['Error'] ?? 0;

  const seedingPercent = totalTorrents > 0 ? (seedingCount / totalTorrents) * 100 : 0;
  const stoppedPercent = totalTorrents > 0 ? (stoppedCount / totalTorrents) * 100 : 0;
  const queuedPercent = totalTorrents > 0 ? (queuedCount / totalTorrents) * 100 : 0;
  const errorPercent = totalTorrents > 0 ? (errorCount / totalTorrents) * 100 : 0;

  const topTorrents = [...(torrents ?? [])]
    .sort((a, b) => b.uploaded - a.uploaded)
    .slice(0, 10);

  const maxUploadSpeed = Math.max(...(history ?? []).map((h) => h.uploadSpeed), 1);
  const maxDownloadSpeed = Math.max(...(history ?? []).map((h) => h.downloadSpeed), 1);
  const maxSpeed = Math.max(maxUploadSpeed, maxDownloadSpeed);

  return (
    <div>
      <div className="page-heading-row">
        <h1 className="page-heading">Statistics</h1>
      </div>

      <div className="tracker-stats-grid">
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Active Torrents</div>
          <div className="tracker-stat-value">
            {statsLoading ? '-' : (stats?.activeTorrents ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Uploaded</div>
          <div className="tracker-stat-value">
            {statsLoading ? '-' : formatBytes(stats?.totalUploaded ?? 0)}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Downloaded</div>
          <div className="tracker-stat-value">
            {statsLoading ? '-' : formatBytes(stats?.totalDownloaded ?? 0)}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Average Ratio</div>
          <div className="tracker-stat-value">
            {statsLoading ? '-' : formatRatio(stats?.averageRatio ?? 0)}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Torrents</div>
          <div className="tracker-stat-value">
            {torrentsLoading ? '-' : totalTorrents.toLocaleString()}
          </div>
        </div>
      </div>

      <div className="card">
        <h3>Torrent Status Breakdown</h3>
        <div style={{ marginBottom: '1rem' }}>
          <div
            style={{
              display: 'flex',
              height: '40px',
              borderRadius: '4px',
              overflow: 'hidden',
              border: '1px solid var(--color-border)',
            }}
          >
            {seedingCount > 0 && (
              <div
                style={{
                  width: `${seedingPercent}%`,
                  backgroundColor: 'var(--color-success)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  color: 'white',
                  fontSize: '0.875rem',
                  fontWeight: 500,
                }}
              >
                {seedingCount > 0 && seedingPercent > 5 && seedingCount}
              </div>
            )}
            {stoppedCount > 0 && (
              <div
                style={{
                  width: `${stoppedPercent}%`,
                  backgroundColor: 'var(--color-danger)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  color: 'white',
                  fontSize: '0.875rem',
                  fontWeight: 500,
                }}
              >
                {stoppedCount > 0 && stoppedPercent > 5 && stoppedCount}
              </div>
            )}
            {queuedCount > 0 && (
              <div
                style={{
                  width: `${queuedPercent}%`,
                  backgroundColor: 'var(--color-warning)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  color: 'white',
                  fontSize: '0.875rem',
                  fontWeight: 500,
                }}
              >
                {queuedCount > 0 && queuedPercent > 5 && queuedCount}
              </div>
            )}
            {errorCount > 0 && (
              <div
                style={{
                  width: `${errorPercent}%`,
                  backgroundColor: '#c0392b',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  color: 'white',
                  fontSize: '0.875rem',
                  fontWeight: 500,
                }}
              >
                {errorCount > 0 && errorPercent > 5 && errorCount}
              </div>
            )}
          </div>
        </div>
        <div style={{ display: 'flex', gap: '2rem', flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <div
              style={{
                width: '16px',
                height: '16px',
                backgroundColor: 'var(--color-success)',
                borderRadius: '2px',
              }}
            />
            <span>Seeding ({seedingCount})</span>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <div
              style={{
                width: '16px',
                height: '16px',
                backgroundColor: 'var(--color-danger)',
                borderRadius: '2px',
              }}
            />
            <span>Stopped ({stoppedCount})</span>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <div
              style={{
                width: '16px',
                height: '16px',
                backgroundColor: 'var(--color-warning)',
                borderRadius: '2px',
              }}
            />
            <span>Queued ({queuedCount})</span>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <div
              style={{
                width: '16px',
                height: '16px',
                backgroundColor: '#c0392b',
                borderRadius: '2px',
              }}
            />
            <span>Error ({errorCount})</span>
          </div>
        </div>
      </div>

      <div className="card">
        <h3>Speed History (Last 25 Minutes)</h3>
        {!history || history.length === 0 ? (
          <p className="loading">No speed history available</p>
        ) : (
          <div style={{ position: 'relative' }}>
            <svg
              viewBox="0 0 800 300"
              style={{ width: '100%', height: 'auto', maxHeight: '300px' }}
            >
              <defs>
                <linearGradient id="uploadGradient" x1="0%" y1="0%" x2="0%" y2="100%">
                  <stop offset="0%" stopColor="var(--color-success)" stopOpacity="0.3" />
                  <stop offset="100%" stopColor="var(--color-success)" stopOpacity="0" />
                </linearGradient>
                <linearGradient id="downloadGradient" x1="0%" y1="0%" x2="0%" y2="100%">
                  <stop offset="0%" stopColor="var(--color-primary)" stopOpacity="0.3" />
                  <stop offset="100%" stopColor="var(--color-primary)" stopOpacity="0" />
                </linearGradient>
              </defs>

              <rect x="60" y="10" width="720" height="240" fill="none" stroke="var(--color-border)" />

              {[0, 1, 2, 3, 4].map((i) => {
                const y = 10 + (240 / 4) * i;
                const speed = maxSpeed * (1 - i / 4);
                return (
                  <g key={i}>
                    <line x1="60" y1={y} x2="780" y2={y} stroke="var(--color-border)" strokeDasharray="4" />
                    <text x="50" y={y + 4} textAnchor="end" fontSize="11" fill="var(--color-text)">
                      {formatSpeed(speed)}
                    </text>
                  </g>
                );
              })}

              {(() => {
                const width = 720;
                const height = 240;
                const pointCount = history.length;
                const xStep = width / Math.max(pointCount - 1, 1);

                const uploadPoints = history
                  .map((h, i) => {
                    const x = 60 + i * xStep;
                    const y = 10 + height - (h.uploadSpeed / maxSpeed) * height;
                    return `${x},${y}`;
                  })
                  .join(' ');

                const uploadArea = `60,250 ${uploadPoints} 780,250`;

                const downloadPoints = history
                  .map((h, i) => {
                    const x = 60 + i * xStep;
                    const y = 10 + height - (h.downloadSpeed / maxSpeed) * height;
                    return `${x},${y}`;
                  })
                  .join(' ');

                const downloadArea = `60,250 ${downloadPoints} 780,250`;

                return (
                  <>
                    <polygon points={downloadArea} fill="url(#downloadGradient)" />
                    <polyline
                      points={downloadPoints}
                      fill="none"
                      stroke="var(--color-primary)"
                      strokeWidth="2"
                    />
                    <polygon points={uploadArea} fill="url(#uploadGradient)" />
                    <polyline
                      points={uploadPoints}
                      fill="none"
                      stroke="var(--color-success)"
                      strokeWidth="2"
                    />
                  </>
                );
              })()}

              <text x="400" y="280" textAnchor="middle" fontSize="12" fill="var(--color-text)">
                Time
              </text>
              <text x="20" y="130" textAnchor="middle" fontSize="12" fill="var(--color-text)" transform="rotate(-90 20 130)">
                Speed
              </text>
            </svg>
            <div style={{ display: 'flex', gap: '2rem', justifyContent: 'center', marginTop: '1rem' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <div
                  style={{
                    width: '20px',
                    height: '3px',
                    backgroundColor: 'var(--color-success)',
                  }}
                />
                <span>Upload</span>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <div
                  style={{
                    width: '20px',
                    height: '3px',
                    backgroundColor: 'var(--color-primary)',
                  }}
                />
                <span>Download</span>
              </div>
            </div>
          </div>
        )}
      </div>

      <div className="card">
        <h3>Top Torrents by Upload</h3>
        {torrentsLoading ? (
          <p className="loading">Loading torrents...</p>
        ) : (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Name</th>
                  <th className="torrent-table-th">Uploaded</th>
                  <th className="torrent-table-th">Ratio</th>
                  <th className="torrent-table-th">Status</th>
                  <th className="torrent-table-th">Upload Speed</th>
                </tr>
              </thead>
              <tbody>
                {topTorrents.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="torrent-table-empty">
                      No torrents
                    </td>
                  </tr>
                ) : (
                  topTorrents.map((t) => (
                    <tr key={t.id} className="torrent-table-row">
                      <td>{t.name}</td>
                      <td>{formatBytes(t.uploaded)}</td>
                      <td>{formatRatio(t.ratio)}</td>
                      <td>
                        <span className={`badge badge-${t.status.toLowerCase()}`}>{t.status}</span>
                      </td>
                      <td>{formatSpeed(t.uploadSpeed)}</td>
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

export default Statistics;
