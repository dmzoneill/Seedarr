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
  CpuIcon,
} from "./icons/UIIcons";
import { useTranslation } from "../i18n";

export interface StatusBarProps {
  connected?: boolean;
  isReconnecting?: boolean;
}

export function StatusBar({
  connected = true,
  isReconnecting = false,
}: StatusBarProps = {}) {
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

        prevRef.current = {
          totalUploaded: stats.totalUploaded,
          totalDownloaded: stats.totalDownloaded,
          timestamp: now,
        };
      }
    } else {
      prevRef.current = {
        totalUploaded: stats.totalUploaded,
        totalDownloaded: stats.totalDownloaded,
        timestamp: now,
      };
    }
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

  const isWarningOrError = (type?: string) => {
    const t = type?.toLowerCase();
    return t === "warning" || t === "error";
  };

  const hasIssues =
    healthChecks && healthChecks.some((c) => isWarningOrError(c.type));
  const issuesCount = hasIssues
    ? healthChecks.filter((c) => isWarningOrError(c.type)).length
    : 0;

  const formattedUptime = systemStatus
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
    : "...";

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
          {t(
            "statusbar.uptime",
            { uptime: formattedUptime },
            `Uptime: ${formattedUptime}`,
          )}
        </span>
        {systemStatus?.cpuUsagePercentage != null && (
          <span className="status-bar-item">
            <CpuIcon size={14} /> CPU:{" "}
            {systemStatus.cpuUsagePercentage.toFixed(1)}%
          </span>
        )}
        <span
          className="status-bar-item"
          style={{ color: hasIssues ? "var(--danger)" : "var(--success)" }}
        >
          {hasIssues ? <ErrorIcon size={14} /> : <InfoIcon size={14} />}
          {t(
            "statusbar.health",
            {
              health: hasIssues
                ? issuesCount === 1
                  ? t("statusbar.healthIssues", { count: issuesCount, issuesCount }, "1 Issue")
                  : t(
                      "statusbar.healthIssuesPlural",
                      { count: issuesCount, issuesCount },
                      `${issuesCount} Issues`,
                    )
                : t("statusbar.healthOk", undefined, "All Systems Operational"),
            },
            `Health: ${
              hasIssues
                ? `${issuesCount} Issue${issuesCount !== 1 ? "s" : ""}`
                : "All Systems Operational"
            }`,
          )}
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
              ? t("statusbar.reconnecting", undefined, "Reconnecting...")
              : connected
                ? t("statusbar.connected", undefined, "Connected")
                : t("statusbar.disconnected", undefined, "Disconnected")}
          </span>
        )}

        <div className="status-bar-separator" style={{ flexGrow: 1 }} />

        <span className="status-bar-item">
          <SeedingIcon size={14} />{" "}
          {t(
            "statusbar.active",
            { count: stats?.activeTorrents ?? 0 },
            `Active: ${stats?.activeTorrents ?? 0}`,
          )}
        </span>
        <span className="status-bar-item status-bar-download">
          <DownloadIcon size={14} /> {formatSpeed(downloadSpeed)}
        </span>
        <span className="status-bar-item status-bar-upload">
          <UploadIcon size={14} /> {formatSpeed(uploadSpeed)}
        </span>
        <span className="status-bar-item">
          <UsersIcon size={14} />{" "}
          {t(
            "statusbar.peers",
            { seeders: totalSeeders, total: totalPeers },
            `Peers: ${totalSeeders} / ${totalPeers}`,
          )}
        </span>
        <span className="status-bar-item">
          <UploadIcon size={14} />{" "}
          {t(
            "statusbar.totalUp",
            { bytes: formatBytes(stats?.totalUploaded ?? 0) },
            `Total Up: ${formatBytes(stats?.totalUploaded ?? 0)}`,
          )}
        </span>
        <span className="status-bar-item">
          <DownloadIcon size={14} />{" "}
          {t(
            "statusbar.totalDown",
            { bytes: formatBytes(stats?.totalDownloaded ?? 0) },
            `Total Down: ${formatBytes(stats?.totalDownloaded ?? 0)}`,
          )}
        </span>
        <span className="status-bar-item">
          {t(
            "statusbar.ratio",
            {
              ratio: formatRatio(
                stats?.totalDownloaded && stats.totalDownloaded > 0
                  ? stats.totalUploaded / stats.totalDownloaded
                  : 0,
              ),
            },
            `Ratio: ${formatRatio(
              stats?.totalDownloaded && stats.totalDownloaded > 0
                ? stats.totalUploaded / stats.totalDownloaded
                : 0,
            )}`,
          )}
        </span>
        <span className="status-bar-item">
          <WifiIcon size={14} />{" "}
          {t(
            "statusbar.ip",
            { ip: network?.externalIp || "..." },
            `IP: ${network?.externalIp || "..."}`,
          )}
        </span>
      </div>
    </footer>
  );
}

export default StatusBar;
