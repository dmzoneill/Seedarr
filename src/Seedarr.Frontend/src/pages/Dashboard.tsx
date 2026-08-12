import { useMemo } from "react";
import { Link } from "react-router";
import {
  useTorrents,
  useSeedingStats,
  useActiveSpeedLimits,
  useArrConnections,
  useIndexers,
  useDownloadClients,
  useDownloadHistory,
} from "../api/hooks";
import {
  formatBytes,
  formatSpeed,
  formatRatio,
  formatDate,
} from "../utils/formatters";
import { getMediaDeepLink } from "../utils/arrLinks";
import { calculateAchievements, calculateHnrStatus } from "../utils/milestones";
import HealthAlerts from "../components/HealthAlerts";
import SpeedGraph from "../components/SpeedGraph";
import { SkeletonGrid, SkeletonLine } from "../components/Skeleton";

const STATUS_COLORS: Record<string, string> = {
  Seeding: "var(--color-success, #27ae60)",
  Stopped: "var(--color-danger, #e74c3c)",
  Queued: "var(--color-warning, #f39c12)",
  Error: "#c0392b",
};

function StatusDonut({
  counts,
  total,
}: {
  counts: Record<string, number>;
  total: number;
}) {
  if (total === 0) return null;
  const entries = Object.entries(counts).filter(([, v]) => v > 0);
  let offset = 0;
  const radius = 40;
  const circumference = 2 * Math.PI * radius;

  return (
    <div
      className="card"
      style={{
        display: "flex",
        alignItems: "center",
        gap: 24,
        borderRadius: "8px",
        boxShadow:
          "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
      }}
    >
      <svg width={100} height={100} viewBox="0 0 100 100">
        {entries.map(([status, count]) => {
          const pct = count / total;
          const dashLength = pct * circumference;
          const dashOffset = -offset * circumference;
          offset += pct;
          return (
            <circle
              key={status}
              cx={50}
              cy={50}
              r={radius}
              fill="none"
              stroke={STATUS_COLORS[status] || "#666"}
              strokeWidth={16}
              strokeDasharray={`${dashLength} ${circumference - dashLength}`}
              strokeDashoffset={dashOffset}
              transform="rotate(-90 50 50)"
            />
          );
        })}
        <text
          x={50}
          y={54}
          textAnchor="middle"
          fontSize={16}
          fontWeight={700}
          fill="var(--color-text, #ccc)"
        >
          {total}
        </text>
      </svg>
      <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
        {entries.map(([status, count]) => (
          <div
            key={status}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 6,
              fontSize: 13,
            }}
          >
            <div
              style={{
                width: 10,
                height: 10,
                borderRadius: "50%",
                backgroundColor: STATUS_COLORS[status] || "#666",
              }}
            />
            <span>
              {status}: {count}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

function Dashboard() {
  const { data: torrents, isLoading, isError } = useTorrents();
  const {
    data: stats,
    isLoading: statsLoading,
    isError: statsError,
  } = useSeedingStats();
  const { data: activeLimits } = useActiveSpeedLimits();
  const { data: arrConnections } = useArrConnections();
  const { data: indexers } = useIndexers();
  const { data: downloadClients } = useDownloadClients();
  const { data: history } = useDownloadHistory();

  const achievements = useMemo(
    () => calculateAchievements(torrents, stats),
    [torrents, stats],
  );

  const hnrPendingCount = useMemo(() => {
    return (torrents ?? []).filter((t) => !calculateHnrStatus(t).isCleared)
      .length;
  }, [torrents]);

  const totalSize = (torrents ?? []).reduce((sum, t) => sum + t.totalSize, 0);
  const recent = [...(torrents ?? [])]
    .sort(
      (a, b) =>
        new Date(b.dateAdded).getTime() - new Date(a.dateAdded).getTime(),
    )
    .slice(0, 6);

  const statusCounts: Record<string, number> = {};
  (torrents ?? []).forEach((t) => {
    const s = t.status || "Unknown";
    statusCounts[s] = (statusCounts[s] || 0) + 1;
  });

  const topTrackers = useMemo(() => {
    const trackerCounts: Record<string, number> = {};
    (torrents ?? []).forEach((t) => {
      let domain = "No tracker";
      if (t.trackerUrl) {
        try {
          domain = new URL(t.trackerUrl).hostname;
        } catch {
          domain = t.trackerUrl;
        }
      }
      trackerCounts[domain] = (trackerCounts[domain] || 0) + 1;
    });
    return Object.entries(trackerCounts)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 5);
  }, [torrents]);

  return (
    <div>
      <div
        className="page-heading-row"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
        }}
      >
        <h1 className="page-heading" style={{ margin: 0 }}>
          Dashboard
        </h1>
      </div>

      <HealthAlerts />

      {/* Gamification / Seeding Mastery Header Widget */}
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1.25rem",
          background:
            "linear-gradient(90deg, rgba(200, 168, 78, 0.15) 0%, rgba(30, 30, 30, 0.8) 100%)",
          borderLeft: "4px solid var(--accent)",
          padding: "1rem 1.25rem",
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "1rem",
            flexWrap: "wrap",
          }}
        >
          <div style={{ fontSize: "2rem" }}>🏆</div>
          <div>
            <div
              style={{
                fontWeight: 700,
                fontSize: "1.05rem",
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
              }}
            >
              <span>
                Level {achievements.overallLevel}: {achievements.rankTitle}
              </span>
              <span
                className="badge badge-primary"
                style={{ fontSize: "0.75rem" }}
              >
                {achievements.unlockedCount}/{achievements.totalCount} Badges
              </span>
            </div>
            <div
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              {achievements.totalSwarmGuardians.length > 0 && (
                <span style={{ color: "#e67e22", fontWeight: 600 }}>
                  🛡️ Keeping {achievements.totalSwarmGuardians.length} rare
                  swarms alive •{" "}
                </span>
              )}
              <span>
                {hnrPendingCount} torrents working towards minimum seed time
              </span>
            </div>
          </div>
        </div>

        <Link
          to="/statistics"
          className="btn btn-outline"
          style={{ fontSize: "0.85rem", textDecoration: "none" }}
        >
          Hall of Fame & Buffers →
        </Link>
      </div>

      {statsLoading ? (
        <SkeletonGrid count={4} />
      ) : statsError ? (
        <p className="error">Failed to load data.</p>
      ) : (
        <div className="stats-grid">
          <div className="stat-card">
            <div className="stat-value">{stats?.activeTorrents ?? 0}</div>
            <div className="stat-label">Active Torrents</div>
          </div>
          <div className="stat-card">
            <div className="stat-value">
              {formatBytes(stats?.totalUploaded ?? 0)}
            </div>
            <div className="stat-label">Total Uploaded</div>
          </div>
          <div className="stat-card">
            <div className="stat-value">
              {formatRatio(stats?.averageRatio ?? 0)}
            </div>
            <div className="stat-label">Average Ratio</div>
          </div>
          <div className="stat-card">
            <div className="stat-value">{formatBytes(totalSize)}</div>
            <div className="stat-label">Total Size</div>
          </div>
        </div>
      )}

      {/* Arr & Client Ecosystem Integration Bar */}
      {((arrConnections && arrConnections.length > 0) ||
        (indexers && indexers.length > 0) ||
        (downloadClients && downloadClients.length > 0)) && (
        <div
          className="card"
          style={{
            marginBottom: "1.25rem",
            padding: "1.1rem 1.25rem",
            borderRadius: "8px",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "0.85rem",
            }}
          >
            <h3 style={{ margin: 0, border: "none", padding: 0 }}>
              Connected Ecosystem
            </h3>
            <Link
              to="/settings/connections"
              style={{
                fontSize: "0.82rem",
                color: "var(--accent)",
                textDecoration: "none",
                fontWeight: 500,
              }}
            >
              Manage Connections ⚙️
            </Link>
          </div>

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))",
              gap: "0.75rem",
            }}
          >
            {arrConnections?.map((conn) => (
              <div
                key={`arr-${conn.id}`}
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  padding: "0.6rem 0.85rem",
                  backgroundColor: "var(--bg-primary)",
                  borderRadius: "6px",
                  border: "1px solid var(--border-light)",
                  boxShadow: "0 2px 6px rgba(0, 0, 0, 0.2)",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.5rem",
                  }}
                >
                  <span style={{ fontSize: "1.2rem" }}>
                    {conn.arrType === "Sonarr"
                      ? "📺"
                      : conn.arrType === "Radarr"
                        ? "🎬"
                        : conn.arrType === "Lidarr"
                          ? "🎵"
                          : "📦"}
                  </span>
                  <div>
                    <div style={{ fontWeight: 600, fontSize: "0.85rem" }}>
                      {conn.name}
                    </div>
                    <div
                      style={{
                        fontSize: "0.7rem",
                        color: conn.enable
                          ? "var(--success)"
                          : "var(--text-muted)",
                      }}
                    >
                      {conn.enable ? "● Connected" : "○ Disabled"}
                    </div>
                  </div>
                </div>
                {conn.url && (
                  <a
                    href={conn.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="btn btn-small btn-outline"
                    style={{
                      fontSize: "0.75rem",
                      padding: "0.2rem 0.45rem",
                      textDecoration: "none",
                      borderRadius: "4px",
                    }}
                    title={`Open ${conn.name} Web UI`}
                  >
                    ↗
                  </a>
                )}
              </div>
            ))}

            {indexers
              ?.filter((i) => i.enable)
              .slice(0, 3)
              .map((idx) => (
                <div
                  key={`idx-${idx.id}`}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "0.6rem 0.85rem",
                    backgroundColor: "var(--bg-primary)",
                    borderRadius: "6px",
                    border: "1px solid var(--border-light)",
                    boxShadow: "0 2px 6px rgba(0, 0, 0, 0.2)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                    }}
                  >
                    <span style={{ fontSize: "1.2rem" }}>🔍</span>
                    <div>
                      <div style={{ fontWeight: 600, fontSize: "0.85rem" }}>
                        {idx.name}
                      </div>
                      <div
                        style={{ fontSize: "0.7rem", color: "var(--success)" }}
                      >
                        ● {idx.indexerType}
                      </div>
                    </div>
                  </div>
                  {idx.url && (
                    <a
                      href={idx.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="btn btn-small btn-outline"
                      style={{
                        fontSize: "0.75rem",
                        padding: "0.2rem 0.45rem",
                        textDecoration: "none",
                        borderRadius: "4px",
                      }}
                      title={`Open ${idx.name} Web UI`}
                    >
                      ↗
                    </a>
                  )}
                </div>
              ))}

            {downloadClients
              ?.filter((c) => c.enable)
              .map((client) => (
                <div
                  key={`client-${client.id}`}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "0.6rem 0.85rem",
                    backgroundColor: "var(--bg-primary)",
                    borderRadius: "6px",
                    border: "1px solid var(--border-light)",
                    boxShadow: "0 2px 6px rgba(0, 0, 0, 0.2)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                    }}
                  >
                    <span style={{ fontSize: "1.2rem" }}>⚡</span>
                    <div>
                      <div style={{ fontWeight: 600, fontSize: "0.85rem" }}>
                        {client.name}
                      </div>
                      <div
                        style={{ fontSize: "0.7rem", color: "var(--success)" }}
                      >
                        ● {client.clientType}
                      </div>
                    </div>
                  </div>
                  {client.host && (
                    <a
                      href={`${client.useSsl ? "https" : "http"}://${client.host}${client.port ? `:${client.port}` : ""}`}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="btn btn-small btn-outline"
                      style={{
                        fontSize: "0.75rem",
                        padding: "0.2rem 0.45rem",
                        textDecoration: "none",
                        borderRadius: "4px",
                      }}
                      title={`Open ${client.name} Web UI`}
                    >
                      ↗
                    </a>
                  )}
                </div>
              ))}
          </div>
        </div>
      )}

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
          gap: "1rem",
          marginBottom: "1.25rem",
        }}
      >
        <StatusDonut counts={statusCounts} total={torrents?.length ?? 0} />

        {activeLimits && (
          <div
            className="card"
            style={{
              borderRadius: "8px",
              boxShadow:
                "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            }}
          >
            <h3 style={{ marginBottom: "0.75rem", border: "none", padding: 0 }}>
              Speed Schedule
            </h3>
            <div className="status-row">
              <span className="status-label">Active</span>
              <span className="status-value">
                {activeLimits.isScheduleActive ? (
                  <span className="badge badge-seeding">
                    {activeLimits.activeScheduleName}
                  </span>
                ) : (
                  <span className="badge badge-stopped">None</span>
                )}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Upload Limit</span>
              <span className="status-value">
                {activeLimits.maxUploadSpeed > 0
                  ? formatSpeed(activeLimits.maxUploadSpeed)
                  : "Global"}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Download Limit</span>
              <span className="status-value">
                {activeLimits.maxDownloadSpeed > 0
                  ? formatSpeed(activeLimits.maxDownloadSpeed)
                  : "Global"}
              </span>
            </div>
          </div>
        )}

        {topTrackers.length > 0 && (
          <div
            className="card"
            style={{
              borderRadius: "8px",
              boxShadow:
                "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            }}
          >
            <h3 style={{ marginBottom: "0.75rem", border: "none", padding: 0 }}>
              Top Trackers
            </h3>
            {topTrackers.map(([domain, count]) => (
              <div key={domain} className="status-row">
                <Link
                  to={`/torrents?tracker=${encodeURIComponent(domain)}`}
                  className="status-label"
                  style={{
                    fontSize: "0.82rem",
                    textDecoration: "none",
                    color: "inherit",
                    fontWeight: 500,
                  }}
                  title="Filter torrents by tracker"
                >
                  {domain} ↗
                </Link>
                <span className="status-value" style={{ fontWeight: 600 }}>
                  {count}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>

      <SpeedGraph />

      {/* Recent Torrents with Media Metadata & Arr Links */}
      <div
        className="card"
        style={{
          marginTop: "1.25rem",
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "0.75rem",
          }}
        >
          <h3 style={{ margin: 0, border: "none", padding: 0 }}>
            Recent Torrents
          </h3>
          <Link
            to="/torrents"
            style={{
              fontSize: "0.82rem",
              color: "var(--accent)",
              textDecoration: "none",
              fontWeight: 500,
            }}
          >
            View All Torrents →
          </Link>
        </div>

        {isLoading && (
          <>
            {[0, 1, 2].map((i) => (
              <div key={i} className="status-row">
                <SkeletonLine width="50%" height="0.85rem" />
                <SkeletonLine width="20%" height="0.85rem" />
              </div>
            ))}
          </>
        )}
        {!isLoading && isError && <p className="error">Failed to load data.</p>}
        {!isLoading && !isError && recent.length === 0 && (
          <p className="loading">No torrents added yet.</p>
        )}
        {recent.map((t) => {
          const match = history?.find(
            (h) =>
              (t.infoHash &&
                h.infoHash?.toLowerCase() === t.infoHash.toLowerCase()) ||
              h.title?.toLowerCase() === t.name?.toLowerCase(),
          );
          const meta = match?.metadata;
          const arrLink = match
            ? getMediaDeepLink(match, arrConnections)
            : null;

          return (
            <div
              key={t.id}
              className="status-row"
              style={{ alignItems: "center", padding: "0.5rem 0" }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.75rem",
                  minWidth: 0,
                  flex: 1,
                }}
              >
                {meta?.posterUrl ? (
                  <img
                    src={meta.posterUrl}
                    alt=""
                    style={{
                      width: "32px",
                      height: "46px",
                      objectFit: "cover",
                      borderRadius: "4px",
                      border: "1px solid rgba(255, 255, 255, 0.1)",
                      flexShrink: 0,
                    }}
                  />
                ) : (
                  <span style={{ fontSize: "1.4rem", flexShrink: 0 }}>📦</span>
                )}
                <div style={{ minWidth: 0, flex: 1 }}>
                  <Link
                    to="/torrents"
                    style={{
                      fontWeight: 500,
                      fontSize: "0.85rem",
                      textDecoration: "none",
                      color: "inherit",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                      display: "block",
                    }}
                    title={t.name}
                  >
                    {meta?.title || t.name} {meta?.year ? `(${meta.year})` : ""}
                  </Link>
                  <div
                    style={{
                      fontSize: "0.7rem",
                      color: "var(--text-muted)",
                      display: "flex",
                      gap: "0.5rem",
                    }}
                  >
                    <span>{formatBytes(t.totalSize)}</span>
                    {t.trackerUrl && (
                      <span>• {new URL(t.trackerUrl).hostname}</span>
                    )}
                  </div>
                </div>
              </div>

              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                  flexShrink: 0,
                }}
              >
                {arrLink && (
                  <a
                    href={arrLink.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="badge badge-secondary"
                    style={{
                      fontSize: "0.7rem",
                      padding: "0.15rem 0.4rem",
                      textDecoration: "none",
                      color: "inherit",
                      borderRadius: "4px",
                    }}
                    title={arrLink.label}
                  >
                    {arrLink.appName} ↗
                  </a>
                )}
                <span
                  className={`badge badge-${(t.status ?? "unknown").toLowerCase()}`}
                  style={{ borderRadius: "4px" }}
                >
                  {t.status}
                </span>
                <span
                  style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}
                >
                  {formatDate(t.dateAdded)}
                </span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export default Dashboard;
