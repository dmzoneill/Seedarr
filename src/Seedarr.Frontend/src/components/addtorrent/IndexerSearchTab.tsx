import React, { useState, useEffect, useMemo } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "../../i18n";
import {
  useTorrents,
  useIndexers,
  useIndexerSearch,
  useDownloadIndexerRelease,
} from "../../api/hooks";
import { formatBytes, formatDate } from "../../utils/formatters";
import { useToast } from "../../context/ToastContext";
import type { ReleaseInfo } from "../../api/types";
import { trackIndexerSearch, trackReleaseGrab } from "../../utils/analytics";

export type SearchSortField = "title" | "size" | "peers" | "date";
export type SearchSortDirection = "asc" | "desc";

export const TORZNAB_CATEGORIES = [
  { id: "", name: "All Categories" },
  { id: "2000", name: "Movies (2000)" },
  { id: "5000", name: "TV (5000)" },
  { id: "3000", name: "Audio (3000)" },
  { id: "4000", name: "Software (4000)" },
  { id: "1000", name: "Games (1000)" },
  { id: "7000", name: "Books (7000)" },
  { id: "5070", name: "Anime (5070)" },
];

export function buildExistingHashesSet(
  torrents?: { infoHash?: string | null }[],
): Set<string> {
  const set = new Set<string>();
  if (!torrents || torrents.length === 0) return set;
  for (const t of torrents) {
    if (t.infoHash && typeof t.infoHash === "string" && t.infoHash.trim()) {
      set.add(t.infoHash.trim().toLowerCase());
    }
  }
  return set;
}

export function isReleaseInLibrary(
  release: Pick<ReleaseInfo, "infoHash">,
  existingHashes: Set<string>,
): boolean {
  if (
    !release.infoHash ||
    typeof release.infoHash !== "string" ||
    !release.infoHash.trim()
  ) {
    return false;
  }
  return existingHashes.has(release.infoHash.trim().toLowerCase());
}

export function isReleaseAdded(
  release: Pick<ReleaseInfo, "guid" | "infoHash" | "title">,
  addedKeys: Set<string>,
): boolean {
  if (release.guid && addedKeys.has(release.guid)) return true;
  if (release.infoHash && addedKeys.has(release.infoHash.toLowerCase()))
    return true;
  if (release.title && addedKeys.has(release.title)) return true;
  return false;
}

export function getReleaseButtonState(params: {
  isDownloading: boolean;
  isAdded: boolean;
  isInLibrary: boolean;
}): { label: string; disabled: boolean; className: string } {
  if (params.isDownloading) {
    return {
      label: "Adding...",
      disabled: true,
      className: "btn btn-success",
    };
  }
  if (params.isAdded) {
    return {
      label: "✓ Added",
      disabled: true,
      className: "btn btn-secondary",
    };
  }
  if (params.isInLibrary) {
    return {
      label: "In Library",
      disabled: true,
      className: "btn btn-secondary",
    };
  }
  return {
    label: "+ Add",
    disabled: false,
    className: "btn btn-success",
  };
}

export function sortReleases(
  releases: ReleaseInfo[] | undefined,
  field: SearchSortField,
  direction: SearchSortDirection,
): ReleaseInfo[] {
  if (!releases || releases.length === 0) return [];
  const multiplier = direction === "asc" ? 1 : -1;
  return [...releases].sort((a, b) => {
    switch (field) {
      case "title":
        return multiplier * (a.title || "").localeCompare(b.title || "");
      case "size":
        return multiplier * ((a.size || 0) - (b.size || 0));
      case "peers": {
        const seedersDiff = (a.seeders || 0) - (b.seeders || 0);
        if (seedersDiff !== 0) return multiplier * seedersDiff;
        return multiplier * ((a.leechers || 0) - (b.leechers || 0));
      }
      case "date": {
        const dateA = a.publishDate ? new Date(a.publishDate).getTime() : 0;
        const dateB = b.publishDate ? new Date(b.publishDate).getTime() : 0;
        return multiplier * (dateA - dateB);
      }
      default:
        return 0;
    }
  });
}

export function paginateReleases(
  releases: ReleaseInfo[] | undefined,
  page: number,
  pageSize: number,
): {
  items: ReleaseInfo[];
  totalCount: number;
  totalPages: number;
  currentPage: number;
  startIndex: number;
  endIndex: number;
} {
  const totalCount = releases?.length || 0;
  const safePageSize = Math.max(1, pageSize);
  const totalPages = Math.max(1, Math.ceil(totalCount / safePageSize));
  const currentPage = Math.min(Math.max(1, page), totalPages);
  const startIndex = (currentPage - 1) * safePageSize;
  const endIndex = Math.min(startIndex + safePageSize, totalCount);
  const items = releases ? releases.slice(startIndex, endIndex) : [];

  return {
    items,
    totalCount,
    totalPages,
    currentPage,
    startIndex,
    endIndex,
  };
}

export interface IndexerSearchTabProps {
  initialQuery?: string;
  selectedCategory?: string;
  isModal?: boolean;
  onClose?: () => void;
}

export function IndexerSearchTab({
  initialQuery = "",
  selectedCategory: _selectedCategory = "",
  isModal: _isModal = false,
  onClose,
}: IndexerSearchTabProps) {
  const { t } = useTranslation();
  const { showToast } = useToast();
  const navigate = useNavigate();

  const [searchQuery, setSearchQuery] = useState(initialQuery);
  const [activeSearchTerm, setActiveSearchTerm] = useState(initialQuery);
  const [selectedIndexerId, setSelectedIndexerId] = useState<
    number | undefined
  >(undefined);
  const [searchCategory, setSearchCategory] = useState<string>("");
  const [sortField, setSortField] = useState<SearchSortField>("peers");
  const [sortDirection, setSortDirection] =
    useState<SearchSortDirection>("desc");
  const [page, setPage] = useState<number>(1);
  const [pageSize, setPageSize] = useState<number>(25);
  const [downloadingGuid, setDownloadingGuid] = useState<string | null>(null);
  const [addedKeys, setAddedKeys] = useState<Set<string>>(new Set());

  const { data: torrents } = useTorrents();
  const { data: indexers } = useIndexers();
  const enabledIndexers = indexers?.filter((i) => i.enable) || [];

  const existingHashes = useMemo(
    () => buildExistingHashesSet(torrents),
    [torrents],
  );

  const searchResults = useIndexerSearch(
    {
      query: activeSearchTerm,
      indexerId: selectedIndexerId,
      category: searchCategory || undefined,
    },
    Boolean(activeSearchTerm.trim()),
  );

  const downloadReleaseMutation = useDownloadIndexerRelease();

  useEffect(() => {
    if (initialQuery) {
      setSearchQuery(initialQuery);
      setActiveSearchTerm(initialQuery);
    }
  }, [initialQuery]);

  // Debounced auto-search
  useEffect(() => {
    const trimmed = searchQuery.trim();
    if (trimmed !== activeSearchTerm) {
      const timer = setTimeout(() => {
        setActiveSearchTerm(trimmed);
        setPage(1);
      }, 350);
      return () => clearTimeout(timer);
    }
  }, [searchQuery, activeSearchTerm]);

  const handleSearchSubmit = (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (searchQuery.trim()) {
      setActiveSearchTerm(searchQuery.trim());
      setPage(1);
      trackIndexerSearch(searchQuery.trim(), selectedIndexerId);
    }
  };

  const handleSort = (field: SearchSortField) => {
    if (sortField === field) {
      setSortDirection((prev) => (prev === "asc" ? "desc" : "asc"));
    } else {
      setSortField(field);
      setSortDirection(field === "title" ? "asc" : "desc");
    }
    setPage(1);
  };

  const sortedReleases = useMemo(
    () => sortReleases(searchResults.data, sortField, sortDirection),
    [searchResults.data, sortField, sortDirection],
  );

  const pagination = useMemo(
    () => paginateReleases(sortedReleases, page, pageSize),
    [sortedReleases, page, pageSize],
  );

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
        indexerName: release.indexer || "",
      },
      {
        onSuccess: () => {
          setDownloadingGuid(null);
          setAddedKeys((prev) => {
            const next = new Set(prev);
            if (release.guid) next.add(release.guid);
            if (release.infoHash) next.add(release.infoHash.toLowerCase());
            if (release.title) next.add(release.title);
            return next;
          });
          trackReleaseGrab(release.title, release.indexer);
          showToast(
            t("addTorrent.addedToDownloadQueue", {
              title: release.title,
              defaultValue: `Added "${release.title}" to download queue`,
            }),
            "success",
          );
        },
        onError: (err) => {
          setDownloadingGuid(null);
          showToast(
            t("addTorrent.failedToAddRelease", {
              message: err.message || "Unknown error",
              defaultValue: `Failed to add release: ${err.message || "Unknown error"}`,
            }),
            "error",
          );
        },
      },
    );
  };

  if (enabledIndexers.length === 0) {
    return (
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          flex: "1 1 auto",
          minHeight: 0,
          overflow: "hidden",
        }}
      >
        <div
          style={{
            padding: "2.5rem 1rem",
            textAlign: "center",
            backgroundColor: "var(--bg-primary, #10111a)",
            borderRadius: "8px",
            border: "1px solid var(--border-light)",
          }}
        >
          <div style={{ fontSize: "2rem", marginBottom: "0.5rem" }}>🔌</div>
          <div style={{ fontWeight: 600, marginBottom: "0.4rem" }}>
            {t(
              "addTorrent.noEnabledIndexers",
              "No Enabled Indexers Configured",
            )}
          </div>
          <p
            style={{
              color: "var(--text-muted, #7e8092)",
              fontSize: "0.85rem",
              maxWidth: "420px",
              margin: "0 auto 1.25rem",
            }}
          >
            {t(
              "addTorrent.connectIndexersDesc",
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
            {t("addTorrent.configureIndexers", "⚙️ Configure Indexers")}
          </button>
        </div>
      </div>
    );
  }

  return (
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

        <select
          aria-label="Filter by indexer"
          className="form-control"
          value={selectedIndexerId ?? ""}
          onChange={(e) => {
            setSelectedIndexerId(
              e.target.value ? Number(e.target.value) : undefined,
            );
            setPage(1);
          }}
          style={{
            minWidth: "140px",
            padding: "0.5rem 0.75rem",
            borderRadius: "6px",
            backgroundColor: "var(--bg-primary)",
            border: "1px solid var(--border-light)",
            color: "inherit",
            fontSize: "0.85rem",
          }}
        >
          <option value="">All Indexers ({enabledIndexers.length})</option>
          {enabledIndexers.map((idx) => (
            <option key={idx.id} value={idx.id}>
              {idx.name}
            </option>
          ))}
        </select>

        <select
          aria-label="Filter by category"
          className="form-control"
          value={searchCategory}
          onChange={(e) => {
            setSearchCategory(e.target.value);
            setPage(1);
          }}
          style={{
            minWidth: "140px",
            padding: "0.5rem 0.75rem",
            borderRadius: "6px",
            backgroundColor: "var(--bg-primary)",
            border: "1px solid var(--border-light)",
            color: "inherit",
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
          disabled={!searchQuery.trim() || searchResults.isFetching}
          style={{
            padding: "0.5rem 1.25rem",
            borderRadius: "6px",
            whiteSpace: "nowrap",
          }}
        >
          {searchResults.isFetching ? "Searching..." : "🔍 Search"}
        </button>
      </form>

      {/* Results Container */}
      <div
        style={{
          flex: "1 1 auto",
          minHeight: 0,
          overflowY: "auto",
          border: "1px solid var(--border-light)",
          borderRadius: "6px",
          backgroundColor: "var(--bg-primary)",
          display: "flex",
          flexDirection: "column",
        }}
      >
        {searchResults.isFetching && !searchResults.data ? (
          <div
            style={{
              padding: "3rem 1rem",
              textAlign: "center",
              color: "var(--text-muted)",
              margin: "auto",
            }}
          >
            Searching enabled indexers...
          </div>
        ) : searchResults.isError ? (
          <div
            style={{
              padding: "2rem 1rem",
              textAlign: "center",
              color: "var(--danger, #ef4444)",
              margin: "auto",
            }}
          >
            Failed to search indexers. Check your connection or API keys.
          </div>
        ) : !activeSearchTerm.trim() ? (
          <div
            style={{
              padding: "3rem 1rem",
              textAlign: "center",
              color: "var(--text-muted)",
              margin: "auto",
            }}
          >
            Enter a query above to search configured Torznab/Newznab indexers.
          </div>
        ) : sortedReleases.length === 0 ? (
          <div
            style={{
              padding: "3rem 1rem",
              textAlign: "center",
              color: "var(--text-muted)",
              margin: "auto",
            }}
          >
            No releases found matching "{activeSearchTerm}".
          </div>
        ) : (
          <table
            style={{
              width: "100%",
              borderCollapse: "collapse",
              fontSize: "0.85rem",
              textAlign: "left",
            }}
          >
            <thead>
              <tr
                style={{
                  borderBottom: "1px solid var(--border-light)",
                  backgroundColor: "var(--bg-secondary)",
                  color: "var(--text-muted)",
                  position: "sticky",
                  top: 0,
                  zIndex: 2,
                }}
              >
                <th
                  style={{
                    padding: "0.6rem 0.85rem",
                    cursor: "pointer",
                    userSelect: "none",
                  }}
                  onClick={() => handleSort("title")}
                >
                  Release Name{" "}
                  {sortField === "title" &&
                    (sortDirection === "asc" ? "▲" : "▼")}
                </th>
                <th style={{ padding: "0.6rem 0.85rem" }}>Indexer</th>
                <th
                  style={{
                    padding: "0.6rem 0.85rem",
                    cursor: "pointer",
                    userSelect: "none",
                    whiteSpace: "nowrap",
                  }}
                  onClick={() => handleSort("size")}
                >
                  Size{" "}
                  {sortField === "size" &&
                    (sortDirection === "asc" ? "▲" : "▼")}
                </th>
                <th
                  style={{
                    padding: "0.6rem 0.85rem",
                    cursor: "pointer",
                    userSelect: "none",
                    whiteSpace: "nowrap",
                  }}
                  onClick={() => handleSort("peers")}
                >
                  Peers{" "}
                  {sortField === "peers" &&
                    (sortDirection === "asc" ? "▲" : "▼")}
                </th>
                <th
                  style={{
                    padding: "0.6rem 0.85rem",
                    cursor: "pointer",
                    userSelect: "none",
                    whiteSpace: "nowrap",
                  }}
                  onClick={() => handleSort("date")}
                >
                  Published{" "}
                  {sortField === "date" &&
                    (sortDirection === "asc" ? "▲" : "▼")}
                </th>
                <th style={{ padding: "0.6rem 0.85rem", textAlign: "right" }}>
                  Action
                </th>
              </tr>
            </thead>
            <tbody>
              {pagination.items.map((rel, idx) => {
                const itemKey = rel.guid || rel.infoHash || rel.title;
                const isDownloading = downloadingGuid === itemKey;
                const isAdded = isReleaseAdded(rel, addedKeys);
                const isAlreadyInLibrary = isReleaseInLibrary(
                  rel,
                  existingHashes,
                );
                const buttonState = getReleaseButtonState({
                  isDownloading,
                  isAdded,
                  isInLibrary: isAlreadyInLibrary,
                });

                return (
                  <tr
                    key={itemKey || idx}
                    style={{
                      borderBottom: "1px solid var(--border-light)",
                      backgroundColor:
                        idx % 2 === 0
                          ? "transparent"
                          : "rgba(255,255,255,0.015)",
                    }}
                  >
                    <td
                      style={{
                        padding: "0.65rem 0.85rem",
                        wordBreak: "break-word",
                      }}
                    >
                      <div style={{ fontWeight: 500 }}>{rel.title}</div>
                      {rel.categories && rel.categories.length > 0 && (
                        <div
                          style={{
                            fontSize: "0.75rem",
                            color: "var(--text-muted)",
                            marginTop: "0.2rem",
                          }}
                        >
                          {rel.categories.join(", ")}
                        </div>
                      )}
                    </td>
                    <td
                      style={{
                        padding: "0.65rem 0.85rem",
                        whiteSpace: "nowrap",
                      }}
                    >
                      <span className="badge" style={{ fontSize: "0.75rem" }}>
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
                        style={{ color: "var(--success)", fontWeight: 600 }}
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
                      {rel.publishDate ? formatDate(rel.publishDate) : "-"}
                    </td>
                    <td
                      style={{ padding: "0.65rem 0.85rem", textAlign: "right" }}
                    >
                      <button
                        type="button"
                        className={buttonState.className}
                        style={{
                          fontSize: "0.78rem",
                          padding: "0.3rem 0.65rem",
                          borderRadius: "4px",
                          opacity:
                            buttonState.disabled && !isDownloading ? 0.75 : 1,
                          cursor: buttonState.disabled ? "default" : "pointer",
                        }}
                        onClick={() => handleAddRelease(rel)}
                        disabled={buttonState.disabled}
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
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <span>
              Showing {pagination.startIndex + 1}–{pagination.endIndex} of{" "}
              {pagination.totalCount} results
            </span>
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.35rem" }}
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

          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={pagination.currentPage <= 1}
              style={{
                padding: "0.25rem 0.65rem",
                fontSize: "0.8rem",
                borderRadius: "4px",
                cursor: pagination.currentPage <= 1 ? "default" : "pointer",
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
                  pagination.currentPage >= pagination.totalPages ? 0.5 : 1,
              }}
            >
              Next ▶
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

export default IndexerSearchTab;
