import { useState, useCallback, useRef, useEffect, useMemo } from "react";
import { useTranslation } from "../i18n";
import {
  useTorrents,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useBulkTorrentAction,
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
import MediaArtwork from "./MediaArtwork";
import type { Torrent } from "../api/types";

import {
  ALL_COLUMNS,
  COLUMN_I18N_KEYS,
  DEFAULT_VISIBLE,
  loadVisibleColumns,
  saveVisibleColumns,
  loadTableSortPreferences,
  saveTableSortPreferences,
  resetTableSortPreferences,
  loadColumnOrder,
  saveColumnOrder,
  loadColumnWidths,
  saveColumnWidths,
  SORT_KEY_STORAGE,
  SORT_ASC_STORAGE,
  ColumnKey,
  ColumnDef,
} from "../pages/torrentindex/columnPreferences";

export {
  ALL_COLUMNS,
  COLUMN_I18N_KEYS,
  DEFAULT_VISIBLE,
  loadVisibleColumns,
  saveVisibleColumns,
  loadTableSortPreferences,
  saveTableSortPreferences,
  resetTableSortPreferences,
  SORT_KEY_STORAGE,
  SORT_ASC_STORAGE,
};
export type { ColumnKey, ColumnDef };

type SortKey = ColumnKey;

const STRING_COLUMNS: ReadonlySet<ColumnKey> = new Set([
  "name",
  "status",
  "trackerUrl",
  "dateAdded",
  "lastActive",
  "creationDate",
  "createdBy",
  "comment",
  "label",
  "infoHash",
]);

interface ContextMenuState {
  x: number;
  y: number;
  torrent: Torrent | null;
  selectedTorrents?: Torrent[];
}

function getSortValue(
  t: Torrent,
  key: SortKey,
): string | number | null | undefined {
  switch (key) {
    case "#":
      return t.sortOrder ?? t.id;
    case "queuePosition":
      return t.sortOrder ?? t.id;
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
      return t.infoHash ?? "";
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
      return (t as unknown as Record<string, unknown>)[key] as
        string | number | null | undefined;
  }
}

export interface TorrentTableProps {
  torrents?: Torrent[];
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  categoryFilter?: string;
  tagFilter?: string;
  selectedTagIds?: number[] | Set<number>;
  tagMatchMode?: "AND" | "OR";
  selectedTorrentId?: number | null;
  onSelectTorrent?: (id: number | null) => void;
  selectedIds?: Set<number>;
  onToggleSelect?: (id: number) => void;
  onSelectAll?: (ids: number[]) => void;
  onSelectRange?: (ids: number[]) => void;
  onSelectMultiple?: (ids: Set<number>) => void;
  onDeleteSelected?: () => void;
  onToggleActive?: () => void;
  visibleColumns?: Set<string>;
  onToggleColumn?: (key: string) => void;
  columnOrder?: ColumnKey[];
  onColumnOrderChange?: (order: ColumnKey[]) => void;
  columnWidths?: Record<string, number>;
  onColumnWidthsChange?: (widths: Record<string, number>) => void;
  sortKey?: ColumnKey | null;
  sortAsc?: boolean;
  onSortChange?: (key: ColumnKey | null, asc: boolean) => void;
  onResetSort?: () => void;
}

function TorrentTable({
  torrents: propTorrents,
  filter,
  stateFilter,
  trackerFilter,
  categoryFilter,
  tagFilter,
  selectedTagIds,
  tagMatchMode,
  selectedTorrentId,
  onSelectTorrent,
  selectedIds,
  onToggleSelect,
  onSelectAll,
  onSelectRange,
  onSelectMultiple,
  onDeleteSelected,
  onToggleActive,
  visibleColumns: propVisibleColumns,
  onToggleColumn: propToggleColumn,
  columnOrder: propColumnOrder,
  onColumnOrderChange: propOnColumnOrderChange,
  columnWidths: propColumnWidths,
  onColumnWidthsChange: propOnColumnWidthsChange,
  sortKey: propSortKey,
  sortAsc: propSortAsc,
  onSortChange: propOnSortChange,
  onResetSort: propOnResetSort,
}: TorrentTableProps) {
  const { t } = useTranslation();
  const { data: fetchedTorrents, isLoading, isError } = useTorrents();
  const torrents = propTorrents ?? fetchedTorrents;
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const bulkAction = useBulkTorrentAction();
  const updateTorrent = useUpdateTorrent();
  const announceTorrent = useAnnounceTorrent();
  const recheckTorrent = useRecheckTorrent();
  const moveTorrentQueue = useMoveTorrentQueue();

  const [internalSortKey, setInternalSortKey] = useState<SortKey | null>(
    () => loadTableSortPreferences().sortKey,
  );
  const [internalSortAsc, setInternalSortAsc] = useState<boolean>(
    () => loadTableSortPreferences().sortAsc,
  );

  const sortKey = propSortKey !== undefined ? propSortKey : internalSortKey;
  const sortAsc = propSortAsc !== undefined ? propSortAsc : internalSortAsc;

  const handleResetSort = useCallback(() => {
    setInternalSortKey(null);
    setInternalSortAsc(true);
    resetTableSortPreferences();
    propOnResetSort?.();
    propOnSortChange?.(null, true);
  }, [propOnResetSort, propOnSortChange]);
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
  const [internalVisibleColumns, setInternalVisibleColumns] =
    useState<Set<string>>(loadVisibleColumns);
  const visibleColumns = propVisibleColumns ?? internalVisibleColumns;

  const [internalColumnOrder, setInternalColumnOrder] =
    useState<ColumnKey[]>(loadColumnOrder);
  const [internalColumnWidths, setInternalColumnWidths] =
    useState<Record<string, number>>(loadColumnWidths);

  const columnOrder = propColumnOrder ?? internalColumnOrder;
  const columnWidths = propColumnWidths ?? internalColumnWidths;

  const updateColumnOrder = useCallback(
    (next: ColumnKey[] | ((prev: ColumnKey[]) => ColumnKey[])) => {
      if (propOnColumnOrderChange) {
        const resolved = typeof next === "function" ? next(columnOrder) : next;
        propOnColumnOrderChange(resolved);
      } else {
        setInternalColumnOrder((prev) => {
          const resolved = typeof next === "function" ? next(prev) : next;
          saveColumnOrder(resolved);
          return resolved;
        });
      }
    },
    [propOnColumnOrderChange, columnOrder],
  );

  const updateColumnWidths = useCallback(
    (
      next:
        | Record<string, number>
        | ((prev: Record<string, number>) => Record<string, number>),
    ) => {
      if (propOnColumnWidthsChange) {
        const resolved =
          typeof next === "function" ? next(columnWidths) : next;
        propOnColumnWidthsChange(resolved);
      } else {
        setInternalColumnWidths((prev) => {
          const resolved = typeof next === "function" ? next(prev) : next;
          saveColumnWidths(resolved);
          return resolved;
        });
      }
    },
    [propOnColumnWidthsChange, columnWidths],
  );

  // Drag-to-reorder state
  const dragColRef = useRef<ColumnKey | null>(null);
  const dragOverColRef = useRef<ColumnKey | null>(null);
  const [dragOverKey, setDragOverKey] = useState<ColumnKey | null>(null);

  // Column resize state
  const resizeStateRef = useRef<{
    key: string;
    startX: number;
    startWidth: number;
  } | null>(null);
  const resizeListenersRef = useRef<{
    move: (e: MouseEvent) => void;
    up: (e: MouseEvent) => void;
  } | null>(null);

  useEffect(() => {
    return () => {
      if (resizeListenersRef.current) {
        document.removeEventListener(
          "mousemove",
          resizeListenersRef.current.move,
        );
        document.removeEventListener("mouseup", resizeListenersRef.current.up);
        resizeListenersRef.current = null;
      }
    };
  }, []);

  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

  const closeContextMenu = useCallback(() => setContextMenu(null), []);

  const filtered = propTorrents
    ? propTorrents
    : filterTorrents(torrents, {
        filter,
        stateFilter,
        trackerFilter,
        categoryFilter,
        tagFilter,
        selectedTagIds,
        tagMatchMode,
      });

  const sorted = useMemo(() => {
    if (!sortKey) {
      return [...filtered].sort((a, b) => {
        const orderA = a.sortOrder ?? 0;
        const orderB = b.sortOrder ?? 0;
        if (orderA !== orderB) return orderA - orderB;
        return (a.id ?? 0) - (b.id ?? 0);
      });
    }

    return [...filtered].sort((a, b) => {
      if (sortKey === "eta") {
        const parseEta = (t: Torrent): number => {
          if (t.eta == null) return 0;
          const num = typeof t.eta === "number" ? t.eta : Number(t.eta);
          return Number.isNaN(num) ? 0 : num;
        };

        const etaA = parseEta(a);
        const etaB = parseEta(b);

        if (sortAsc) {
          const valA = etaA <= 0 ? Number.POSITIVE_INFINITY : etaA;
          const valB = etaB <= 0 ? Number.POSITIVE_INFINITY : etaB;
          if (valA === valB) return 0;
          if (valA === Number.POSITIVE_INFINITY) return 1;
          if (valB === Number.POSITIVE_INFINITY) return -1;
          return valA - valB;
        } else {
          const valA = etaA <= 0 ? 0 : etaA;
          const valB = etaB <= 0 ? 0 : etaB;
          return valB - valA;
        }
      }

      const va = getSortValue(a, sortKey);
      const vb = getSortValue(b, sortKey);

      let cmp = 0;
      if (
        STRING_COLUMNS.has(sortKey) ||
        typeof va === "string" ||
        typeof vb === "string"
      ) {
        const sa = va != null ? String(va) : "";
        const sb = vb != null ? String(vb) : "";
        cmp = sa.localeCompare(sb);
      } else {
        const parseNum = (val: unknown): number => {
          if (val == null) return 0;
          const num = typeof val === "number" ? val : Number(val);
          return Number.isNaN(num) ? 0 : num;
        };
        const na = parseNum(va);
        const nb = parseNum(vb);
        cmp = na - nb;
      }

      return sortAsc ? cmp : -cmp;
    });
  }, [filtered, sortKey, sortAsc]);

  useEffect(() => {
    if (selectedTorrentId != null && sorted.length > 0) {
      const idx = sorted.findIndex((t) => t.id === selectedTorrentId);
      if (idx !== -1) {
        setFocusedIndex(idx);
        if (anchorIndex === null) {
          setAnchorIndex(idx);
        }
        rowRefs.current[idx]?.scrollIntoView({ block: "nearest" });
      }
    }
  }, [selectedTorrentId, sorted, anchorIndex]);

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
        if (deleteModalState.ids.length === 1) {
          await deleteTorrent.mutateAsync({
            id: deleteModalState.ids[0],
            deleteFiles,
          });
        } else {
          await bulkAction.mutateAsync({
            torrentIds: deleteModalState.ids,
            action: "delete",
            deleteFiles,
          });
        }
        setDeleteModalState(null);
      } catch {
        // Handled by query client
      }
    },
    [deleteModalState, deleteTorrent, bulkAction],
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
    if (propToggleColumn) {
      propToggleColumn(key);
      return;
    }
    setInternalVisibleColumns((prev) => {
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
    e.stopPropagation();
    let effectiveSelectedIds = selectedIds;
    if (torrent && selectedIds) {
      if (!selectedIds.has(torrent.id)) {
        effectiveSelectedIds = new Set([torrent.id]);
        if (onSelectAll) {
          onSelectAll([torrent.id]);
        } else if (onSelectMultiple) {
          onSelectMultiple(effectiveSelectedIds);
        }
        onSelectTorrent?.(torrent.id);
      }
    }
    const currentTorrents = sorted || [];
    const selectedTorrents = effectiveSelectedIds
      ? currentTorrents.filter((t) => effectiveSelectedIds.has(t.id))
      : torrent
        ? [torrent]
        : [];
    setContextMenu({
      x: e.clientX,
      y: e.clientY,
      torrent,
      selectedTorrents,
    });
  }

  function handleSort(key: SortKey) {
    let nextKey: SortKey | null = key;
    let nextAsc = true;

    if (sortKey === key) {
      if (sortAsc) {
        nextKey = key;
        nextAsc = false;
      } else {
        nextKey = null;
        nextAsc = true;
      }
    } else {
      nextKey = key;
      nextAsc = true;
    }

    setInternalSortKey(nextKey);
    setInternalSortAsc(nextAsc);
    saveTableSortPreferences(nextKey, nextAsc);
    propOnSortChange?.(nextKey, nextAsc);
  }

  const colDefMap = useMemo(
    () => new Map(ALL_COLUMNS.map((c) => [c.key, c])),
    [],
  );
  const columns = useMemo(() => {
    const ordered = columnOrder
      .map((k) => colDefMap.get(k))
      .filter(
        (c): c is ColumnDef => c !== undefined && visibleColumns.has(c.key),
      );
    const inOrder = new Set(ordered.map((c) => c.key));
    for (const c of ALL_COLUMNS) {
      if (visibleColumns.has(c.key) && !inOrder.has(c.key)) ordered.push(c);
    }
    return ordered;
  }, [columnOrder, visibleColumns, colDefMap]);

  // --- Column drag-to-reorder ---
  const handleColDragStart = useCallback((key: ColumnKey) => {
    dragColRef.current = key;
  }, []);

  const handleColDragOver = useCallback(
    (e: React.DragEvent, key: ColumnKey) => {
      e.preventDefault();
      dragOverColRef.current = key;
      setDragOverKey(key);
    },
    [],
  );

  const handleColDrop = useCallback(
    (e: React.DragEvent, targetKey: ColumnKey) => {
      e.preventDefault();
      const fromKey = dragColRef.current;
      if (!fromKey || fromKey === targetKey) {
        dragColRef.current = null;
        dragOverColRef.current = null;
        setDragOverKey(null);
        return;
      }

      const visibleKeys = columns.map((c) => c.key);
      const fromIdx = visibleKeys.indexOf(fromKey);
      const toIdx = visibleKeys.indexOf(targetKey);
      if (fromIdx === -1 || toIdx === -1) {
        dragColRef.current = null;
        dragOverColRef.current = null;
        setDragOverKey(null);
        return;
      }

      const newVisibleOrder = [...visibleKeys];
      const [movedKey] = newVisibleOrder.splice(fromIdx, 1);
      newVisibleOrder.splice(toIdx, 0, movedKey);

      updateColumnOrder((prev) => {
        const allKeys = ALL_COLUMNS.map((c) => c.key);
        const fullBase = [...new Set([...prev, ...allKeys])];
        const visibleSet = new Set(newVisibleOrder);
        let visibleIdx = 0;
        const next = fullBase.map((key) => {
          if (visibleSet.has(key)) {
            return newVisibleOrder[visibleIdx++];
          }
          return key;
        });
        saveColumnOrder(next);
        return next;
      });

      dragColRef.current = null;
      dragOverColRef.current = null;
      setDragOverKey(null);
    },
    [columns, updateColumnOrder],
  );

  const handleColDragEnd = useCallback(() => {
    dragColRef.current = null;
    dragOverColRef.current = null;
    setDragOverKey(null);
  }, []);

  // --- Column resize ---
  const handleResizeMouseDown = useCallback(
    (e: React.MouseEvent, key: string, thEl: HTMLElement) => {
      e.preventDefault();
      e.stopPropagation();

      if (resizeListenersRef.current) {
        document.removeEventListener(
          "mousemove",
          resizeListenersRef.current.move,
        );
        document.removeEventListener("mouseup", resizeListenersRef.current.up);
        resizeListenersRef.current = null;
      }

      const startWidth = thEl.getBoundingClientRect().width;
      resizeStateRef.current = { key, startX: e.clientX, startWidth };

      const onMouseMove = (ev: MouseEvent) => {
        if (!resizeStateRef.current) return;
        const delta = ev.clientX - resizeStateRef.current.startX;
        const newWidth = Math.max(
          48,
          resizeStateRef.current.startWidth + delta,
        );
        updateColumnWidths((prev) => ({
          ...prev,
          [resizeStateRef.current!.key]: newWidth,
        }));
      };

      const onMouseUp = () => {
        if (resizeStateRef.current) {
          updateColumnWidths((prev) => {
            saveColumnWidths(prev);
            return prev;
          });
          resizeStateRef.current = null;
        }
        document.removeEventListener("mousemove", onMouseMove);
        document.removeEventListener("mouseup", onMouseUp);
        resizeListenersRef.current = null;
      };

      resizeListenersRef.current = { move: onMouseMove, up: onMouseUp };
      document.addEventListener("mousemove", onMouseMove);
      document.addEventListener("mouseup", onMouseUp);
    },
    [updateColumnWidths],
  );

  if (isLoading) {
    return (
      <div className="torrent-table-wrapper">
        <table className="torrent-table">
          <thead>
            <tr>
              <th className="torrent-table-th" style={{ width: 36 }} />
              {columns.map((c) => (
                <th
                  key={c.key}
                  className="torrent-table-th"
                  style={{
                    width: columnWidths[c.key]
                      ? `${columnWidths[c.key]}px`
                      : undefined,
                    minWidth: columnWidths[c.key]
                      ? `${columnWidths[c.key]}px`
                      : undefined,
                    maxWidth: columnWidths[c.key]
                      ? `${columnWidths[c.key]}px`
                      : undefined,
                  }}
                >
                  {t(
                    COLUMN_I18N_KEYS[c.key] || `torrents.table.${c.key}`,
                    undefined,
                    c.label,
                  )}
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
      case "queuePosition":
        return torrent.sortOrder != null ? torrent.sortOrder + 1 : "-";
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
            {(meta?.posterUrl || torrent.posterUrl) && (
              <MediaArtwork
                src={meta?.posterUrl || torrent.posterUrl}
                alt={meta?.title || torrent.name}
                title={meta?.title || torrent.name}
                category={meta?.mediaType || torrent.source || undefined}
                width="20px"
                height="28px"
                aspectRatio="auto"
                borderRadius="2px"
                style={{ flexShrink: 0 }}
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
              {t(
                "torrents.pausedVpnKillSwitch",
                undefined,
                "Paused (VPN Kill Switch)",
              )}
            </span>
          );
        }
        return (
          <span
            className={`badge badge-${torrent.status.toLowerCase()}`}
            aria-label={ariaLabel}
          >
            {t(
              `torrents.${torrent.status.toLowerCase()}`,
              undefined,
              torrent.status === "QueuedForChecking"
                ? "Queued for Recheck"
                : torrent.status,
            )}
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
            <TrackerFavicon
              urlOrHost={torrent.trackerUrl || domain}
              size={14}
            />
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
        return torrent.uploadLimit > 0
          ? `${torrent.uploadLimit} KB/s`
          : "Global";
      case "downloadLimit":
        return torrent.downloadLimit > 0
          ? `${torrent.downloadLimit} KB/s`
          : "Global";
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
                aria-label="Select all torrents"
                checked={
                  sorted.length > 0 && selectedIds?.size === sorted.length
                }
                onChange={() => onSelectAll?.(sorted.map((t) => t.id))}
              />
            </th>
            {columns.map((col) => (
              <th
                key={col.key}
                draggable
                onDragStart={() => handleColDragStart(col.key)}
                onDragOver={(e) => handleColDragOver(e, col.key)}
                onDrop={(e) => handleColDrop(e, col.key)}
                onDragEnd={handleColDragEnd}
                onClick={() => col.sortable && handleSort(col.key)}
                className={`torrent-table-th${col.key === "#" ? " torrent-table-index" : ""}`}
                style={{
                  cursor: col.sortable ? "pointer" : "default",
                  userSelect: "none",
                  whiteSpace: "nowrap",
                  position: "relative",
                  width: columnWidths[col.key]
                    ? `${columnWidths[col.key]}px`
                    : undefined,
                  minWidth: columnWidths[col.key]
                    ? `${columnWidths[col.key]}px`
                    : undefined,
                  maxWidth: columnWidths[col.key]
                    ? `${columnWidths[col.key]}px`
                    : undefined,
                  backgroundColor:
                    dragOverKey === col.key
                      ? "rgba(200, 168, 78, 0.12)"
                      : undefined,
                  borderLeft:
                    dragOverKey === col.key
                      ? "2px solid var(--accent, #ffd166)"
                      : undefined,
                  transition: "background-color 0.1s, border-color 0.1s",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "4px",
                    overflow: "hidden",
                  }}
                >
                  <span
                    title={t(
                      "torrents.table.dragToReorder",
                      undefined,
                      "Drag to reorder",
                    )}
                    style={{
                      cursor: "grab",
                      opacity: 0.35,
                      fontSize: "0.7rem",
                      flexShrink: 0,
                      lineHeight: 1,
                    }}
                  >
                    ⠿
                  </span>
                  <span
                    style={{ overflow: "hidden", textOverflow: "ellipsis" }}
                  >
                    {t(
                      COLUMN_I18N_KEYS[col.key] || `torrents.table.${col.key}`,
                      undefined,
                      col.label,
                    )}
                  </span>
                  {sortKey === col.key && (
                    <span style={{ flexShrink: 0 }}>
                      {sortAsc ? " ▲" : " ▼"}
                    </span>
                  )}
                </div>

                <div
                  onMouseDown={(e) => {
                    const th = e.currentTarget.parentElement as HTMLElement;
                    handleResizeMouseDown(e, col.key, th);
                  }}
                  onClick={(e) => e.stopPropagation()}
                  title={t(
                    "torrents.table.dragToResize",
                    undefined,
                    "Drag to resize",
                  )}
                  style={{
                    position: "absolute",
                    top: 0,
                    right: 0,
                    width: "5px",
                    height: "100%",
                    cursor: "col-resize",
                    zIndex: 1,
                    userSelect: "none",
                  }}
                />
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sorted.map((t, index) => (
            <tr
              key={t.id}
              role="row"
              ref={(el) => {
                rowRefs.current[index] = el;
              }}
              tabIndex={0}
              aria-selected={
                selectedTorrentId === t.id ||
                (selectedIds?.has(t.id) ?? false)
              }
              className={`torrent-table-row${selectedTorrentId === t.id ? " torrent-table-row-selected" : ""}${selectedIds?.has(t.id) ? " torrent-table-row-selected" : ""}${focusedIndex === index ? " torrent-table-row-focused" : ""}`}
              onFocus={() => {
                setFocusedIndex(index);
                if (anchorIndex === null) {
                  setAnchorIndex(index);
                }
              }}
              onKeyDown={(e) => {
                if (
                  typeof HTMLInputElement !== "undefined" &&
                  e.target instanceof HTMLInputElement
                ) {
                  return;
                }
                if (e.key === " ") {
                  e.preventDefault();
                  e.stopPropagation?.();
                  onToggleSelect?.(t.id);
                } else if (e.key === "Enter") {
                  e.preventDefault();
                  e.stopPropagation?.();
                  onSelectTorrent?.(selectedTorrentId === t.id ? null : t.id);
                }
              }}
              onClick={(e) => {
                setFocusedIndex(index);
                if (
                  e.shiftKey &&
                  (anchorIndex !== null || lastClickedIndex !== null)
                ) {
                  const base =
                    anchorIndex !== null ? anchorIndex : lastClickedIndex!;
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
                  aria-label={`Select ${t.name}`}
                  checked={selectedIds?.has(t.id) ?? false}
                  onChange={() => {}}
                  onClick={(e) => {
                    e.stopPropagation();
                    setFocusedIndex(index);
                    if (
                      e.shiftKey &&
                      (anchorIndex !== null || lastClickedIndex !== null)
                    ) {
                      const base =
                        anchorIndex !== null ? anchorIndex : lastClickedIndex!;
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
          selectedTorrents={contextMenu.selectedTorrents}
          visibleColumns={visibleColumns}
          allColumns={ALL_COLUMNS}
          onClose={closeContextMenu}
          onToggleColumn={toggleColumn}
          onResetSort={handleResetSort}
          onStart={(id) => startSeeding.mutate(id)}
          onStop={(id) => stopSeeding.mutate(id)}
          onUpdate={(torrent) => updateTorrent.mutate(torrent)}
          onAnnounce={(id) => announceTorrent.mutate(id)}
          onRecheck={(id) => recheckTorrent.mutate(id)}
          onDelete={(payload) => deleteTorrent.mutate(payload)}
          onMoveQueue={(payload) => moveTorrentQueue.mutate(payload)}
          onBatchStart={(ids) => ids.forEach((id) => startSeeding.mutate(id))}
          onBatchStop={(ids) => ids.forEach((id) => stopSeeding.mutate(id))}
          onBatchAnnounce={(ids) =>
            ids.forEach((id) => announceTorrent.mutate(id))
          }
          onBatchRecheck={(ids) =>
            ids.forEach((id) => recheckTorrent.mutate(id))
          }
          onBatchDelete={(payload) => {
            if (payload.ids.length > 0) {
              setDeleteModalState({
                isOpen: true,
                ids: payload.ids,
                torrentName:
                  payload.ids.length === 1
                    ? sorted.find((t) => t.id === payload.ids[0])?.name
                    : undefined,
              });
            }
          }}
          onBatchUpdate={(torrents) =>
            torrents.forEach((tor) => updateTorrent.mutate(tor))
          }
          onBatchMoveQueue={(payload) =>
            payload.ids.forEach((id) =>
              moveTorrentQueue.mutate({ id, position: payload.position }),
            )
          }
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
          isPending={deleteTorrent.isPending || bulkAction.isPending}
          onClose={() => {
            if (!deleteTorrent.isPending && !bulkAction.isPending) setDeleteModalState(null);
          }}
          onConfirm={handleConfirmDefaultDelete}
        />
      )}
    </div>
  );
}

export default TorrentTable;
