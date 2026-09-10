import { useTranslation } from "../i18n";
import {
  useTorrents,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useArrConnections,
} from "../api/hooks";
import {
  formatBytes,
  formatRatio,
  formatDate,
  formatDuration,
  extractTrackerDomain,
} from "../utils/formatters";
import { getMediaDeepLink } from "../utils/arrLinks";

interface TorrentGridProps {
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  selectedTorrentId?: number | null;
  onSelectTorrent?: (id: number | null) => void;
}

function TorrentGrid({
  filter,
  stateFilter,
  trackerFilter,
  selectedTorrentId,
  onSelectTorrent,
}: TorrentGridProps) {
  const { t } = useTranslation();
  const { data: torrents, isLoading } = useTorrents();
  const { data: arrConnections } = useArrConnections();
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();

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

  const filtered = (torrents ?? []).filter((t) => {
    if (filter && !t.name.toLowerCase().includes(filter.toLowerCase()))
      return false;
    if (stateFilter && stateFilter !== "All" && t.status !== stateFilter)
      return false;
    if (trackerFilter && trackerFilter !== "All") {
      const urls =
        t.trackers && t.trackers.length > 0
          ? t.trackers
          : t.trackerUrl
            ? [t.trackerUrl]
            : [];
      const hasTracker = urls.some(
        (u) => extractTrackerDomain(u) === trackerFilter,
      );
      if (!hasTracker) return false;
    }
    return true;
  });

  if (filtered.length === 0) {
    return <div className="torrent-grid-empty">{t("torrents.noTorrents", undefined, "No torrents found")}</div>;
  }

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
      {filtered.map((torrent) => {
        const displayTitle = torrent.mediaTitle || torrent.name;
        const hasPoster = Boolean(torrent.posterUrl);
        const isSelected = selectedTorrentId === torrent.id;
        const isSeeding = torrent.status === "Seeding";
        const arrLink = getMediaDeepLink(
          {
            source: torrent.source,
            metadata: { title: torrent.mediaTitle, mediaId: 0 } as any,
            title: torrent.name,
          },
          arrConnections,
        );

        return (
          <div
            key={torrent.id}
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
              border: isSelected
                ? "1px solid var(--accent)"
                : "1px solid rgba(255, 255, 255, 0.08)",
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
            onClick={() => onSelectTorrent?.(isSelected ? null : torrent.id)}
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
              {hasPoster ? (
                <img
                  src={torrent.posterUrl || ""}
                  alt={displayTitle}
                  style={{
                    position: "absolute",
                    top: 0,
                    left: 0,
                    width: "100%",
                    height: "100%",
                    objectFit: "cover",
                  }}
                  loading="lazy"
                />
              ) : (
                <div
                  style={{
                    position: "absolute",
                    top: 0,
                    left: 0,
                    width: "100%",
                    height: "100%",
                    display: "flex",
                    flexDirection: "column",
                    alignItems: "center",
                    justifyContent: "center",
                    padding: "1rem",
                    textAlign: "center",
                    background:
                      "linear-gradient(180deg, #2a2620 0%, #151412 100%)",
                  }}
                >
                  <span style={{ fontSize: "2.5rem", marginBottom: "0.5rem" }}>
                    {torrent.source === "Radarr"
                      ? "🎬"
                      : torrent.source === "Sonarr"
                        ? "📺"
                        : torrent.source === "Lidarr"
                          ? "🎵"
                          : "📡"}
                  </span>
                  <div
                    style={{
                      fontSize: "0.82rem",
                      fontWeight: 600,
                      wordBreak: "break-word",
                      color: "var(--text-secondary)",
                      lineHeight: "1.25",
                    }}
                  >
                    {displayTitle}
                  </div>
                </div>
              )}

              {/* Source Badge (Top Left) */}
              {torrent.source && (
                <div
                  style={{
                    position: "absolute",
                    top: "8px",
                    left: "8px",
                    zIndex: 2,
                  }}
                  onClick={(e) => {
                    if (arrLink) {
                      e.stopPropagation();
                      window.open(arrLink.url, "_blank", "noopener,noreferrer");
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
                      border: "1px solid rgba(255,255,255,0.18)",
                      cursor: arrLink ? "pointer" : "default",
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.25rem",
                      borderRadius: "4px",
                    }}
                    title={
                      arrLink ? `${arrLink.label} (${arrLink.url})` : torrent.source
                    }
                  >
                    {torrent.source} {arrLink ? "↗" : ""}
                  </span>
                </div>
              )}

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
                  borderTop: "1px solid rgba(255,255,255,0.1)",
                }}
              >
                <span style={{ color: "#eee" }}>
                  ↑ {formatBytes(torrent.uploaded)}
                </span>
                <span
                  className={`badge badge-${torrent.status.toLowerCase()}`}
                  style={{
                    fontSize: "0.68rem",
                    padding: "0.15rem 0.45rem",
                    borderRadius: "3px",
                  }}
                >
                  {t(`torrents.${torrent.status.toLowerCase()}`, undefined, torrent.status)}
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
                      textOverflow: "ellipsis",
                      lineHeight: 1.35,
                    }}
                    title={torrent.overview}
                  >
                    {torrent.overview}
                  </p>
                )}
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
                  border: "1px solid rgba(255,255,255,0.04)",
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
                style={{
                  display: "flex",
                  gap: "0.3rem",
                  marginTop: "0.5rem",
                  paddingTop: "0.4rem",
                  borderTop: "1px solid var(--border-light)",
                }}
                onClick={(e) => e.stopPropagation()}
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
                    onClick={() => stopSeeding.mutate(torrent.id)}
                    title="Stop seeding"
                  >
                    <span>⏹</span> <span>{t("torrents.stop", undefined, "Stop")}</span>
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
                    onClick={() => startSeeding.mutate(torrent.id)}
                    title="Start seeding"
                  >
                    <span>▶</span> <span>{t("torrents.start", undefined, "Start")}</span>
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
                  onClick={() => {
                    if (confirm(`Delete "${torrent.name}"?`)) {
                      deleteTorrent.mutate({ id: torrent.id });
                    }
                  }}
                  title="Delete torrent"
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
                  onClick={() => onSelectTorrent?.(isSelected ? null : torrent.id)}
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
}

export default TorrentGrid;
