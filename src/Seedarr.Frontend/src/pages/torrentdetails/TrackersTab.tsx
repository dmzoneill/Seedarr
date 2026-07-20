import { Torrent } from '../../api/types';
import { useTorrentTrackers } from '../../api/hooks';
import { formatDate } from '../../utils/formatters';
import { SkeletonLine } from '../../components/Skeleton';

function trackerStatusBadgeClass(status: string): string {
  switch (status) {
    case 'Working': return 'badge-seeding';
    case 'Announcing': return 'badge-announcing';
    case 'Failed': return 'badge-error';
    case 'Disabled': return 'badge-stopped';
    case 'Unknown':
    default: return 'badge-warning';
  }
}

export function TrackersTab({ torrent }: { torrent: Torrent }) {
  const { data: trackers, isLoading, error } = useTorrentTrackers(torrent.id);

  return (
    <div className="card">
      <h3>Trackers</h3>
      {isLoading && (
        <div className="torrent-table-wrapper">
          <SkeletonLine width="100%" height="2rem" />
          <SkeletonLine width="100%" height="1.5rem" />
          <SkeletonLine width="100%" height="1.5rem" />
        </div>
      )}
      {error && <p className="error">Failed to load trackers.</p>}
      {trackers && trackers.length === 0 && (
        <p className="torrent-table-empty">No trackers configured</p>
      )}
      {trackers && trackers.length > 0 && (
        <div className="torrent-table-wrapper">
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th">URL</th>
                <th className="torrent-table-th">Tier</th>
                <th className="torrent-table-th">Status</th>
                <th className="torrent-table-th">Seeders</th>
                <th className="torrent-table-th">Leechers</th>
                <th className="torrent-table-th">Announces</th>
                <th className="torrent-table-th">Last Announce</th>
                <th className="torrent-table-th">Next Announce</th>
              </tr>
            </thead>
            <tbody>
              {trackers.map((tracker) => (
                <tr key={tracker.id} className="torrent-table-row">
                  <td className="mono">{tracker.url}</td>
                  <td>{tracker.tier}</td>
                  <td>
                    <span className={`badge ${trackerStatusBadgeClass(tracker.status)}`}>
                      {tracker.status}
                    </span>
                  </td>
                  <td>{tracker.seeders}</td>
                  <td>{tracker.leechers}</td>
                  <td>{tracker.successfulAnnounces}/{tracker.totalAnnounces}</td>
                  <td>{formatDate(tracker.lastAnnounce)}</td>
                  <td>{formatDate(tracker.nextAnnounce)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
