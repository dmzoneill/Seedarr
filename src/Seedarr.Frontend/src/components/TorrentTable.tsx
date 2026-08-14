import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  useTorrents,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
} from '../api/hooks';
import { formatBytes, formatRatio, formatDate } from '../utils/formatters';
import { SkeletonTableRow } from './Skeleton';
import type { Torrent } from '../api/types';

type SortKey = 'name' | 'totalSize' | 'status' | 'uploaded' | 'ratio' | 'dateAdded';

interface TorrentTableProps {
  filter?: string;
}

function TorrentTable({ filter }: TorrentTableProps) {
  const { data: torrents, isLoading } = useTorrents();
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const [sortKey, setSortKey] = useState<SortKey>('name');
  const [sortAsc, setSortAsc] = useState(true);

  const columns: { key: SortKey; label: string }[] = [
    { key: 'name', label: 'Name' },
    { key: 'totalSize', label: 'Size' },
    { key: 'status', label: 'Status' },
    { key: 'uploaded', label: 'Uploaded' },
    { key: 'ratio', label: 'Ratio' },
    { key: 'dateAdded', label: 'Added' },
  ];

  if (isLoading) {
    return (
      <div className="torrent-table-wrapper">
        <table className="torrent-table">
          <thead>
            <tr>
              {columns.map((col) => (
                <th key={col.key} className="torrent-table-th">{col.label}</th>
              ))}
              <th className="torrent-table-th">Actions</th>
            </tr>
          </thead>
          <tbody>
            {[0, 1, 2, 3, 4].map((i) => (
              <SkeletonTableRow key={i} columns={7} />
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  const filtered = (torrents ?? []).filter(
    (t) => !filter || t.name.toLowerCase().includes(filter.toLowerCase())
  );

  const sorted = [...filtered].sort((a, b) => {
    const va = a[sortKey];
    const vb = b[sortKey];
    const cmp =
      typeof va === 'string' && typeof vb === 'string'
        ? va.localeCompare(vb)
        : Number(va) - Number(vb);
    return sortAsc ? cmp : -cmp;
  });

  function handleSort(key: SortKey) {
    if (sortKey === key) {
      setSortAsc(!sortAsc);
    } else {
      setSortKey(key);
      setSortAsc(true);
    }
  }

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
              deleteTorrent.mutate(torrent.id);
            }
          }}
        >
          Delete
        </button>
      </div>
    );
  }

  return (
    <div className="torrent-table-wrapper">
      <table className="torrent-table">
        <thead>
          <tr>
            {columns.map((col) => (
              <th
                key={col.key}
                onClick={() => handleSort(col.key)}
                className="torrent-table-th"
              >
                {col.label}
                {sortKey === col.key && (sortAsc ? ' ▲' : ' ▼')}
              </th>
            ))}
            <th className="torrent-table-th">Actions</th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((t) => (
            <tr key={t.id} className="torrent-table-row">
              <td>
                <Link to={`/torrents/${t.id}`} className="torrent-link">
                  {t.name}
                </Link>
              </td>
              <td>{formatBytes(t.totalSize)}</td>
              <td>{statusBadge(t.status)}</td>
              <td>{formatBytes(t.uploaded)}</td>
              <td>{formatRatio(t.ratio)}</td>
              <td>{formatDate(t.dateAdded)}</td>
              <td>{renderActions(t)}</td>
            </tr>
          ))}
          {sorted.length === 0 && (
            <tr>
              <td colSpan={7} className="torrent-table-empty">
                No torrents found
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

export default TorrentTable;
