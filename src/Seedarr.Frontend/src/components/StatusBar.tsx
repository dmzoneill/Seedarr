import { useRef, useEffect, useState } from "react";
import {
  useSeedingStats,
  useNetworkStatus,
  useTorrents,
  useSystemStatus,
  useHealthChecks,
} from "../api/hooks";
import {
  formatBytes,
  formatSpeed,
  formatRatio,
  formatUptime,
} from "../utils/formatters";
import {
  SeedingIcon,
  UploadIcon,
  DownloadIcon,
  UsersIcon,
  WifiIcon,
  ActivityIcon,
  InfoIcon,
  ErrorIcon,
} from "./icons/UIIcons";

function StatusBar() {
  const { data: stats } = useSeedingStats();
  const { data: network } = useNetworkStatus();
  const { data: torrents } = useTorrents();
  const { data: systemStatus } = useSystemStatus();
  const { data: healthChecks } = useHealthChecks();

  // Derive instantaneous speed from polling deltas (same approach as Activity.tsx)
  const prevRef = useRef<{
    totalUploaded: number;
    totalDownloaded: number;
    timestamp: number;
  } | null>(null);
  const [speed, setSpeed] = useState({ uploadSpeed: 0, downloadSpeed: 0 });

  useEffect(() => {
    if (!stats) return;

    const now = Date.now();
    const prev = prevRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;
      if (timeDelta >= 1) {
        setSpeed({
          uploadSpeed: Math.max(
            0,
            (stats.totalUploaded - prev.totalUploaded) / timeDelta,
          ),
          downloadSpeed: Math.max(
            0,
            (stats.totalDownloaded - prev.totalDownloaded) / timeDelta,
          ),
        });
      }
    }

    prevRef.current = {
      totalUploaded: stats.totalUploaded,
      totalDownloaded: stats.totalDownloaded,
      timestamp: now,
    };
  }, [stats]);

  const { uploadSpeed, downloadSpeed } = speed;

  // Aggregate peer counts across all torrents
  const totalSeeders = (torrents ?? []).reduce(
    (sum, t) => sum + (t.seeders ?? 0),
    0,
  );
  const totalLeechers = (torrents ?? []).reduce(
    (sum, t) => sum + (t.leechers ?? 0),
    0,
  );
  const totalPeers = totalSeeders + totalLeechers;

  const hasIssues =
    healthChecks &&
    healthChecks.some((c) => c.type === "Warning" || c.type === "Error");
  const issuesCount = hasIssues
    ? healthChecks.filter((c) => c.type === "Warning" || c.type === "Error")
        .length
    : 0;

  return (
    <footer className="status-bar">
      <div className="status-bar-content">
        <span className="status-bar-item">
          <InfoIcon size={14} />{" "}
          {systemStatus?.version ? `v${systemStatus.version}` : "Loading..."}
        </span>
        <span className="status-bar-item">
          <ActivityIcon size={14} /> Uptime:{" "}
          {systemStatus ? formatUptime(systemStatus.uptimeSeconds) : "..."}
        </span>
        <span
          className="status-bar-item"
          style={{ color: hasIssues ? "var(--danger)" : "var(--success)" }}
        >
          {hasIssues ? <ErrorIcon size={14} /> : <InfoIcon size={14} />}
          Health:{" "}
          {hasIssues
            ? `${issuesCount} Issue${issuesCount !== 1 ? "s" : ""}`
            : "OK"}
        </span>

        <div className="status-bar-separator" style={{ flexGrow: 1 }} />

        <span className="status-bar-item">
          <SeedingIcon size={14} /> Active: {stats?.activeTorrents ?? 0}
        </span>
        <span className="status-bar-item status-bar-upload">
          <UploadIcon size={14} /> {formatSpeed(uploadSpeed)}
        </span>
        <span className="status-bar-item status-bar-download">
          <DownloadIcon size={14} /> {formatSpeed(downloadSpeed)}
        </span>
        <span className="status-bar-item">
          <UsersIcon size={14} /> Peers: {totalSeeders} / {totalPeers}
        </span>
        <span className="status-bar-item">
          <UploadIcon size={14} /> Total Up:{" "}
          {formatBytes(stats?.totalUploaded ?? 0)}
        </span>
        <span className="status-bar-item">
          <DownloadIcon size={14} /> Total Down:{" "}
          {formatBytes(stats?.totalDownloaded ?? 0)}
        </span>
        <span className="status-bar-item">
          Ratio: {formatRatio(stats?.averageRatio ?? 0)}
        </span>
        <span className="status-bar-item">
          <WifiIcon size={14} /> IP: {network?.externalIp ?? "..."}
        </span>
      </div>
    </footer>
  );
}

export default StatusBar;
