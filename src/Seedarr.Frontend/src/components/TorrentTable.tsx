import React, {
  useState,
  useCallback,
  useRef,
  useEffect,
  useMemo,
} from "react";
import { useVirtualizer, type VirtualItem } from "@tanstack/react-virtual";
import { useTranslation } from "../i18n";
import { useTorrentStore, applyTelemetry } from "../stores/useTorrentStore";
import {
  useTorrents,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useBulkTorrentAction,
  useUpdateTorrent,
  useAnnounceTorrent,
  useRecheckTorrent,
  useMoveTorrentQueue,
  useDownloadHistory,
  useArrConnections,
} from "../api/hooks";
import {
  formatBytes,
  formatSpeed,
  formatRatio,
  formatDate,
  formatSeconds,
  formatEta,
  extractTrackerDomain,
} from "../utils/formatters";
import { getMediaDeepLink } from "../utils/arrLinks";
import { getTorrentBadges } from "../utils/milestones";
import { filterTorrents } from "../utils/filterUtils";
import { SkeletonTableRow } from "./Skeleton";
import TorrentContextMenu from "./TorrentContextMenu";
import AddTorrentModal from "./AddTorrentModal";
import DeleteTorrentModal from "./DeleteTorrentModal";
import TrackerFavicon from "./TrackerFavicon";
import MediaArtwork from "./MediaArtwork";
import type { Torrent, DownloadHistoryEntry } from "../api/types";
import {
  ALL_COLUMNS,
  COLUMN_I18N_KEYS,
  DEFAULT_VISIBLE,
  loadVisibleColumns,
  saveVisibleColumns,
  loadTableSortPreferences,
  saveTableSortPreferences,
  resetTableSortPreferences,
  loadColumnOrder,
  saveColumnOrder,
  loadColumnWidths,
  saveColumnWidths,
  SORT_KEY_STORAGE,
  SORT_ASC_STORAGE,
  ColumnKey,
  ColumnDef,
} from "../pages/torrentindex/columnPreferences";

export const getColumnLabel = (
  key: ColumnKey,
  t: (k: string, ...args: any[]) => string,
): string => {
  const i18nKey = COLUMN_I18N_KEYS[key] || `torrents.table.${key}`;
  const def = ALL_COLUMNS.find((c) => c.key === key);
  return t(i18nKey, undefined, def?.label || key);
};

export {
  ALL_COLUMNS,
  COLUMN_I18N_KEYS,
  DEFAULT_VISIBLE,
  loadVisibleColumns,
  saveVisibleColumns,
  loadTableSortPreferences,
  saveTableSortPreferences,
  resetTableSortPreferences,
  SORT_KEY_STORAGE,
  SORT_ASC_STORAGE,
};
export type { ColumnKey, ColumnDef };

type SortKey = ColumnKey;

const STRING_COLUMNS: ReadonlySet<ColumnKey> = new Set([
  "name",
  "status",
  "trackerUrl",
  "dateAdded",
  "lastActive",
  "creationDate",
  "createdBy",
  "comment",
  "label",
  "infoHash",
]);

interface ContextMenuState {
  x: number;
  y: number;
  torrent: Torrent | null;
  selectedTorrents?: Torrent[];
}

function getSortValue(
  t: Torrent,
  key: SortKey,
): string | number | null | undefined {
  switch (key) {
    case "#":
      return t.sortOrder ?? t.id;
    case "queuePosition":
      return t.sortOrder ?? t.id;
    case "trackerUrl":
      return t.trackerUrl ?? "";
    case "lastActive":
      return t.lastActive ?? "";
    case "creationDate":
      return t.creationDate ?? "";
    case "comment":
      return t.comment ?? "";
    case "createdBy":
      return t.createdBy ?? "";
    case "label":
      return t.label ?? "";
    case "infoHash":
      return t.infoHash ?? "";
    case "isPrivate":
      return t.isPrivate ? 1 : 0;
    case "superSeeding":
      return t.superSeeding ? 1 : 0;
    case "sequentialDownload":
      return t.sequentialDownload ? 1 : 0;
    case "forceStart":
      return t.forceStart ? 1 : 0;
    case "active":
      return t.active ? 1 : 0;
    default:
      return (t as unknown as Record<string, unknown>)[key] as
        string | number | null | undefined;
  }
}

// ---------------------------------------------------------------------------
// Leaf Telemetry Cell Components (Granular React.memo Subscriptions)
// ---------------------------------------------------------------------------

export const TorrentSpeedCell: React.FC<{
  torrentId: number;
  fallbackSpeed?: number;
  type: "download" | "upload";
}> = React.memo(({ torrentId, fallbackSpeed = 0, type }) => {
  const speed = useTorrentStore((state) => {
    const tel = state.telemetry[torrentId];
    if (!tel) return fallbackSpeed;
    const effectiveStatus = (tel.status || "").toLowerCase();
    const isInactive =
      effectiveStatus === "paused" ||
      effectiveStatus === "stopped" ||
      effectiveStatus === "error" ||
      effectiveStatus === "queued";
    if (isInactive) return 0;
    return type === "download"
      ? (tel.downloadSpeed ?? fallbackSpeed)
      : (tel.uploadSpeed ?? fallbackSpeed);
  });

  const isDownload = type === "download";
  return (
    <span
      style={{
        color:
          speed > 0
            ? isDownload
              ? "var(--success, #22c55e)"
              : "var(--accent, #ffd166)"
            : "var(--text-muted, #7e8092)",
        fontWeight: speed > 0 ? 600 : 400,
      }}
    >
      {formatSpeed(speed)}
    </span>
  );
});
TorrentSpeedCell.displayName = "TorrentSpeedCell";

export const TorrentProgressCell: React.FC<{
  torrentId: number;
  fallbackProgress?: number;
  fallbackStatus?: string;
  fallbackRatio?: number;
}> = React.memo(
  ({ torrentId, fallbackProgress = 0, fallbackStatus, fallbackRatio = 0 }) => {
    const progress = useTorrentStore(
      (state) => state.telemetry[torrentId]?.progress ?? fallbackProgress,
    );
    const status = useTorrentStore(
      (state) => state.telemetry[torrentId]?.status ?? fallbackStatus,
    );
    const ratio = useTorrentStore(
      (state) => state.telemetry[torrentId]?.ratio ?? fallbackRatio,
    );

    const rawPct = Math.min(100, Math.max(0, progress * 100));
    const isChecking = (status || "").toLowerCase() === "checking";

    return (
      <div
        className="torrent-progress"
        role="progressbar"
        aria-valuenow={Math.round(rawPct)}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuetext={`${rawPct.toFixed(1)}% downloaded, ratio ${formatRatio(ratio)}`}
        style={{
          display: "flex",
          alignItems: "center",
          gap: "8px",
          minWidth: 120,
        }}
      >
        <div
          style={{
            flex: 1,
            height: 6,
            backgroundColor: "rgba(255, 255, 255, 0.1)",
            borderRadius: 3,
            overflow: "hidden",
          }}
        >
          <div
            className="torrent-progress-fill"
            style={{
              width: `${rawPct}%`,
              height: "100%",
              backgroundColor: isChecking
                ? "var(--info, #38bdf8)"
                : rawPct >= 100
                  ? "var(--success, #22c55e)"
                  : "var(--accent, #ffd166)",
              transition: "width 0.3s",
            }}
          />
        </div>
        <span
          className="torrent-progress-text"
          style={{
            fontSize: "0.75rem",
            fontWeight: 600,
            minWidth: 44,
            textAlign: "right",
            color: isChecking ? "var(--info, #38bdf8)" : undefined,
          }}
        >
          {rawPct.toFixed(1)}%
        </span>
      </div>
    );
  },
);
TorrentProgressCell.displayName = "TorrentProgressCell";

export const TorrentStatusCell: React.FC<{
  torrentId: number;
  fallbackStatus?: string;
  fallbackProgress?: number;
  fallbackDownloadSpeed?: number;
  fallbackUploadSpeed?: number;
  fallbackEta?: number;
  isVpnPaused?: boolean;
}> = React.memo(
  ({
    torrentId,
    fallbackStatus = "idle",
    fallbackProgress = 0,
    fallbackDownloadSpeed = 0,
    fallbackUploadSpeed = 0,
    fallbackEta,
    isVpnPaused = false,
  }) => {
    const { t } = useTranslation();
    const rawStatus = useTorrentStore(
      (state) => state.telemetry[torrentId]?.status ?? fallbackStatus,
    );
    const progress = useTorrentStore(
      (state) => state.telemetry[torrentId]?.progress ?? fallbackProgress,
    );
    const downloadSpeed = useTorrentStore(
      (state) =>
        state.telemetry[torrentId]?.downloadSpeed ?? fallbackDownloadSpeed,
    );
    const uploadSpeed = useTorrentStore(
      (state) => state.telemetry[torrentId]?.uploadSpeed ?? fallbackUploadSpeed,
    );
    const eta = useTorrentStore(
      (state) => state.telemetry[torrentId]?.eta ?? fallbackEta,
    );

    const pct = Math.min((progress ?? 0) * 100, 100);
    const st = (rawStatus || "idle").toLowerCase();
    const etaStr = eta != null ? formatEta(eta) : "∞";
    const ariaLabel = `${rawStatus}: ${pct.toFixed(1)}% complete, Down: ${formatSpeed(downloadSpeed)}, Up: ${formatSpeed(uploadSpeed)}, ETA: ${etaStr}`;

    if (isVpnPaused) {
      return (
        <span className="badge badge-vpn-paused" aria-label={ariaLabel}>
          {t(
            "torrents.pausedVpnKillSwitch",
            undefined,
            "Paused (VPN Kill Switch)",
          )}
        </span>
      );
    }

    const isQueuedRecheck =
      st === "queuedforchecking" ||
      st === "checking_queued" ||
      st === "queued_check";

    const statusLabel =
      st === "checking"
        ? `${t("torrentStatus.checking", "Checking")} (${pct.toFixed(1)}%)`
        : isQueuedRecheck
          ? t("torrentStatus.queuedforchecking", "Queued for Recheck")
          : t(
              `torrents.${st}`,
              undefined,
              rawStatus === "QueuedForChecking"
                ? "Queued for Recheck"
                : rawStatus || "Idle",
            );

    return (
      <span
        className={`badge badge-${st}`}
        aria-label={ariaLabel}
        style={{
          fontWeight: 600,
          fontSize: "0.72rem",
          padding: "0.15rem 0.5rem",
          textTransform: "capitalize",
        }}
      >
        {statusLabel}
      </span>
    );
  },
);
TorrentStatusCell.displayName = "TorrentStatusCell";

export const TorrentEtaCell: React.FC<{
  torrentId: number;
  totalSize: number;
  fallbackEta?: number;
  fallbackProgress?: number;
  fallbackSpeed?: number;
  fallbackStatus?: string;
}> = React.memo(
  ({
    torrentId,
    totalSize,
    fallbackEta,
    fallbackProgress = 0,
    fallbackSpeed = 0,
    fallbackStatus,
  }) => {
    const { t } = useTranslation();
    const eta = useTorrentStore(
      (state) => state.telemetry[torrentId]?.eta ?? fallbackEta,
    );
    const progress = useTorrentStore(
      (state) => state.telemetry[torrentId]?.progress ?? fallbackProgress,
    );
    const downloadSpeed = useTorrentStore((state) => {
      const tel = state.telemetry[torrentId];
      if (!tel) return fallbackSpeed;
      const st = (tel.status || fallbackStatus || "").toLowerCase();
      const isInactive =
        st === "paused" ||
        st === "stopped" ||
        st === "error" ||
        st === "queued";
      return isInactive ? 0 : (tel.downloadSpeed ?? fallbackSpeed);
    });
    const status = useTorrentStore(
      (state) => state.telemetry[torrentId]?.status ?? fallbackStatus,
    );

    if (progress >= 1.0) {
      return <span>{t("torrents.table.done", undefined, "Done")}</span>;
    }
    const isInactive =
      status === "paused" ||
      status === "stopped" ||
      status === "error" ||
      status === "queued";
    if (isInactive) {
      return <span>∞</span>;
    }

    const etaSec =
      eta && eta > 0
        ? typeof eta === "number"
          ? eta
          : Number(eta)
        : downloadSpeed > 0
          ? Math.floor((totalSize * (1 - (progress || 0))) / downloadSpeed)
          : 0;

    return <span>{etaSec > 0 ? formatSeconds(etaSec) : "∞"}</span>;
  },
);
TorrentEtaCell.displayName = "TorrentEtaCell";

export const TorrentDownloadedCell: React.FC<{
  torrentId: number;
  totalSize: number;
  fallbackDownloaded?: number;
  fallbackProgress?: number;
}> = React.memo(
  ({ torrentId, totalSize, fallbackDownloaded, fallbackProgress = 0 }) => {
    const downloaded = useTorrentStore((state) => {
      const tel = state.telemetry[torrentId];
      if (tel?.downloaded !== undefined) return tel.downloaded;
      if (tel?.progress !== undefined) return totalSize * tel.progress;
      if (fallbackDownloaded !== undefined) return fallbackDownloaded;
      return totalSize * fallbackProgress;
    });

    return <span>{formatBytes(downloaded)}</span>;
  },
);
TorrentDownloadedCell.displayName = "TorrentDownloadedCell";

export const TorrentUploadedCell: React.FC<{
  torrentId: number;
  fallbackUploaded?: number;
}> = React.memo(({ torrentId, fallbackUploaded = 0 }) => {
  const uploaded = useTorrentStore(
    (state) => state.telemetry[torrentId]?.uploaded ?? fallbackUploaded,
  );
  return <span>{formatBytes(uploaded)}</span>;
});
TorrentUploadedCell.displayName = "TorrentUploadedCell";

export const TorrentRatioCell: React.FC<{
  torrentId: number;
  fallbackRatio?: number;
}> = React.memo(({ torrentId, fallbackRatio = 0 }) => {
  const ratio = useTorrentStore(
    (state) => state.telemetry[torrentId]?.ratio ?? fallbackRatio,
  );
  return (
    <span
      className={`badge ${ratio >= 2.0 ? "badge-success" : ratio >= 1.0 ? "badge-primary" : "badge-secondary"}`}
      style={{
        fontWeight: 600,
      }}
    >
      {formatRatio(ratio || 0)}
    </span>
  );
});
TorrentRatioCell.displayName = "TorrentRatioCell";

export const TorrentSeedsPeersCell: React.FC<{
  torrentId: number;
  type: "seeds" | "peers";
  fallbackCount?: number;
  fallbackStatus?: string;
}> = React.memo(({ torrentId, type, fallbackCount = 0, fallbackStatus }) => {
  const count = useTorrentStore((state) => {
    const tel = state.telemetry[torrentId];
    const status = (tel?.status ?? fallbackStatus ?? "").toLowerCase();
    const isInactive =
      status === "paused" ||
      status === "stopped" ||
      status === "error" ||
      status === "queued";
    if (isInactive) return 0;
    return type === "seeds"
      ? (tel?.seeders ?? fallbackCount)
      : (tel?.leechers ?? fallbackCount);
  });

  if (type === "seeds") {
    return (
      <span style={{ color: "var(--success, #22c55e)", fontWeight: 600 }}>
        {count}
      </span>
    );
  }
  return <span>{count}</span>;
});
TorrentSeedsPeersCell.displayName = "TorrentSeedsPeersCell";

export const TorrentNameCell: React.FC<{
  torrent: Torrent;
  historyMatch?: DownloadHistoryEntry;
  arrConnections?: any;
}> = React.memo(({ torrent, historyMatch, arrConnections }) => {
  const { t } = useTranslation();
  const meta = historyMatch?.metadata;
  const arrLink = historyMatch
    ? getMediaDeepLink(historyMatch, arrConnections)
    : null;
  const badges = getTorrentBadges(torrent);
  const posterSrc =
    meta?.posterUrl ||
    torrent.posterUrl ||
    (torrent as any).artworkUrl ||
    torrent.bannerUrl;

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: "0.5rem",
        minWidth: 0,
      }}
    >
      {posterSrc && (
        <MediaArtwork
          src={posterSrc}
          alt={meta?.title || torrent.name}
          title={meta?.title || torrent.name}
          category={meta?.mediaType || torrent.source || undefined}
          width="20px"
          height="28px"
          aspectRatio="auto"
          borderRadius="2px"
          style={{ flexShrink: 0 }}
        />
      )}
      <span
        style={{
          fontWeight: 500,
          overflow: "hidden",
          textOverflow: "ellipsis",
          whiteSpace: "nowrap",
        }}
        title={torrent.name}
      >
        {meta?.title || torrent.name} {meta?.year ? `(${meta.year})` : ""}
      </span>
      {badges.map((b, i) => (
        <span
          key={i}
          title={b.title}
          style={{
            fontSize: "0.75rem",
            cursor: "help",
            display: "inline-flex",
            alignItems: "center",
          }}
        >
          {b.icon}
        </span>
      ))}
      {arrLink && (
        <a
          href={arrLink.url}
          target="_blank"
          rel="noopener noreferrer"
          className="badge badge-secondary"
          style={{
            fontSize: "0.65rem",
            padding: "0.05rem 0.35rem",
            textDecoration: "none",
            color: "inherit",
            flexShrink: 0,
          }}
          title={arrLink.label}
          onClick={(e) => e.stopPropagation()}
        >
          {arrLink.appName} ↗
        </a>
      )}
    </div>
  );
});
TorrentNameCell.displayName = "TorrentNameCell";

export interface TorrentCellProps {
  columnKey: ColumnKey;
  torrent: Torrent;
  rowIndex: number;
  historyMatch?: DownloadHistoryEntry;
  arrConnections?: any;
}

export const TorrentCell: React.FC<TorrentCellProps> = React.memo(
  ({ columnKey, torrent: t, rowIndex, historyMatch, arrConnections }) => {
    const { t: translate } = useTranslation();

    switch (columnKey) {
      case "#":
        return <span>{rowIndex + 1}</span>;

      case "queuePosition":
        return <span>{t.sortOrder != null ? t.sortOrder + 1 : "-"}</span>;

      case "name":
        return (
          <TorrentNameCell
            torrent={t}
            historyMatch={historyMatch}
            arrConnections={arrConnections}
          />
        );

      case "status":
        return (
          <TorrentStatusCell
            torrentId={t.id}
            fallbackStatus={t.status}
            fallbackProgress={t.progress}
            fallbackDownloadSpeed={t.downloadSpeed}
            fallbackUploadSpeed={t.uploadSpeed}
            fallbackEta={t.eta}
            isVpnPaused={t.isVpnPaused}
          />
        );

      case "progress":
        return (
          <TorrentProgressCell
            torrentId={t.id}
            fallbackProgress={t.progress}
            fallbackStatus={t.status}
            fallbackRatio={t.ratio}
          />
        );

      case "totalSize":
        return <span>{formatBytes(t.totalSize)}</span>;

      case "downloaded":
        return (
          <TorrentDownloadedCell
            torrentId={t.id}
            totalSize={t.totalSize}
            fallbackDownloaded={t.downloaded}
            fallbackProgress={t.progress}
          />
        );

      case "uploaded":
        return (
          <TorrentUploadedCell torrentId={t.id} fallbackUploaded={t.uploaded} />
        );

      case "downloadSpeed":
        return (
          <TorrentSpeedCell
            torrentId={t.id}
            fallbackSpeed={t.downloadSpeed}
            type="download"
          />
        );

      case "uploadSpeed":
        return (
          <TorrentSpeedCell
            torrentId={t.id}
            fallbackSpeed={t.uploadSpeed}
            type="upload"
          />
        );

      case "ratio":
        return <TorrentRatioCell torrentId={t.id} fallbackRatio={t.ratio} />;

      case "seeders":
        return (
          <TorrentSeedsPeersCell
            torrentId={t.id}
            type="seeds"
            fallbackCount={t.seeders}
            fallbackStatus={t.status}
          />
        );

      case "leechers":
        return (
          <TorrentSeedsPeersCell
            torrentId={t.id}
            type="peers"
            fallbackCount={t.leechers}
            fallbackStatus={t.status}
          />
        );

      case "eta":
        return (
          <TorrentEtaCell
            torrentId={t.id}
            totalSize={t.totalSize}
            fallbackEta={t.eta}
            fallbackProgress={t.progress}
            fallbackSpeed={t.downloadSpeed}
            fallbackStatus={t.status}
          />
        );

      case "trackerUrl": {
        const domain = extractTrackerDomain(t.trackerUrl);
        return (
          <div
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.35rem",
            }}
          >
            <TrackerFavicon urlOrHost={t.trackerUrl || domain} size={14} />
            <span>{domain}</span>
          </div>
        );
      }

      case "announceInterval":
        return <span>{formatSeconds(t.announceInterval)}</span>;

      case "nextUpdate":
        return <span>{formatSeconds(t.nextUpdate)}</span>;

      case "dateAdded":
        return <span>{formatDate(t.dateAdded)}</span>;

      case "lastActive":
        return <span>{formatDate(t.lastActive)}</span>;

      case "creationDate":
        return <span>{formatDate(t.creationDate)}</span>;

      case "pieceCount":
        return <span>{t.pieceCount?.toLocaleString() ?? "-"}</span>;

      case "pieceLength":
        return <span>{formatBytes(t.pieceLength)}</span>;

      case "comment":
        return <span>{t.comment ?? "-"}</span>;

      case "createdBy":
        return <span>{t.createdBy ?? "-"}</span>;

      case "isPrivate":
        return <span>{t.isPrivate ? "Yes" : "No"}</span>;

      case "infoHash":
        return (
          <span className="mono" style={{ fontSize: "0.75rem" }}>
            {t.infoHash}
          </span>
        );

      case "priority":
        return (
          <span>
            {t.priority === 2 ? "High" : t.priority === 1 ? "Normal" : "Low"}
          </span>
        );

      case "uploadLimit":
        return (
          <span>{t.uploadLimit > 0 ? `${t.uploadLimit} KB/s` : "Global"}</span>
        );

      case "downloadLimit":
        return (
          <span>
            {t.downloadLimit > 0 ? `${t.downloadLimit} KB/s` : "Global"}
          </span>
        );

      case "superSeeding":
        return <span>{t.superSeeding ? "Yes" : "No"}</span>;

      case "sequentialDownload":
        return <span>{t.sequentialDownload ? "Yes" : "No"}</span>;

      case "forceStart":
        return <span>{t.forceStart ? "Yes" : "No"}</span>;

      case "label":
        return <span>{t.label ?? "-"}</span>;

      case "active":
        return <span>{t.active ? "Yes" : "No"}</span>;

      case "availability":
        return (
          <span>
            {typeof t.availability === "number"
              ? t.availability.toFixed(2)
              : "-"}
          </span>
        );

      case "threshold":
        return (
          <span>{t.threshold !== undefined ? `${t.threshold}%` : "-"}</span>
        );

      case "smallTorrentLimit":
        return <span>{formatBytes(t.smallTorrentLimit)}</span>;

      case "sessionUploaded":
        return <span>{formatBytes(t.sessionUploaded)}</span>;

      case "sessionDownloaded":
        return <span>{formatBytes(t.sessionDownloaded)}</span>;

      default:
        return <span>{String((t as any)[columnKey] ?? "-")}</span>;
    }
  },
);
TorrentCell.displayName = "TorrentCell";

// ---------------------------------------------------------------------------
// Table Row Component
// ---------------------------------------------------------------------------

export interface TorrentTableRowProps {
  torrent: Torrent;
  rowIndex: number;
  virtualRow?: VirtualItem;
  columns: ColumnDef[];
  isSelected: boolean;
  isChecked: boolean;
  isFocused: boolean;
  onSelectTorrent?: (id: number | null) => void;
  onSelect?: (torrent: Torrent) => void;
  onToggleSelect?: (id: number) => void;
  onToggleActive?: () => void;
  onRowClick: (
    torrent: Torrent,
    index: number,
    event: React.MouseEvent,
  ) => void;
  onContextMenu: (e: React.MouseEvent, torrent: Torrent) => void;
  onFocus: (index: number) => void;
  historyMatch?: DownloadHistoryEntry;
  arrConnections?: any;
  measureElement?: (node: HTMLElement | null) => void;
  rowRef?: (el: HTMLTableRowElement | null) => void;
}

export const TorrentTableRow = React.memo<TorrentTableRowProps>(
  ({
    torrent: t,
    rowIndex,
    virtualRow,
    columns,
    isSelected,
    isChecked,
    isFocused,
    onSelectTorrent,
    onSelect,
    onToggleSelect,
    onToggleActive,
    onRowClick,
    onContextMenu,
    onFocus,
    historyMatch,
    arrConnections,
    measureElement,
    rowRef,
  }) => {
    const handleKeyDown = useCallback(
      (e: React.KeyboardEvent) => {
        if (
          typeof HTMLInputElement !== "undefined" &&
          e.target instanceof HTMLInputElement
        ) {
          return;
        }
        if (e.key === " ") {
          e.preventDefault();
          e.stopPropagation?.();
          if (e.shiftKey || e.ctrlKey || e.metaKey) {
            onToggleSelect?.(t.id);
          } else {
            onToggleActive?.();
          }
        } else if (e.key === "Enter") {
          e.preventDefault();
          e.stopPropagation?.();
          if (onSelectTorrent) {
            onSelectTorrent(isSelected ? null : t.id);
          } else if (onSelect) {
            onSelect(
              applyTelemetry(t, useTorrentStore.getState().telemetry[t.id]),
            );
          }
        }
      },
      [
        onToggleSelect,
        onToggleActive,
        onSelectTorrent,
        onSelect,
        isSelected,
        t,
      ],
    );

    const setRef = useCallback(
      (el: HTMLTableRowElement | null) => {
        rowRef?.(el);
        measureElement?.(el);
      },
      [rowRef, measureElement],
    );

    return (
      <tr
        key={t.id}
        ref={setRef}
        role="row"
        data-index={rowIndex}
        tabIndex={0}
        aria-selected={isSelected || isChecked}
        className={`torrent-table-row${isSelected ? " torrent-table-row-selected" : ""}${isChecked ? " torrent-table-row-selected" : ""}${isFocused ? " torrent-table-row-focused" : ""}`}
        onFocus={() => onFocus(rowIndex)}
        onKeyDown={handleKeyDown}
        onClick={(e) => onRowClick(t, rowIndex, e)}
        onContextMenu={(e) => onContextMenu(e, t)}
        style={{
          cursor: "pointer",
          backgroundColor: isSelected
            ? "var(--bg-card-hover, #23284b)"
            : isChecked
              ? "rgba(255, 209, 102, 0.05)"
              : "transparent",
          borderBottom: "1px solid var(--border-light)",
          fontSize: "0.82rem",
        }}
      >
        <td style={{ textAlign: "center", padding: "0.5rem", width: 36 }}>
          <input
            type="checkbox"
            className="torrent-checkbox"
            aria-label={`Select ${t.name}`}
            checked={isChecked}
            onChange={() => {}}
            onClick={(e) => {
              e.stopPropagation();
              onToggleSelect?.(t.id);
            }}
          />
        </td>

        {columns.map((col) => (
          <td
            key={col.key}
            className={col.key === "#" ? "torrent-table-index" : undefined}
            style={{
              padding: "0.55rem 0.75rem",
              whiteSpace: "nowrap",
            }}
          >
            <TorrentCell
              columnKey={col.key}
              torrent={t}
              rowIndex={rowIndex}
              historyMatch={historyMatch}
              arrConnections={arrConnections}
            />
          </td>
        ))}
      </tr>
    );
  },
);
TorrentTableRow.displayName = "TorrentTableRow";

// ---------------------------------------------------------------------------
// Main TorrentTable Component
// ---------------------------------------------------------------------------

export interface TorrentTableProps {
  torrents?: Torrent[];
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  categoryFilter?: string;
  tagFilter?: string;
  selectedTagIds?: number[] | Set<number>;
  tagMatchMode?: "AND" | "OR";
  selectedTorrentId?: number | null;
  onSelectTorrent?: (id: number | null) => void;
  selectedIds?: Set<number>;
  onToggleSelect?: (id: number) => void;
  onSelectAll?: (ids: number[]) => void;
  onSelectRange?: (ids: number[]) => void;
  onSelectMultiple?: (ids: Set<number>) => void;
  onDeleteSelected?: () => void;
  onToggleActive?: () => void;
  visibleColumns?: Set<string>;
  onToggleColumn?: (key: string) => void;
  columnOrder?: ColumnKey[];
  onColumnOrderChange?: (order: ColumnKey[]) => void;
  columnWidths?: Record<string, number>;
  onColumnWidthsChange?: (widths: Record<string, number>) => void;
  sortKey?: ColumnKey | null;
  sortAsc?: boolean;
  onSortChange?: (key: ColumnKey | null, asc: boolean) => void;
  onResetSort?: () => void;
  // Compatibility props
  selectedId?: number | null;
  onSelect?: (torrent: Torrent) => void;
  onPause?: (id: number) => void;
  onResume?: (id: number) => void;
  onDelete?: (payload: { id: number; deleteFiles?: boolean }) => void;
  onSearchIndexers?: (query: string) => void;
  onNavigateTab?: (nav: string, subNav?: string) => void;
  onOpenBulkTag?: () => void;
}

export function TorrentTable({
  torrents: propTorrents,
  filter,
  stateFilter,
  trackerFilter,
  categoryFilter,
  tagFilter,
  selectedTagIds,
  tagMatchMode,
  selectedTorrentId: propSelectedTorrentId,
  onSelectTorrent,
  selectedIds: propSelectedIds,
  onToggleSelect: propToggleSelect,
  onSelectAll: propSelectAll,
  onSelectRange,
  onSelectMultiple,
  onDeleteSelected,
  onToggleActive,
  onOpenBulkTag,
  visibleColumns: propVisibleColumns,
  onToggleColumn: propToggleColumn,
  columnOrder: propColumnOrder,
  onColumnOrderChange: propOnColumnOrderChange,
  columnWidths: propColumnWidths,
  onColumnWidthsChange: propOnColumnWidthsChange,
  sortKey: propSortKey,
  sortAsc: propSortAsc,
  onSortChange: propOnSortChange,
  onResetSort: propOnResetSort,
  selectedId: propSelectedId,
  onSelect: propOnSelect,
  onPause,
  onResume,
  onDelete: propOnDelete,
  onSearchIndexers,
}: TorrentTableProps) {
  const { t } = useTranslation();
  const { data: fetchedTorrents, isLoading, isError } = useTorrents();
  const torrents = propTorrents ?? fetchedTorrents;
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const bulkAction = useBulkTorrentAction();
  const updateTorrent = useUpdateTorrent();
  const announceTorrent = useAnnounceTorrent();
  const recheckTorrent = useRecheckTorrent();
  const moveTorrentQueue = useMoveTorrentQueue();

  const selectedTorrentId =
    propSelectedTorrentId !== undefined
      ? propSelectedTorrentId
      : propSelectedId !== undefined
        ? propSelectedId
        : null;

  const selectedIds = propSelectedIds ?? new Set<number>();

  const [internalSortKey, setInternalSortKey] = useState<SortKey | null>(
    () => loadTableSortPreferences().sortKey,
  );
  const [internalSortAsc, setInternalSortAsc] = useState<boolean>(
    () => loadTableSortPreferences().sortAsc,
  );

  const sortKey = propSortKey !== undefined ? propSortKey : internalSortKey;
  const sortAsc = propSortAsc !== undefined ? propSortAsc : internalSortAsc;

  const handleResetSort = useCallback(() => {
    setInternalSortKey(null);
    setInternalSortAsc(true);
    resetTableSortPreferences();
    propOnResetSort?.();
    propOnSortChange?.(null, true);
  }, [propOnResetSort, propOnSortChange]);

  const [lastClickedIndex, setLastClickedIndex] = useState<number | null>(null);
  const [focusedIndex, setFocusedIndex] = useState<number>(0);
  const [anchorIndex, setAnchorIndex] = useState<number | null>(null);
  const rowRefs = useRef<(HTMLTableRowElement | null)[]>([]);
  const tableContainerRef = useRef<HTMLDivElement>(null);
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [searchModalQuery, setSearchModalQuery] = useState<string | null>(null);
  const [deleteModalState, setDeleteModalState] = useState<{
    isOpen: boolean;
    ids: number[];
    torrentName?: string;
  } | null>(null);

  const [internalVisibleColumns, setInternalVisibleColumns] =
    useState<Set<string>>(loadVisibleColumns);
  const visibleColumns = propVisibleColumns ?? internalVisibleColumns;

  const [internalColumnOrder, setInternalColumnOrder] =
    useState<ColumnKey[]>(loadColumnOrder);
  const [internalColumnWidths, setInternalColumnWidths] =
    useState<Record<string, number>>(loadColumnWidths);

  const columnOrder = propColumnOrder ?? internalColumnOrder;
  const columnWidths = propColumnWidths ?? internalColumnWidths;

  const updateColumnOrder = useCallback(
    (next: ColumnKey[] | ((prev: ColumnKey[]) => ColumnKey[])) => {
      if (propOnColumnOrderChange) {
        const resolved = typeof next === "function" ? next(columnOrder) : next;
        propOnColumnOrderChange(resolved);
      } else {
        setInternalColumnOrder((prev) => {
          const resolved = typeof next === "function" ? next(prev) : next;
          saveColumnOrder(resolved);
          return resolved;
        });
      }
    },
    [propOnColumnOrderChange, columnOrder],
  );

  const updateColumnWidths = useCallback(
    (
      next:
        | Record<string, number>
        | ((prev: Record<string, number>) => Record<string, number>),
    ) => {
      if (propOnColumnWidthsChange) {
        const resolved = typeof next === "function" ? next(columnWidths) : next;
        propOnColumnWidthsChange(resolved);
      } else {
        setInternalColumnWidths((prev) => {
          const resolved = typeof next === "function" ? next(prev) : next;
          saveColumnWidths(resolved);
          return resolved;
        });
      }
    },
    [propOnColumnWidthsChange, columnWidths],
  );

  // Hover and Sort snapshot tracking for freeze-on-interaction
  const [isHovered, setIsHovered] = useState(false);
  const frozenOrderRef = useRef<number[]>([]);
  const lastSortKeyRef = useRef<SortKey | null>(sortKey);
  const lastSortAscRef = useRef<boolean>(sortAsc);
  const lastFilterKeyRef = useRef<string>("");

  // Drag-to-reorder state
  const dragColRef = useRef<ColumnKey | null>(null);
  const dragOverColRef = useRef<ColumnKey | null>(null);
  const [dragOverKey, setDragOverKey] = useState<ColumnKey | null>(null);

  // Column resize state
  const resizeStateRef = useRef<{
    key: string;
    startX: number;
    startWidth: number;
  } | null>(null);
  const resizeListenersRef = useRef<{
    move: (e: MouseEvent) => void;
    up: (e: MouseEvent) => void;
  } | null>(null);

  useEffect(() => {
    return () => {
      if (resizeListenersRef.current) {
        document.removeEventListener(
          "mousemove",
          resizeListenersRef.current.move,
        );
        document.removeEventListener("mouseup", resizeListenersRef.current.up);
        resizeListenersRef.current = null;
      }
    };
  }, []);

  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

  const historyByHashOrTitle = useMemo(() => {
    const map = new Map<string, DownloadHistoryEntry>();
    if (history) {
      for (const h of history) {
        if (h.infoHash) {
          map.set(h.infoHash.toLowerCase(), h);
        }
        if (h.title) {
          map.set(h.title.toLowerCase(), h);
        }
      }
    }
    return map;
  }, [history]);

  const closeContextMenu = useCallback(() => setContextMenu(null), []);

  const filtered = propTorrents
    ? propTorrents
    : filterTorrents(torrents, {
        filter,
        stateFilter,
        trackerFilter,
        categoryFilter,
        tagFilter,
        selectedTagIds,
        tagMatchMode,
      });

  const filterSignature = `${filter || ""}|${stateFilter || ""}|${trackerFilter || ""}|${categoryFilter || ""}|${tagFilter || ""}|${sortKey || ""}|${sortAsc}`;

  const sorted = useMemo(() => {
    const isExplicitChange =
      lastSortKeyRef.current !== sortKey ||
      lastSortAscRef.current !== sortAsc ||
      lastFilterKeyRef.current !== filterSignature;

    if (isExplicitChange) {
      lastSortKeyRef.current = sortKey;
      lastSortAscRef.current = sortAsc;
      lastFilterKeyRef.current = filterSignature;
    }

    if (isHovered && !isExplicitChange && frozenOrderRef.current.length > 0) {
      const torrentMap = new Map(filtered.map((t) => [t.id, t]));
      const preserved: Torrent[] = [];
      const seen = new Set<number>();

      for (const id of frozenOrderRef.current) {
        const t = torrentMap.get(id);
        if (t) {
          preserved.push(t);
          seen.add(id);
        }
      }
      for (const t of filtered) {
        if (!seen.has(t.id)) {
          preserved.push(t);
        }
      }
      return preserved;
    }

    const currentTelemetry = useTorrentStore.getState().telemetry;

    if (!sortKey) {
      const result = [...filtered].sort((a, b) => {
        const orderA = a.sortOrder ?? 0;
        const orderB = b.sortOrder ?? 0;
        if (orderA !== orderB) return orderA - orderB;
        return (a.id ?? 0) - (b.id ?? 0);
      });
      frozenOrderRef.current = result.map((t) => t.id);
      return result;
    }

    const result = [...filtered].sort((a, b) => {
      const telA = currentTelemetry[a.id];
      const telB = currentTelemetry[b.id];
      const mergedA = applyTelemetry(a, telA);
      const mergedB = applyTelemetry(b, telB);

      if (sortKey === "eta") {
        const parseEta = (t: Torrent): number => {
          if (t.eta == null) return 0;
          const num = typeof t.eta === "number" ? t.eta : Number(t.eta);
          return Number.isNaN(num) ? 0 : num;
        };

        const etaA = parseEta(mergedA);
        const etaB = parseEta(mergedB);

        if (sortAsc) {
          const valA = etaA <= 0 ? Number.POSITIVE_INFINITY : etaA;
          const valB = etaB <= 0 ? Number.POSITIVE_INFINITY : etaB;
          if (valA === valB) return 0;
          if (valA === Number.POSITIVE_INFINITY) return 1;
          if (valB === Number.POSITIVE_INFINITY) return -1;
          return valA - valB;
        } else {
          const valA = etaA <= 0 ? 0 : etaA;
          const valB = etaB <= 0 ? 0 : etaB;
          return valB - valA;
        }
      }

      const va = getSortValue(mergedA, sortKey);
      const vb = getSortValue(mergedB, sortKey);

      let cmp = 0;
      if (
        STRING_COLUMNS.has(sortKey) ||
        typeof va === "string" ||
        typeof vb === "string"
      ) {
        const sa = va != null ? String(va) : "";
        const sb = vb != null ? String(vb) : "";
        cmp = sa.localeCompare(sb);
      } else {
        const parseNum = (val: unknown): number => {
          if (val == null) return 0;
          const num = typeof val === "number" ? val : Number(val);
          return Number.isNaN(num) ? 0 : num;
        };
        const na = parseNum(va);
        const nb = parseNum(vb);
        cmp = na - nb;
      }

      return sortAsc ? cmp : -cmp;
    });

    frozenOrderRef.current = result.map((t) => t.id);
    return result;
  }, [filtered, sortKey, sortAsc, isHovered, filterSignature]);

  useEffect(() => {
    if (selectedTorrentId != null && sorted.length > 0) {
      const idx = sorted.findIndex((t) => t.id === selectedTorrentId);
      if (idx !== -1) {
        setFocusedIndex(idx);
        if (anchorIndex === null) {
          setAnchorIndex(idx);
        }
        rowRefs.current[idx]?.scrollIntoView({ block: "nearest" });
      }
    }
  }, [selectedTorrentId, sorted, anchorIndex]);

  useEffect(() => {
    if (sorted.length > 0 && focusedIndex >= sorted.length) {
      setFocusedIndex(sorted.length - 1);
    }
  }, [sorted.length, focusedIndex]);

  const moveFocus = useCallback(
    (targetIndex: number, isShift: boolean) => {
      if (sorted.length === 0) return;
      const nextIndex = Math.max(0, Math.min(sorted.length - 1, targetIndex));
      setFocusedIndex(nextIndex);

      const rowEl = rowRefs.current[nextIndex];
      if (rowEl) {
        rowEl.focus();
        rowEl.scrollIntoView({ block: "nearest" });
      }

      if (isShift) {
        const effectiveAnchor =
          anchorIndex !== null ? anchorIndex : focusedIndex;
        if (anchorIndex === null) {
          setAnchorIndex(focusedIndex);
        }
        const start = Math.min(effectiveAnchor, nextIndex);
        const end = Math.max(effectiveAnchor, nextIndex);
        const rangeIds = sorted.slice(start, end + 1).map((item) => item.id);
        if (onSelectMultiple) {
          onSelectMultiple(new Set(rangeIds));
        } else if (onSelectRange) {
          onSelectRange(rangeIds);
        }
        onSelectTorrent?.(sorted[nextIndex].id);
      } else {
        setAnchorIndex(nextIndex);
        setLastClickedIndex(nextIndex);
        if (onSelectMultiple) {
          onSelectMultiple(new Set([sorted[nextIndex].id]));
        } else if (onSelectRange) {
          onSelectRange([sorted[nextIndex].id]);
        }
        onSelectTorrent?.(sorted[nextIndex].id);
      }
    },
    [
      sorted,
      anchorIndex,
      focusedIndex,
      onSelectMultiple,
      onSelectRange,
      onSelectTorrent,
    ],
  );

  const handleToggleSelect = useCallback(
    (id: number) => {
      if (propToggleSelect) {
        propToggleSelect(id);
      } else {
        useTorrentStore.getState().toggleSelectedId(id);
      }
    },
    [propToggleSelect],
  );

  const handleDefaultToggleActive = useCallback(() => {
    const ids =
      selectedIds.size > 0
        ? Array.from(selectedIds)
        : selectedTorrentId != null
          ? [selectedTorrentId]
          : sorted[focusedIndex]
            ? [sorted[focusedIndex].id]
            : [];
    if (ids.length === 0) return;

    const anyActive = ids.some((id) => {
      const t = sorted.find((item) => item.id === id);
      return t?.active;
    });

    if (anyActive) {
      ids.forEach((id) => (onPause ? onPause(id) : stopSeeding.mutate(id)));
    } else {
      ids.forEach((id) => (onResume ? onResume(id) : startSeeding.mutate(id)));
    }
  }, [
    selectedIds,
    selectedTorrentId,
    sorted,
    focusedIndex,
    onPause,
    onResume,
    stopSeeding,
    startSeeding,
  ]);

  const handleDefaultDeleteSelected = useCallback(() => {
    const ids =
      selectedIds.size > 0
        ? Array.from(selectedIds)
        : selectedTorrentId != null
          ? [selectedTorrentId]
          : sorted[focusedIndex]
            ? [sorted[focusedIndex].id]
            : [];
    if (ids.length === 0) return;

    const torrentName =
      ids.length === 1 ? sorted.find((t) => t.id === ids[0])?.name : undefined;
    setDeleteModalState({
      isOpen: true,
      ids,
      torrentName,
    });
  }, [selectedIds, selectedTorrentId, sorted, focusedIndex]);

  const handleConfirmDefaultDelete = useCallback(
    async (deleteFiles: boolean) => {
      if (!deleteModalState || deleteModalState.ids.length === 0) return;
      try {
        if (propOnDelete && deleteModalState.ids.length === 1) {
          propOnDelete({
            id: deleteModalState.ids[0],
            deleteFiles,
          });
        } else if (deleteModalState.ids.length === 1) {
          await deleteTorrent.mutateAsync({
            id: deleteModalState.ids[0],
            deleteFiles,
          });
        } else {
          await bulkAction.mutateAsync({
            torrentIds: deleteModalState.ids,
            action: "delete",
            deleteFiles,
          });
        }
        setDeleteModalState(null);
      } catch {
        // Handled by query client
      }
    },
    [deleteModalState, propOnDelete, deleteTorrent, bulkAction],
  );

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (
        e.target instanceof HTMLInputElement ||
        e.target instanceof HTMLTextAreaElement ||
        e.target instanceof HTMLSelectElement ||
        (e.target as HTMLElement)?.isContentEditable
      ) {
        return;
      }

      if (sorted.length === 0) return;

      if (e.key === "ArrowDown") {
        if (e.ctrlKey || e.metaKey) {
          e.preventDefault();
          e.stopPropagation();
          const pos: "down" | "bottom" = e.shiftKey ? "bottom" : "down";
          const ids =
            selectedIds.size > 0
              ? Array.from(selectedIds)
              : selectedTorrentId != null
                ? [selectedTorrentId]
                : sorted[focusedIndex]
                  ? [sorted[focusedIndex].id]
                  : [];
          if (ids.length > 0) {
            const orderedIds = sorted
              .filter((t) => ids.includes(t.id))
              .map((t) => t.id)
              .reverse();
            orderedIds.forEach((id) => {
              moveTorrentQueue.mutate({ id, position: pos });
            });
          }
          return;
        }
        e.preventDefault();
        e.stopPropagation();
        moveFocus(focusedIndex + 1, e.shiftKey);
        return;
      }

      if (e.key === "ArrowUp") {
        if (e.ctrlKey || e.metaKey) {
          e.preventDefault();
          e.stopPropagation();
          const pos: "up" | "top" = e.shiftKey ? "top" : "up";
          const ids =
            selectedIds.size > 0
              ? Array.from(selectedIds)
              : selectedTorrentId != null
                ? [selectedTorrentId]
                : sorted[focusedIndex]
                  ? [sorted[focusedIndex].id]
                  : [];
          if (ids.length > 0) {
            const orderedIds = sorted
              .filter((t) => ids.includes(t.id))
              .map((t) => t.id);
            orderedIds.forEach((id) => {
              moveTorrentQueue.mutate({ id, position: pos });
            });
          }
          return;
        }
        e.preventDefault();
        e.stopPropagation();
        moveFocus(focusedIndex - 1, e.shiftKey);
        return;
      }

      if (e.key === "Home") {
        e.preventDefault();
        e.stopPropagation();
        moveFocus(0, e.shiftKey);
        return;
      }

      if (e.key === "End") {
        e.preventDefault();
        e.stopPropagation();
        moveFocus(sorted.length - 1, e.shiftKey);
        return;
      }

      if (
        (e.ctrlKey || e.metaKey) &&
        e.shiftKey &&
        (e.key === "i" || e.key === "I")
      ) {
        e.preventDefault();
        e.stopPropagation();
        const inverted = new Set<number>();
        sorted.forEach((t) => {
          if (!selectedIds.has(t.id)) {
            inverted.add(t.id);
          }
        });
        if (onSelectMultiple) {
          onSelectMultiple(inverted);
        } else if (onSelectRange) {
          onSelectRange(Array.from(inverted));
        } else {
          useTorrentStore.getState().selectAllIds(Array.from(inverted));
        }
        return;
      }

      if ((e.ctrlKey || e.metaKey) && (e.key === "a" || e.key === "A")) {
        e.preventDefault();
        e.stopPropagation();
        const allIds = sorted.map((t) => t.id);
        if (onSelectMultiple) {
          onSelectMultiple(new Set(allIds));
        } else if (propSelectAll) {
          propSelectAll(allIds);
        } else {
          useTorrentStore.getState().selectAllIds(allIds);
        }
        return;
      }

      if (
        ((e.ctrlKey || e.metaKey) && (e.key === "r" || e.key === "R")) ||
        e.key === "F5"
      ) {
        e.preventDefault();
        e.stopPropagation();
        const ids =
          selectedIds.size > 0
            ? Array.from(selectedIds)
            : selectedTorrentId != null
              ? [selectedTorrentId]
              : sorted[focusedIndex]
                ? [sorted[focusedIndex].id]
                : [];
        ids.forEach((id) => recheckTorrent.mutate(id));
        return;
      }

      if (
        e.key === "F6" ||
        (!e.ctrlKey &&
          !e.metaKey &&
          !e.shiftKey &&
          !e.altKey &&
          (e.key === "a" || e.key === "A"))
      ) {
        e.preventDefault();
        e.stopPropagation();
        const ids =
          selectedIds.size > 0
            ? Array.from(selectedIds)
            : selectedTorrentId != null
              ? [selectedTorrentId]
              : sorted[focusedIndex]
                ? [sorted[focusedIndex].id]
                : [];
        ids.forEach((id) => announceTorrent.mutate(id));
        return;
      }

      if ((e.ctrlKey || e.metaKey) && (e.key === "t" || e.key === "T")) {
        e.preventDefault();
        e.stopPropagation();
        onOpenBulkTag?.();
        return;
      }

      if (e.key === "Enter") {
        e.preventDefault();
        e.stopPropagation();
        const curr = sorted[focusedIndex];
        if (curr) {
          onSelectTorrent?.(curr.id);
        }
        return;
      }

      if (e.key === " ") {
        e.preventDefault();
        e.stopPropagation();
        if (e.shiftKey || e.ctrlKey || e.metaKey) {
          const curr = sorted[focusedIndex];
          if (curr) {
            handleToggleSelect(curr.id);
          }
        } else {
          if (onToggleActive) {
            onToggleActive();
          } else {
            handleDefaultToggleActive();
          }
        }
        return;
      }

      if (e.key === "Delete" || e.key === "Backspace") {
        e.preventDefault();
        e.stopPropagation();
        if (onDeleteSelected) {
          onDeleteSelected();
        } else {
          handleDefaultDeleteSelected();
        }
        return;
      }
    },
    [
      sorted,
      focusedIndex,
      moveFocus,
      selectedIds,
      selectedTorrentId,
      moveTorrentQueue,
      recheckTorrent,
      announceTorrent,
      onOpenBulkTag,
      handleToggleSelect,
      onSelectMultiple,
      onSelectRange,
      propSelectAll,
      onSelectTorrent,
      onToggleActive,
      handleDefaultToggleActive,
      onDeleteSelected,
      handleDefaultDeleteSelected,
    ],
  );

  const toggleColumn = useCallback(
    (key: string) => {
      if (propToggleColumn) {
        propToggleColumn(key);
        return;
      }
      setInternalVisibleColumns((prev) => {
        const next = new Set(prev);
        if (next.has(key)) {
          if (next.size > 1) next.delete(key);
        } else {
          next.add(key);
        }
        saveVisibleColumns(next);
        return next;
      });
    },
    [propToggleColumn],
  );

  const handleContextMenu = useCallback(
    (e: React.MouseEvent, torrent: Torrent | null) => {
      e.preventDefault();
      e.stopPropagation();
      let effectiveSelectedIds = selectedIds;
      if (torrent && selectedIds) {
        if (!selectedIds.has(torrent.id)) {
          effectiveSelectedIds = new Set([torrent.id]);
          if (propSelectAll) {
            propSelectAll([torrent.id]);
          } else if (onSelectMultiple) {
            onSelectMultiple(effectiveSelectedIds);
          }
        }
      }
      const currentTorrents = torrents || [];
      const selectedTorrents = currentTorrents.filter((t) =>
        effectiveSelectedIds.has(t.id),
      );
      setContextMenu({
        x: e.clientX,
        y: e.clientY,
        torrent,
        selectedTorrents,
      });
    },
    [selectedIds, propSelectAll, onSelectMultiple, torrents],
  );

  const handleSort = useCallback(
    (key: ColumnKey) => {
      if (sortKey === key) {
        const nextAsc = !sortAsc;
        if (propOnSortChange) {
          propOnSortChange(key, nextAsc);
        } else {
          setInternalSortAsc(nextAsc);
          saveTableSortPreferences(key, nextAsc);
        }
      } else {
        if (propOnSortChange) {
          propOnSortChange(key, true);
        } else {
          setInternalSortKey(key);
          setInternalSortAsc(true);
          saveTableSortPreferences(key, true);
        }
      }
    },
    [sortKey, sortAsc, propOnSortChange],
  );

  // Column drag-to-reorder handlers
  const handleColDragStart = useCallback((key: ColumnKey) => {
    dragColRef.current = key;
  }, []);

  const handleColDragOver = useCallback(
    (e: React.DragEvent, key: ColumnKey) => {
      e.preventDefault();
      dragOverColRef.current = key;
      setDragOverKey(key);
    },
    [],
  );

  const handleColDrop = useCallback(
    (e: React.DragEvent, targetKey: ColumnKey) => {
      e.preventDefault();
      const fromKey = dragColRef.current;
      if (!fromKey || fromKey === targetKey) {
        dragColRef.current = null;
        dragOverColRef.current = null;
        setDragOverKey(null);
        return;
      }

      updateColumnOrder((prev) => {
        const list = [...prev];
        const fromIdx = list.indexOf(fromKey);
        const toIdx = list.indexOf(targetKey);
        if (fromIdx === -1 || toIdx === -1) return prev;
        list.splice(fromIdx, 1);
        list.splice(toIdx, 0, fromKey);
        return list;
      });

      dragColRef.current = null;
      dragOverColRef.current = null;
      setDragOverKey(null);
    },
    [updateColumnOrder],
  );

  const handleColDragEnd = useCallback(() => {
    dragColRef.current = null;
    dragOverColRef.current = null;
    setDragOverKey(null);
  }, []);

  // Column resize mousedown handler
  const handleResizeMouseDown = useCallback(
    (e: React.MouseEvent, key: ColumnKey, thElement: HTMLElement) => {
      e.preventDefault();
      e.stopPropagation();

      const startX = e.clientX;
      const startWidth = thElement.getBoundingClientRect().width;
      resizeStateRef.current = { key, startX, startWidth };

      const onMouseMove = (moveEvt: MouseEvent) => {
        if (!resizeStateRef.current) return;
        const delta = moveEvt.clientX - resizeStateRef.current.startX;
        const newWidth = Math.max(
          40,
          Math.round(resizeStateRef.current.startWidth + delta),
        );
        updateColumnWidths((prev) => ({
          ...prev,
          [resizeStateRef.current!.key]: newWidth,
        }));
      };

      const onMouseUp = () => {
        if (resizeStateRef.current) {
          updateColumnWidths((prev) => {
            saveColumnWidths(prev);
            return prev;
          });
          resizeStateRef.current = null;
        }
        document.removeEventListener("mousemove", onMouseMove);
        document.removeEventListener("mouseup", onMouseUp);
        resizeListenersRef.current = null;
      };

      resizeListenersRef.current = { move: onMouseMove, up: onMouseUp };
      document.addEventListener("mousemove", onMouseMove);
      document.addEventListener("mouseup", onMouseUp);
    },
    [updateColumnWidths],
  );

  const colDefMap = useMemo(
    () => new Map(ALL_COLUMNS.map((c) => [c.key, c])),
    [],
  );

  const columns = useMemo(() => {
    const ordered = columnOrder
      .map((k) => colDefMap.get(k))
      .filter(
        (c): c is ColumnDef => c !== undefined && visibleColumns.has(c.key),
      );
    const inOrder = new Set(ordered.map((c) => c.key));
    for (const c of ALL_COLUMNS) {
      if (visibleColumns.has(c.key) && !inOrder.has(c.key)) ordered.push(c);
    }
    return ordered;
  }, [columnOrder, visibleColumns, colDefMap]);

  // Virtualizer setup
  const rowVirtualizer = useVirtualizer({
    count: sorted.length,
    getScrollElement: () => tableContainerRef.current,
    estimateSize: () => 44,
    overscan: 10,
    measureElement: (element) => element?.getBoundingClientRect().height,
  });

  const virtualRows = rowVirtualizer.getVirtualItems();
  const totalHeight = rowVirtualizer.getTotalSize();
  const paddingTop = virtualRows.length > 0 ? virtualRows[0].start : 0;
  const paddingBottom =
    virtualRows.length > 0
      ? totalHeight - virtualRows[virtualRows.length - 1].end
      : 0;

  // Fallback for SSR / static render where refs are unmeasured
  const rowsToRender =
    virtualRows.length > 0
      ? virtualRows.map((v) => ({
          virtualRow: v,
          index: v.index,
          torrent: sorted[v.index],
        }))
      : sorted.slice(0, 50).map((tor, idx) => ({
          virtualRow: undefined,
          index: idx,
          torrent: tor,
        }));

  const handleRowClick = useCallback(
    (torrent: Torrent, index: number, e: React.MouseEvent) => {
      setFocusedIndex(index);
      if (e.shiftKey && (anchorIndex !== null || lastClickedIndex !== null)) {
        const base = anchorIndex !== null ? anchorIndex : lastClickedIndex!;
        const start = Math.min(base, index);
        const end = Math.max(base, index);
        const rangeIds = sorted.slice(start, end + 1).map((item) => item.id);
        if (onSelectMultiple) {
          onSelectMultiple(new Set(rangeIds));
        } else if (onSelectRange) {
          onSelectRange(rangeIds);
        } else if (propSelectAll) {
          propSelectAll(rangeIds);
        }
        onSelectTorrent?.(torrent.id);
        propOnSelect?.(
          applyTelemetry(
            torrent,
            useTorrentStore.getState().telemetry[torrent.id],
          ),
        );
      } else if (e.ctrlKey || e.metaKey) {
        setAnchorIndex(index);
        handleToggleSelect(torrent.id);
        onSelectTorrent?.(torrent.id);
        propOnSelect?.(
          applyTelemetry(
            torrent,
            useTorrentStore.getState().telemetry[torrent.id],
          ),
        );
        setLastClickedIndex(index);
      } else {
        setAnchorIndex(index);
        onSelectTorrent?.(selectedTorrentId === torrent.id ? null : torrent.id);
        propOnSelect?.(
          applyTelemetry(
            torrent,
            useTorrentStore.getState().telemetry[torrent.id],
          ),
        );
        setLastClickedIndex(index);
      }
    },
    [
      anchorIndex,
      lastClickedIndex,
      sorted,
      onSelectMultiple,
      onSelectRange,
      propSelectAll,
      onSelectTorrent,
      propOnSelect,
      handleToggleSelect,
      selectedTorrentId,
    ],
  );

  const handleSelectAll = useCallback(() => {
    if (sorted.length === 0) return;
    const allSelected = sorted.every((t) => selectedIds.has(t.id));
    if (allSelected) {
      if (onSelectMultiple) {
        onSelectMultiple(new Set());
      } else if (propSelectAll) {
        propSelectAll([]);
      } else {
        useTorrentStore.getState().clearSelection();
      }
    } else {
      const allIds = sorted.map((t) => t.id);
      if (onSelectMultiple) {
        onSelectMultiple(new Set(allIds));
      } else if (propSelectAll) {
        propSelectAll(allIds);
      } else {
        useTorrentStore.getState().selectAllIds(allIds);
      }
    }
  }, [sorted, selectedIds, onSelectMultiple, propSelectAll]);

  if (isLoading) {
    return (
      <div className="torrent-table-wrapper">
        <table className="torrent-table">
          <thead>
            <tr>
              <th className="torrent-table-th" style={{ width: 36 }} />
              {columns.map((c) => (
                <th
                  key={c.key}
                  className="torrent-table-th"
                  style={{
                    width: columnWidths[c.key]
                      ? `${columnWidths[c.key]}px`
                      : undefined,
                    minWidth: columnWidths[c.key]
                      ? `${columnWidths[c.key]}px`
                      : undefined,
                    maxWidth: columnWidths[c.key]
                      ? `${columnWidths[c.key]}px`
                      : undefined,
                  }}
                >
                  {t(
                    COLUMN_I18N_KEYS[c.key] || `torrents.table.${c.key}`,
                    undefined,
                    c.label,
                  )}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {[0, 1, 2, 3, 4].map((i) => (
              <SkeletonTableRow key={i} columns={columns.length} />
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  if (isError) {
    return <p className="error">Failed to load data.</p>;
  }

  const allSelected =
    sorted.length > 0 && sorted.every((t) => selectedIds.has(t.id));
  const someSelected =
    sorted.length > 0 &&
    sorted.some((t) => selectedIds.has(t.id)) &&
    !allSelected;

  return (
    <div
      className="torrent-table-wrapper"
      tabIndex={0}
      onKeyDown={handleKeyDown}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      onFocus={(e) => {
        if (e.target === e.currentTarget && sorted.length > 0) {
          const idx = Math.min(Math.max(0, focusedIndex), sorted.length - 1);
          rowRefs.current[idx]?.focus();
        }
      }}
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        position: "relative",
      }}
    >
      <div
        ref={tableContainerRef}
        style={{ flex: "1 1 auto", minHeight: 0, overflow: "auto" }}
      >
        <table
          className="torrent-table"
          style={{ width: "100%", borderCollapse: "collapse" }}
        >
          <thead onContextMenu={(e) => handleContextMenu(e, null)}>
            <tr
              style={{
                position: "sticky",
                top: 0,
                backgroundColor: "var(--bg-primary, #10111a)",
                zIndex: 2,
                borderBottom: "1px solid var(--border-light)",
              }}
            >
              <th
                className="torrent-table-th"
                style={{ width: 36, textAlign: "center" }}
              >
                <input
                  type="checkbox"
                  className="torrent-checkbox"
                  aria-label="Select all torrents"
                  checked={allSelected}
                  ref={(el) => {
                    if (el) el.indeterminate = someSelected;
                  }}
                  onChange={handleSelectAll}
                />
              </th>

              {columns.map((col) => (
                <th
                  key={col.key}
                  draggable
                  onDragStart={() => handleColDragStart(col.key)}
                  onDragOver={(e) => handleColDragOver(e, col.key)}
                  onDrop={(e) => handleColDrop(e, col.key)}
                  onDragEnd={handleColDragEnd}
                  onClick={() => col.sortable && handleSort(col.key)}
                  className={`torrent-table-th${col.key === "#" ? " torrent-table-index" : ""}`}
                  style={{
                    cursor: col.sortable ? "pointer" : "default",
                    userSelect: "none",
                    whiteSpace: "nowrap",
                    position: "relative",
                    width: columnWidths[col.key]
                      ? `${columnWidths[col.key]}px`
                      : undefined,
                    minWidth: columnWidths[col.key]
                      ? `${columnWidths[col.key]}px`
                      : undefined,
                    maxWidth: columnWidths[col.key]
                      ? `${columnWidths[col.key]}px`
                      : undefined,
                    backgroundColor:
                      dragOverKey === col.key
                        ? "rgba(200, 168, 78, 0.12)"
                        : undefined,
                    borderLeft:
                      dragOverKey === col.key
                        ? "2px solid var(--accent, #ffd166)"
                        : undefined,
                    transition: "background-color 0.1s, border-color 0.1s",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "4px",
                      overflow: "hidden",
                    }}
                  >
                    <span
                      title={t(
                        "torrents.table.dragToReorder",
                        undefined,
                        "Drag to reorder",
                      )}
                      style={{
                        cursor: "grab",
                        opacity: 0.35,
                        fontSize: "0.7rem",
                        flexShrink: 0,
                        lineHeight: 1,
                      }}
                    >
                      ⠿
                    </span>
                    <span
                      style={{ overflow: "hidden", textOverflow: "ellipsis" }}
                    >
                      {t(
                        COLUMN_I18N_KEYS[col.key] ||
                          `torrents.table.${col.key}`,
                        undefined,
                        col.label,
                      )}
                    </span>
                    {sortKey === col.key && (
                      <span style={{ flexShrink: 0 }}>
                        {sortAsc ? " ▲" : " ▼"}
                      </span>
                    )}
                  </div>

                  <div
                    onMouseDown={(e) => {
                      const th = e.currentTarget.parentElement as HTMLElement;
                      handleResizeMouseDown(e, col.key, th);
                    }}
                    onClick={(e) => e.stopPropagation()}
                    title={t(
                      "torrents.table.dragToResize",
                      undefined,
                      "Drag to resize",
                    )}
                    style={{
                      position: "absolute",
                      top: 0,
                      right: 0,
                      width: "5px",
                      height: "100%",
                      cursor: "col-resize",
                      zIndex: 1,
                      userSelect: "none",
                    }}
                  />
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {paddingTop > 0 && (
              <tr>
                <td
                  colSpan={columns.length + 1}
                  style={{ height: `${paddingTop}px`, padding: 0, border: 0 }}
                />
              </tr>
            )}

            {rowsToRender.map(({ virtualRow, index, torrent: t }) => {
              if (!t) return null;
              const isSelected = selectedTorrentId === t.id;
              const isChecked = selectedIds.has(t.id);
              const isFocused = focusedIndex === index;
              const historyMatch =
                (t.infoHash
                  ? historyByHashOrTitle.get(t.infoHash.toLowerCase())
                  : undefined) ||
                (t.name
                  ? historyByHashOrTitle.get(t.name.toLowerCase())
                  : undefined);

              return (
                <TorrentTableRow
                  key={t.id}
                  torrent={t}
                  rowIndex={index}
                  virtualRow={virtualRow}
                  columns={columns}
                  isSelected={isSelected}
                  isChecked={isChecked}
                  isFocused={isFocused}
                  onSelectTorrent={onSelectTorrent}
                  onSelect={propOnSelect}
                  onToggleSelect={handleToggleSelect}
                  onToggleActive={onToggleActive || handleDefaultToggleActive}
                  onRowClick={handleRowClick}
                  onContextMenu={handleContextMenu}
                  onFocus={setFocusedIndex}
                  historyMatch={historyMatch}
                  arrConnections={arrConnections}
                  measureElement={rowVirtualizer.measureElement}
                  rowRef={(el) => {
                    rowRefs.current[index] = el;
                  }}
                />
              );
            })}

            {paddingBottom > 0 && (
              <tr>
                <td
                  colSpan={columns.length + 1}
                  style={{
                    height: `${paddingBottom}px`,
                    padding: 0,
                    border: 0,
                  }}
                />
              </tr>
            )}

            {sorted.length === 0 && (
              <tr>
                <td
                  colSpan={columns.length + 1}
                  className="torrent-table-empty"
                >
                  {t("torrents.noTorrents", undefined, "No torrents found")}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {contextMenu && (
        <TorrentContextMenu
          x={contextMenu.x}
          y={contextMenu.y}
          torrent={contextMenu.torrent}
          selectedTorrents={contextMenu.selectedTorrents}
          visibleColumns={visibleColumns}
          allColumns={ALL_COLUMNS}
          onClose={closeContextMenu}
          onToggleColumn={toggleColumn}
          onResetSort={handleResetSort}
          onStart={(id) => (onResume ? onResume(id) : startSeeding.mutate(id))}
          onStop={(id) => (onPause ? onPause(id) : stopSeeding.mutate(id))}
          onUpdate={(torrent) => updateTorrent.mutate(torrent)}
          onAnnounce={(id) => announceTorrent.mutate(id)}
          onRecheck={(id) => recheckTorrent.mutate(id)}
          onDelete={(payload) =>
            propOnDelete ? propOnDelete(payload) : deleteTorrent.mutate(payload)
          }
          onMoveQueue={(payload) => moveTorrentQueue.mutate(payload)}
          onBatchStart={(ids) =>
            ids.forEach((id) =>
              onResume ? onResume(id) : startSeeding.mutate(id),
            )
          }
          onBatchStop={(ids) =>
            ids.forEach((id) =>
              onPause ? onPause(id) : stopSeeding.mutate(id),
            )
          }
          onBatchAnnounce={(ids) =>
            ids.forEach((id) => announceTorrent.mutate(id))
          }
          onBatchRecheck={(ids) =>
            ids.forEach((id) => recheckTorrent.mutate(id))
          }
          onBatchDelete={(payload) => {
            if (payload.ids.length > 0) {
              setDeleteModalState({
                isOpen: true,
                ids: payload.ids,
                torrentName:
                  payload.ids.length === 1
                    ? sorted.find((t) => t.id === payload.ids[0])?.name
                    : undefined,
              });
            }
          }}
          onBatchUpdate={(torrents) =>
            torrents.forEach((tor) => updateTorrent.mutate(tor))
          }
          onBatchMoveQueue={(payload) =>
            payload.ids.forEach((id) =>
              moveTorrentQueue.mutate({ id, position: payload.position }),
            )
          }
          onSearchIndexers={
            onSearchIndexers ? onSearchIndexers : (q) => setSearchModalQuery(q)
          }
        />
      )}

      {searchModalQuery && (
        <AddTorrentModal
          initialMode="search"
          initialQuery={searchModalQuery}
          onClose={() => setSearchModalQuery(null)}
        />
      )}

      {deleteModalState?.isOpen && (
        <DeleteTorrentModal
          isOpen={deleteModalState.isOpen}
          count={deleteModalState.ids.length}
          torrentName={deleteModalState.torrentName}
          isPending={deleteTorrent.isPending || bulkAction.isPending}
          onClose={() => {
            if (!deleteTorrent.isPending && !bulkAction.isPending)
              setDeleteModalState(null);
          }}
          onConfirm={handleConfirmDefaultDelete}
        />
      )}
    </div>
  );
}

export default TorrentTable;
