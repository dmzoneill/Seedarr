import { useDownloadHistory, useArrConnections, useIndexers } from "../../api/hooks";
import { Torrent } from "../../api/types";
import {
  formatBytes,
  formatDate,
  formatRatio,
  formatSeconds,
} from "../../utils/formatters";
import {
  getMediaDeepLink,
  getImdbUrl,
  getTmdbUrl,
  getProwlarrUrl,
} from "../../utils/arrLinks";
import {
  getTorrentBadges,
  calculateHnrStatus,
} from "../../utils/milestones";
import { StatusRow } from "./shared";

export function GeneralTab({ torrent }: { torrent: Torrent }) {
  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();
  const { data: indexers } = useIndexers();

  const historyMatch = history?.find(
    (h) =>
      (torrent.infoHash && h.infoHash?.toLowerCase() === torrent.infoHash.toLowerCase()) ||
      h.title?.toLowerCase() === torrent.name?.toLowerCase(),
  );

  const meta = historyMatch?.metadata;
  const arrLink = historyMatch ? getMediaDeepLink(historyMatch, arrConnections) : null;
  const imdbUrl = getImdbUrl(meta?.imdbId, meta?.title || torrent.name);
  const tmdbUrl = getTmdbUrl(meta?.tmdbId, meta?.mediaType);
  const prowlarrUrl = getProwlarrUrl(indexers, meta?.title || torrent.name);

  const badges = getTorrentBadges(torrent);
  const hnr = calculateHnrStatus(torrent);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      {/* Media & Arr Integration Banner */}
      {(arrLink || meta || prowlarrUrl) && (
        <div
          className="card"
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: "1rem",
            padding: "1rem",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
            {meta?.posterUrl && (
              <img
                src={meta.posterUrl}
                alt=""
                style={{ width: "42px", height: "60px", objectFit: "cover", borderRadius: "4px" }}
              />
            )}
            <div>
              <div style={{ fontWeight: 600, fontSize: "1rem" }}>
                {meta?.title || torrent.name} {meta?.year ? `(${meta.year})` : ""}
              </div>
              {meta?.genres && (
                <div style={{ fontSize: "0.75rem", color: "var(--text-muted, #888)", marginTop: "0.2rem" }}>
                  {meta.genres.join(", ")}
                </div>
              )}
            </div>
          </div>

          <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
            {arrLink && (
              <a
                href={arrLink.url}
                target="_blank"
                rel="noopener noreferrer"
                className="btn btn-primary"
                style={{ fontSize: "0.8rem", textDecoration: "none" }}
                title={arrLink.label}
              >
                🔗 {arrLink.label} ↗
              </a>
            )}
            <a
              href={imdbUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="badge"
              style={{
                backgroundColor: "#f5c518",
                color: "#000",
                fontWeight: 700,
                fontSize: "0.75rem",
                padding: "0.25rem 0.5rem",
                textDecoration: "none",
              }}
              title="Open on IMDb"
            >
              IMDb ↗
            </a>
            {tmdbUrl && (
              <a
                href={tmdbUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="badge"
                style={{
                  backgroundColor: "#01b4e4",
                  color: "#fff",
                  fontWeight: 700,
                  fontSize: "0.75rem",
                  padding: "0.25rem 0.5rem",
                  textDecoration: "none",
                }}
                title="Open on TMDb"
              >
                TMDb ↗
              </a>
            )}
            {prowlarrUrl && (
              <a
                href={prowlarrUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="badge badge-secondary"
                style={{ fontSize: "0.75rem", padding: "0.25rem 0.5rem", textDecoration: "none" }}
                title="Search on Prowlarr"
              >
                Prowlarr ↗
              </a>
            )}
          </div>
        </div>
      )}

      {/* Gamification & HNR Seeding Status Card */}
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          padding: "1rem",
          background: "rgba(25, 25, 25, 0.6)",
          border: "1px solid var(--border-light)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
          <span style={{ fontSize: "0.85rem", fontWeight: 600, color: "var(--text-secondary)" }}>
            Milestones Earned:
          </span>
          {badges.length === 0 ? (
            <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
              Seeding towards 1.0x Ratio...
            </span>
          ) : (
            badges.map((b, i) => (
              <span
                key={i}
                className="badge"
                style={{
                  backgroundColor: b.color,
                  color: "#fff",
                  fontSize: "0.75rem",
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "0.25rem",
                }}
                title={b.title}
              >
                <span>{b.icon}</span>
                <span>{b.label}</span>
              </span>
            ))
          )}
        </div>

        {/* HNR Seeding Minimum Requirement Meter */}
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
            HNR Requirement:
          </span>
          <span
            className={`badge ${hnr.isCleared ? "badge-success" : "badge-warning"}`}
            style={{ fontSize: "0.75rem" }}
            title={
              hnr.isCleared
                ? "Safe to remove or pause without private tracker penalties"
                : `Need ${formatSeconds(hnr.remainingSeconds)} more seed time or reach 1.0 ratio`
            }
          >
            {hnr.label}
          </span>
        </div>
      </div>

      <div className="detail-grid">
        <div className="card">
          <h3>Info</h3>
          <StatusRow label="Name">{torrent.name}</StatusRow>
          <StatusRow label="Info Hash" mono>
            {torrent.infoHash}
          </StatusRow>
          <StatusRow label="Size">{formatBytes(torrent.totalSize)}</StatusRow>
          <StatusRow label="Pieces">
            {torrent.pieceCount} x {formatBytes(torrent.pieceLength)}
          </StatusRow>
          <StatusRow label="Private">
            {torrent.isPrivate ? "Yes" : "No"}
          </StatusRow>
          <StatusRow label="Created">
            {formatDate(torrent.creationDate)}
          </StatusRow>
          {torrent.createdBy && (
            <StatusRow label="Created By">{torrent.createdBy}</StatusRow>
          )}
          {torrent.comment && (
            <StatusRow label="Comment">{torrent.comment}</StatusRow>
          )}
          {torrent.sourcePath && (
            <StatusRow label="Source Path" mono>
              {torrent.sourcePath}
            </StatusRow>
          )}
        </div>

        <div className="card">
          <h3>Stats</h3>
          <StatusRow label="Status">
            <span className={`badge badge-${torrent.status.toLowerCase()}`}>
              {torrent.status}
            </span>
          </StatusRow>
          <StatusRow label="Uploaded">{formatBytes(torrent.uploaded)}</StatusRow>
          <StatusRow label="Downloaded">
            {formatBytes(torrent.downloaded)}
          </StatusRow>
          <StatusRow label="Ratio">
            <span className={`badge ${torrent.ratio >= 2.0 ? "badge-success" : torrent.ratio >= 1.0 ? "badge-primary" : "badge-secondary"}`}>
              {formatRatio(torrent.ratio)}
            </span>
          </StatusRow>
          <StatusRow label="Seeding Time">
            {formatSeconds(torrent.seedingTime)}
          </StatusRow>
          <StatusRow label="Seeders">{torrent.seeders}</StatusRow>
          <StatusRow label="Leechers">{torrent.leechers}</StatusRow>
          <StatusRow label="Added">{formatDate(torrent.dateAdded)}</StatusRow>
          <StatusRow label="Last Active">
            {formatDate(torrent.lastActive)}
          </StatusRow>
        </div>
      </div>
    </div>
  );
}
