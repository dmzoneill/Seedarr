import { useParams, Link } from 'react-router-dom';
import { useTorrent, useStartSeeding, useStopSeeding } from '../api/hooks';
import { formatBytes, formatRatio, formatDate } from '../utils/formatters';
import { SkeletonLine } from '../components/Skeleton';
import PeerList from '../components/PeerList';

function TorrentDetails() {
  const { id } = useParams<{ id: string }>();
  const torrentId = Number(id) || 0;
  const { data: torrent, isLoading, error } = useTorrent(torrentId);
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();

  if (isLoading) {
    return (
      <div>
        <Link to="/torrents" className="back-link">Back to Torrents</Link>
        <SkeletonLine width="40%" height="1.5rem" />
        <div className="detail-grid" style={{ marginTop: '1.5rem' }}>
          <div className="card">
            <SkeletonLine width="30%" height="1rem" />
            {[0, 1, 2, 3, 4].map((i) => (
              <div key={i} className="status-row">
                <SkeletonLine width="25%" height="0.85rem" />
                <SkeletonLine width="40%" height="0.85rem" />
              </div>
            ))}
          </div>
          <div className="card">
            <SkeletonLine width="30%" height="1rem" />
            {[0, 1, 2, 3, 4, 5].map((i) => (
              <div key={i} className="status-row">
                <SkeletonLine width="25%" height="0.85rem" />
                <SkeletonLine width="40%" height="0.85rem" />
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  }
  if (error || !torrent) {
    return (
      <div>
        <Link to="/torrents" className="back-link">Back to Torrents</Link>
        <p className="error">Torrent not found.</p>
      </div>
    );
  }

  const isSeeding = torrent.status === 'Seeding';

  return (
    <div>
      <Link to="/torrents" className="back-link">Back to Torrents</Link>
      <h1 className="page-heading">{torrent.name}</h1>

      <div className="detail-grid">
        <div className="card">
          <h3>Info</h3>
          <div className="status-row">
            <span className="status-label">Info Hash</span>
            <span className="status-value mono">{torrent.infoHash}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Size</span>
            <span className="status-value">{formatBytes(torrent.totalSize)}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Pieces</span>
            <span className="status-value">{torrent.pieceCount} x {formatBytes(torrent.pieceLength)}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Private</span>
            <span className="status-value">{torrent.isPrivate ? 'Yes' : 'No'}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Created</span>
            <span className="status-value">{formatDate(torrent.creationDate)}</span>
          </div>
          {torrent.comment && (
            <div className="status-row">
              <span className="status-label">Comment</span>
              <span className="status-value">{torrent.comment}</span>
            </div>
          )}
        </div>

        <div className="card">
          <h3>Stats</h3>
          <div className="status-row">
            <span className="status-label">Status</span>
            <span className="status-value">
              <span className={`badge badge-${torrent.status.toLowerCase()}`}>{torrent.status}</span>
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">Uploaded</span>
            <span className="status-value">{formatBytes(torrent.uploaded)}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Downloaded</span>
            <span className="status-value">{formatBytes(torrent.downloaded)}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Ratio</span>
            <span className="status-value">{formatRatio(torrent.ratio)}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Seeders</span>
            <span className="status-value">{torrent.seeders}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Leechers</span>
            <span className="status-value">{torrent.leechers}</span>
          </div>
          {torrent.trackerUrl && (
            <div className="status-row">
              <span className="status-label">Tracker</span>
              <span className="status-value">{torrent.trackerUrl}</span>
            </div>
          )}
          <div className="status-row">
            <span className="status-label">Added</span>
            <span className="status-value">{formatDate(torrent.dateAdded)}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Last Active</span>
            <span className="status-value">{formatDate(torrent.lastActive)}</span>
          </div>
        </div>
      </div>

      <div className="torrent-detail-actions">
        {isSeeding ? (
          <button className="btn btn-danger" onClick={() => stopSeeding.mutate(torrent.id)}>
            Stop Seeding
          </button>
        ) : (
          <button className="btn btn-success" onClick={() => startSeeding.mutate(torrent.id)}>
            Start Seeding
          </button>
        )}
      </div>

      <PeerList torrentId={torrent.id} />
    </div>
  );
}

export default TorrentDetails;
