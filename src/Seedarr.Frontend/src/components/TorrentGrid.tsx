import { useState, useCallback, useEffect, useMemo, useRef } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
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
  useArrConnections,
} from "../api/hooks";
import {
  formatBytes,
  formatSpeed,
  formatRatio,
  formatDate,
  formatEta,
} from "../utils/formatters";
import { getMediaDeepLink } from "../utils/arrLinks";
import { filterTorrents } from "../utils/filterUtils";
import { usePermissions } from "../hooks/usePermissions";
import TorrentContextMenu from "./TorrentContextMenu";
import AddTorrentModal from "./AddTorrentModal";
import DeleteTorrentModal from "./DeleteTorrentModal";
import MediaArtwork from "./MediaArtwork";
import type { Torrent } from "../api/types";

export const TORRENT_GRID_MIN_CARD_PX = 240;
export const TORRENT_GRID_GAP_PX = 20;
export const TORRENT_GRID_PADDING_PX = 20;
export const TORRENT_GRID_INITIAL_WIDTH = 1200;
export const TORRENT_GRID_INITIAL_HEIGHT = 900;
const TORRENT_GRID_BODY_ESTIMATE_PX = 230;

export function torrentGridColumnCount(viewportWidth: number): number {
  const inner = Math.max(0, viewportWidth - TORRENT_GRID_PADDING_PX * 2);
  if (inner <= 0) return 1;
  return Math.max(
    1,
    Math.floor(
      (inner + TORRENT_GRID_GAP_PX) / (TORRENT_GRID_MIN_CARD_PX + TORRENT_GRID_GAP_PX),
    ),
  );
}

export function torrentGridRowEstimate(
  viewportWidth: number,
  columnCount: number,
): number {
  const inner = Math.max(
    TORRENT_GRID_MIN_CARD_PX,
    viewportWidth - TORRENT_GRID_PADDING_PX * 2,
  );
  const columns = Math.max(1, columnCount);
  const columnWidth =
    columns === 1
      ? inner
      : (inner - TORRENT_GRID_GAP_PX * (columns - 1)) / columns;
  return Math.round(columnWidth * 1.5 + TORRENT_GRID_BODY_ESTIMATE_PX);
}

export interface TorrentGridProps {
  torrents?: Torrent[];
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  categoryFilter?: string;
  tagFilter?: string;
  selectedTagIds?: number[] | Set<number>;
  tagMatchMode?: "AND" | "OR";
  selectedIds?: Set<number>;
  onToggleSelect?: (id: number) => void;
  onSelectAll?: () => void;
  selectedTorrentId?: number | null;
  onSelectTorrent?: (id: number | null) => void;
}

function TorrentGrid({
  torrents: propTorrents,
  filter,
  stateFilter,
  trackerFilter,
  categoryFilter,
  tagFilter,
  selectedTagIds,
  tagMatchMode,
  selectedIds,
  onToggleSelect,
  onSelectAll: _onSelectAll,
  selectedTorrentId,
  onSelectTorrent,
}: TorrentGridProps) {
  const { t } = useTranslation();
  const { canMutateTorrents, canDeleteTorrent } = usePermissions();
  const { data: fetchedTorrents, isLoading, isError } = useTorrents();
  const torrents = propTorrents ?? fetchedTorrents;
  const { data: arrConnections } = useArrConnections();
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const updateTorrent = useUpdateTorrent();
  const announceTorrent = useAnnounceTorrent();
  const recheckTorrent = useRecheckTorrent();
  const moveTorrentQueue = useMoveTorrentQueue();

  const [contextMenu, setContextMenu] = useState<{
    x: number;
    y: number;
    torrent: Torrent;
  } | null>(null);
  const [searchModalQuery, setSearchModalQuery] = useState<string | null>(null);
  const [torrentToDelete, setTorrentToDelete] = useState<Torrent | null>(null);

  const handleContextMenu = useCallback((e: React.MouseEvent, t: Torrent) => {
    e.preventDefault();
    setContextMenu({ x: e.clientX, y: e.clientY, torrent: t });
  }, []);

  const closeContextMenu = useCallback(() => { setContextMenu(null); }, []);

  const filtered = useMemo(() => {
    if (propTorrents) return propTorrents;
    return filterTorrents(torrents, {
      filter,
      stateFilter,
      trackerFilter,
      categoryFilter,
      tagFilter,
      selectedTagIds,
      tagMatchMode,
    });
  }, [
    propTorrents,
    torrents,
    filter,
    stateFilter,
    trackerFilter,
    categoryFilter,
    tagFilter,
    selectedTagIds,
    tagMatchMode,
  ]);

  const parentRef = useRef<HTMLDivElement>(null);
  const [measuredWidth, setMeasuredWidth] = useState(0);
  const layoutWidth =
    measuredWidth > 0 ? measuredWidth : TORRENT_GRID_INITIAL_WIDTH;
  const columnCount = torrentGridColumnCount(layoutWidth);
  const rowCount =
    filtered.length === 0 ? 0 : Math.ceil(filtered.length / columnCount);
  // A failed background refetch sets isError while cached rows are still
  // present. Replace the grid only when there is nothing left to show.
  const showLoadError =
    isError && filtered.length === 0 && fetchedTorrents == null;
  const showRefreshWarning = isError && !showLoadError;
  const showCards = !isLoading && !showLoadError && filtered.length > 0;

  const rowVirtualizer = useVirtualizer({
    count: showCards ? rowCount : 0,
    getScrollElement: () => parentRef.current,
    estimateSize: () => torrentGridRowEstimate(layoutWidth, columnCount),
    overscan: 2,
    gap: TORRENT_GRID_GAP_PX,
    paddingStart: TORRENT_GRID_PADDING_PX,
    paddingEnd: TORRENT_GRID_PADDING_PX,
    initialRect: {
      width: TORRENT_GRID_INITIAL_WIDTH,
      height: TORRENT_GRID_INITIAL_HEIGHT,
    },
  });

  useEffect(() => {
    const element = parentRef.current;
    if (!element || typeof ResizeObserver === "undefined") return;
    const publish = () => {
      const next = element.clientWidth;
      setMeasuredWidth((prev) => (prev === next ? prev : next));
    };
    publish();
    const observer = new ResizeObserver(publish);
    observer.observe(element);
    return () => {
      observer.disconnect();
    };
  }, [showCards]);

  if (isLoading) {
    return (
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fill, minmax(240px, 1fr))",
          gridAutoRows: "max-content",
          alignContent: "start",
          gap: "1.25rem",
          padding: "1.25rem",
          overflowY: "auto",
          overflowX: "hidden",
          flex: "1 1 0%",
          minHeight: 0,
          height: "100%",
          width: "100%",
        }}
      >
        {[0, 1, 2, 3, 4, 5, 6, 7].map((i) => (
          <div
            key={i}
            className="card"
            style={{
              padding: 0,
              overflow: "hidden",
              display: "flex",
              flexDirection: "column",
              height: "auto",
              minHeight: "min-content",
              flexShrink: 0,
              borderRadius: "8px",
              boxShadow: "0 4px 14px rgba(0, 0, 0, 0.35)",
            }}
          >
            <div
              className="skeleton"
              style={{ width: "100%", aspectRatio: "2 / 3", flexShrink: 0 }}
            />
            <div
              style={{
                padding: "0.75rem",
                display: "flex",
                flexDirection: "column",
                flex: "0 0 auto",
                gap: "0.5rem",
              }}
            >
              <span
                className="skeleton skeleton-line"
                style={{ width: "85%", height: "1rem" }}
              />
              <span
                className="skeleton skeleton-line"
                style={{ width: "50%", height: "0.8rem" }}
              />
            </div>
          </div>
        ))}
      </div>
    );
  }

  if (showLoadError) {
    return <p className="error">Failed to load data.</p>;
  }

  if (filtered.length === 0) {
    return (
      <>
        {showRefreshWarning && (
          <p className="error" role="alert">
            Failed to refresh torrents.
          </p>
        )}
        <div className="torrent-grid-empty">
          {t("torrents.noTorrents", undefined, "No torrents found")}
        </div>
      </>
    );
  }

  const virtualRows = rowVirtualizer.getVirtualItems();
  const rowsToRender =
    virtualRows.length > 0
      ? virtualRows.map((row) => ({
          key: row.key,
          index: row.index,
          start: row.start,
        }))
      : Array.from({ length: Math.min(rowCount, 4) }, (_, index) => ({
          key: `fallback-${index}`,
          index,
          start:
            TORRENT_GRID_PADDING_PX +
            index *
              (torrentGridRowEstimate(layoutWidth, columnCount) +
                TORRENT_GRID_GAP_PX),
        }));

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        flex: "1 1 0%",
        minHeight: 0,
        height: "100%",
        width: "100%",
        position: "relative",
      }}
    >
      {showRefreshWarning && (
        <p className="error" role="alert">
          Failed to refresh torrents.
        </p>
      )}
    <div
      ref={parentRef}
      style={{
        overflowY: "auto",
        overflowX: "hidden",
        flex: "1 1 0%",
        minHeight: 0,
        height: "100%",
        width: "100%",
      }}
    >
      <div
        style={{
          height: rowVirtualizer.getTotalSize(),
          width: "100%",
          position: "relative",
        }}
      >
      {rowsToRender.map((virtualRow) => {
        const start = virtualRow.index * columnCount;
        const rowItems = filtered.slice(start, start + columnCount);
        return (
          <div
            key={virtualRow.key}
            data-index={virtualRow.index}
            ref={rowVirtualizer.measureElement}
            style={{
              position: "absolute",
              top: 0,
              left: 0,
              width: "100%",
              transform: `translateY(${virtualRow.start}px)`,
              display: "grid",
              gridTemplateColumns: `repeat(${columnCount}, minmax(${TORRENT_GRID_MIN_CARD_PX}px, 1fr))`,
              gap: TORRENT_GRID_GAP_PX,
              paddingLeft: TORRENT_GRID_PADDING_PX,
              paddingRight: TORRENT_GRID_PADDING_PX,
              boxSizing: "border-box",
            }}
          >
      {rowItems.map((torrent) => {
        const displayTitle = torrent.mediaTitle || torrent.name;
        const isSelected =
          selectedTorrentId === torrent.id ||
          (selectedIds?.has(torrent.id) ?? false);
        const isSeeding = torrent.status === "Seeding";
        const pct = Math.min((torrent.progress) * 100, 100);
        const arrLink = getMediaDeepLink(
          {
            source: torrent.source,
            metadata: { title: torrent.mediaTitle, mediaId: 0 },
            title: torrent.name,
          },
          arrConnections,
        );

        return (
          <div
            key={torrent.id}
            role="button"
            tabIndex={0}
            aria-selected={isSelected}
            className={`card${isSelected ? " torrent-card-selected" : ""}`}
            style={{
              padding: 0,
              overflow: "hidden",
              display: "flex",
              flexDirection: "column",
              height: "auto",
              minHeight: "min-content",
              flexShrink: 0,
              borderRadius: "8px",
              border: isSelected
                ? "1px solid var(--accent)"
                : "1px solid var(--border-light)",
              backgroundColor: isSelected
                ? "var(--bg-hover-elevated)"
                : "var(--bg-secondary)",
              boxShadow: isSelected
                ? "0 8px 24px rgba(200, 168, 78, 0.25), 0 2px 6px rgba(0, 0, 0, 0.3)"
                : "0 4px 14px rgba(0, 0, 0, 0.35), 0 1px 3px rgba(0, 0, 0, 0.2)",
              transition:
                "transform 0.18s ease, box-shadow 0.18s ease, border-color 0.18s ease",
              cursor: "pointer",
            }}
            onClick={(e) => {
              if (e.ctrlKey || e.metaKey) {
                onToggleSelect?.(torrent.id);
              } else {
                onSelectTorrent?.(
                  isSelected && selectedTorrentId === torrent.id
                    ? null
                    : torrent.id,
                );
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
                e.stopPropagation();
                onToggleSelect?.(torrent.id);
              } else if (e.key === "Enter") {
                e.preventDefault();
                e.stopPropagation?.();
                onSelectTorrent?.(
                  isSelected && selectedTorrentId === torrent.id
                    ? null
                    : torrent.id,
                );
              }
            }}
            onContextMenu={(e) => { handleContextMenu(e, torrent); }}
          >
            {/* Poster Artwork Box */}
            <div
              style={{
                position: "relative",
                width: "100%",
                aspectRatio: "2 / 3",
                backgroundColor: "#141414",
                overflow: "hidden",
                flexShrink: 0,
              }}
            >
              <MediaArtwork
                src={torrent.posterUrl}
                alt={displayTitle}
                title={displayTitle}
                category={torrent.source || undefined}
                style={{
                  position: "absolute",
                  top: 0,
                  left: 0,
                  width: "100%",
                  height: "100%",
                }}
              />

              {/* Selection Checkbox & Source Badge (Top Left) */}
              <div
                role="button"
                tabIndex={0}
                style={{
                  position: "absolute",
                  top: "8px",
                  left: "8px",
                  zIndex: 3,
                  display: "flex",
                  alignItems: "center",
                  gap: "6px",
                }}
                onClick={(e) => { e.stopPropagation(); }}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    e.stopPropagation();
                  }
                }}
              >
                <input
                  type="checkbox"
                  className="torrent-checkbox"
                  checked={selectedIds?.has(torrent.id) ?? false}
                  onChange={() => {}}
                  onClick={(e) => {
                    e.stopPropagation();
                    onToggleSelect?.(torrent.id);
                  }}
                  style={{
                    cursor: "pointer",
                    width: 16,
                    height: 16,
                    margin: 0,
                  }}
                  aria-label={`Select ${displayTitle}`}
                />
                {torrent.source && (
                  <div
                    role="button"
                    tabIndex={0}
                    onClick={(e) => {
                      if (arrLink) {
                        e.stopPropagation();
                        window.open(
                          arrLink.url,
                          "_blank",
                          "noopener,noreferrer",
                        );
                      }
                    }}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        if (arrLink) {
                          e.preventDefault();
                          e.stopPropagation();
                          window.open(
                            arrLink.url,
                            "_blank",
                            "noopener,noreferrer",
                          );
                        }
                      }
                    }}
                  >
                    <span
                      className="badge"
                      style={{
                        backgroundColor: "rgba(0, 0, 0, 0.78)",
                        backdropFilter: "blur(4px)",
                        color: "#fff",
                        fontSize: "0.68rem",
                        padding: "0.2rem 0.5rem",
                        border: "1px solid var(--border)",
                        cursor: arrLink ? "pointer" : "default",
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.25rem",
                        borderRadius: "4px",
                      }}
                      title={
                        arrLink
                          ? `${arrLink.label} (${arrLink.url})`
                          : torrent.source
                      }
                    >
                      {torrent.source} {arrLink ? "↗" : ""}
                    </span>
                  </div>
                )}
              </div>

              {/* Top-right Ratio Badge */}
              <div
                style={{
                  position: "absolute",
                  top: "8px",
                  right: "8px",
                  zIndex: 2,
                  display: "flex",
                  flexDirection: "column",
                  gap: "4px",
                  alignItems: "flex-end",
                }}
              >
                <span
                  className={`badge ${
                    torrent.ratio >= 2.0
                      ? "badge-success"
                      : torrent.ratio >= 1.0
                        ? "badge-primary"
                        : "badge-secondary"
                  }`}
                  style={{
                    fontSize: "0.72rem",
                    padding: "0.2rem 0.5rem",
                    boxShadow: "0 2px 6px rgba(0,0,0,0.5)",
                    borderRadius: "4px",
                  }}
                >
                  ★ {formatRatio(torrent.ratio)}
                </span>
                {torrent.rating && (
                  <span
                    className="badge"
                    style={{
                      backgroundColor: "rgba(0, 0, 0, 0.8)",
                      backdropFilter: "blur(4px)",
                      color: "var(--accent)",
                      fontSize: "0.68rem",
                      padding: "0.15rem 0.45rem",
                      border: "1px solid rgba(200, 168, 78, 0.3)",
                      borderRadius: "4px",
                    }}
                  >
                    ⭐ {torrent.rating}
                  </span>
                )}
              </div>

              {/* Bottom Telemetry Overlay Bar */}
              <div
                style={{
                  position: "absolute",
                  bottom: 0,
                  left: 0,
                  right: 0,
                  zIndex: 2,
                  backgroundColor: "rgba(0, 0, 0, 0.82)",
                  backdropFilter: "blur(6px)",
                  padding: "0.3rem 0.5rem",
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  fontSize: "0.7rem",
                  borderTop: "1px solid var(--border-light)",
                }}
              >
                <span style={{ color: "#eee" }}>
                  ↑ {formatBytes(torrent.uploaded)}
                </span>
                <span
                  className={
                    torrent.isVpnPaused
                      ? "badge badge-vpn-paused"
                      : `badge badge-${torrent.status.toLowerCase()}`
                  }
                  style={{
                    fontSize: "0.68rem",
                    padding: "0.15rem 0.45rem",
                    borderRadius: "3px",
                  }}
                  aria-label={`${torrent.status}: ${pct.toFixed(1)}% complete, Down: ${formatSpeed(torrent.downloadSpeed)}, Up: ${formatSpeed(torrent.uploadSpeed)}, ETA: ${formatEta(torrent.eta)}`}
                >
                  {torrent.isVpnPaused
                    ? t(
                        "torrents.pausedVpnKillSwitch",
                        undefined,
                        "Paused (VPN Kill Switch)",
                      )
                    : t(
                        `torrents.${torrent.status.toLowerCase()}`,
                        undefined,
                        torrent.status === "QueuedForChecking"
                          ? "Queued for Recheck"
                          : torrent.status,
                      )}
                </span>
              </div>
            </div>

            {/* Card Info Body */}
            <div
              style={{
                padding: "0.75rem",
                display: "flex",
                flexDirection: "column",
                gap: "0.45rem",
                flex: 1,
                justifyContent: "space-between",
              }}
            >
              <div>
                <h4
                  style={{
                    margin: "0 0 0.25rem 0",
                    fontSize: "0.92rem",
                    fontWeight: 600,
                    lineHeight: 1.25,
                    color: "var(--text-primary)",
                    display: "-webkit-box",
                    WebkitLineClamp: 2,
                    WebkitBoxOrient: "vertical",
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                  }}
                  title={displayTitle}
                >
                  {displayTitle} {torrent.year ? `(${torrent.year})` : ""}
                </h4>
                {torrent.overview && (
                  <p
                    style={{
                      margin: 0,
                      fontSize: "0.75rem",
                      color: "var(--text-secondary)",
                      display: "-webkit-box",
                      WebkitLineClamp: 2,
                      WebkitBoxOrient: "vertical",
                      overflow: "hidden",
                      wordBreak: "break-word",
                      textOverflow: "ellipsis",
                      lineHeight: 1.35,
                    }}
                    title={torrent.overview}
                  >
                    {torrent.overview}
                  </p>
                )}
              </div>

              {/* Progress Bar */}
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

              {/* Metrics Grid */}
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "1fr 1fr",
                  gap: "0.35rem 0.5rem",
                  fontSize: "0.72rem",
                  color: "var(--text-secondary)",
                  backgroundColor: "rgba(0, 0, 0, 0.2)",
                  padding: "0.45rem",
                  borderRadius: "4px",
                  border: "1px solid var(--border-light)",
                }}
              >
                <div>
                  <span>{t("common.size", undefined, "Size")}: </span>
                  <strong style={{ color: "var(--text-primary)" }}>
                    {formatBytes(torrent.totalSize)}
                  </strong>
                </div>
                <div>
                  <span>{t("common.peers", undefined, "Peers")}: </span>
                  <strong style={{ color: "var(--text-primary)" }}>
                    {torrent.seeders} / {torrent.leechers}
                  </strong>
                </div>
                <div>
                  <span>{t("common.ratio", undefined, "Ratio")}: </span>
                  <strong
                    style={{
                      color:
                        torrent.ratio >= 1.0
                          ? "var(--success)"
                          : "var(--text-primary)",
                    }}
                  >
                    {formatRatio(torrent.ratio)}
                  </strong>
                </div>
                <div>
                  <span>{t("torrents.table.added", undefined, "Added")}: </span>
                  <strong style={{ color: "var(--text-primary)" }}>
                    {formatDate(torrent.dateAdded).split(" ")[0]}
                  </strong>
                </div>
              </div>

              {/* Quick Card Action Buttons */}
              <div
                role="button"
                tabIndex={0}
                style={{
                  display: "flex",
                  gap: "0.3rem",
                  marginTop: "0.5rem",
                  paddingTop: "0.4rem",
                  borderTop: "1px solid var(--border-light)",
                }}
                onClick={(e) => { e.stopPropagation(); }}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    e.stopPropagation();
                  }
                }}
              >
                {isSeeding ? (
                  <button
                    className="btn btn-outline"
                    style={{
                      flex: 1,
                      fontSize: "0.75rem",
                      padding: "0.25rem 0.4rem",
                      display: "inline-flex",
                      alignItems: "center",
                      justifyContent: "center",
                      gap: "0.35rem",
                    }}
                    onClick={(e) => {
                      e.stopPropagation();
                      stopSeeding.mutate(torrent.id);
                    }}
                    disabled={!canMutateTorrents}
                    title={
                      !canMutateTorrents
                        ? "Pausing/stopping requires operator or admin role"
                        : "Stop seeding"
                    }
                  >
                    <span>⏹</span>{" "}
                    <span>{t("torrents.stop", undefined, "Stop")}</span>
                  </button>
                ) : (
                  <button
                    className="btn btn-primary"
                    style={{
                      flex: 1,
                      fontSize: "0.75rem",
                      padding: "0.25rem 0.4rem",
                      display: "inline-flex",
                      alignItems: "center",
                      justifyContent: "center",
                      gap: "0.35rem",
                    }}
                    onClick={(e) => {
                      e.stopPropagation();
                      startSeeding.mutate(torrent.id);
                    }}
                    disabled={!canMutateTorrents}
                    title={
                      !canMutateTorrents
                        ? "Starting torrents requires operator or admin role"
                        : "Start seeding"
                    }
                  >
                    <span>▶</span>{" "}
                    <span>{t("torrents.start", undefined, "Start")}</span>
                  </button>
                )}
                <button
                  className="btn btn-danger"
                  style={{
                    fontSize: "0.75rem",
                    padding: "0.25rem 0.45rem",
                    display: "inline-flex",
                    alignItems: "center",
                    justifyContent: "center",
                  }}
                  onClick={(e) => {
                    e.stopPropagation();
                    setTorrentToDelete(torrent);
                  }}
                  disabled={deleteTorrent.isPending || !canDeleteTorrent}
                  title={
                    !canDeleteTorrent
                      ? "Deleting torrents requires operator or admin role"
                      : "Delete torrent"
                  }
                >
                  <span>{t("common.delete", undefined, "Delete")}</span>
                </button>
                <button
                  className="btn btn-outline"
                  style={{
                    fontSize: "0.75rem",
                    padding: "0.25rem 0.45rem",
                    display: "inline-flex",
                    alignItems: "center",
                    justifyContent: "center",
                  }}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSelectTorrent?.(isSelected ? null : torrent.id);
                  }}
                  title="View full torrent details"
                >
                  ℹ️
                </button>
              </div>
            </div>
          </div>
        );
      })}
          </div>
        );
      })}
      </div>
    </div>

      {contextMenu && (
        <TorrentContextMenu
          x={contextMenu.x}
          y={contextMenu.y}
          torrent={contextMenu.torrent}
          visibleColumns={new Set()}
          allColumns={[]}
          onClose={closeContextMenu}
          onToggleColumn={() => {}}
          onStart={(id) => { startSeeding.mutate(id); }}
          onStop={(id) => { stopSeeding.mutate(id); }}
          onUpdate={(t) => { updateTorrent.mutate(t); }}
          onAnnounce={(id) => { announceTorrent.mutate(id); }}
          onRecheck={(id) => { recheckTorrent.mutate(id); }}
          onDelete={(payload) => { deleteTorrent.mutate(payload); }}
          onMoveQueue={(payload) => { moveTorrentQueue.mutate(payload); }}
          onSearchIndexers={(q) => { setSearchModalQuery(q); }}
        />
      )}

      {searchModalQuery && (
        <AddTorrentModal
          initialMode="search"
          initialQuery={searchModalQuery}
          onClose={() => { setSearchModalQuery(null); }}
        />
      )}

      {torrentToDelete && (
        <DeleteTorrentModal
          isOpen={true}
          count={1}
          torrentName={torrentToDelete.name}
          isPending={deleteTorrent.isPending}
          onClose={() => {
            if (!deleteTorrent.isPending) setTorrentToDelete(null);
          }}
          onConfirm={async (deleteFiles: boolean) => {
            try {
              await deleteTorrent.mutateAsync({
                id: torrentToDelete.id,
                deleteFiles,
              });
              setTorrentToDelete(null);
            } catch {
              // Mutation error handled by query client
            }
          }}
        />
      )}
    </div>
  );
}

export default TorrentGrid;
