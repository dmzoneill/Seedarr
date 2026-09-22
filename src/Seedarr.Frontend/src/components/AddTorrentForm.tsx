import { useState, useRef, useCallback, useEffect, useMemo } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "../i18n";
import {
  useAddTorrent,
  useTorrents,
  useCategories,
  useIndexers,
  useIndexerSearch,
  useDownloadIndexerRelease,
  AddTorrentResult,
} from "../api/hooks";
import { formatBytes, formatDate } from "../utils/formatters";
import { useToast } from "../context/ToastContext";
import { validateTorrentFile } from "../utils/magnetParser";
import type { ReleaseInfo } from "../api/types";
import {
  trackTorrentAdd,
  trackIndexerSearch,
  trackReleaseGrab,
} from "../utils/analytics";

export interface AddTorrentFormProps {
  initialMode?: "file" | "magnet" | "search";
  initialQuery?: string;
  isModal?: boolean;
  onClose?: () => void;
  onSuccess?: () => void;
}

export type InputMode = "file" | "magnet" | "search";

export interface MagnetPreviewInfo {
  name?: string;
  hash?: string;
  trackerCount: number;
  isV2?: boolean;
}

export function parseMagnetPreview(uri: string): MagnetPreviewInfo | null {
  const trimmed = uri.trim();
  if (!trimmed.toLowerCase().startsWith("magnet:?")) return null;
  try {
    const rawParams = trimmed.substring(8);
    const params = new URLSearchParams(rawParams);
    const xtList = params.getAll("xt");
    let hash: string | undefined;
    let isV2 = false;

    // Check for BEP 52 v2 multihash (urn:btmh:)
    for (const xt of xtList) {
      const v2Match = xt.match(/^urn:btmh:(?:1220)?([0-9a-fA-F]{64})/i);
      if (v2Match) {
        hash = v2Match[1].toLowerCase();
        isV2 = true;
        break;
      }
    }

    // Check for v1 btih (40 hex or 32 base32 characters, stripping trailing '=' padding)
    for (const xt of xtList) {
      if (xt.toLowerCase().startsWith("urn:btih:")) {
        const cleaned = xt.replace(/^urn:btih:/i, "").trim().replace(/=+$/, "");
        if (/^[0-9a-fA-F]{40}$/i.test(cleaned) || /^[2-7a-zA-Z]{32}$/i.test(cleaned)) {
          if (!isV2) {
            hash = cleaned;
          }
          break;
        }
      }
    }

    const name = params.get("dn") || undefined;
    const trackers = params.getAll("tr");
    if (!hash && !name && trackers.length === 0) return null;

    return {
      name: name ? decodeURIComponent(name.replace(/\+/g, " ")) : undefined,
      hash,
      trackerCount: trackers.length,
      isV2,
    };
  } catch {
    return null;
  }
}

export function buildExistingHashesSet(
  torrents?: Array<{ infoHash?: string | null }>,
): Set<string> {
  return new Set(
    torrents
      ?.map((t) => t.infoHash?.toLowerCase())
      .filter((hash): hash is string => Boolean(hash)),
  );
}

export function isReleaseInLibrary(
  release: Pick<ReleaseInfo, "infoHash">,
  existingHashes: Set<string>,
): boolean {
  if (!release.infoHash) return false;
  return existingHashes.has(release.infoHash.toLowerCase());
}

export function isReleaseAdded(
  release: Pick<ReleaseInfo, "guid" | "infoHash" | "title">,
  addedKeys: Set<string>,
): boolean {
  const itemKey = release.guid || release.infoHash || release.title;
  if (addedKeys.has(itemKey)) return true;
  if (release.infoHash && addedKeys.has(release.infoHash.toLowerCase())) return true;
  if (release.guid && addedKeys.has(release.guid)) return true;
  return false;
}

export function getReleaseButtonState({
  isDownloading,
  isAdded,
  isInLibrary,
}: {
  isDownloading: boolean;
  isAdded: boolean;
  isInLibrary: boolean;
}): { label: string; disabled: boolean; className: string } {
  if (isDownloading) {
    return { label: "Adding...", disabled: true, className: "btn btn-success" };
  }
  if (isAdded) {
    return { label: "✓ Added", disabled: true, className: "btn btn-secondary" };
  }
  if (isInLibrary) {
    return { label: "In Library", disabled: true, className: "btn btn-secondary" };
  }
  return { label: "+ Add", disabled: false, className: "btn btn-success" };
}

export const TORZNAB_CATEGORIES = [
  { id: "", name: "All Categories" },
  { id: "2000", name: "Movies (2000)" },
  { id: "5000", name: "TV (5000)" },
  { id: "3000", name: "Audio (3000)" },
  { id: "4000", name: "Software (4000)" },
  { id: "1000", name: "Games (1000)" },
  { id: "7000", name: "Books (7000)" },
  { id: "5070", name: "Anime (5070)" },
] as const;

export type SearchSortField = "title" | "size" | "peers" | "seeders" | "date";
export type SearchSortDirection = "asc" | "desc";

export function sortReleases(
  releases: ReleaseInfo[] | undefined,
  sortField: SearchSortField | null | undefined,
  sortDirection: SearchSortDirection,
): ReleaseInfo[] {
  if (!releases || releases.length === 0) return [];
  if (!sortField) return [...releases];

  return [...releases].sort((a, b) => {
    let cmp = 0;
    switch (sortField) {
      case "title":
        cmp = (a.title || "").localeCompare(b.title || "", undefined, {
          sensitivity: "base",
          numeric: true,
        });
        break;
      case "size":
        cmp = (a.size ?? 0) - (b.size ?? 0);
        break;
      case "peers":
      case "seeders": {
        const seedersA = a.seeders ?? 0;
        const seedersB = b.seeders ?? 0;
        if (seedersA !== seedersB) {
          cmp = seedersA - seedersB;
        } else {
          cmp = (a.leechers ?? 0) - (b.leechers ?? 0);
        }
        break;
      }
      case "date": {
        const timeA = a.publishDate ? new Date(a.publishDate).getTime() : 0;
        const timeB = b.publishDate ? new Date(b.publishDate).getTime() : 0;
        cmp = timeA - timeB;
        break;
      }
      default:
        cmp = 0;
    }

    if (cmp !== 0) {
      return sortDirection === "asc" ? cmp : -cmp;
    }

    // Deterministic tie-breaker
    return (a.guid || a.title || "").localeCompare(b.guid || b.title || "");
  });
}

export function paginateReleases(
  releases: ReleaseInfo[] | undefined,
  page: number,
  pageSize: number,
): {
  items: ReleaseInfo[];
  totalPages: number;
  currentPage: number;
  startIndex: number;
  endIndex: number;
  totalCount: number;
} {
  const allItems = releases ?? [];
  const totalCount = allItems.length;
  const safePageSize = Math.max(1, pageSize);
  const totalPages = Math.max(1, Math.ceil(totalCount / safePageSize));
  const currentPage = Math.min(Math.max(1, page), totalPages);
  const startIndex = totalCount === 0 ? 0 : (currentPage - 1) * safePageSize;
  const endIndex = Math.min(startIndex + safePageSize, totalCount);
  const items = allItems.slice(startIndex, endIndex);

  return {
    items,
    totalPages,
    currentPage,
    startIndex,
    endIndex,
    totalCount,
  };
}

export function AddTorrentForm({
  initialMode = "file",
  initialQuery = "",
  isModal = false,
  onClose,
  onSuccess,
}: AddTorrentFormProps) {
  const [mode, setMode] = useState<InputMode>(initialMode);
  const [files, setFiles] = useState<File[]>([]);
  const [magnetLink, setMagnetLink] = useState("");
  const [isDragOver, setIsDragOver] = useState(false);
  const [resultMessage, setResultMessage] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const addTorrent = useAddTorrent();
  const { t } = useTranslation();
  const { showToast } = useToast();
  const navigate = useNavigate();

  // Ingestion Controls State
  const { data: categories } = useCategories();
  const [selectedCategory, setSelectedCategory] = useState<string>("");
  const [customSavePath, setCustomSavePath] = useState<string>("");
  const [startPaused, setStartPaused] = useState<boolean>(false);
  const [sequentialDownload, setSequentialDownload] = useState<boolean>(false);
  const [firstLastPiecePrio, setFirstLastPiecePrio] = useState<boolean>(false);

  const activeCategoryObj = useMemo(
    () => categories?.find((c) => c.name === selectedCategory),
    [categories, selectedCategory],
  );

  // Library Torrents Cache & Tracked Grabs
  const { data: torrents } = useTorrents();
  const existingHashes = useMemo(
    () => buildExistingHashesSet(torrents),
    [torrents],
  );
  const [addedKeys, setAddedKeys] = useState<Set<string>>(new Set());

  // Indexer Search State
  const [searchQuery, setSearchQuery] = useState(initialQuery);
  const [activeSearchTerm, setActiveSearchTerm] = useState(initialQuery);
  const [selectedIndexerId, setSelectedIndexerId] = useState<
    number | undefined
  >(undefined);
  const [searchCategory, setSearchCategory] = useState<string>("");
  const [sortField, setSortField] = useState<SearchSortField>("peers");
  const [sortDirection, setSortDirection] = useState<SearchSortDirection>("desc");
  const [page, setPage] = useState<number>(1);
  const [pageSize, setPageSize] = useState<number>(25);
  const [downloadingGuid, setDownloadingGuid] = useState<string | null>(null);

  const { data: indexers } = useIndexers();
  const enabledIndexers = indexers?.filter((i) => i.enable) || [];

  const searchResults = useIndexerSearch(
    {
      query: activeSearchTerm,
      indexerId: selectedIndexerId,
      category: searchCategory || undefined,
    },
    mode === "search" && Boolean(activeSearchTerm.trim()),
  );

  const sortedResults = useMemo(
    () => sortReleases(searchResults.data, sortField, sortDirection),
    [searchResults.data, sortField, sortDirection],
  );

  const pagination = useMemo(
    () => paginateReleases(sortedResults, page, pageSize),
    [sortedResults, page, pageSize],
  );

  useEffect(() => {
    setPage(1);
  }, [activeSearchTerm, selectedIndexerId, searchCategory]);

  const handleSort = (field: SearchSortField) => {
    if (
      sortField === field ||
      (field === "peers" && sortField === "seeders") ||
      (field === "seeders" && sortField === "peers")
    ) {
      setSortDirection((prev) => (prev === "asc" ? "desc" : "asc"));
    } else {
      setSortField(field);
      setSortDirection(field === "title" ? "asc" : "desc");
    }
    setPage(1);
  };

  const downloadReleaseMutation = useDownloadIndexerRelease();

  useEffect(() => {
    if (initialQuery) {
      setSearchQuery(initialQuery);
      setActiveSearchTerm(initialQuery);
    }
  }, [initialQuery]);

  // Debounced auto-search as user types
  useEffect(() => {
    const trimmed = searchQuery.trim();
    if (trimmed !== activeSearchTerm) {
      const timer = setTimeout(() => {
        setActiveSearchTerm(trimmed);
      }, 350);
      return () => clearTimeout(timer);
    }
  }, [searchQuery, activeSearchTerm]);

  const addFiles = useCallback(
    (incoming: FileList | File[]) => {
      const list = Array.from(incoming);
      const validFiles: File[] = [];

      for (const f of list) {
        const validation = validateTorrentFile({ name: f.name, size: f.size });
        if (!validation.valid) {
          showToast(
            `${f.name}: ${validation.error || "Invalid torrent file"}`,
            "error",
          );
        } else {
          validFiles.push(f);
        }
      }

      if (validFiles.length === 0) return;
      setFiles((prev) => {
        const existing = new Set(prev.map((f) => f.name));
        const merged = [...prev];
        for (const f of validFiles) {
          if (!existing.has(f.name)) {
            merged.push(f);
            existing.add(f.name);
          }
        }
        return merged;
      });
    },
    [showToast],
  );

  const removeFile = (name: string) => {
    setFiles((prev) => prev.filter((f) => f.name !== name));
  };

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setIsDragOver(false);
      addFiles(e.dataTransfer.files);
    },
    [addFiles],
  );

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      addFiles(e.target.files);
    }
    e.target.value = "";
  };

  const handleSubmit = () => {
    const finalSavePath =
      customSavePath.trim() || activeCategoryObj?.savePath || undefined;
    const finalCategory = selectedCategory || undefined;

    if (mode === "file" && files.length > 0) {
      setResultMessage(null);
      addTorrent.mutate(
        {
          files,
          category: finalCategory,
          savePath: finalSavePath,
          paused: startPaused,
          sequentialDownload,
          firstLastPiecePrio,
        },
        {
          onSuccess: (result: AddTorrentResult) => {
            trackTorrentAdd("file", result.added.length, finalCategory, {
              start_paused: startPaused,
              sequential: sequentialDownload,
            });
            if (result.failed.length === 0) {
              showToast(
                t(
                  "modals.addTorrent.addedSuccess",
                  { count: result.added.length },
                  `Added ${result.added.length} torrent(s)`,
                ),
                "success",
              );
              if (onSuccess) onSuccess();
              if (onClose) onClose();
              return;
            }
            const failedNames = new Set(result.failed.map((f) => f.fileName));
            setFiles((prev) => prev.filter((f) => failedNames.has(f.name)));
            setResultMessage(
              `${result.added.length} added, ${result.failed.length} skipped: ${result.failed
                .map((f) => `${f.fileName} (${f.reason})`)
                .join("; ")}`,
            );
          },
        },
      );
    } else if (mode === "magnet" && magnetLink.trim()) {
      addTorrent.mutate(
        {
          magnetLink: magnetLink.trim(),
          category: finalCategory,
          savePath: finalSavePath,
          paused: startPaused,
          sequentialDownload,
          firstLastPiecePrio,
        },
        {
          onSuccess: () => {
            trackTorrentAdd("magnet", 1, finalCategory, {
              start_paused: startPaused,
              sequential: sequentialDownload,
            });
            showToast(
              t(
                "modals.addTorrent.magnetAddedSuccess",
                undefined,
                "Magnet link added successfully",
              ),
              "success",
            );
            setMagnetLink("");
            if (onSuccess) onSuccess();
            if (onClose) onClose();
          },
        },
      );
    }
  };

  const handleSearchSubmit = (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (searchQuery.trim()) {
      trackIndexerSearch(searchQuery.trim());
      setActiveSearchTerm(searchQuery.trim());
    }
  };

  const handleAddRelease = (release: ReleaseInfo) => {
    const itemKey = release.guid || release.infoHash || release.title;
    setDownloadingGuid(itemKey);

    downloadReleaseMutation.mutate(
      {
        title: release.title,
        downloadUrl: release.downloadUrl || undefined,
        magnetUrl: release.magnetUrl || undefined,
        infoHash: release.infoHash || undefined,
        indexerId: release.indexerId,
        indexerName: release.indexer,
        imdbId: release.imdbId || undefined,
        tmdbId: release.tmdbId ?? undefined,
        tvdbId: release.tvdbId ?? undefined,
        minimumRatio: release.minimumRatio ?? undefined,
        minimumSeedTime: release.minimumSeedTime ?? undefined,
      },
      {
        onSuccess: () => {
          trackReleaseGrab(release.title, release.indexer);
          setDownloadingGuid(null);
          setAddedKeys((prev) => {
            const next = new Set(prev);
            next.add(itemKey);
            if (release.infoHash) next.add(release.infoHash.toLowerCase());
            if (release.guid) next.add(release.guid);
            return next;
          });
          showToast(
            `Added "${release.title}" to active seeding library`,
            "success",
          );
        },
        onError: (err) => {
          setDownloadingGuid(null);
          showToast(
            `Failed to add release: ${err.message || "Unknown error"}`,
            "error",
          );
        },
      },
    );
  };

  const magnetPreview = useMemo(
    () => parseMagnetPreview(magnetLink),
    [magnetLink],
  );
  const isMagnetValid = Boolean(magnetPreview?.hash);
  const canSubmit =
    (mode === "file" && files.length > 0) ||
    (mode === "magnet" && isMagnetValid);

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        flex: "1 1 auto",
        minHeight: 0,
        height: "100%",
        overflow: "hidden",
      }}
    >
      {/* Mode Switcher Tabs */}
      <div
        className="tab-nav"
        style={{
          display: "flex",
          gap: "0.5rem",
          marginBottom: "1.25rem",
          borderBottom: "1px solid var(--border-light)",
          paddingBottom: "0.5rem",
          flexShrink: 0,
        }}
      >
        <button
          type="button"
          className={`tab-btn ${mode === "file" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("file")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          📁 {t("modals.addTorrent.byFile", "Torrent File")}
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "magnet" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("magnet")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          🧲 {t("modals.addTorrent.byMagnet", "Magnet Link")}
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "search" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("search")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          🔍 {t("modals.addTorrent.bySearch", "Indexer Search")}
        </button>
      </div>

      {/* Mode 1: File Upload */}
      {mode === "file" && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            flex: "1 1 auto",
            minHeight: 0,
            justifyContent: files.length > 0 ? "flex-start" : "center",
            alignItems: "center",
            width: "100%",
            padding: "1rem 0",
          }}
        >
          <div
            className={`drop-zone ${isDragOver ? "drop-zone-active" : ""} ${files.length > 0 ? "drop-zone-has-file" : ""}`}
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            onClick={() => fileInputRef.current?.click()}
            style={{
              border: isDragOver
                ? "2px dashed var(--accent)"
                : "2px dashed rgba(255, 255, 255, 0.15)",
              borderRadius: "8px",
              padding: isModal ? "2.5rem 1.5rem" : "4rem 2rem",
              textAlign: "center",
              cursor: "pointer",
              backgroundColor: isDragOver
                ? "rgba(200, 168, 78, 0.08)"
                : "var(--bg-primary)",
              transition: "all 0.2s ease",
              width: "100%",
              maxWidth: isModal ? "100%" : "640px",
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
              justifyContent: "center",
              margin: files.length > 0 ? "0 auto" : "auto",
            }}
          >
            <div style={{ fontSize: "2.5rem", marginBottom: "0.6rem" }}>📤</div>
            {files.length > 0 ? (
              <div>
                <span style={{ fontWeight: 600, color: "var(--accent)" }}>
                  {files.length === 1
                    ? t("modals.addTorrent.singleFileSelected", { name: files[0].name }, `${files[0].name} selected`)
                    : t("modals.addTorrent.filesSelected", { count: files.length }, `${files.length} torrent files selected`)}
                </span>
                <div
                  style={{
                    fontSize: "0.8rem",
                    color: "var(--text-muted)",
                    marginTop: "0.25rem",
                  }}
                >
                  {t("modals.addTorrent.clickOrDragMore", "Click or drag more files to add")}
                </div>
              </div>
            ) : (
              <div>
                <div style={{ fontWeight: 500, fontSize: "1rem" }}>
                  {t("modals.addTorrent.dropHere", "Drop .torrent files here or click to browse")}
                </div>
                <div
                  style={{
                    fontSize: "0.82rem",
                    color: "var(--text-muted)",
                    marginTop: "0.35rem",
                  }}
                >
                  {t("modals.addTorrent.supportsMultiple", "Supports multiple .torrent files simultaneously")}
                </div>
              </div>
            )}
          </div>

          <input
            ref={fileInputRef}
            type="file"
            accept=".torrent"
            multiple
            style={{ display: "none" }}
            onChange={handleFileChange}
          />

          {files.length > 0 && (
            <div
              style={{
                marginTop: "1rem",
                width: "100%",
                maxWidth: isModal ? "100%" : "640px",
              }}
            >
              <div
                style={{
                  fontSize: "0.8rem",
                  fontWeight: 600,
                  textTransform: "uppercase",
                  color: "var(--text-muted)",
                  marginBottom: "0.4rem",
                }}
              >
                {t("modals.addTorrent.selectedFiles", { count: files.length }, `Selected Files (${files.length})`)}
              </div>
              <ul
                style={{
                  listStyle: "none",
                  padding: 0,
                  margin: 0,
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.4rem",
                  maxHeight: "180px",
                  overflowY: "auto",
                }}
              >
                {files.map((f) => (
                  <li
                    key={f.name}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      padding: "0.4rem 0.75rem",
                      backgroundColor: "var(--bg-primary)",
                      borderRadius: "6px",
                      border: "1px solid var(--border-light)",
                      fontSize: "0.85rem",
                    }}
                  >
                    <span
                      style={{
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                        marginRight: "0.5rem",
                      }}
                    >
                      📄 {f.name}
                    </span>
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        removeFile(f.name);
                      }}
                      style={{
                        background: "none",
                        border: "none",
                        color: "var(--danger)",
                        cursor: "pointer",
                        fontSize: "0.85rem",
                        padding: "0.1rem 0.3rem",
                      }}
                      title={t("modals.addTorrent.removeFile", "Remove file")}
                    >
                      ✕
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {/* Mode 2: Magnet Link */}
      {mode === "magnet" && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            flex: "1 1 auto",
            minHeight: 0,
            justifyContent: "center",
            alignItems: "center",
            width: "100%",
            padding: "1rem 0",
          }}
        >
          <div
            style={{
              width: "100%",
              maxWidth: isModal ? "100%" : "640px",
              display: "flex",
              flexDirection: "column",
              gap: "0.85rem",
              margin: "auto",
            }}
          >
            {/* Header with Title & Quick Action Buttons */}
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
              }}
            >
              <label
                style={{
                  fontSize: "0.95rem",
                  fontWeight: 600,
                  color: "var(--text-primary)",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.4rem",
                  margin: 0,
                }}
              >
                <span>🧲</span> {t("modals.addTorrent.magnetUriLabel", "Magnet URI / Link")}
              </label>

              <div style={{ display: "flex", gap: "0.4rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={async () => {
                    try {
                      const text = await navigator.clipboard.readText();
                      if (text) setMagnetLink(text.trim());
                    } catch {
                      // clipboard access rejected or unsupported
                    }
                  }}
                  style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                  title={t("modals.addTorrent.pasteTooltip", "Paste link from clipboard")}
                >
                  📋 {t("modals.addTorrent.pasteClipboard", "Paste Clipboard")}
                </button>
                {magnetLink && (
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    onClick={() => setMagnetLink("")}
                    style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                    title={t("modals.addTorrent.clearTooltip", "Clear input")}
                  >
                    ✕ {t("modals.addTorrent.clear", "Clear")}
                  </button>
                )}
              </div>
            </div>

            {/* Textarea container with border feedback */}
            <div
              style={{
                borderRadius: "8px",
                border: magnetLink.trim()
                  ? isMagnetValid
                    ? "1px solid rgba(34, 197, 94, 0.6)"
                    : "1px solid rgba(239, 68, 68, 0.6)"
                  : "1px solid var(--border-light)",
                backgroundColor: "var(--bg-primary)",
                boxShadow:
                  magnetLink.trim() && isMagnetValid
                    ? "0 0 0 1px rgba(34, 197, 94, 0.2)"
                    : "none",
                transition: "all 0.2s ease",
              }}
            >
              <textarea
                className="form-control"
                placeholder={t("modals.addTorrent.magnetPlaceholder", "magnet:?xt=urn:btih:...")}
                value={magnetLink}
                onChange={(e) => setMagnetLink(e.target.value)}
                rows={isModal ? 4 : 5}
                style={{
                  width: "100%",
                  minHeight: isModal ? "100px" : "130px",
                  maxHeight: "220px",
                  padding: "0.85rem",
                  borderRadius: "8px",
                  backgroundColor: "transparent",
                  border: "none",
                  outline: "none",
                  boxShadow: "none",
                  color: "inherit",
                  fontFamily:
                    "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace",
                  fontSize: "0.85rem",
                  lineHeight: "1.45",
                  resize: isModal ? "vertical" : "none",
                }}
                autoFocus
              />
            </div>

            {/* Status & Validation Message */}
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                fontSize: "0.8rem",
              }}
            >
              <span style={{ color: "var(--text-muted)" }}>
                {t("modals.addTorrent.pasteHelp", "Paste any valid BitTorrent v1 or v2 magnet link.")}
              </span>
              {magnetLink.trim() && (
                <span
                  style={{
                    color: isMagnetValid
                      ? "var(--success, #22c55e)"
                      : "var(--danger, #ef4444)",
                    fontWeight: 600,
                  }}
                >
                  {isMagnetValid
                    ? t("modals.addTorrent.validMagnet", "✓ Valid Magnet Format")
                    : t("modals.addTorrent.invalidMagnet", "✗ Must start with magnet:?")}
                </span>
              )}
            </div>

            {/* Extracted Magnet Details Preview */}
            {isMagnetValid && (
              <div
                style={{
                  marginTop: "0.25rem",
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  backgroundColor: "rgba(34, 197, 94, 0.08)",
                  border: "1px solid rgba(34, 197, 94, 0.2)",
                  fontSize: "0.82rem",
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.35rem",
                }}
              >
                {magnetPreview?.name && (
                  <div style={{ display: "flex", gap: "0.5rem" }}>
                    <span
                      style={{ color: "var(--text-muted)", minWidth: "75px" }}
                    >
                      {t("modals.addTorrent.name", "Name:")}
                    </span>
                    <span
                      style={{
                        fontWeight: 600,
                        color: "var(--text-primary)",
                        wordBreak: "break-all",
                      }}
                    >
                      {magnetPreview.name}
                    </span>
                  </div>
                )}
                {magnetPreview?.hash && (
                  <div
                    style={{
                      display: "flex",
                      gap: "0.5rem",
                      alignItems: "center",
                      flexWrap: "wrap",
                    }}
                  >
                    <span
                      style={{ color: "var(--text-muted)", minWidth: "75px" }}
                    >
                      {t("modals.addTorrent.infoHash", "Info Hash:")}
                    </span>
                    <span
                      style={{
                        fontFamily: "monospace",
                        color: "#60a5fa",
                        wordBreak: "break-all",
                      }}
                    >
                      {magnetPreview.hash}
                    </span>
                    {magnetPreview.isV2 && (
                      <span
                        className="badge badge-primary"
                        style={{
                          fontSize: "0.68rem",
                          padding: "0.1rem 0.35rem",
                          whiteSpace: "nowrap",
                        }}
                      >
                        v2 (BEP 52)
                      </span>
                    )}
                  </div>
                )}
                {magnetPreview?.trackerCount !== undefined &&
                  magnetPreview.trackerCount > 0 && (
                    <div style={{ display: "flex", gap: "0.5rem" }}>
                      <span
                        style={{ color: "var(--text-muted)", minWidth: "75px" }}
                      >
                        {t("modals.addTorrent.trackers", "Trackers:")}
                      </span>
                      <span style={{ color: "#4ade80" }}>
                        {t("modals.addTorrent.bundledTrackers", { count: magnetPreview.trackerCount }, `${magnetPreview.trackerCount} bundled tracker(s)`)}
                      </span>
                    </div>
                  )}
              </div>
            )}
          </div>
        </div>
      )}

      {/* Mode 3: Indexer Search */}
      {mode === "search" && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            flex: "1 1 auto",
            minHeight: 0,
            overflow: "hidden",
          }}
        >
          {enabledIndexers.length === 0 ? (
            <div
              style={{
                padding: "2.5rem 1rem",
                textAlign: "center",
                backgroundColor: "var(--bg-primary)",
                borderRadius: "8px",
                border: "1px solid var(--border-light)",
              }}
            >
              <div style={{ fontSize: "2rem", marginBottom: "0.5rem" }}>🔌</div>
              <div style={{ fontWeight: 600, marginBottom: "0.4rem" }}>
                {t("modals.addTorrent.noIndexersTitle", "No Enabled Indexers Configured")}
              </div>
              <p
                style={{
                  color: "var(--text-muted)",
                  fontSize: "0.85rem",
                  maxWidth: "420px",
                  margin: "0 auto 1.25rem",
                }}
              >
                {t(
                  "modals.addTorrent.noIndexersDesc",
                  "Connect Jackett, Prowlarr, Torznab, or Newznab indexers in Settings to search releases directly.",
                )}
              </p>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => {
                  if (onClose) onClose();
                  navigate("/settings/indexers");
                }}
              >
                {t("modals.addTorrent.configureIndexers", "⚙️ Configure Indexers")}
              </button>
            </div>
          ) : (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                flex: "1 1 auto",
                minHeight: 0,
                overflow: "hidden",
              }}
            >
              <form
                onSubmit={handleSearchSubmit}
                style={{
                  display: "flex",
                  gap: "0.5rem",
                  flexWrap: "wrap",
                  marginBottom: "1rem",
                  flexShrink: 0,
                }}
              >
                <input
                  type="text"
                  className="form-control"
                  placeholder="Search releases (e.g. Ubuntu, Debian, release name)..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  style={{
                    flex: 1,
                    minWidth: "240px",
                    padding: "0.5rem 0.85rem",
                    borderRadius: "6px",
                    backgroundColor: "var(--bg-primary)",
                    border: "1px solid var(--border-light)",
                    color: "inherit",
                    fontSize: "0.9rem",
                  }}
                  autoFocus
                />
                {enabledIndexers.length > 1 && (
                  <select
                    className="form-control"
                    value={selectedIndexerId ?? ""}
                    onChange={(e) =>
                      setSelectedIndexerId(
                        e.target.value ? Number(e.target.value) : undefined,
                      )
                    }
                    style={{
                      backgroundColor: "var(--bg-primary)",
                      color: "inherit",
                      border: "1px solid var(--border-light)",
                      borderRadius: "6px",
                      padding: "0.5rem 0.85rem",
                      fontSize: "0.85rem",
                    }}
                  >
                    <option value="">
                      All Indexers ({enabledIndexers.length})
                    </option>
                    {enabledIndexers.map((idx) => (
                      <option key={idx.id} value={idx.id}>
                        {idx.name} ({idx.indexerType})
                      </option>
                    ))}
                  </select>
                )}
                <select
                  aria-label="Filter by Category"
                  className="form-control"
                  value={searchCategory}
                  onChange={(e) => setSearchCategory(e.target.value)}
                  style={{
                    backgroundColor: "var(--bg-primary)",
                    color: "inherit",
                    border: "1px solid var(--border-light)",
                    borderRadius: "6px",
                    padding: "0.5rem 0.85rem",
                    fontSize: "0.85rem",
                  }}
                >
                  {TORZNAB_CATEGORIES.map((cat) => (
                    <option key={cat.id} value={cat.id}>
                      {cat.name}
                    </option>
                  ))}
                </select>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={searchResults.isFetching}
                  style={{ borderRadius: "6px", padding: "0.5rem 1.25rem" }}
                >
                  {searchResults.isFetching ? "Searching..." : "Search"}
                </button>
              </form>

              {/* Results Container */}
              <div
                style={{
                  flex: isModal ? undefined : "1 1 auto",
                  maxHeight: isModal ? "480px" : undefined,
                  minHeight: 0,
                  overflowY: "auto",
                  border: "1px solid var(--border-light)",
                  borderRadius: "8px",
                  backgroundColor: "var(--bg-primary)",
                  boxShadow: "inset 0 2px 6px rgba(0, 0, 0, 0.2)",
                }}
              >
                {searchResults.isFetching && (
                  <div style={{ padding: "3rem", textAlign: "center" }}>
                    <div className="loading">
                      Searching configured indexers...
                    </div>
                  </div>
                )}

                {searchResults.isError && (
                  <div
                    style={{
                      padding: "2rem",
                      color: "var(--danger)",
                      textAlign: "center",
                    }}
                  >
                    Search failed:{" "}
                    {(searchResults.error as Error)?.message ||
                      "Check indexer connection"}
                  </div>
                )}

                {!searchResults.isFetching &&
                  !searchResults.isError &&
                  activeSearchTerm &&
                  (searchResults.data?.length ?? 0) === 0 && (
                    <div
                      style={{
                        padding: "3rem",
                        textAlign: "center",
                        color: "var(--text-muted)",
                      }}
                    >
                      No releases found for "{activeSearchTerm}". Try different
                      keywords or indexer.
                    </div>
                  )}

                {!searchResults.isFetching && !activeSearchTerm && (
                  <div
                    style={{
                      padding: "3rem",
                      textAlign: "center",
                      color: "var(--text-muted)",
                    }}
                  >
                    Type a keyword above to search across your configured
                    indexers ({enabledIndexers.map((i) => i.name).join(", ")}).
                  </div>
                )}

                {!searchResults.isFetching &&
                  (searchResults.data?.length ?? 0) > 0 && (
                    <table
                      className="table"
                      style={{ width: "100%", borderCollapse: "collapse" }}
                    >
                      <thead>
                        <tr
                          style={{
                            borderBottom: "1px solid var(--border-light)",
                            textAlign: "left",
                            fontSize: "0.8rem",
                            color: "var(--text-muted)",
                            position: "sticky",
                            top: 0,
                            backgroundColor: "var(--bg-primary)",
                            zIndex: 2,
                          }}
                        >
                          <th
                            onClick={() => handleSort("title")}
                            style={{
                              padding: "0.65rem 0.85rem",
                              cursor: "pointer",
                              userSelect: "none",
                            }}
                            title="Sort by Title"
                          >
                            Title {sortField === "title" && (sortDirection === "asc" ? "▲" : "▼")}
                          </th>
                          <th
                            style={{
                              padding: "0.65rem 0.85rem",
                              width: "130px",
                            }}
                          >
                            Indexer
                          </th>
                          <th
                            onClick={() => handleSort("size")}
                            style={{
                              padding: "0.65rem 0.85rem",
                              width: "100px",
                              cursor: "pointer",
                              userSelect: "none",
                            }}
                            title="Sort by Size"
                          >
                            Size {sortField === "size" && (sortDirection === "asc" ? "▲" : "▼")}
                          </th>
                          <th
                            onClick={() => handleSort("peers")}
                            style={{
                              padding: "0.65rem 0.85rem",
                              width: "95px",
                              cursor: "pointer",
                              userSelect: "none",
                            }}
                            title="Sort by Peers / Seeders"
                          >
                            Peers {(sortField === "peers" || sortField === "seeders") && (sortDirection === "asc" ? "▲" : "▼")}
                          </th>
                          <th
                            onClick={() => handleSort("date")}
                            style={{
                              padding: "0.65rem 0.85rem",
                              width: "100px",
                              cursor: "pointer",
                              userSelect: "none",
                            }}
                            title="Sort by Date"
                          >
                            Date {sortField === "date" && (sortDirection === "asc" ? "▲" : "▼")}
                          </th>
                          <th
                            style={{
                              padding: "0.65rem 0.85rem",
                              width: "90px",
                              textAlign: "right",
                            }}
                          >
                            Action
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {pagination.items.map((rel) => {
                          const itemKey = rel.guid || rel.infoHash || rel.title;
                          const isDownloading = downloadingGuid === itemKey;
                          const isAlreadyInLibrary = isReleaseInLibrary(rel, existingHashes);
                          const isAdded = isReleaseAdded(rel, addedKeys);
                          const buttonState = getReleaseButtonState({
                            isDownloading,
                            isAdded,
                            isInLibrary: isAlreadyInLibrary,
                          });

                          return (
                            <tr
                              key={itemKey}
                              style={{
                                borderBottom:
                                  "1px solid var(--border-light)",
                                fontSize: "0.85rem",
                              }}
                            >
                              <td style={{ padding: "0.65rem 0.85rem" }}>
                                <div
                                  style={{
                                    fontWeight: 500,
                                    wordBreak: "break-word",
                                  }}
                                >
                                  {rel.title}
                                </div>
                                {(isAlreadyInLibrary ||
                                  (rel.categories && rel.categories.length > 0)) && (
                                  <div
                                    style={{
                                      display: "flex",
                                      gap: "0.3rem",
                                      marginTop: "0.25rem",
                                      flexWrap: "wrap",
                                      alignItems: "center",
                                    }}
                                  >
                                    {isAlreadyInLibrary && (
                                      <span
                                        className="badge badge-info"
                                        style={{
                                          fontSize: "0.65rem",
                                          padding: "0.1rem 0.35rem",
                                          borderRadius: "3px",
                                          backgroundColor: "rgba(56, 189, 248, 0.2)",
                                          color: "#38bdf8",
                                          border: "1px solid rgba(56, 189, 248, 0.4)",
                                        }}
                                      >
                                        Already in Library
                                      </span>
                                    )}
                                    {rel.categories
                                      ?.slice(0, 3)
                                      .map((c, i) => (
                                        <span
                                          key={i}
                                          className="badge badge-secondary"
                                          style={{
                                            fontSize: "0.65rem",
                                            padding: "0.1rem 0.35rem",
                                            borderRadius: "3px",
                                          }}
                                        >
                                          {c}
                                        </span>
                                      ))}
                                  </div>
                                )}
                              </td>

                              <td style={{ padding: "0.65rem 0.85rem" }}>
                                <span
                                  className="badge badge-primary"
                                  style={{
                                    fontSize: "0.75rem",
                                    borderRadius: "4px",
                                  }}
                                >
                                  {rel.indexer || "Indexer"}
                                </span>
                              </td>

                              <td
                                style={{
                                  padding: "0.65rem 0.85rem",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                {formatBytes(rel.size)}
                              </td>

                              <td
                                style={{
                                  padding: "0.65rem 0.85rem",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                <span
                                  style={{
                                    color: "var(--success)",
                                    fontWeight: 600,
                                  }}
                                >
                                  ▲ {rel.seeders ?? 0}
                                </span>{" "}
                                <span
                                  style={{
                                    color: "var(--text-muted)",
                                    marginLeft: "0.2rem",
                                  }}
                                >
                                  ▼ {rel.leechers ?? 0}
                                </span>
                              </td>

                              <td
                                style={{
                                  padding: "0.65rem 0.85rem",
                                  fontSize: "0.8rem",
                                  color: "var(--text-muted)",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                {rel.publishDate
                                  ? formatDate(rel.publishDate)
                                  : "-"}
                              </td>

                              <td
                                style={{
                                  padding: "0.65rem 0.85rem",
                                  textAlign: "right",
                                }}
                              >
                                <button
                                  type="button"
                                  className={buttonState.className}
                                  style={{
                                    fontSize: "0.78rem",
                                    padding: "0.3rem 0.65rem",
                                    borderRadius: "4px",
                                    opacity: buttonState.disabled && !isDownloading ? 0.75 : 1,
                                    cursor: buttonState.disabled ? "default" : "pointer",
                                  }}
                                  onClick={() => handleAddRelease(rel)}
                                  disabled={buttonState.disabled}
                                  title={
                                    isAdded
                                      ? "Release grabbed"
                                      : isAlreadyInLibrary
                                        ? "Already in library"
                                        : "Add release"
                                  }
                                >
                                  {buttonState.label}
                                </button>
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  )}
              </div>

              {/* Pagination Controls */}
              {pagination.totalCount > 0 && (
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "0.65rem 0.25rem 0.25rem",
                    flexWrap: "wrap",
                    gap: "0.5rem",
                    fontSize: "0.85rem",
                    color: "var(--text-muted)",
                    flexShrink: 0,
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.75rem",
                    }}
                  >
                    <span>
                      Showing {pagination.startIndex + 1}–{pagination.endIndex}{" "}
                      of {pagination.totalCount} results
                    </span>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "0.35rem",
                      }}
                    >
                      <span style={{ fontSize: "0.8rem" }}>Per page:</span>
                      <select
                        aria-label="Items per page"
                        className="form-control"
                        value={pageSize}
                        onChange={(e) => {
                          setPageSize(Number(e.target.value));
                          setPage(1);
                        }}
                        style={{
                          padding: "0.2rem 0.4rem",
                          fontSize: "0.8rem",
                          backgroundColor: "var(--bg-primary)",
                          color: "inherit",
                          border: "1px solid var(--border-light)",
                          borderRadius: "4px",
                          width: "auto",
                        }}
                      >
                        <option value={25}>25</option>
                        <option value={50}>50</option>
                        <option value={100}>100</option>
                      </select>
                    </div>
                  </div>

                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                    }}
                  >
                    <button
                      type="button"
                      className="btn btn-secondary"
                      onClick={() => setPage((p) => Math.max(1, p - 1))}
                      disabled={pagination.currentPage <= 1}
                      style={{
                        padding: "0.25rem 0.65rem",
                        fontSize: "0.8rem",
                        borderRadius: "4px",
                        cursor:
                          pagination.currentPage <= 1 ? "default" : "pointer",
                        opacity: pagination.currentPage <= 1 ? 0.5 : 1,
                      }}
                    >
                      ◀ Previous
                    </button>
                    <span style={{ fontWeight: 500 }}>
                      Page {pagination.currentPage} of {pagination.totalPages}
                    </span>
                    <button
                      type="button"
                      className="btn btn-secondary"
                      onClick={() =>
                        setPage((p) => Math.min(pagination.totalPages, p + 1))
                      }
                      disabled={pagination.currentPage >= pagination.totalPages}
                      style={{
                        padding: "0.25rem 0.65rem",
                        fontSize: "0.8rem",
                        borderRadius: "4px",
                        cursor:
                          pagination.currentPage >= pagination.totalPages
                            ? "default"
                            : "pointer",
                        opacity:
                          pagination.currentPage >= pagination.totalPages
                            ? 0.5
                            : 1,
                      }}
                    >
                      Next ▶
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {mode !== "search" && (
        <div
          style={{
            marginTop: "1rem",
            padding: "0.85rem 1rem",
            backgroundColor: "var(--bg-secondary, rgba(255,255,255,0.03))",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255,255,255,0.1))",
            display: "flex",
            flexDirection: "column",
            gap: "0.75rem",
            flexShrink: 0,
          }}
        >
          <div
            style={{
              fontWeight: 600,
              fontSize: "0.85rem",
              color: "var(--text-primary)",
            }}
          >
            {t("modals.addTorrent.ingestionOptions", "⚙️ Ingestion Options")}
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "1fr 1fr",
              gap: "0.75rem",
            }}
          >
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.25rem",
              }}
            >
              <label
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  fontWeight: 500,
                }}
              >
                {t("modals.addTorrent.category", "Category")}
              </label>
              <select
                className="form-input"
                value={selectedCategory}
                onChange={(e) => setSelectedCategory(e.target.value)}
                style={{
                  fontSize: "0.85rem",
                  padding: "0.4rem 0.6rem",
                  borderRadius: "4px",
                  background: "var(--bg-primary)",
                  color: "inherit",
                  border: "1px solid var(--border-light)",
                }}
              >
                <option value="">{t("modals.addTorrent.noneCategory", "(None)")}</option>
                {categories?.map((cat) => (
                  <option key={cat.id} value={cat.name}>
                    {cat.name} {cat.savePath ? `(${cat.savePath})` : ""}
                  </option>
                ))}
              </select>
            </div>

            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.25rem",
              }}
            >
              <label
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  fontWeight: 500,
                }}
              >
                {t("modals.addTorrent.savePath", "Save Path")}
              </label>
              <input
                type="text"
                className="form-input"
                placeholder={
                  activeCategoryObj?.savePath
                    ? `Default: ${activeCategoryObj.savePath}`
                    : "e.g. /downloads"
                }
                value={customSavePath}
                onChange={(e) => setCustomSavePath(e.target.value)}
                style={{
                  fontSize: "0.85rem",
                  padding: "0.4rem 0.6rem",
                  borderRadius: "4px",
                  background: "var(--bg-primary)",
                  color: "inherit",
                  border: "1px solid var(--border-light)",
                }}
              />
            </div>
          </div>

          <div
            style={{
              display: "flex",
              gap: "1.5rem",
              alignItems: "center",
              paddingTop: "0.25rem",
              flexWrap: "wrap",
            }}
          >
            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                fontSize: "0.85rem",
                cursor: "pointer",
                userSelect: "none",
              }}
            >
              <input
                type="checkbox"
                checked={startPaused}
                onChange={(e) => setStartPaused(e.target.checked)}
              />
              <span>⏸️ {t("modals.addTorrent.startPaused", "Start Paused")}</span>
            </label>

            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                fontSize: "0.85rem",
                cursor: "pointer",
                userSelect: "none",
              }}
            >
              <input
                type="checkbox"
                checked={sequentialDownload}
                onChange={(e) => setSequentialDownload(e.target.checked)}
              />
              <span>⏩ {t("modals.addTorrent.sequentialDownload", "Sequential Download")}</span>
            </label>
            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                fontSize: "0.85rem",
                cursor: "pointer",
                userSelect: "none",
              }}
            >
              <input
                type="checkbox"
                checked={firstLastPiecePrio}
                onChange={(e) => setFirstLastPiecePrio(e.target.checked)}
              />
              <span>🎯 {t("modals.addTorrent.prioritizeFirstLast", "Prioritize First & Last Pieces")}</span>
            </label>
          </div>
        </div>
      )}

      {(addTorrent.isError || resultMessage) && (
        <div
          className="modal-error"
          style={{ marginTop: "1rem", borderRadius: "6px", flexShrink: 0 }}
        >
          {addTorrent.isError
            ? addTorrent.error instanceof Error
              ? addTorrent.error.message
              : "Failed to add torrent"
            : resultMessage}
        </div>
      )}

      {mode !== "search" && (
        <div
          className="modal-actions"
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.5rem",
            marginTop: "1.25rem",
            flexShrink: 0,
          }}
        >
          {isModal && onClose && (
            <button
              type="button"
              className="btn btn-outline"
              onClick={onClose}
              disabled={addTorrent.isPending}
              style={{ borderRadius: "6px" }}
            >
              {t("common.cancel", "Cancel")}
            </button>
          )}
          <button
            type="button"
            className="btn btn-success"
            onClick={handleSubmit}
            disabled={!canSubmit || addTorrent.isPending}
            style={{ borderRadius: "6px", padding: "0.45rem 1.25rem" }}
          >
            {addTorrent.isPending
              ? t("modals.addTorrent.adding", "Adding...")
              : t("modals.addTorrent.submit", "Add Torrent")}
          </button>
        </div>
      )}
    </div>
  );
}

export default AddTorrentForm;
