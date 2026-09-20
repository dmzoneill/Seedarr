import { useState, useCallback, useEffect } from "react";

export type ColumnKey =
  | "#"
  | "queuePosition"
  | "name"
  | "status"
  | "progress"
  | "totalSize"
  | "uploaded"
  | "downloaded"
  | "ratio"
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

export type ColumnCategory =
  | "basic"
  | "transfer"
  | "swarm"
  | "activity"
  | "metadata";

export interface ColumnDef {
  key: ColumnKey;
  label: string;
  sortable: boolean;
  category: ColumnCategory;
}

export interface ColumnCategoryDef {
  id: ColumnCategory;
  label: string;
  i18nKey: string;
}

export const COLUMN_CATEGORIES: ColumnCategoryDef[] = [
  {
    id: "basic",
    label: "Basic Info",
    i18nKey: "torrents.columnCustomizer.categories.basic",
  },
  {
    id: "transfer",
    label: "Transfer & Speeds",
    i18nKey: "torrents.columnCustomizer.categories.transfer",
  },
  {
    id: "swarm",
    label: "Swarm & Peers",
    i18nKey: "torrents.columnCustomizer.categories.swarm",
  },
  {
    id: "activity",
    label: "Activity & Settings",
    i18nKey: "torrents.columnCustomizer.categories.activity",
  },
  {
    id: "metadata",
    label: "Dates & Details",
    i18nKey: "torrents.columnCustomizer.categories.metadata",
  },
];

export const COLUMN_I18N_KEYS: Record<ColumnKey, string> = {
  "#": "torrents.table.index",
  queuePosition: "torrents.table.queuePosition",
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

export const ALL_COLUMNS: ColumnDef[] = [
  // Basic Info
  { key: "#", label: "#", sortable: true, category: "basic" },
  { key: "queuePosition", label: "Queue #", sortable: true, category: "basic" },
  { key: "name", label: "Name", sortable: true, category: "basic" },
  { key: "status", label: "Status", sortable: true, category: "basic" },
  { key: "progress", label: "Progress", sortable: true, category: "basic" },
  { key: "totalSize", label: "Size", sortable: true, category: "basic" },
  { key: "label", label: "Label", sortable: true, category: "basic" },
  { key: "priority", label: "Priority", sortable: true, category: "basic" },

  // Transfer & Speeds
  { key: "uploaded", label: "Total Uploaded", sortable: true, category: "transfer" },
  { key: "downloaded", label: "Total Downloaded", sortable: true, category: "transfer" },
  { key: "sessionUploaded", label: "Session Uploaded", sortable: true, category: "transfer" },
  { key: "sessionDownloaded", label: "Session Downloaded", sortable: true, category: "transfer" },
  { key: "uploadSpeed", label: "Upload Speed", sortable: true, category: "transfer" },
  { key: "downloadSpeed", label: "Download Speed", sortable: true, category: "transfer" },
  { key: "ratio", label: "Ratio", sortable: true, category: "transfer" },
  { key: "eta", label: "ETA", sortable: true, category: "transfer" },
  { key: "uploadLimit", label: "Upload Limit", sortable: true, category: "transfer" },
  { key: "downloadLimit", label: "Download Limit", sortable: true, category: "transfer" },

  // Swarm & Peers
  { key: "seeders", label: "Seeders", sortable: true, category: "swarm" },
  { key: "leechers", label: "Leechers", sortable: true, category: "swarm" },
  { key: "trackerUrl", label: "Tracker", sortable: true, category: "swarm" },
  { key: "announceInterval", label: "Announce Interval", sortable: true, category: "swarm" },
  { key: "nextUpdate", label: "Next Update", sortable: true, category: "swarm" },
  { key: "availability", label: "Availability", sortable: true, category: "swarm" },

  // Activity & Settings
  { key: "active", label: "Active", sortable: true, category: "activity" },
  { key: "superSeeding", label: "Super Seeding", sortable: true, category: "activity" },
  { key: "sequentialDownload", label: "Sequential", sortable: true, category: "activity" },
  { key: "forceStart", label: "Force Start", sortable: true, category: "activity" },
  { key: "threshold", label: "Threshold", sortable: true, category: "activity" },
  { key: "smallTorrentLimit", label: "Small Torrent Limit", sortable: true, category: "activity" },

  // Dates & Details
  { key: "dateAdded", label: "Added", sortable: true, category: "metadata" },
  { key: "lastActive", label: "Last Active", sortable: true, category: "metadata" },
  { key: "creationDate", label: "Created", sortable: true, category: "metadata" },
  { key: "createdBy", label: "Created By", sortable: true, category: "metadata" },
  { key: "comment", label: "Comment", sortable: true, category: "metadata" },
  { key: "pieceCount", label: "Pieces", sortable: true, category: "metadata" },
  { key: "pieceLength", label: "Piece Length", sortable: true, category: "metadata" },
  { key: "isPrivate", label: "Private", sortable: true, category: "metadata" },
  { key: "infoHash", label: "Info Hash", sortable: true, category: "metadata" },
];

export const STORAGE_KEY = "seedarr-visible-columns-v2";

export const DEFAULT_VISIBLE: ReadonlySet<string> = new Set([
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

export const COMPACT_VISIBLE: ReadonlySet<string> = new Set([
  "name",
  "status",
  "totalSize",
  "progress",
  "uploadSpeed",
  "downloadSpeed",
  "ratio",
]);

export function loadVisibleColumns(): Set<string> {
  try {
    if (typeof localStorage !== "undefined") {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored) as string[];
        if (Array.isArray(parsed) && parsed.length > 0) {
          const known = new Set(ALL_COLUMNS.map((c) => c.key));
          const valid = parsed.filter((key) => known.has(key as ColumnKey));
          if (valid.length > 0) return new Set(valid);
        }
      }
    }
  } catch (err) {
    console.warn("Failed to parse localStorage column preferences:", err);
  }
  return new Set(DEFAULT_VISIBLE);
}

export function saveVisibleColumns(cols: Set<string>): void {
  try {
    if (typeof localStorage !== "undefined") {
      localStorage.setItem(STORAGE_KEY, JSON.stringify([...cols]));
    }
  } catch (err) {
    console.warn("Failed to save column preferences to localStorage:", err);
  }
}

export type PresetName = "default" | "compact" | "all";

export interface ColumnPreferencesHook {
  visibleColumns: Set<string>;
  setVisibleColumns: (cols: Set<string>) => void;
  toggleColumn: (key: string) => void;
  setColumnVisibility: (key: string, visible: boolean) => void;
  resetToDefaults: () => void;
  selectAll: () => void;
  deselectAll: () => void;
  applyPreset: (preset: PresetName) => void;
  toggleCategory: (category: ColumnCategory, enable?: boolean) => void;
  isDefault: boolean;
}

export function useColumnPreferences(): ColumnPreferencesHook {
  const [visibleColumns, setVisibleColumnsState] = useState<Set<string>>(loadVisibleColumns);

  const setVisibleColumns = useCallback((cols: Set<string>) => {
    let nextCols = cols;
    if (nextCols.size === 0) {
      nextCols = new Set(["name"]);
    }
    setVisibleColumnsState(nextCols);
    saveVisibleColumns(nextCols);
  }, []);

  const toggleColumn = useCallback((key: string) => {
    setVisibleColumnsState((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        if (next.size > 1) {
          next.delete(key);
        }
      } else {
        next.add(key);
      }
      saveVisibleColumns(next);
      return next;
    });
  }, []);

  const setColumnVisibility = useCallback((key: string, visible: boolean) => {
    setVisibleColumnsState((prev) => {
      const next = new Set(prev);
      if (visible) {
        next.add(key);
      } else if (next.size > 1) {
        next.delete(key);
      }
      saveVisibleColumns(next);
      return next;
    });
  }, []);

  const resetToDefaults = useCallback(() => {
    const defaults = new Set(DEFAULT_VISIBLE);
    setVisibleColumnsState(defaults);
    saveVisibleColumns(defaults);
  }, []);

  const selectAll = useCallback(() => {
    const all = new Set(ALL_COLUMNS.map((c) => c.key));
    setVisibleColumnsState(all);
    saveVisibleColumns(all);
  }, []);

  const deselectAll = useCallback(() => {
    const min = new Set(["name"]);
    setVisibleColumnsState(min);
    saveVisibleColumns(min);
  }, []);

  const applyPreset = useCallback((preset: PresetName) => {
    let next: Set<string>;
    switch (preset) {
      case "compact":
        next = new Set(COMPACT_VISIBLE);
        break;
      case "all":
        next = new Set(ALL_COLUMNS.map((c) => c.key));
        break;
      case "default":
      default:
        next = new Set(DEFAULT_VISIBLE);
        break;
    }
    setVisibleColumnsState(next);
    saveVisibleColumns(next);
  }, []);

  const toggleCategory = useCallback((category: ColumnCategory, enable?: boolean) => {
    const categoryKeys = ALL_COLUMNS.filter((c) => c.category === category).map((c) => c.key);
    setVisibleColumnsState((prev) => {
      const next = new Set(prev);
      const allEnabled = categoryKeys.every((k) => next.has(k));
      const shouldEnable = enable !== undefined ? enable : !allEnabled;

      if (shouldEnable) {
        categoryKeys.forEach((k) => next.add(k));
      } else {
        categoryKeys.forEach((k) => {
          if (next.size > 1) {
            next.delete(k);
          }
        });
      }
      saveVisibleColumns(next);
      return next;
    });
  }, []);

  useEffect(() => {
    function handleStorage(e: StorageEvent) {
      if (e.key === STORAGE_KEY && e.newValue) {
        try {
          const parsed = JSON.parse(e.newValue) as string[];
          if (Array.isArray(parsed) && parsed.length > 0) {
            setVisibleColumnsState(new Set(parsed));
          }
        } catch {
          // Ignore invalid parse
        }
      }
    }
    window.addEventListener("storage", handleStorage);
    return () => window.removeEventListener("storage", handleStorage);
  }, []);

  const isDefault =
    visibleColumns.size === DEFAULT_VISIBLE.size &&
    [...visibleColumns].every((k) => DEFAULT_VISIBLE.has(k));

  return {
    visibleColumns,
    setVisibleColumns,
    toggleColumn,
    setColumnVisibility,
    resetToDefaults,
    selectAll,
    deselectAll,
    applyPreset,
    toggleCategory,
    isDefault,
  };
}
