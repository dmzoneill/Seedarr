import { formatDate } from '../../utils/formatters';
import type { Torrent } from '../../api/types';
import { InfoRow } from './shared';

export function LogTab({ torrent }: { torrent: Torrent }) {
  return (
    <div className="detail-panel-grid">
      <InfoRow label="Added" value={formatDate(torrent.dateAdded)} />
      {torrent.lastActive && (
        <InfoRow label="Last Active" value={`${formatDate(torrent.lastActive)} (${torrent.status})`} />
      )}
    </div>
  );
}
