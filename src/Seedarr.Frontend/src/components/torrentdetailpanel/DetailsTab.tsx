import { formatBytes, formatDate } from '../../utils/formatters';
import type { Torrent } from '../../api/types';
import { InfoRow } from './shared';

export function DetailsTab({ torrent }: { torrent: Torrent }) {
  const rows: [string, string][] = [
    ['Name', torrent.name],
    ['Info Hash', torrent.infoHash],
    ['Total Size', formatBytes(torrent.totalSize)],
    ['Pieces', `${torrent.pieceCount} x ${formatBytes(torrent.pieceLength)}`],
    ['Private', torrent.isPrivate ? 'Yes' : 'No'],
    ['Tracker', torrent.trackerUrl ?? '-'],
  ];
  if (torrent.creationDate) rows.push(['Created', formatDate(torrent.creationDate)]);
  if (torrent.createdBy) rows.push(['Created By', torrent.createdBy]);
  if (torrent.comment) rows.push(['Comment', torrent.comment]);
  if (torrent.sourcePath) rows.push(['Source Path', torrent.sourcePath]);

  return (
    <div className="detail-panel-grid">
      {rows.map(([label, value]) => (
        <InfoRow key={label} label={label} value={value} mono />
      ))}
    </div>
  );
}
