import { usePeers } from '../../api/hooks';
import { formatBytes, formatSpeed } from '../../utils/formatters';
import { PanelLoading, PanelEmpty } from './shared';

export function PeersTab({ torrentId }: { torrentId: number }) {
  const { data: peers, isLoading, isError } = usePeers(torrentId);

  if (isLoading) return <PanelLoading>Loading peers...</PanelLoading>;
  if (isError) return <PanelEmpty>Failed to load peers.</PanelEmpty>;
  if (!peers || peers.length === 0) return <PanelEmpty>No peers connected</PanelEmpty>;

  return (
    <div className="detail-panel-table-wrap">
      <table className="torrent-table">
        <thead>
          <tr>
            <th className="torrent-table-th">Address</th>
            <th className="torrent-table-th">Client</th>
            <th className="torrent-table-th">Progress</th>
            <th className="torrent-table-th">Up Speed</th>
            <th className="torrent-table-th">Down Speed</th>
            <th className="torrent-table-th">Uploaded</th>
            <th className="torrent-table-th">Downloaded</th>
            <th className="torrent-table-th">Flags</th>
          </tr>
        </thead>
        <tbody>
          {peers.map((p) => (
            <tr key={p.id} className="torrent-table-row">
              <td className="mono">{p.ip}:{p.port}</td>
              <td>{p.client}</td>
              <td>{(p.progress * 100).toFixed(1)}%</td>
              <td>{formatSpeed(p.uploadSpeed)}</td>
              <td>{formatSpeed(p.downloadSpeed)}</td>
              <td>{formatBytes(p.uploaded)}</td>
              <td>{formatBytes(p.downloaded)}</td>
              <td className="mono">{p.flags}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
