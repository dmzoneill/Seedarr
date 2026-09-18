import { useState, useCallback, useRef, useEffect } from "react";
import { useTranslation } from "../i18n";
import {
  useTorrents,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useUpdateTorrent,
  useAnnounceTorrent,
  useRecheckTorrent,
  useMoveTorrentQueue,
  useDownloadHistory,
  useArrConnections,
} from "../api/hooks";
import {
  formatBytes,
  formatSpeed,
  formatRatio,
  formatDate,
  formatSeconds,
  formatEta,
  extractTrackerDomain,
} from "../utils/formatters";
import { getMediaDeepLink } from "../utils/arrLinks";
import { getTorrentBadges } from "../utils/milestones";
import { filterTorrents } from "../utils/filterUtils";
import { SkeletonTableRow } from "./Skeleton";
import TorrentContextMenu from "./TorrentContextMenu";
import AddTorrentModal from "./AddTorrentModal";
import DeleteTorrentModal from "./DeleteTorrentModal";
import TrackerFavicon from "./TrackerFavicon";
import type { Torrent } from "../api/types";

type ColumnKey =
  | "#"
  | "name"
  | "status"
  | "totalSize"
  | "uploaded"
  | "downloaded"
  | "ratio"
  | "progress"
  | "seeders"
  | "leechers"
  | "trackerUrl"
  | "dateAdded"
  | "lastActive"
  | "pieceCount"
  | "pieceLength"
  | "comment"
  | "createdBy"
  | "creationDate"
  | "isPrivate"
  | "infoHash"
  | "priority"
  | "uploadLimit"
  | "downloadLimit"
  | "superSeeding"
  | "forceStart"
  | "label"
  | "sequentialDownload"
  | "announceInterval"
  | "nextUpdate"
  | "sessionUploaded"
  | "sessionDownloaded"
  | "uploadSpeed"
  | "downloadSpeed"
  | "active"
  | "availability"
  | "eta"
  | "threshold"
  | "smallTorrentLimit";

interface ColumnDef {
  key: ColumnKey;
  label: string;
  sortable: boolean;
}

export const COLUMN_I18N_KEYS: Record<ColumnKey, string> = {
  "#": "torrents.table.index",
  name: "torrents.table.name",
  status: "torrents.table.status",
  progress: "torrents.table.progress",
  totalSize: "torrents.table.size",
  uploaded: "torrents.table.uploaded",
  downloaded: "torrents.table.downloaded",
  sessionUploaded: "torrents.table.sessionUploaded",
  sessionDownloaded: "torrents.table.sessionDownloaded",
  uploadSpeed: "torrents.table.uploadSpeed",
  downloadSpeed: "torrents.table.downloadSpeed",
  ratio: "torrents.table.ratio",
  seeders: "torrents.table.seeders",
  leechers: "torrents.table.leechers",
  trackerUrl: "torrents.table.tracker",
  announceInterval: "torrents.table.announceInterval",
  nextUpdate: "torrents.table.nextUpdate",
  priority: "torrents.table.priority",
  label: "torrents.table.label",
  active: "torrents.table.active",
  uploadLimit: "torrents.table.uploadLimit",
  downloadLimit: "torrents.table.downloadLimit",
  superSeeding: "torrents.table.superSeeding",
  sequentialDownload: "torrents.table.sequentialDownload",
  forceStart: "torrents.table.forceStart",
  availability: "torrents.table.availability",
  eta: "torrents.table.eta",
  threshold: "torrents.table.threshold",
  smallTorrentLimit: "torrents.table.smallTorrentLimit",
  dateAdded: "torrents.table.added",
  lastActive: "torrents.table.lastActive",
  creationDate: "torrents.table.creationDate",
  createdBy: "torrents.table.createdBy",
  comment: "torrents.table.comment",
  pieceCount: "torrents.table.pieceCount",
  pieceLength: "torrents.table.pieceLength",
  isPrivate: "torrents.table.isPrivate",
  infoHash: "torrents.table.infoHash",
};

const ALL_COLUMNS: ColumnDef[] = [
  { key: "#", label: "#", sortable: true },
  { key: "name", label: "Name", sortable: true },
  { key: "status", label: "Status", sortable: true },
  { key: "progress", label: "Progress", sortable: true },
  { key: "totalSize", label: "Size", sortable: true },
  { key: "uploaded", label: "Total Uploaded", sortable: true },
  { key: "downloaded", label: "Total Downloaded", sortable: true },
  { key: "sessionUploaded", label: "Session Uploaded", sortable: true },
  { key: "sessionDownloaded", label: "Session Downloaded", sortable: true },
  { key: "uploadSpeed", label: "Upload Speed", sortable: true },
  { key: "downloadSpeed", label: "Download Speed", sortable: true },
  { key: "ratio", label: "Ratio", sortable: true },
  { key: "seeders", label: "Seeders", sortable: true },
  { key: "leechers", label: "Leechers", sortable: true },
  { key: "trackerUrl", label: "Tracker", sortable: true },
  { key: "announceInterval", label: "Announce Interval", sortable: true },
  { key: "nextUpdate", label: "Next Update", sortable: true },
  { key: "priority", label: "Priority", sortable: true },
  { key: "label", label: "Label", sortable: true },
  { key: "active", label: "Active", sortable: true },
  { key: "uploadLimit", label: "Upload Limit", sortable: true },
  { key: "downloadLimit", label: "Download Limit", sortable: true },
  { key: "superSeeding", label: "Super Seeding", sortable: true },
  { key: "sequentialDownload", label: "Sequential", sortable: true },
  { key: "forceStart", label: "Force Start", sortable: true },
  { key: "availability", label: "Availability", sortable: true },
  { key: "eta", label: "ETA", sortable: true },
  { key: "threshold", label: "Threshold", sortable: true },
  { key: "smallTorrentLimit", label: "Small Torrent Limit", sortable: true },
  { key: "dateAdded", label: "Added", sortable: true },
  { key: "lastActive", label: "Last Active", sortable: true },
  { key: "creationDate", label: "Created", sortable: true },
  { key: "createdBy", label: "Created By", sortable: true },
  { key: "comment", label: "Comment", sortable: true },
  { key: "pieceCount", label: "Pieces", sortable: true },
  { key: "pieceLength", label: "Piece Length", sortable: true },
  { key: "isPrivate", label: "Private", sortable: true },
  { key: "infoHash", label: "Info Hash", sortable: true },
];

const STORAGE_KEY = "seedarr-visible-columns-v2";

const DEFAULT_VISIBLE: Set<string> = new Set([
  "#",
  "name",
  "status",
  "totalSize",
  "uploaded",
  "ratio",
  "progress",
  "uploadSpeed",
  "downloadSpeed",
  "seeders",
  "leechers",
]);

function loadVisibleColumns(): Set<string> {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      const parsed = JSON.parse(stored) as string[];
      if (Array.isArray(parsed) && parsed.length > 0) return new Set(parsed);
    }
  } catch (err) {
    console.warn("Failed to parse localStorage:", err);
  }
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

function getSortValue(t: Torrent, key: SortKey): string | number {
  switch (key) {
    case "#":
      return t.id;
    case "trackerUrl":
      return t.trackerUrl ?? "";
    case "lastActive":
      return t.lastActive ?? "";
    case "creationDate":
      return t.creationDate ?? "";
    case "comment":
      return t.comment ?? "";
    case "createdBy":
      return t.createdBy ?? "";
    case "label":
      return t.label ?? "";
    case "infoHash":
      return t.infoHash;
    case "isPrivate":
      return t.isPrivate ? 1 : 0;
    case "superSeeding":
      return t.superSeeding ? 1 : 0;
    case "sequentialDownload":
      return t.sequentialDownload ? 1 : 0;
    case "forceStart":
      return t.forceStart ? 1 : 0;
    case "active":
      return t.active ? 1 : 0;
    default:
      return t[key] as string | number;
  }
}

interface TorrentTableProps {
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  categoryFilter?: string;
  tagFilter?: string;
  selectedTorrentId?: number | null;
  onSelectTorrent?: (id: number | null) => void;
  selectedIds?: Set<number>;
  onToggleSelect?: (id: number) => void;
  onSelectAll?: (ids: number[]) => void;
  onSelectRange?: (ids: number[]) => void;
  onSelectMultiple?: (ids: Set<number>) => void;
  onDeleteSelected?: () => void;
  onToggleActive?: () => void;
}

function TorrentTable({
  filter,
  stateFilter,
  trackerFilter,
  categoryFilter,
  tagFilter,
  selectedTorrentId,
  onSelectTorrent,
  selectedIds,
  onToggleSelect,
  onSelectAll,
  onSelectRange,
  onSelectMultiple,
  onDeleteSelected,
  onToggleActive,
}: TorrentTableProps) {
  const { t } = useTranslation();
  const { data: torrents, isLoading, isError } = useTorrents();
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const updateTorrent = useUpdateTorrent();
  const announceTorrent = useAnnounceTorrent();
  const recheckTorrent = useRecheckTorrent();
  const moveTorrentQueue = useMoveTorrentQueue();
  const [sortKey, setSortKey] = useState<SortKey>("name");
  const [sortAsc, setSortAsc] = useState(true);
  const [lastClickedIndex, setLastClickedIndex] = useState<number | null>(null);
  const [focusedIndex, setFocusedIndex] = useState<number>(0);
  const [anchorIndex, setAnchorIndex] = useState<number | null>(null);
  const rowRefs = useRef<(HTMLTableRowElement | null)[]>([]);
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [searchModalQuery, setSearchModalQuery] = useState<string | null>(null);
  const [deleteModalState, setDeleteModalState] = useState<{
    isOpen: boolean;
    ids: number[];
    torrentName?: string;
  } | null>(null);
  const [visibleColumns, setVisibleColumns] =
    useState<Set<string>>(loadVisibleColumns);

  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

  const closeContextMenu = useCallback(() => setContextMenu(null), []);

  const filtered = filterTorrents(torrents, {
    filter,
    stateFilter,
    trackerFilter,
    categoryFilter,
    tagFilter,
  });

  const sorted = [...filtered].sort((a, b) => {
    const va = getSortValue(a, sortKey);
    const vb = getSortValue(b, sortKey);
    const cmp =
      typeof va === "string" && typeof vb === "string"
        ? va.localeCompare(vb)
        : Number(va) - Number(vb);
    return sortAsc ? cmp : -cmp;
  });

  useEffect(() => {
    if (selectedTorrentId != null && sorted.length > 0) {
      const idx = sorted.findIndex((t) => t.id === selectedTorrentId);
      if (idx !== -1) {
        setFocusedIndex(idx);
        if (anchorIndex === null) {
          setAnchorIndex(idx);
        }
      }
    }
  }, [selectedTorrentId, sorted]);

  useEffect(() => {
    if (sorted.length > 0 && focusedIndex >= sorted.length) {
      setFocusedIndex(sorted.length - 1);
    }
  }, [sorted.length, focusedIndex]);

  const moveFocus = useCallback(
    (targetIndex: number, isShift: boolean) => {
      if (sorted.length === 0) return;
      const nextIndex = Math.max(0, Math.min(sorted.length - 1, targetIndex));
      setFocusedIndex(nextIndex);

      const rowEl = rowRefs.current[nextIndex];
      if (rowEl) {
        rowEl.focus();
        rowEl.scrollIntoView({ block: "nearest" });
      }

      if (isShift) {
        const effectiveAnchor =
          anchorIndex !== null ? anchorIndex : focusedIndex;
        if (anchorIndex === null) {
          setAnchorIndex(focusedIndex);
        }
        const start = Math.min(effectiveAnchor, nextIndex);
        const end = Math.max(effectiveAnchor, nextIndex);
        const rangeIds = sorted.slice(start, end + 1).map((item) => item.id);
        if (onSelectMultiple) {
          onSelectMultiple(new Set(rangeIds));
        } else if (onSelectRange) {
          onSelectRange(rangeIds);
        }
        onSelectTorrent?.(sorted[nextIndex].id);
      } else {
        setAnchorIndex(nextIndex);
        setLastClickedIndex(nextIndex);
        if (onSelectMultiple) {
          onSelectMultiple(new Set([sorted[nextIndex].id]));
        } else if (onSelectRange) {
          onSelectRange([sorted[nextIndex].id]);
        }
        onSelectTorrent?.(sorted[nextIndex].id);
      }
    },
    [
      sorted,
      anchorIndex,
      focusedIndex,
      onSelectMultiple,
      onSelectRange,
      onSelectTorrent,
    ],
  );

  const handleDefaultToggleActive = useCallback(() => {
    if (selectedIds && selectedIds.size > 0) {
      const selectedTorrents = sorted.filter((t) => selectedIds.has(t.id));
      const anyActive = selectedTorrents.some(
        (t) => t.status === "Seeding" || t.active,
      );
      for (const t of selectedTorrents) {
        if (anyActive) {
          stopSeeding.mutate(t.id);
        } else {
          startSeeding.mutate(t.id);
        }
      }
    } else {
      const curr =
        sorted[focusedIndex] ??
        (selectedTorrentId != null
          ? sorted.find((t) => t.id === selectedTorrentId)
          : null);
      if (curr) {
        if (curr.status === "Seeding" || curr.active) {
          stopSeeding.mutate(curr.id);
        } else {
          startSeeding.mutate(curr.id);
        }
      }
    }
  }, [
    selectedIds,
    sorted,
    focusedIndex,
    selectedTorrentId,
    startSeeding,
    stopSeeding,
  ]);

  const handleDefaultDeleteSelected = useCallback(() => {
    const ids =
      selectedIds && selectedIds.size > 0
        ? Array.from(selectedIds)
        : selectedTorrentId != null
          ? [selectedTorrentId]
          : sorted[focusedIndex]
            ? [sorted[focusedIndex].id]
            : [];
    if (ids.length === 0) return;
    const torrentName =
      ids.length === 1 ? sorted.find((t) => t.id === ids[0])?.name : undefined;
    setDeleteModalState({
      isOpen: true,
      ids,
      torrentName,
    });
  }, [selectedIds, selectedTorrentId, sorted, focusedIndex]);

  const handleConfirmDefaultDelete = useCallback(
    async (deleteFiles: boolean) => {
      if (!deleteModalState || deleteModalState.ids.length === 0) return;
      try {
        for (const id of deleteModalState.ids) {
          await deleteTorrent.mutateAsync({ id, deleteFiles });
        }
        setDeleteModalState(null);
      } catch {
        // Handled by query client
      }
    },
    [deleteModalState, deleteTorrent],
  );

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (
        e.target instanceof HTMLInputElement ||
        e.target instanceof HTMLTextAreaElement ||
        e.target instanceof HTMLSelectElement ||
        (e.target as HTMLElement)?.isContentEditable
      ) {
        return;
      }

      if (sorted.length === 0) return;

      if (e.key === "ArrowDown") {
        e.preventDefault();
        moveFocus(focusedIndex + 1, e.shiftKey);
        return;
      }

      if (e.key === "ArrowUp") {
        e.preventDefault();
        moveFocus(focusedIndex - 1, e.shiftKey);
        return;
      }

      if (e.key === "Home") {
        e.preventDefault();
        moveFocus(0, e.shiftKey);
        return;
      }

      if (e.key === "End") {
        e.preventDefault();
        moveFocus(sorted.length - 1, e.shiftKey);
        return;
      }

      if ((e.ctrlKey || e.metaKey) && (e.key === "a" || e.key === "A")) {
        e.preventDefault();
        const allIds = sorted.map((t) => t.id);
        if (onSelectMultiple) {
          onSelectMultiple(new Set(allIds));
        } else if (onSelectAll) {
          onSelectAll(allIds);
        }
        return;
      }

      if (e.key === "Enter") {
        e.preventDefault();
        const curr = sorted[focusedIndex];
        if (curr) {
          onSelectTorrent?.(curr.id);
        }
        return;
      }

      if (e.key === " ") {
        e.preventDefault();
        if (onToggleActive) {
          onToggleActive();
        } else {
          handleDefaultToggleActive();
        }
        return;
      }

      if (e.key === "Delete" || e.key === "Backspace") {
        e.preventDefault();
        if (onDeleteSelected) {
          onDeleteSelected();
        } else {
          handleDefaultDeleteSelected();
        }
        return;
      }
    },
    [
      sorted,
      focusedIndex,
      moveFocus,
      onSelectMultiple,
      onSelectAll,
      onSelectTorrent,
      onToggleActive,
      handleDefaultToggleActive,
      onDeleteSelected,
      handleDefaultDeleteSelected,
    ],
  );

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

  function handleSort(key: SortKey) {
    if (sortKey === key) setSortAsc(!sortAsc);
    else {
      setSortKey(key);
      setSortAsc(true);
    }
  }

  const columns = ALL_COLUMNS.filter((col) => visibleColumns.has(col.key));

  if (isLoading) {
    return (
      <div className="torrent-table-wrapper">
        <table className="torrent-table">
          <thead>
            <tr>
              <th className="torrent-table-th" style={{ width: 36 }} />
              {columns.map((c) => (
                <th key={c.key} className="torrent-table-th">
                  {t(COLUMN_I18N_KEYS[c.key] || `torrents.table.${c.key}`, undefined, c.label)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {[0, 1, 2, 3, 4].map((i) => (
              <SkeletonTableRow key={i} columns={columns.length} />
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  if (isError) {
    return <p className="error">Failed to load data.</p>;
  }

  const priorityLabel = (p: number) =>
    p === 2 ? "High" : p === 1 ? "Normal" : "Low";

  function renderCell(torrent: Torrent, key: ColumnKey, index: number) {
    switch (key) {
      case "#":
        return index + 1;
      case "name": {
        const historyMatch = history?.find(
          (h) =>
            (torrent.infoHash &&
              h.infoHash?.toLowerCase() === torrent.infoHash.toLowerCase()) ||
            h.title?.toLowerCase() === torrent.name?.toLowerCase(),
        );
        const meta = historyMatch?.metadata;
        const arrLink = historyMatch
          ? getMediaDeepLink(historyMatch, arrConnections)
          : null;

        const badges = getTorrentBadges(torrent);

        return (
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              minWidth: 0,
            }}
          >
            {meta?.posterUrl && (
              <img
                src={meta.posterUrl}
                alt=""
                style={{
                  width: "20px",
                  height: "28px",
                  objectFit: "cover",
                  borderRadius: "2px",
                  flexShrink: 0,
                }}
              />
            )}
            <span
              style={{
                fontWeight: 500,
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap",
              }}
            >
              {meta?.title || torrent.name} {meta?.year ? `(${meta.year})` : ""}
            </span>
            {badges.map((b, i) => (
              <span
                key={i}
                title={b.title}
                style={{
                  fontSize: "0.75rem",
                  cursor: "help",
                  display: "inline-flex",
                  alignItems: "center",
                }}
              >
                {b.icon}
              </span>
            ))}
            {arrLink && (
              <a
                href={arrLink.url}
                target="_blank"
                rel="noopener noreferrer"
                className="badge badge-secondary"
                style={{
                  fontSize: "0.65rem",
                  padding: "0.05rem 0.35rem",
                  textDecoration: "none",
                  color: "inherit",
                  flexShrink: 0,
                }}
                title={arrLink.label}
                onClick={(e) => e.stopPropagation()}
              >
                {arrLink.appName} ↗
              </a>
            )}
          </div>
        );
      }
      case "status": {
        const pct = Math.min((torrent.progress ?? 0) * 100, 100);
        const ariaLabel = `${torrent.status}: ${pct.toFixed(1)}% complete, Down: ${formatSpeed(torrent.downloadSpeed)}, Up: ${formatSpeed(torrent.uploadSpeed)}, ETA: ${formatEta(torrent.eta)}`;
        if (torrent.isVpnPaused) {
          return (
            <span className="badge badge-vpn-paused" aria-label={ariaLabel}>
              {t("torrents.pausedVpnKillSwitch", undefined, "Paused (VPN Kill Switch)")}
            </span>
          );
        }
        return (
          <span
            className={`badge badge-${torrent.status.toLowerCase()}`}
            aria-label={ariaLabel}
          >
            {t(`torrents.${torrent.status.toLowerCase()}`, undefined, torrent.status === "QueuedForChecking" ? "Queued for Recheck" : torrent.status)}
          </span>
        );
      }
      case "totalSize":
        return formatBytes(torrent.totalSize);
      case "uploaded":
        return formatBytes(torrent.uploaded);
      case "downloaded":
        return formatBytes(torrent.downloaded);
      case "sessionUploaded":
        return formatBytes(torrent.sessionUploaded);
      case "sessionDownloaded":
        return formatBytes(torrent.sessionDownloaded);
      case "uploadSpeed":
        return formatSpeed(torrent.uploadSpeed);
      case "downloadSpeed":
        return formatSpeed(torrent.downloadSpeed);
      case "ratio":
        return (
          <span
            className={`badge ${torrent.ratio >= 2.0 ? "badge-success" : torrent.ratio >= 1.0 ? "badge-primary" : "badge-secondary"}`}
          >
            {formatRatio(torrent.ratio)}
          </span>
        );
      case "progress": {
        const pct = Math.min(torrent.progress * 100, 100);
        return (
          <div
            className="torrent-progress"
            role="progressbar"
            aria-valuenow={Math.round(pct)}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuetext={`${pct.toFixed(1)}% downloaded, ratio ${formatRatio(torrent.ratio)}`}
          >
            <div
              className="torrent-progress-fill"
              style={{ width: `${pct}%` }}
            />
            <span className="torrent-progress-text">
              {pct.toFixed(1)}% ({formatRatio(torrent.ratio)})
            </span>
          </div>
        );
      }
      case "seeders":
        return torrent.seeders;
      case "leechers":
        return torrent.leechers;
      case "trackerUrl": {
        const domain = extractTrackerDomain(torrent.trackerUrl);
        return (
          <div
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.35rem",
            }}
          >
            <TrackerFavicon urlOrHost={torrent.trackerUrl || domain} size={14} />
            <span>{domain}</span>
          </div>
        );
      }
      case "announceInterval":
        return formatSeconds(torrent.announceInterval);
      case "nextUpdate":
        return formatSeconds(torrent.nextUpdate);
      case "dateAdded":
        return formatDate(torrent.dateAdded);
      case "lastActive":
        return formatDate(torrent.lastActive);
      case "creationDate":
        return formatDate(torrent.creationDate);
      case "pieceCount":
        return torrent.pieceCount.toLocaleString();
      case "pieceLength":
        return formatBytes(torrent.pieceLength);
      case "comment":
        return torrent.comment ?? "-";
      case "createdBy":
        return torrent.createdBy ?? "-";
      case "isPrivate":
        return torrent.isPrivate ? "Yes" : "No";
      case "infoHash":
        return (
          <span className="mono" style={{ fontSize: "0.75rem" }}>
            {torrent.infoHash}
          </span>
        );
      case "priority":
        return priorityLabel(torrent.priority);
      case "uploadLimit":
        return torrent.uploadLimit > 0 ? `${torrent.uploadLimit} KB/s` : "Global";
      case "downloadLimit":
        return torrent.downloadLimit > 0 ? `${torrent.downloadLimit} KB/s` : "Global";
      case "superSeeding":
        return torrent.superSeeding ? "Yes" : "No";
      case "sequentialDownload":
        return torrent.sequentialDownload ? "Yes" : "No";
      case "forceStart":
        return torrent.forceStart ? "Yes" : "No";
      case "label":
        return torrent.label ?? "-";
      case "active":
        return torrent.active ? "Yes" : "No";
      case "availability":
        return torrent.availability.toFixed(2);
      case "eta":
        return formatSeconds(torrent.eta);
      case "threshold":
        return `${torrent.threshold}%`;
      case "smallTorrentLimit":
        return formatBytes(torrent.smallTorrentLimit);
      default:
        return null;
    }
  }

  return (
    <div
      className="torrent-table-wrapper"
      tabIndex={0}
      onKeyDown={handleKeyDown}
      onFocus={(e) => {
        if (e.target === e.currentTarget && sorted.length > 0) {
          const idx = Math.min(Math.max(0, focusedIndex), sorted.length - 1);
          rowRefs.current[idx]?.focus();
        }
      }}
    >
      <table className="torrent-table">
        <thead onContextMenu={(e) => handleContextMenu(e, null)}>
          <tr>
            <th className="torrent-table-th" style={{ width: 36 }}>
              <input
                type="checkbox"
                className="torrent-checkbox"
                checked={
                  sorted.length > 0 && selectedIds?.size === sorted.length
                }
                onChange={() => onSelectAll?.(sorted.map((t) => t.id))}
              />
            </th>
            {columns.map((col) => (
              <th
                key={col.key}
                onClick={() => col.sortable && handleSort(col.key)}
                className={`torrent-table-th${col.key === "#" ? " torrent-table-index" : ""}`}
              >
                {t(COLUMN_I18N_KEYS[col.key] || `torrents.table.${col.key}`, undefined, col.label)}
                {sortKey === col.key && (sortAsc ? " ▲" : " ▼")}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sorted.map((t, index) => (
            <tr
              key={t.id}
              ref={(el) => {
                rowRefs.current[index] = el;
              }}
              tabIndex={focusedIndex === index ? 0 : -1}
              className={`torrent-table-row${selectedTorrentId === t.id ? " torrent-table-row-selected" : ""}${selectedIds?.has(t.id) ? " torrent-table-row-selected" : ""}${focusedIndex === index ? " torrent-table-row-focused" : ""}`}
              onFocus={() => {
                setFocusedIndex(index);
                if (anchorIndex === null) {
                  setAnchorIndex(index);
                }
              }}
              onClick={(e) => {
                setFocusedIndex(index);
                if (e.shiftKey && (anchorIndex !== null || lastClickedIndex !== null)) {
                  const base = anchorIndex !== null ? anchorIndex : lastClickedIndex!;
                  const start = Math.min(base, index);
                  const end = Math.max(base, index);
                  const rangeIds = sorted
                    .slice(start, end + 1)
                    .map((item) => item.id);
                  if (onSelectMultiple) {
                    onSelectMultiple(new Set(rangeIds));
                  } else if (onSelectRange) {
                    onSelectRange(rangeIds);
                  }
                  onSelectTorrent?.(t.id);
                } else if (e.ctrlKey || e.metaKey) {
                  setAnchorIndex(index);
                  onToggleSelect?.(t.id);
                  onSelectTorrent?.(t.id);
                  setLastClickedIndex(index);
                } else {
                  setAnchorIndex(index);
                  onSelectTorrent?.(selectedTorrentId === t.id ? null : t.id);
                  setLastClickedIndex(index);
                }
              }}
              onContextMenu={(e) => handleContextMenu(e, t)}
            >
              <td>
                <input
                  type="checkbox"
                  className="torrent-checkbox"
                  checked={selectedIds?.has(t.id) ?? false}
                  onChange={() => {}}
                  onClick={(e) => {
                    e.stopPropagation();
                    setFocusedIndex(index);
                    if (e.shiftKey && (anchorIndex !== null || lastClickedIndex !== null)) {
                      const base = anchorIndex !== null ? anchorIndex : lastClickedIndex!;
                      const start = Math.min(base, index);
                      const end = Math.max(base, index);
                      const rangeIds = sorted
                        .slice(start, end + 1)
                        .map((item) => item.id);
                      if (onSelectMultiple) {
                        onSelectMultiple(new Set(rangeIds));
                      } else if (onSelectRange) {
                        onSelectRange(rangeIds);
                      }
                    } else {
                      setAnchorIndex(index);
                      onToggleSelect?.(t.id);
                      setLastClickedIndex(index);
                    }
                  }}
                />
              </td>
              {columns.map((col) => (
                <td
                  key={col.key}
                  className={
                    col.key === "#" ? "torrent-table-index" : undefined
                  }
                >
                  {renderCell(t, col.key, index)}
                </td>
              ))}
            </tr>
          ))}
          {sorted.length === 0 && (
            <tr>
              <td colSpan={columns.length + 1} className="torrent-table-empty">
                {t("torrents.noTorrents", undefined, "No torrents found")}
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {contextMenu && (
        <TorrentContextMenu
          x={contextMenu.x}
          y={contextMenu.y}
          torrent={contextMenu.torrent}
          visibleColumns={visibleColumns}
          allColumns={ALL_COLUMNS}
          onClose={closeContextMenu}
          onToggleColumn={toggleColumn}
          onStart={(id) => startSeeding.mutate(id)}
          onStop={(id) => stopSeeding.mutate(id)}
          onUpdate={(torrent) => updateTorrent.mutate(torrent)}
          onAnnounce={(id) => announceTorrent.mutate(id)}
          onRecheck={(id) => recheckTorrent.mutate(id)}
          onDelete={(payload) => deleteTorrent.mutate(payload)}
          onMoveQueue={(payload) => moveTorrentQueue.mutate(payload)}
          onSearchIndexers={(q) => setSearchModalQuery(q)}
        />
      )}

      {searchModalQuery && (
        <AddTorrentModal
          initialMode="search"
          initialQuery={searchModalQuery}
          onClose={() => setSearchModalQuery(null)}
        />
      )}

      {deleteModalState?.isOpen && (
        <DeleteTorrentModal
          isOpen={deleteModalState.isOpen}
          count={deleteModalState.ids.length}
          torrentName={deleteModalState.torrentName}
          isPending={deleteTorrent.isPending}
          onClose={() => {
            if (!deleteTorrent.isPending) setDeleteModalState(null);
          }}
          onConfirm={handleConfirmDefaultDelete}
        />
      )}
    </div>
  );
}

export default TorrentTable;
