import { useState, useMemo, useCallback, useEffect, useRef } from "react";
import { useSearchParams } from "react-router";
import {
  useTorrents,
  useTags,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useStartAllSeeding,
  useStopAllSeeding,
  useSeedingConfig,
  useSaveSeedingConfig,
} from "../../api/hooks";
import { extractTrackerDomain } from "../../utils/formatters";
import { filterTorrents } from "../../utils/filterUtils";
import { ViewMode } from "./types";
import type { Torrent, Tag } from "../../api/types";
import { trackViewModeChange } from "../../utils/analytics";

export interface TagGroupItem {
  id: number;
  label: string;
  color?: string;
  count: number;
}

function getInitialViewMode(): ViewMode {
  const stored = localStorage.getItem("seedarr-view-mode");
  return stored === "grid" ? "grid" : "table";
}

export function useTorrentIndexState() {
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: torrents } = useTorrents();
  const { data: tags } = useTags();
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const startAll = useStartAllSeeding();
  const stopAll = useStopAllSeeding();
  const { data: seedingConfig } = useSeedingConfig();
  const saveSeedingConfig = useSaveSeedingConfig();

  const [filter, setFilter] = useState(() => searchParams.get("q") || "");
  const [showAddModal, setShowAddModal] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(() => {
    const select = searchParams.get("select");
    if (select) {
      const id = parseInt(select.trim(), 10);
      if (!Number.isNaN(id) && id > 0) return new Set([id]);
    }
    return new Set();
  });
  const [viewMode, setViewMode] = useState<ViewMode>(getInitialViewMode);
  const [selectedState, setSelectedState] = useState<string>("All");
  const [selectedTracker, setSelectedTracker] = useState<string>("All");
  const [selectedCategory, setSelectedCategory] = useState<string>("All");
  const [selectedTag, setSelectedTag] = useState<string>("All");
  const [selectedTagIds, setSelectedTagIds] = useState<Set<number>>(new Set());
  const [tagMatchMode, setTagMatchMode] = useState<"AND" | "OR">("OR");

  const toggleTag = useCallback((id: number) => {
    setSelectedTagIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
    setSelectedTag("All");
  }, []);

  const clearTags = useCallback(() => {
    setSelectedTagIds(new Set());
    setSelectedTag("All");
  }, []);

  // Listen for tag deletion events and synchronize filter state
  useEffect(() => {
    const handleTagDeleted = (e: Event) => {
      const customEvent = e as CustomEvent<{ id: number; label?: string }>;
      const deletedId = customEvent.detail?.id;
      const deletedLabel = customEvent.detail?.label;

      if (deletedId !== undefined) {
        setSelectedTagIds((prev) => {
          if (prev.has(deletedId)) {
            const next = new Set(prev);
            next.delete(deletedId);
            return next;
          }
          return prev;
        });
      }

      if (deletedLabel) {
        setSelectedTag((prev) => (prev === deletedLabel ? "All" : prev));
      }
    };

    window.addEventListener("seedarr:tag-deleted", handleTagDeleted);
    return () => {
      window.removeEventListener("seedarr:tag-deleted", handleTagDeleted);
    };
  }, []);

  // Synchronize filter state when tags query data updates (e.g. tag deleted)
  useEffect(() => {
    if (!tags) return;
    const validIds = new Set(tags.map((t) => t.id));
    const validLabels = new Set(tags.map((t) => t.label));

    if (selectedTag !== "All" && selectedTag !== "Untagged") {
      if (!validLabels.has(selectedTag)) {
        setSelectedTag("All");
      }
    }

    setSelectedTagIds((prev) => {
      let hasInvalid = false;
      const next = new Set<number>();
      for (const id of prev) {
        if (validIds.has(id)) {
          next.add(id);
        } else {
          hasInvalid = true;
        }
      }
      return hasInvalid ? next : prev;
    });
  }, [tags, selectedTag]);
  const [selectedTorrentId, setSelectedTorrentId] = useState<number | null>(
    () => {
      const select = searchParams.get("select");
      if (select) {
        const id = parseInt(select.trim(), 10);
        if (!Number.isNaN(id) && id > 0) return id;
      }
      return null;
    },
  );
  const [isFilterCollapsed, setIsFilterCollapsed] = useState<boolean>(() => {
    return localStorage.getItem("seedarr_filter_collapsed") === "true";
  });
  const [isQuickControlsOpen, setIsQuickControlsOpen] = useState<boolean>(
    () => {
      return localStorage.getItem("seedarr_quick_controls_open") === "true";
    },
  );

  const pendingSelectIdRef = useRef<number | null>(
    (() => {
      const select = searchParams.get("select");
      if (select) {
        const id = parseInt(select.trim(), 10);
        if (!Number.isNaN(id) && id > 0) return id;
      }
      return null;
    })(),
  );

  const adjustFiltersForTorrent = useCallback((target: Torrent) => {
    setSelectedState((current) =>
      current !== "All" && target.status !== current ? "All" : current,
    );
    setSelectedTracker((current) => {
      if (current === "All") return current;
      const urls =
        target.trackers && target.trackers.length > 0
          ? target.trackers
          : target.trackerUrl
            ? [target.trackerUrl]
            : [];
      const hasTracker = urls.some(
        (u) => extractTrackerDomain(u) === current,
      );
      return hasTracker ? current : "All";
    });
    setSelectedCategory((current) => {
      if (current === "All") return current;
      const cat = target.category?.trim() || "Uncategorized";
      return cat === current ? current : "All";
    });
    setSelectedTag((current) => {
      if (current === "All") return current;
      const tag = target.label?.trim() || "Untagged";
      return tag === current ? current : "All";
    });
    setSelectedTagIds((current) => {
      if (current.size === 0) return current;
      const tTags = target.tagIds ?? [];
      const match =
        tagMatchMode === "AND"
          ? Array.from(current).every((id) => tTags.includes(id))
          : Array.from(current).some((id) => tTags.includes(id));
      return match ? current : new Set();
    });
    setFilter((current) => {
      if (!current) return current;
      return target.name.toLowerCase().includes(current.toLowerCase())
        ? current
        : "";
    });
  }, []);

  // When torrents load or update, ensure pending deep-linked selection is visible
  useEffect(() => {
    if (pendingSelectIdRef.current != null && torrents && torrents.length > 0) {
      const target = torrents.find((t) => t.id === pendingSelectIdRef.current);
      if (target) {
        adjustFiltersForTorrent(target);
      }
      pendingSelectIdRef.current = null;
    }
  }, [torrents, adjustFiltersForTorrent]);

  const toggleFilterCollapse = useCallback(() => {
    setIsFilterCollapsed((prev) => {
      const next = !prev;
      localStorage.setItem("seedarr_filter_collapsed", String(next));
      return next;
    });
  }, []);

  const toggleQuickControls = useCallback(() => {
    setIsQuickControlsOpen((prev) => {
      const next = !prev;
      localStorage.setItem("seedarr_quick_controls_open", String(next));
      return next;
    });
  }, []);

  const closeQuickControls = useCallback(() => {
    setIsQuickControlsOpen(false);
    localStorage.setItem("seedarr_quick_controls_open", "false");
  }, []);

  // Consume ?q= and ?select= from URL then clean them so the URL stays tidy without stripping other valid params
  useEffect(() => {
    const q = searchParams.get("q");
    const selectParam = searchParams.get("select");

    if (q === null && selectParam === null) {
      return;
    }

    const nextParams = new URLSearchParams(searchParams);
    let shouldUpdateUrl = false;

    if (q !== null) {
      setFilter(q);
      nextParams.delete("q");
      shouldUpdateUrl = true;
    }

    if (selectParam !== null) {
      const numericId = parseInt(selectParam.trim(), 10);
      if (!Number.isNaN(numericId) && numericId > 0) {
        setSelectedTorrentId(numericId);
        setSelectedIds((prev) => new Set(prev).add(numericId));

        if (torrents && torrents.length > 0) {
          const target = torrents.find((t) => t.id === numericId);
          if (target) {
            adjustFiltersForTorrent(target);
          }
          pendingSelectIdRef.current = null;
        } else {
          pendingSelectIdRef.current = numericId;
        }
      }
      nextParams.delete("select");
      shouldUpdateUrl = true;
    }

    if (shouldUpdateUrl) {
      setSearchParams(nextParams, { replace: true });
    }
  }, [searchParams, setSearchParams, torrents, adjustFiltersForTorrent]);

  const adjustSpeed = useCallback(
    (field: "maxUploadSpeedKbps" | "maxDownloadSpeedKbps", factor: number) => {
      if (!seedingConfig) return;
      const fallbacks = { maxUploadSpeedKbps: 625, maxDownloadSpeedKbps: 1250 };
      const current =
        seedingConfig[field] > 0 ? seedingConfig[field] : fallbacks[field];
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
      const urls =
        t.trackers && t.trackers.length > 0
          ? t.trackers
          : t.trackerUrl
            ? [t.trackerUrl]
            : [];
      const domains = new Set(urls.map((u) => extractTrackerDomain(u)));
      if (domains.size === 0) {
        domains.add("Unknown");
      }
      for (const domain of domains) {
        groups[domain] = (groups[domain] || 0) + 1;
      }
    }
    return Object.entries(groups).sort((a, b) => a[0].localeCompare(b[0]));
  }, [torrents]);

  const categoryGroups = useMemo(() => {
    const groups: Record<string, number> = {};
    for (const t of torrents ?? []) {
      const cat = t.category?.trim() || "Uncategorized";
      groups[cat] = (groups[cat] || 0) + 1;
    }
    return Object.entries(groups).sort((a, b) => a[0].localeCompare(b[0]));
  }, [torrents]);

  const untaggedCount = useMemo(() => {
    let count = 0;
    for (const t of torrents ?? []) {
      if (t.tagIds == null || t.tagIds.length === 0) {
        count++;
      }
    }
    return count;
  }, [torrents]);

  const tagGroups = useMemo<TagGroupItem[]>(() => {
    return (tags ?? [])
      .map((tag) => {
        const count = (torrents ?? []).reduce((acc, t) => {
          return t.tagIds?.includes(tag.id) ? acc + 1 : acc;
        }, 0);
        return {
          id: tag.id,
          label: tag.label,
          color: tag.color,
          count,
        };
      })
      .sort((a, b) => a.label.localeCompare(b.label));
  }, [tags, torrents]);

  const filteredTorrents = useMemo(() => {
    return filterTorrents(torrents, {
      filter,
      stateFilter: selectedState,
      trackerFilter: selectedTracker,
      categoryFilter: selectedCategory,
      tagFilter: selectedTag,
      selectedTagIds,
      tagMatchMode,
    });
  }, [
    torrents,
    filter,
    selectedState,
    selectedTracker,
    selectedCategory,
    selectedTag,
    selectedTagIds,
    tagMatchMode,
  ]);

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
    trackViewModeChange(mode);
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

  const anchorIndexRef = useRef<number | null>(null);

  const setAnchorIndex = useCallback((idx: number | null) => {
    anchorIndexRef.current = idx;
  }, []);

  function handleSelectRange(rangeIds: number[]) {
    // If a single target ID is provided from keyboard navigation
    if (rangeIds.length === 1 && filteredTorrents.length > 0) {
      const targetId = rangeIds[0];
      const targetIdx = filteredTorrents.findIndex((t) => t.id === targetId);
      if (targetIdx !== -1) {
        if (anchorIndexRef.current === null) {
          const currIdx =
            selectedTorrentId != null
              ? filteredTorrents.findIndex((t) => t.id === selectedTorrentId)
              : -1;
          anchorIndexRef.current = currIdx !== -1 ? currIdx : targetIdx;
        }
        const start = Math.min(anchorIndexRef.current, targetIdx);
        const end = Math.max(anchorIndexRef.current, targetIdx);
        const nextIds = filteredTorrents.slice(start, end + 1).map((t) => t.id);
        setSelectedIds(new Set(nextIds));
        return;
      }
    }

    // When rangeIds is explicitly passed (e.g. from table with anchor or contraction)
    setSelectedIds(new Set(rangeIds));
  }

  return {
    torrents,
    filteredTorrents,
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
    selectedIds,
    setSelectedIds,
    viewMode,
    selectedState,
    setSelectedState,
    selectedTracker,
    setSelectedTracker,
    selectedCategory,
    setSelectedCategory,
    selectedTag,
    setSelectedTag,
    tags,
    selectedTagIds,
    setSelectedTagIds,
    toggleTag,
    clearTags,
    tagMatchMode,
    setTagMatchMode,
    untaggedCount,
    selectedTorrentId,
    setSelectedTorrentId,
    adjustSpeed,
    stateCounts,
    trackerGroups,
    categoryGroups,
    tagGroups,
    totalUploadSpeed,
    totalDownloadSpeed,
    handleViewMode,
    handleToggleSelect,
    handleSelectAll,
    handleSelectRange,
    setAnchorIndex,
    isFilterCollapsed,
    toggleFilterCollapse,
    isQuickControlsOpen,
    toggleQuickControls,
    closeQuickControls,
  };
}
