import { Torrent } from '../../api/types';
import { useTorrentLogs } from '../../api/hooks';
import { formatDate } from '../../utils/formatters';
import { StatusRow } from './shared';

function levelBadgeClass(level: string): string {
  switch (level.toLowerCase()) {
    case 'debug': return 'torrent-log-level-debug';
    case 'warn': case 'warning': return 'torrent-log-level-warn';
    case 'error': case 'fatal': return 'torrent-log-level-error';
    default: return 'torrent-log-level-info';
  }
}

export function LogTab({ torrent }: { torrent: Torrent }) {
  const { data: logs, isLoading, isError } = useTorrentLogs(torrent.id);

  return (
    <div className="card">
      <h3>Log</h3>
      <div className="torrent-table-wrapper">
        <table className="torrent-table">
          <thead>
            <tr>
              <th className="torrent-table-th">Time</th>
              <th className="torrent-table-th">Level</th>
              <th className="torrent-table-th">Source</th>
              <th className="torrent-table-th">Event</th>
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <tr className="torrent-table-row">
                <td colSpan={4} style={{ color: 'var(--text-dim)', textAlign: 'center' }}>Loading log entries...</td>
              </tr>
            ) : isError ? (
              <tr className="torrent-table-row">
                <td colSpan={4} style={{ color: 'var(--danger)', textAlign: 'center' }}>Failed to load log entries</td>
              </tr>
            ) : !logs || logs.length === 0 ? (
              <tr className="torrent-table-row">
                <td colSpan={4} style={{ color: 'var(--text-dim)', textAlign: 'center' }}>No events recorded yet</td>
              </tr>
            ) : (
              logs.map((entry) => (
                <tr key={entry.id} className="torrent-table-row">
                  <td>{formatDate(entry.timeStamp)}</td>
                  <td>
                    <span className={`torrent-log-level ${levelBadgeClass(entry.level)}`}>
                      {entry.level.toUpperCase()}
                    </span>
                  </td>
                  <td>{entry.source}</td>
                  <td>{entry.message}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      <div style={{ marginTop: '1rem' }}>
        <StatusRow label="Info Hash" mono>{torrent.infoHash}</StatusRow>
        <StatusRow label="Current Status">
          <span className={`badge badge-${torrent.status.toLowerCase()}`}>{torrent.status}</span>
        </StatusRow>
      </div>
    </div>
  );
}
