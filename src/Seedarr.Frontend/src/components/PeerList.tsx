import { usePeers } from '../api/hooks';
import { formatBytes, formatSpeed } from '../utils/formatters';

interface PeerListProps {
  torrentId: number;
}

function PeerList({ torrentId }: PeerListProps) {
  const { data: peers, isLoading } = usePeers(torrentId);

  if (isLoading) return <p className="loading">Loading peers...</p>;

  return (
    <div className="card">
      <h3>Peers</h3>
      {!peers || peers.length === 0 ? (
        <p className="peer-table-empty">No peers connected</p>
      ) : (
        <div className="peer-table-wrapper">
          <table className="peer-table">
            <thead>
              <tr>
                <th className="peer-table-th">IP:Port</th>
                <th className="peer-table-th">Client</th>
                <th className="peer-table-th">Progress</th>
                <th className="peer-table-th">Upload Speed</th>
                <th className="peer-table-th">Download Speed</th>
                <th className="peer-table-th">Uploaded</th>
                <th className="peer-table-th">Downloaded</th>
                <th className="peer-table-th">Flags</th>
              </tr>
            </thead>
            <tbody>
              {peers.map((peer) => (
                <tr key={peer.id} className="peer-table-row">
                  <td className="mono">{peer.ip}:{peer.port}</td>
                  <td>{peer.client}</td>
                  <td>
                    <div className="peer-progress">
                      <div
                        className="peer-progress-bar"
                        style={{ width: `${(peer.progress * 100).toFixed(1)}%` }}
                      />
                      <span className="peer-progress-text">
                        {(peer.progress * 100).toFixed(1)}%
                      </span>
                    </div>
                  </td>
                  <td>{formatSpeed(peer.uploadSpeed)}</td>
                  <td>{formatSpeed(peer.downloadSpeed)}</td>
                  <td>{formatBytes(peer.uploaded)}</td>
                  <td>{formatBytes(peer.downloaded)}</td>
                  <td className="mono">{peer.flags}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export default PeerList;
