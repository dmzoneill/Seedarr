import { useState, useMemo, useCallback, useEffect } from "react";
import { useSearchParams } from "react-router";
import {
  useTorrents,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useStartAllSeeding,
  useStopAllSeeding,
  useSeedingConfig,
  useSaveSeedingConfig,
} from "../../api/hooks";
import { extractTrackerDomain } from "../../utils/formatters";
import { ViewMode } from "./types";

function getInitialViewMode(): ViewMode {
  const stored = localStorage.getItem("seedarr-view-mode");
  return stored === "grid" ? "grid" : "table";
}

export function useTorrentIndexState() {
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: torrents } = useTorrents();
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const startAll = useStartAllSeeding();
  const stopAll = useStopAllSeeding();
  const { data: seedingConfig } = useSeedingConfig();
  const saveSeedingConfig = useSaveSeedingConfig();

  const [filter, setFilter] = useState(() => searchParams.get("q") || "");
  const [showAddModal, setShowAddModal] = useState(false);
  const [selectMode, setSelectMode] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [viewMode, setViewMode] = useState<ViewMode>(getInitialViewMode);
  const [selectedState, setSelectedState] = useState<string>("All");
  const [selectedTracker, setSelectedTracker] = useState<string>("All");
  const [selectedTorrentId, setSelectedTorrentId] = useState<number | null>(
    null,
  );

  // Consume ?q= from URL then clean it so the URL stays tidy
  useEffect(() => {
    const q = searchParams.get("q");
    if (q) {
      setFilter(q);
      setSearchParams({}, { replace: true });
    }
  }, [searchParams, setSearchParams]);

  const adjustSpeed = useCallback(
    (field: "maxUploadSpeedKbps" | "maxDownloadSpeedKbps", factor: number) => {
      if (!seedingConfig) return;
      const current = seedingConfig[field];
      if (current === 0) return; // 0 means unlimited; adjusting would silently cap to 1 KB/s
      saveSeedingConfig.mutate({
        ...seedingConfig,
        [field]: Math.max(1, Math.round(current * factor)),
      });
    },
    [seedingConfig, saveSeedingConfig],
  );

  const stateCounts = useMemo(() => {
    const all = torrents ?? [];
    const counts: Record<string, number> = {
      All: all.length,
      Seeding: 0,
      Stopped: 0,
      Queued: 0,
      Error: 0,
    };
    for (const t of all) {
      if (t.status in counts) counts[t.status]++;
    }
    return counts;
  }, [torrents]);

  const trackerGroups = useMemo(() => {
    const groups: Record<string, number> = {};
    for (const t of torrents ?? []) {
      const domain = extractTrackerDomain(t.trackerUrl);
      groups[domain] = (groups[domain] || 0) + 1;
    }
    return Object.entries(groups).sort((a, b) => a[0].localeCompare(b[0]));
  }, [torrents]);

  const { totalUploadSpeed, totalDownloadSpeed } = useMemo(() => {
    let ul = 0;
    let dl = 0;
    for (const t of torrents ?? []) {
      ul += t.uploadSpeed ?? 0;
      dl += t.downloadSpeed ?? 0;
    }
    return { totalUploadSpeed: ul, totalDownloadSpeed: dl };
  }, [torrents]);

  function handleViewMode(mode: ViewMode) {
    setViewMode(mode);
    localStorage.setItem("seedarr-view-mode", mode);
  }

  function handleToggleSelect(id: number) {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function handleSelectAll(ids: number[]) {
    setSelectedIds((prev) =>
      prev.size === ids.length ? new Set() : new Set(ids),
    );
  }

  return {
    torrents,
    startSeeding,
    stopSeeding,
    deleteTorrent,
    startAll,
    stopAll,
    seedingConfig,
    filter,
    setFilter,
    showAddModal,
    setShowAddModal,
    selectMode,
    setSelectMode,
    selectedIds,
    setSelectedIds,
    viewMode,
    selectedState,
    setSelectedState,
    selectedTracker,
    setSelectedTracker,
    selectedTorrentId,
    setSelectedTorrentId,
    adjustSpeed,
    stateCounts,
    trackerGroups,
    totalUploadSpeed,
    totalDownloadSpeed,
    handleViewMode,
    handleToggleSelect,
    handleSelectAll,
  };
}
