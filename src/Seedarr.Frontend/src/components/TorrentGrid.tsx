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
          gap: "1.25rem",
          padding: "1.25rem",
          overflowY: "auto",
        }}
      >
        {[0, 1, 2, 3, 4, 5, 6, 7].map((i) => (
          <div
            key={i}
            className="card"
            style={{
              padding: 0,
              overflow: "hidden",
              borderRadius: "8px",
              boxShadow: "0 4px 14px rgba(0, 0, 0, 0.35)",
            }}
          >
            <div
              className="skeleton"
              style={{ width: "100%", paddingTop: "145%" }}
            />
            <div
              style={{
                padding: "0.75rem",
                display: "flex",
                flexDirection: "column",
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
      if (extractTrackerDomain(t.trackerUrl) !== trackerFilter) return false;
    }
    return true;
  });

  if (filtered.length === 0) {
    return <div className="torrent-grid-empty">No torrents found</div>;
  }

  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "repeat(auto-fill, minmax(240px, 1fr))",
        gap: "1.25rem",
        padding: "1.25rem",
        overflowY: "auto",
        flex: "1 1 auto",
        minHeight: 0,
      }}
    >
      {filtered.map((t) => {
        const displayTitle = t.mediaTitle || t.name;
        const hasPoster = Boolean(t.posterUrl);
        const isSelected = selectedTorrentId === t.id;
        const isSeeding = t.status === "Seeding";
        const arrLink = getMediaDeepLink(
          {
            source: t.source,
            metadata: { title: t.mediaTitle, mediaId: 0 } as any,
            title: t.name,
          },
          arrConnections,
        );

        return (
          <div
            key={t.id}
            className="card"
            style={{
              padding: 0,
              overflow: "hidden",
              display: "flex",
              flexDirection: "column",
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
            onClick={() => onSelectTorrent?.(isSelected ? null : t.id)}
          >
            {/* Poster Container */}
            <div
              style={{
                position: "relative",
                width: "100%",
                paddingTop: "145%", // 2:3 aspect ratio matching DownloadHistory
                backgroundColor: "#141414",
                overflow: "hidden",
              }}
            >
              {hasPoster ? (
                <img
                  src={t.posterUrl || ""}
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
                      "linear-gradient(180deg, #2a2a2a 0%, #151515 100%)",
                  }}
                >
                  <span style={{ fontSize: "2.5rem", marginBottom: "0.5rem" }}>
                    {t.source === "Radarr"
                      ? "🎬"
                      : t.source === "Sonarr"
                        ? "📺"
                        : t.source === "Lidarr"
                          ? "🎵"
                          : "📦"}
                  </span>
                  <div
                    style={{
                      fontSize: "0.85rem",
                      fontWeight: 600,
                      wordBreak: "break-word",
                      color: "#ccc",
                    }}
                  >
                    {displayTitle}
                  </div>
                </div>
              )}

              {/* Source Badge (Top Left) */}
              {t.source && (
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
                      fontSize: "0.7rem",
                      padding: "0.2rem 0.5rem",
                      border: "1px solid rgba(255,255,255,0.18)",
                      cursor: arrLink ? "pointer" : "default",
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.25rem",
                      borderRadius: "4px",
                    }}
                    title={
                      arrLink ? `${arrLink.label} (${arrLink.url})` : t.source
                    }
                  >
                    {t.source} {arrLink ? "↗" : ""}
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
                    t.ratio >= 2.0
                      ? "badge-success"
                      : t.ratio >= 1.0
                        ? "badge-primary"
                        : "badge-secondary"
                  }`}
                  style={{
                    fontSize: "0.75rem",
                    padding: "0.2rem 0.55rem",
                    boxShadow: "0 2px 6px rgba(0,0,0,0.5)",
                    borderRadius: "4px",
                  }}
                >
                  ★ {formatRatio(t.ratio)}
                </span>
                {t.rating && (
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
                    ⭐ {t.rating}
                  </span>
                )}
              </div>

              {/* Bottom Telemetry Overlay Bar matching DownloadHistory */}
              <div
                style={{
                  position: "absolute",
                  bottom: 0,
                  left: 0,
                  right: 0,
                  padding: "0.4rem 0.6rem",
                  background:
                    "linear-gradient(180deg, transparent 0%, rgba(0,0,0,0.85) 40%, rgba(0,0,0,0.95) 100%)",
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  fontSize: "0.75rem",
                  color: "#eee",
                  zIndex: 2,
                }}
              >
                <span>↑ {formatBytes(t.uploaded)}</span>
                <span
                  className={`badge badge-${t.status.toLowerCase()}`}
                  style={{
                    fontSize: "0.68rem",
                    padding: "0.15rem 0.45rem",
                    borderRadius: "3px",
                  }}
                >
                  {t.status}
                </span>
              </div>
            </div>

            {/* Card Info Body */}
            <div
              style={{
                padding: "0.75rem",
                flex: 1,
                display: "flex",
                flexDirection: "column",
                justifyContent: "space-between",
              }}
            >
              <div>
                <div
                  style={{
                    fontWeight: 600,
                    fontSize: "0.9rem",
                    lineHeight: "1.25",
                    marginBottom: "0.25rem",
                    overflow: "hidden",
                    display: "-webkit-box",
                    WebkitLineClamp: 2,
                    WebkitBoxOrient: "vertical",
                  }}
                  title={t.name}
                >
                  {displayTitle}{" "}
                  {t.year ? (
                    <span
                      style={{
                        color: "var(--text-muted, #888)",
                        fontWeight: 400,
                      }}
                    >
                      ({t.year})
                    </span>
                  ) : null}
                </div>

                {/* Genres chips */}
                {t.genres && t.genres.length > 0 && (
                  <div
                    style={{
                      display: "flex",
                      gap: "0.25rem",
                      flexWrap: "wrap",
                      margin: "0.35rem 0",
                    }}
                  >
                    {t.genres.slice(0, 2).map((g) => (
                      <span
                        key={g}
                        className="badge badge-secondary"
                        style={{
                          fontSize: "0.65rem",
                          padding: "0.1rem 0.35rem",
                        }}
                      >
                        {g}
                      </span>
                    ))}
                  </div>
                )}

                {/* Mini Meta Info */}
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    fontSize: "0.72rem",
                    color: "var(--text-muted)",
                    marginTop: "0.35rem",
                  }}
                >
                  <span>Size: {formatBytes(t.totalSize)}</span>
                  <span>Added: {formatDate(t.dateAdded).split(" ")[0]}</span>
                </div>
              </div>

              {/* Quick Card Action Buttons matching DownloadHistory style */}
              <div
                style={{
                  display: "flex",
                  gap: "0.3rem",
                  marginTop: "0.6rem",
                  paddingTop: "0.5rem",
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
                    }}
                    onClick={() => stopSeeding.mutate(t.id)}
                    title="Stop seeding"
                  >
                    ⏹ Stop
                  </button>
                ) : (
                  <button
                    className="btn btn-primary"
                    style={{
                      flex: 1,
                      fontSize: "0.75rem",
                      padding: "0.25rem 0.4rem",
                    }}
                    onClick={() => startSeeding.mutate(t.id)}
                    title="Start seeding"
                  >
                    ▶ Start
                  </button>
                )}
                <button
                  className="btn btn-danger"
                  style={{
                    fontSize: "0.75rem",
                    padding: "0.25rem 0.4rem",
                  }}
                  onClick={() => {
                    if (confirm(`Delete "${t.name}"?`)) {
                      deleteTorrent.mutate({ id: t.id });
                    }
                  }}
                  title="Delete torrent"
                >
                  🗑️
                </button>
                <button
                  className="btn btn-outline"
                  style={{
                    fontSize: "0.75rem",
                    padding: "0.25rem 0.4rem",
                  }}
                  onClick={() => onSelectTorrent?.(isSelected ? null : t.id)}
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
