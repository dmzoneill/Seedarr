import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import {
  useTorrents,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useUpdateTorrent,
  useAnnounceTorrent,
  useRecheckTorrent,
  useMoveTorrentQueue,
} from '../api/hooks';
import { formatBytes, formatSpeed, formatRatio, formatDate, formatSeconds, extractTrackerDomain } from '../utils/formatters';
import { SkeletonTableRow } from './Skeleton';
import type { Torrent } from '../api/types';

type ColumnKey =
  | 'name' | 'status' | 'totalSize' | 'uploaded' | 'downloaded' | 'ratio'
  | 'progress' | 'seeders' | 'leechers' | 'trackerUrl' | 'dateAdded' | 'lastActive'
  | 'pieceCount' | 'pieceLength' | 'comment' | 'createdBy' | 'creationDate' | 'isPrivate'
  | 'infoHash' | 'priority' | 'uploadLimit' | 'downloadLimit' | 'superSeeding'
  | 'forceStart' | 'label' | 'sequentialDownload' | 'announceInterval' | 'nextUpdate'
  | 'sessionUploaded' | 'sessionDownloaded' | 'uploadSpeed' | 'downloadSpeed'
  | 'active' | 'availability' | 'eta' | 'threshold' | 'smallTorrentLimit';

interface ColumnDef {
  key: ColumnKey;
  label: string;
  sortable: boolean;
}

const ALL_COLUMNS: ColumnDef[] = [
  { key: 'name', label: 'Name', sortable: true },
  { key: 'status', label: 'Status', sortable: true },
  { key: 'progress', label: 'Progress', sortable: true },
  { key: 'totalSize', label: 'Size', sortable: true },
  { key: 'uploaded', label: 'Total Uploaded', sortable: true },
  { key: 'downloaded', label: 'Total Downloaded', sortable: true },
  { key: 'sessionUploaded', label: 'Session Uploaded', sortable: true },
  { key: 'sessionDownloaded', label: 'Session Downloaded', sortable: true },
  { key: 'uploadSpeed', label: 'Upload Speed', sortable: true },
  { key: 'downloadSpeed', label: 'Download Speed', sortable: true },
  { key: 'ratio', label: 'Ratio', sortable: true },
  { key: 'seeders', label: 'Seeders', sortable: true },
  { key: 'leechers', label: 'Leechers', sortable: true },
  { key: 'trackerUrl', label: 'Tracker', sortable: true },
  { key: 'announceInterval', label: 'Announce Interval', sortable: true },
  { key: 'nextUpdate', label: 'Next Update', sortable: true },
  { key: 'priority', label: 'Priority', sortable: true },
  { key: 'label', label: 'Label', sortable: true },
  { key: 'active', label: 'Active', sortable: true },
  { key: 'uploadLimit', label: 'Upload Limit', sortable: true },
  { key: 'downloadLimit', label: 'Download Limit', sortable: true },
  { key: 'superSeeding', label: 'Super Seeding', sortable: true },
  { key: 'sequentialDownload', label: 'Sequential', sortable: true },
  { key: 'forceStart', label: 'Force Start', sortable: true },
  { key: 'availability', label: 'Availability', sortable: true },
  { key: 'eta', label: 'ETA', sortable: true },
  { key: 'threshold', label: 'Threshold', sortable: true },
  { key: 'smallTorrentLimit', label: 'Small Torrent Limit', sortable: true },
  { key: 'dateAdded', label: 'Added', sortable: true },
  { key: 'lastActive', label: 'Last Active', sortable: true },
  { key: 'creationDate', label: 'Created', sortable: true },
  { key: 'createdBy', label: 'Created By', sortable: true },
  { key: 'comment', label: 'Comment', sortable: true },
  { key: 'pieceCount', label: 'Pieces', sortable: true },
  { key: 'pieceLength', label: 'Piece Length', sortable: true },
  { key: 'isPrivate', label: 'Private', sortable: true },
  { key: 'infoHash', label: 'Info Hash', sortable: true },
];

const STORAGE_KEY = 'seedarr-visible-columns';

const DEFAULT_VISIBLE: Set<string> = new Set([
  'name', 'status', 'totalSize', 'uploaded', 'ratio', 'progress',
  'uploadSpeed', 'downloadSpeed', 'seeders', 'leechers',
]);

function loadVisibleColumns(): Set<string> {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      const parsed = JSON.parse(stored) as string[];
      if (Array.isArray(parsed) && parsed.length > 0) return new Set(parsed);
    }
  } catch { /* ignore */ }
  return new Set(DEFAULT_VISIBLE);
}

function saveVisibleColumns(cols: Set<string>) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify([...cols]));
}

type SortKey = ColumnKey;

interface ContextMenuState {
  x: number;
  y: number;
  torrent: Torrent | null;
}

interface TorrentTableProps {
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  selectedTorrentId?: number | null;
  onSelectTorrent?: (id: number | null) => void;
  selectMode?: boolean;
  selectedIds?: Set<number>;
  onToggleSelect?: (id: number) => void;
  onSelectAll?: (ids: number[]) => void;
}

function TorrentTable({ filter, stateFilter, trackerFilter, selectedTorrentId, onSelectTorrent, selectMode, selectedIds, onToggleSelect, onSelectAll }: TorrentTableProps) {
  const { data: torrents, isLoading } = useTorrents();
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const updateTorrent = useUpdateTorrent();
  const announceTorrent = useAnnounceTorrent();
  const recheckTorrent = useRecheckTorrent();
  const moveTorrentQueue = useMoveTorrentQueue();
  const [sortKey, setSortKey] = useState<SortKey>('name');
  const [sortAsc, setSortAsc] = useState(true);
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [openSubmenu, setOpenSubmenu] = useState<string | null>(null);
  const [visibleColumns, setVisibleColumns] = useState<Set<string>>(loadVisibleColumns);

  const closeContextMenu = useCallback(() => {
    setContextMenu(null);
    setOpenSubmenu(null);
  }, []);

  useEffect(() => {
    if (!contextMenu) return;
    const handleClick = () => closeContextMenu();
    const handleKeyDown = (e: KeyboardEvent) => { if (e.key === 'Escape') closeContextMenu(); };
    document.addEventListener('click', handleClick);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('click', handleClick);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [contextMenu, closeContextMenu]);

  function toggleColumn(key: string) {
    setVisibleColumns((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        if (next.size > 1) next.delete(key);
      } else {
        next.add(key);
      }
      saveVisibleColumns(next);
      return next;
    });
  }

  function handleContextMenu(e: React.MouseEvent, torrent: Torrent | null) {
    e.preventDefault();
    setContextMenu({ x: e.clientX, y: e.clientY, torrent });
  }

  function handleCopy(text: string) {
    navigator.clipboard.writeText(text);
    closeContextMenu();
  }

  function buildMagnetLink(t: Torrent): string {
    let magnet = `magnet:?xt=urn:btih:${t.infoHash}&dn=${encodeURIComponent(t.name)}`;
    if (t.trackerUrl) magnet += `&tr=${encodeURIComponent(t.trackerUrl)}`;
    return magnet;
  }

  const columns = ALL_COLUMNS.filter((col) => visibleColumns.has(col.key));

  if (isLoading) {
    return (
      <div className="torrent-table-wrapper">
        <table className="torrent-table">
          <thead><tr>{columns.map((c) => <th key={c.key} className="torrent-table-th">{c.label}</th>)}</tr></thead>
          <tbody>{[0,1,2,3,4].map((i) => <SkeletonTableRow key={i} columns={columns.length} />)}</tbody>
        </table>
      </div>
    );
  }

  const filtered = (torrents ?? []).filter((t) => {
    if (filter && !t.name.toLowerCase().includes(filter.toLowerCase())) return false;
    if (stateFilter && stateFilter !== 'All' && t.status !== stateFilter) return false;
    if (trackerFilter && trackerFilter !== 'All' && extractTrackerDomain(t.trackerUrl) !== trackerFilter) return false;
    return true;
  });

  function getSortValue(t: Torrent, key: SortKey): string | number {
    switch (key) {
      case 'trackerUrl': return t.trackerUrl ?? '';
      case 'lastActive': return t.lastActive ?? '';
      case 'creationDate': return t.creationDate ?? '';
      case 'comment': return t.comment ?? '';
      case 'createdBy': return t.createdBy ?? '';
      case 'label': return t.label ?? '';
      case 'infoHash': return t.infoHash;
      case 'isPrivate': return t.isPrivate ? 1 : 0;
      case 'superSeeding': return t.superSeeding ? 1 : 0;
      case 'sequentialDownload': return t.sequentialDownload ? 1 : 0;
      case 'forceStart': return t.forceStart ? 1 : 0;
      case 'active': return t.active ? 1 : 0;
      default: return t[key] as string | number;
    }
  }

  const sorted = [...filtered].sort((a, b) => {
    const va = getSortValue(a, sortKey);
    const vb = getSortValue(b, sortKey);
    const cmp = typeof va === 'string' && typeof vb === 'string' ? va.localeCompare(vb) : Number(va) - Number(vb);
    return sortAsc ? cmp : -cmp;
  });

  function handleSort(key: SortKey) {
    if (sortKey === key) setSortAsc(!sortAsc);
    else { setSortKey(key); setSortAsc(true); }
  }

  const priorityLabel = (p: number) => p === 2 ? 'High' : p === 1 ? 'Normal' : 'Low';

  function renderCell(t: Torrent, key: ColumnKey) {
    switch (key) {
      case 'name': return <Link to={`/torrents/${t.id}`} className="torrent-link">{t.name}</Link>;
      case 'status': return <span className={`badge badge-${t.status.toLowerCase()}`}>{t.status}</span>;
      case 'totalSize': return formatBytes(t.totalSize);
      case 'uploaded': return formatBytes(t.uploaded);
      case 'downloaded': return formatBytes(t.downloaded);
      case 'sessionUploaded': return formatBytes(t.sessionUploaded);
      case 'sessionDownloaded': return formatBytes(t.sessionDownloaded);
      case 'uploadSpeed': return formatSpeed(t.uploadSpeed);
      case 'downloadSpeed': return formatSpeed(t.downloadSpeed);
      case 'ratio': return formatRatio(t.ratio);
      case 'progress': {
        const pct = Math.min(t.progress * 100, 100);
        return (
          <div className="torrent-progress">
            <div className="torrent-progress-fill" style={{ width: `${pct}%` }} />
            <span className="torrent-progress-text">{pct.toFixed(1)}% ({formatRatio(t.ratio)})</span>
          </div>
        );
      }
      case 'seeders': return t.seeders;
      case 'leechers': return t.leechers;
      case 'trackerUrl': return extractTrackerDomain(t.trackerUrl);
      case 'announceInterval': return formatSeconds(t.announceInterval);
      case 'nextUpdate': return formatSeconds(t.nextUpdate);
      case 'dateAdded': return formatDate(t.dateAdded);
      case 'lastActive': return formatDate(t.lastActive);
      case 'creationDate': return formatDate(t.creationDate);
      case 'pieceCount': return t.pieceCount.toLocaleString();
      case 'pieceLength': return formatBytes(t.pieceLength);
      case 'comment': return t.comment ?? '-';
      case 'createdBy': return t.createdBy ?? '-';
      case 'isPrivate': return t.isPrivate ? 'Yes' : 'No';
      case 'infoHash': return <span className="mono" style={{ fontSize: '0.75rem' }}>{t.infoHash}</span>;
      case 'priority': return priorityLabel(t.priority);
      case 'uploadLimit': return t.uploadLimit > 0 ? `${t.uploadLimit} KB/s` : 'Unlimited';
      case 'downloadLimit': return t.downloadLimit > 0 ? `${t.downloadLimit} KB/s` : 'Unlimited';
      case 'superSeeding': return t.superSeeding ? 'Yes' : 'No';
      case 'sequentialDownload': return t.sequentialDownload ? 'Yes' : 'No';
      case 'forceStart': return t.forceStart ? 'Yes' : 'No';
      case 'label': return t.label ?? '-';
      case 'active': return t.active ? 'Yes' : 'No';
      case 'availability': return t.availability.toFixed(2);
      case 'eta': return formatSeconds(t.eta);
      case 'threshold': return `${t.threshold}%`;
      case 'smallTorrentLimit': return formatBytes(t.smallTorrentLimit);
      default: return null;
    }
  }

  const ct = contextMenu?.torrent;

  return (
    <div className="torrent-table-wrapper">
      <table className="torrent-table">
        <thead onContextMenu={(e) => handleContextMenu(e, null)}>
          <tr>
            {selectMode && (
              <th className="torrent-table-th" style={{ width: 36 }}>
                <input
                  type="checkbox"
                  checked={sorted.length > 0 && selectedIds?.size === sorted.length}
                  onChange={() => onSelectAll?.(sorted.map((t) => t.id))}
                />
              </th>
            )}
            {columns.map((col) => (
              <th key={col.key} onClick={() => col.sortable && handleSort(col.key)} className="torrent-table-th">
                {col.label}{sortKey === col.key && (sortAsc ? ' ▲' : ' ▼')}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sorted.map((t) => (
            <tr
              key={t.id}
              className={`torrent-table-row${selectedTorrentId === t.id ? ' torrent-table-row-selected' : ''}${selectMode && selectedIds?.has(t.id) ? ' torrent-table-row-selected' : ''}`}
              onClick={() => selectMode ? onToggleSelect?.(t.id) : onSelectTorrent?.(selectedTorrentId === t.id ? null : t.id)}
              onContextMenu={(e) => handleContextMenu(e, t)}
            >
              {selectMode && (
                <td>
                  <input
                    type="checkbox"
                    checked={selectedIds?.has(t.id) ?? false}
                    onChange={() => onToggleSelect?.(t.id)}
                    onClick={(e) => e.stopPropagation()}
                  />
                </td>
              )}
              {columns.map((col) => <td key={col.key}>{renderCell(t, col.key)}</td>)}
            </tr>
          ))}
          {sorted.length === 0 && (
            <tr><td colSpan={columns.length + (selectMode ? 1 : 0)} className="torrent-table-empty">No torrents found</td></tr>
          )}
        </tbody>
      </table>

      {contextMenu && (
        <div className="context-menu" style={{ left: contextMenu.x, top: contextMenu.y }} onClick={(e) => e.stopPropagation()}>
          {ct ? (
            <>
              {/* Pause / Resume */}
              {ct.active ? (
                <button className="context-menu-item" onClick={() => { stopSeeding.mutate(ct.id); closeContextMenu(); }}>Pause</button>
              ) : (
                <button className="context-menu-item" onClick={() => { startSeeding.mutate(ct.id); closeContextMenu(); }}>Resume</button>
              )}
              <button className="context-menu-item" onClick={() => { updateTorrent.mutate({ ...ct, forceStart: !ct.forceStart }); closeContextMenu(); }}>
                {ct.forceStart ? '✓ ' : ''}Force Start
              </button>
              <button className="context-menu-item" onClick={() => { announceTorrent.mutate(ct.id); closeContextMenu(); }}>Update Tracker</button>
              <button className="context-menu-item" onClick={() => { recheckTorrent.mutate(ct.id); closeContextMenu(); }}>Force Recheck</button>
              {ct.progress < 1.0 && (
                <button className="context-menu-item" onClick={() => { updateTorrent.mutate({ ...ct, progress: 1.0 }); closeContextMenu(); }}>Force Complete</button>
              )}

              <div className="context-menu-separator" />

              {/* Copy submenu */}
              <div className="context-menu-item context-menu-submenu-trigger" onMouseEnter={() => setOpenSubmenu('copy')} onMouseLeave={() => setOpenSubmenu(null)}>
                Copy ▶
                {openSubmenu === 'copy' && (
                  <div className="context-menu context-menu-submenu">
                    <button className="context-menu-item" onClick={() => handleCopy(ct.name)}>Name</button>
                    <button className="context-menu-item" onClick={() => handleCopy(ct.infoHash)}>Info Hash</button>
                    <button className="context-menu-item" onClick={() => handleCopy(buildMagnetLink(ct))}>Magnet Link</button>
                    <button className="context-menu-item" onClick={() => handleCopy(ct.trackerUrl ?? '')}>Tracker URL</button>
                  </div>
                )}
              </div>

              {/* Priority submenu */}
              <div className="context-menu-item context-menu-submenu-trigger" onMouseEnter={() => setOpenSubmenu('priority')} onMouseLeave={() => setOpenSubmenu(null)}>
                Priority ▶
                {openSubmenu === 'priority' && (
                  <div className="context-menu context-menu-submenu">
                    <button className="context-menu-item" onClick={() => { updateTorrent.mutate({ ...ct, priority: 2 }); closeContextMenu(); }}>{ct.priority === 2 ? '✓ ' : ''}High</button>
                    <button className="context-menu-item" onClick={() => { updateTorrent.mutate({ ...ct, priority: 1 }); closeContextMenu(); }}>{ct.priority === 1 ? '✓ ' : ''}Normal</button>
                    <button className="context-menu-item" onClick={() => { updateTorrent.mutate({ ...ct, priority: 0 }); closeContextMenu(); }}>{ct.priority === 0 ? '✓ ' : ''}Low</button>
                  </div>
                )}
              </div>

              {/* Speed Limits submenu */}
              <div className="context-menu-item context-menu-submenu-trigger" onMouseEnter={() => setOpenSubmenu('speed')} onMouseLeave={() => setOpenSubmenu(null)}>
                Speed Limits ▶
                {openSubmenu === 'speed' && (
                  <div className="context-menu context-menu-submenu">
                    <button className="context-menu-item" onClick={() => { const v = window.prompt('Upload limit (KB/s, 0=unlimited):', String(ct.uploadLimit)); if (v !== null) updateTorrent.mutate({ ...ct, uploadLimit: parseInt(v, 10) || 0 }); closeContextMenu(); }}>Set Upload Limit...</button>
                    <button className="context-menu-item" onClick={() => { const v = window.prompt('Download limit (KB/s, 0=unlimited):', String(ct.downloadLimit)); if (v !== null) updateTorrent.mutate({ ...ct, downloadLimit: parseInt(v, 10) || 0 }); closeContextMenu(); }}>Set Download Limit...</button>
                    <button className="context-menu-item" onClick={() => { updateTorrent.mutate({ ...ct, uploadLimit: 0, downloadLimit: 0 }); closeContextMenu(); }}>Reset to Global Limits</button>
                  </div>
                )}
              </div>

              {/* Queue submenu */}
              <div className="context-menu-item context-menu-submenu-trigger" onMouseEnter={() => setOpenSubmenu('queue')} onMouseLeave={() => setOpenSubmenu(null)}>
                Queue ▶
                {openSubmenu === 'queue' && (
                  <div className="context-menu context-menu-submenu">
                    <button className="context-menu-item" onClick={() => { moveTorrentQueue.mutate({ id: ct.id, position: 'top' }); closeContextMenu(); }}>Top</button>
                    <button className="context-menu-item" onClick={() => { moveTorrentQueue.mutate({ id: ct.id, position: 'up' }); closeContextMenu(); }}>Up</button>
                    <button className="context-menu-item" onClick={() => { moveTorrentQueue.mutate({ id: ct.id, position: 'down' }); closeContextMenu(); }}>Down</button>
                    <button className="context-menu-item" onClick={() => { moveTorrentQueue.mutate({ id: ct.id, position: 'bottom' }); closeContextMenu(); }}>Bottom</button>
                  </div>
                )}
              </div>

              <div className="context-menu-separator" />

              {/* Rename / Label / Toggles */}
              <button className="context-menu-item" onClick={() => { const n = window.prompt('Rename torrent:', ct.name); if (n !== null && n.trim()) updateTorrent.mutate({ ...ct, name: n.trim() }); closeContextMenu(); }}>Rename...</button>
              <button className="context-menu-item" onClick={() => { const l = window.prompt('Set label:', ct.label ?? ''); if (l !== null) updateTorrent.mutate({ ...ct, label: l || null }); closeContextMenu(); }}>
                Set Label...{ct.label ? ` (${ct.label})` : ''}
              </button>

              <div className="context-menu-separator" />

              <button className="context-menu-item" onClick={() => { updateTorrent.mutate({ ...ct, superSeeding: !ct.superSeeding }); closeContextMenu(); }}>
                {ct.superSeeding ? 'Disable' : 'Enable'} Super Seeding
              </button>
              <button className="context-menu-item" onClick={() => { updateTorrent.mutate({ ...ct, sequentialDownload: !ct.sequentialDownload }); closeContextMenu(); }}>
                {ct.sequentialDownload ? 'Disable' : 'Enable'} Sequential Download
              </button>

              <div className="context-menu-separator" />

              {/* Remove submenu */}
              <div className="context-menu-item context-menu-submenu-trigger" onMouseEnter={() => setOpenSubmenu('remove')} onMouseLeave={() => setOpenSubmenu(null)}>
                Remove ▶
                {openSubmenu === 'remove' && (
                  <div className="context-menu context-menu-submenu">
                    <button className="context-menu-item context-menu-item-danger" onClick={() => { if (confirm(`Remove "${ct.name}"?`)) deleteTorrent.mutate({ id: ct.id }); closeContextMenu(); }}>Remove Torrent</button>
                    <button className="context-menu-item context-menu-item-danger" onClick={() => { if (confirm(`Remove "${ct.name}" and all data?`)) deleteTorrent.mutate({ id: ct.id, deleteFiles: true }); closeContextMenu(); }}>Remove Torrent and Data</button>
                  </div>
                )}
              </div>

              <div className="context-menu-separator" />
            </>
          ) : null}

          {/* Columns section - always shown */}
          <div className="context-menu-item context-menu-submenu-trigger" onMouseEnter={() => setOpenSubmenu('columns')} onMouseLeave={() => setOpenSubmenu(null)}>
            Columns ▶
            {openSubmenu === 'columns' && (
              <div className="context-menu context-menu-submenu context-menu-columns">
                {ALL_COLUMNS.map((col) => (
                  <label key={col.key} className="column-menu-item">
                    <input type="checkbox" checked={visibleColumns.has(col.key)} onChange={() => toggleColumn(col.key)} />
                    {col.label}
                  </label>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

export default TorrentTable;
