import type { Torrent } from '../../api/types';
import { useTorrentLogs } from '../../api/hooks';
import { formatDate } from '../../utils/formatters';
import { PanelLoading, PanelEmpty } from './shared';

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

  if (isLoading) return <PanelLoading>Loading log entries...</PanelLoading>;
  if (isError) return <PanelEmpty>Failed to load log entries.</PanelEmpty>;
  if (!logs || logs.length === 0) return <PanelEmpty>No events recorded yet</PanelEmpty>;

  return (
    <div className="detail-panel-table-wrap">
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
          {logs.map((entry) => (
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
          ))}
        </tbody>
      </table>
    </div>
  );
}
