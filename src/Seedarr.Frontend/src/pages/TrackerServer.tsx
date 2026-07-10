import {
  useTrackerServerStats,
  useTrackerServerTorrents,
  useTrackerServerConfig,
  useSaveTrackerServerConfig,
} from '../api/hooks';
import { formatBytes, formatDate, formatUptime } from '../utils/formatters';
import type { TrackerServerConfig } from '../api/types';

function TrackerServer() {
  const { data: stats, isLoading: statsLoading } = useTrackerServerStats();
  const { data: torrents, isLoading: torrentsLoading } = useTrackerServerTorrents();
  const { data: config } = useTrackerServerConfig();
  const saveConfig = useSaveTrackerServerConfig();

  function handleToggleEnabled() {
    if (!config) return;
    const updated: TrackerServerConfig = {
      ...config,
      trackerServerEnabled: !config.trackerServerEnabled,
    };
    saveConfig.mutate(updated);
  }

  return (
    <div>
      <div className="page-heading-row">
        <h1 className="page-heading">Tracker Server</h1>
        {config && (
          <button
            className={`btn ${config.trackerServerEnabled ? 'btn-danger' : 'btn-success'}`}
            onClick={handleToggleEnabled}
            disabled={saveConfig.isPending}
          >
            {config.trackerServerEnabled ? 'Disable Tracker' : 'Enable Tracker'}
          </button>
        )}
      </div>

      <div className="tracker-stats-grid">
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Torrents</div>
          <div className="tracker-stat-value">
            {statsLoading ? '-' : (stats?.totalTorrents ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Internal (Seedarr)</div>
          <div className="tracker-stat-value">
            {statsLoading ? '-' : (stats?.internalTorrents ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Peers</div>
          <div className="tracker-stat-value">
            {statsLoading ? '-' : (stats?.totalPeers ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Announces</div>
          <div className="tracker-stat-value">
            {statsLoading ? '-' : (stats?.totalAnnounces ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Uptime</div>
          <div className="tracker-stat-value">
            {statsLoading ? '-' : formatUptime(stats?.uptime ?? 0)}
          </div>
        </div>
      </div>

      {config && (
        <div className="card tracker-info-card">
          <div className="status-row">
            <span className="status-label">Status</span>
            <span className="status-value">
              <span className={`badge badge-${config.trackerServerEnabled ? 'seeding' : 'stopped'}`}>
                {config.trackerServerEnabled ? 'Enabled' : 'Disabled'}
              </span>
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">HTTP Port</span>
            <span className="status-value">
              {config.trackerHttpEnabled ? config.trackerHttpPort : 'Disabled'}
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">UDP Port</span>
            <span className="status-value">
              {config.trackerUdpEnabled ? config.trackerUdpPort : 'Disabled'}
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">Announce Interval</span>
            <span className="status-value">{config.trackerAnnounceInterval}s</span>
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
                        <span className={`badge ${t.isInternal ? 'badge-seeding' : 'badge-warning'}`}>
                          {t.isInternal ? 'Internal' : 'External'}
                        </span>
                      </td>
                      <td>{t.name}</td>
                      <td>{t.seeders}</td>
                      <td>{t.leechers}</td>
                      <td>{formatBytes(t.uploaded)}</td>
                      <td>{formatBytes(t.downloaded)}</td>
                      <td>{t.completed}</td>
                      <td>{t.peerCount}</td>
                      <td>{formatDate(t.lastActivity)}</td>
                      <td><code className="info-hash">{t.infoHash}</code></td>
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
