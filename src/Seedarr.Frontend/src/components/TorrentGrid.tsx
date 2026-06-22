import { Link } from 'react-router-dom';
import {
  useTorrents,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
} from '../api/hooks';
import { formatBytes, formatRatio, formatDate, extractTrackerDomain } from '../utils/formatters';
import type { Torrent } from '../api/types';

interface TorrentGridProps {
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
}

function TorrentGrid({ filter, stateFilter, trackerFilter }: TorrentGridProps) {
  const { data: torrents, isLoading } = useTorrents();
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();

  if (isLoading) {
    return (
      <div className="torrent-grid">
        {[0, 1, 2, 3, 4, 5].map((i) => (
          <div key={i} className="torrent-grid-card">
            <div className="torrent-grid-card-header">
              <span className="skeleton skeleton-line" style={{ width: '80%', height: '1rem' }} />
            </div>
            <div className="torrent-grid-card-stats">
              <div className="torrent-grid-stat">
                <span className="skeleton skeleton-line" style={{ width: '60%', height: '0.8rem' }} />
              </div>
              <div className="torrent-grid-stat">
                <span className="skeleton skeleton-line" style={{ width: '50%', height: '0.8rem' }} />
              </div>
              <div className="torrent-grid-stat">
                <span className="skeleton skeleton-line" style={{ width: '55%', height: '0.8rem' }} />
              </div>
              <div className="torrent-grid-stat">
                <span className="skeleton skeleton-line" style={{ width: '65%', height: '0.8rem' }} />
              </div>
            </div>
          </div>
        ))}
      </div>
    );
  }

  const filtered = (torrents ?? []).filter((t) => {
    if (filter && !t.name.toLowerCase().includes(filter.toLowerCase())) return false;
    if (stateFilter && stateFilter !== 'All' && t.status !== stateFilter) return false;
    if (trackerFilter && trackerFilter !== 'All') {
      if (extractTrackerDomain(t.trackerUrl) !== trackerFilter) return false;
    }
    return true;
  });

  function statusBadge(status: string) {
    const cls = `badge badge-${status.toLowerCase()}`;
    return <span className={cls}>{status}</span>;
  }

  function renderActions(torrent: Torrent) {
    const isSeeding = torrent.status === 'Seeding';
    return (
      <div className="torrent-actions">
        {isSeeding ? (
          <button
            className="btn btn-small"
            onClick={() => stopSeeding.mutate(torrent.id)}
          >
            Stop
          </button>
        ) : (
          <button
            className="btn btn-small btn-success"
            onClick={() => startSeeding.mutate(torrent.id)}
          >
            Start
          </button>
        )}
        <button
          className="btn btn-small btn-danger"
          onClick={() => {
            if (confirm(`Delete "${torrent.name}"?`)) {
              deleteTorrent.mutate({ id: torrent.id });
            }
          }}
        >
          Delete
        </button>
      </div>
    );
  }

  if (filtered.length === 0) {
    return (
      <div className="torrent-grid-empty">No torrents found</div>
    );
  }

  return (
    <div className="torrent-grid">
      {filtered.map((t) => (
        <div key={t.id} className="torrent-grid-card">
          <div className="torrent-grid-card-header">
            <Link to={`/torrents/${t.id}`} className="torrent-link torrent-grid-card-name">
              {t.name}
            </Link>
            {statusBadge(t.status)}
          </div>
          <div className="torrent-grid-card-stats">
            <div className="torrent-grid-stat">
              <span className="torrent-grid-stat-label">Size</span>
              <span className="torrent-grid-stat-value">{formatBytes(t.totalSize)}</span>
            </div>
            <div className="torrent-grid-stat">
              <span className="torrent-grid-stat-label">Uploaded</span>
              <span className="torrent-grid-stat-value">{formatBytes(t.uploaded)}</span>
            </div>
            <div className="torrent-grid-stat">
              <span className="torrent-grid-stat-label">Ratio</span>
              <span className="torrent-grid-stat-value">{formatRatio(t.ratio)}</span>
            </div>
            <div className="torrent-grid-stat">
              <span className="torrent-grid-stat-label">Added</span>
              <span className="torrent-grid-stat-value">{formatDate(t.dateAdded)}</span>
            </div>
          </div>
          <div className="torrent-grid-card-footer">
            {renderActions(t)}
          </div>
        </div>
      ))}
    </div>
  );
}

export default TorrentGrid;
