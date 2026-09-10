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
import { useTranslation } from "../i18n";

export interface StatusBarProps {
  connected?: boolean;
  isReconnecting?: boolean;
}

export function StatusBar({ connected = true, isReconnecting = false }: StatusBarProps = {}) {
  const { t } = useTranslation();
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
          {systemStatus?.version
            ? `v${systemStatus.version}`
            : t("common.loading", undefined, "Loading...")}
        </span>
        <span className="status-bar-item">
          <ActivityIcon size={14} />{" "}
          {t("statusBar.uptime", undefined, "Uptime")}{" "}
          {systemStatus
            ? formatUptime(
                systemStatus.uptimeSeconds ??
                  (systemStatus.startTime
                    ? Math.floor(
                        (Date.now() -
                          new Date(systemStatus.startTime).getTime()) /
                          1000,
                      )
                    : 0),
              )
            : "..."}
        </span>
        <span
          className="status-bar-item"
          style={{ color: hasIssues ? "var(--danger)" : "var(--success)" }}
        >
          {hasIssues ? <ErrorIcon size={14} /> : <InfoIcon size={14} />}
          {t("statusBar.health", undefined, "Health")}{" "}
          {hasIssues
            ? issuesCount === 1
              ? t("statusBar.healthIssues", { count: issuesCount }, "1 Issue")
              : t(
                  "statusBar.healthIssuesPlural",
                  { count: issuesCount },
                  `${issuesCount} Issues`,
                )
            : t("statusBar.healthOk", undefined, "All Systems Operational")}
        </span>
        {(connected !== undefined || isReconnecting !== undefined) && (
          <span
            className="status-bar-item"
            style={{
              color: isReconnecting
                ? "var(--accent, #ffd166)"
                : connected
                  ? "var(--success)"
                  : "var(--danger)",
            }}
          >
            <WifiIcon size={14} />{" "}
            {isReconnecting
              ? t("statusBar.reconnecting", undefined, "Reconnecting...")
              : connected
                ? t("statusBar.connected", undefined, "Connected")
                : t("statusBar.disconnected", undefined, "Disconnected")}
          </span>
        )}

        <div className="status-bar-separator" style={{ flexGrow: 1 }} />

        <span className="status-bar-item">
          <SeedingIcon size={14} /> {t("statusBar.active", undefined, "Active")}{" "}
          {stats?.activeTorrents ?? 0}
        </span>
        <span className="status-bar-item status-bar-download">
          <DownloadIcon size={14} /> {formatSpeed(downloadSpeed)}
        </span>
        <span className="status-bar-item status-bar-upload">
          <UploadIcon size={14} /> {formatSpeed(uploadSpeed)}
        </span>
        <span className="status-bar-item">
          <UsersIcon size={14} /> {t("statusBar.peers", undefined, "Peers")}{" "}
          {totalSeeders} / {totalPeers}
        </span>
        <span className="status-bar-item">
          <UploadIcon size={14} />{" "}
          {t("statusBar.totalUp", undefined, "Total Up")}{" "}
          {formatBytes(stats?.totalUploaded ?? 0)}
        </span>
        <span className="status-bar-item">
          <DownloadIcon size={14} />{" "}
          {t("statusBar.totalDown", undefined, "Total Down")}{" "}
          {formatBytes(stats?.totalDownloaded ?? 0)}
        </span>
        <span className="status-bar-item">
          {t("statusBar.ratio", undefined, "Ratio")}{" "}
          {formatRatio(stats?.averageRatio ?? 0)}
        </span>
        <span className="status-bar-item">
          <WifiIcon size={14} /> {t("statusBar.ip", undefined, "Ip")}{" "}
          {network?.externalIp || "..."}
        </span>
      </div>
    </footer>
  );
}

export default StatusBar;
