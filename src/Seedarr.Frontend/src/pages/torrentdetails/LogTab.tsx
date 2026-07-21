import { Torrent } from '../../api/types';
import { formatDate } from '../../utils/formatters';
import { StatusRow } from './shared';

export function LogTab({ torrent }: { torrent: Torrent }) {
  const events = [
    { time: torrent.dateAdded, event: 'Torrent added' },
    ...(torrent.forceCompleted
      ? [{ time: torrent.lastActive ?? torrent.dateAdded, event: 'Marked as force-completed (100%)' }]
      : []),
    ...(torrent.progress >= 1.0 && !torrent.forceCompleted && torrent.lastActive
      ? [{ time: torrent.lastActive, event: 'Download completed' }]
      : []),
    ...(torrent.forceStart
      ? [{ time: torrent.lastActive ?? torrent.dateAdded, event: 'Force-start enabled' }]
      : []),
    ...(torrent.lastActive && torrent.lastActive !== torrent.dateAdded
      ? [{ time: torrent.lastActive, event: `Last active — status: ${torrent.status}` }]
      : []),
  ]
    .filter(e => e.time)
    .sort((a, b) => new Date(a.time!).getTime() - new Date(b.time!).getTime());

  return (
    <div className="card">
      <h3>Log</h3>
      <div className="torrent-table-wrapper">
        <table className="torrent-table">
          <thead>
            <tr>
              <th className="torrent-table-th">Time</th>
              <th className="torrent-table-th">Event</th>
            </tr>
          </thead>
          <tbody>
            {events.length === 0 ? (
              <tr className="torrent-table-row">
                <td colSpan={2} style={{ color: 'var(--text-dim)', textAlign: 'center' }}>No events recorded</td>
              </tr>
            ) : (
              events.map((entry) => (
                <tr key={`${entry.time}-${entry.event}`} className="torrent-table-row">
                  <td>{formatDate(entry.time)}</td>
                  <td>{entry.event}</td>
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
